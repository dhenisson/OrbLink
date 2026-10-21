using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AMS.Transfer.Core;
using AMS.Transfer.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;

namespace AMS.Transfer.Network;

public sealed class TransferServer(
    DeviceIdentity identity,
    X509Certificate2 certificate,
    SqliteStore store,
    AppPaths paths,
    NetworkOptions options,
    LocalLog log,
    TimeProvider? suppliedTimeProvider = null) : IAsyncDisposable
{
    private readonly TimeProvider _clock = suppliedTimeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, PendingPair> _pairings = new();
    private readonly object _pairingGate = new();
    private readonly SemaphoreSlim _receives = new(4, 4);
    private Timer? _pairingCleanup;
    private WebApplication? _app;

    public event Func<IncomingPairRequest, Task>? PairingRequested;
    public event Func<TransferRecord, Task>? ItemReceived;
    public event Action<TransferActivity>? ReceiveActivity;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TransferServer).Assembly.FullName,
            Args = []
        });
        // Do not send URLs (including pairing capabilities), bodies or credentials to provider logs.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = options.MaxFileSizeBytes;
            server.ListenAnyIP(options.TransferPort, listen => listen.UseHttps(certificate));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (BadHttpRequestException ex) { context.Response.StatusCode = ex.StatusCode; }
        });
        _pairingCleanup = new Timer(_ => RemoveExpiredPairs(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        CleanupOldPartials();

        app.MapGet("/api/health", () => Results.Ok(new { deviceId = identity.Id, name = identity.Name }));

        app.MapGet("/api/peer/health", async (HttpRequest request, CancellationToken token) =>
        {
            if (await AuthenticateAsync(request, token) is null) return Results.Unauthorized();
            return Results.Ok(new PeerHealthResponse(PeerHealthResponse.ProtocolName, identity.Id, identity.Name));
        });

        app.MapPost("/api/pair/request", async (HttpRequest request, CancellationToken token) =>
        {
            LimitBody(request, 8 * 1024);
            var message = await request.ReadFromJsonAsync<PairRequestMessage>(cancellationToken: token);
            if (message is null || message.RequesterId == Guid.Empty || message.RequesterId == identity.Id ||
                string.IsNullOrWhiteSpace(message.RequesterName) || message.RequesterName.Length > 128 ||
                message.RequesterName.Any(char.IsControl) || message.RequesterPort is < 1 or > 65535 ||
                !IsFingerprint(message.RequesterCertificateFingerprint) || !IsPairToken(message.RequesterReceiveToken))
                return Results.BadRequest(new { error = "Pedido de pareamento inválido." });

            var requestId = Guid.NewGuid();
            var address = request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
            lock (_pairingGate)
            {
                RemoveExpiredPairs();
                if (_pairings.Count >= 32) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                _pairings[requestId] = new PendingPair(message, address, _clock.GetUtcNow().AddMinutes(2));
            }
            var handler = PairingRequested;
            if (handler is not null)
                _ = Task.Run(async () =>
                {
                    try { await handler(new IncomingPairRequest(requestId, message, address)); }
                    catch (Exception ex) { RejectPair(requestId); await log.WriteAsync("ERROR", "Falha ao apresentar pareamento.", ex); }
                }, CancellationToken.None);
            return Results.Ok(new PairRequestReceipt(requestId, "pending"));
        });

        app.MapGet("/api/pair/status/{requestId:guid}", (Guid requestId, HttpRequest request) =>
        {
            RemoveExpiredPairs();
            if (!_pairings.TryGetValue(requestId, out var pending))
                return Results.NotFound(new PairStatusMessage("not_found"));
            if (!Matches(pending.Message.RequesterReceiveToken, request.Headers["X-Pair-Token"].FirstOrDefault()))
                return Results.Unauthorized();
            return Results.Ok(pending.Status);
        });

        app.MapPost("/api/files", async (HttpRequest request, CancellationToken token) =>
        {
            var peer = await AuthenticateAsync(request, token);
            if (peer is null) return Results.Unauthorized();
            if (request.ContentLength is null || request.ContentLength < 0 || request.ContentLength > options.MaxFileSizeBytes)
                return Results.BadRequest(new { error = "Tamanho ausente ou acima do limite configurado." });

            var encodedName = request.Headers["X-File-Name"].FirstOrDefault();
            if (encodedName?.Length > 4096) return Results.BadRequest();
            var expectedHash = request.Headers["X-File-SHA256"].FirstOrDefault();
            if (expectedHash is not null && !IsFingerprint(expectedHash)) return Results.BadRequest();
            if (!_receives.Wait(0)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            var fileName = FileNameSafety.Sanitize(Uri.UnescapeDataString(encodedName ?? "arquivo"));
            var partPath = NewPartialPath();
            var activityId = Guid.NewGuid();
            try
            {
                ReportActivity(new TransferActivity(activityId, fileName, 0, true));
                long total = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var lastProgress = System.Diagnostics.Stopwatch.StartNew();
                await using (var output = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[128 * 1024];
                    int read;
                    while ((read = await request.Body.ReadAsync(buffer, token)) > 0)
                    {
                        total += read;
                        if (total > options.MaxFileSizeBytes) throw new InvalidDataException("Arquivo acima do limite.");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        if (lastProgress.ElapsedMilliseconds >= 100)
                        {
                            ReportActivity(new TransferActivity(activityId, fileName,
                                request.ContentLength == 0 ? 1 : (double)total / request.ContentLength.Value, true));
                            lastProgress.Restart();
                        }
                    }
                    if (total != request.ContentLength.Value) throw new InvalidDataException("Tamanho recebido divergente.");
                    if (expectedHash is not null && !CryptographicOperations.FixedTimeEquals(
                        hash.GetHashAndReset(), Convert.FromHexString(expectedHash)))
                        throw new InvalidDataException("Integridade SHA-256 divergente.");
                    await output.FlushAsync(token);
                    output.Flush(flushToDisk: true);
                }

                token.ThrowIfCancellationRequested();
                var finalPath = FileNameSafety.MoveToUniquePath(partPath, paths.Received, fileName);
                var record = new TransferRecord(Guid.NewGuid(), Path.GetFileName(finalPath),
                    FileNameSafety.DetectKind(finalPath), TransferDirection.Received, TransferStatus.Completed,
                    total, peer.Id, peer.Name, finalPath, DateTimeOffset.UtcNow);
                await RecordCommittedAsync(record);
                return Results.Ok(new { record.Id, record.Name, record.Size });
            }
            catch (Exception ex)
            {
                DeletePartial(partPath);
                await log.WriteAsync("ERROR", $"Falha ao receber {fileName}.", ex);
                return ex is InvalidDataException ? Results.BadRequest(new { error = "Tamanho ou integridade inválidos." }) :
                    Results.Problem("Não foi possível receber o arquivo.");
            }
            finally { ReportActivity(new TransferActivity(activityId, fileName, 1, false)); _receives.Release(); }
        });

        app.MapPost("/api/text", async (HttpRequest request, CancellationToken token) =>
        {
            var peer = await AuthenticateAsync(request, token);
            if (peer is null) return Results.Unauthorized();
            LimitBody(request, 8 * 1024 * 1024);
            var message = await request.ReadFromJsonAsync<TextTransferMessage>(cancellationToken: token);
            if (message is null || string.IsNullOrWhiteSpace(message.Text) || message.Text.Length > 1_000_000 || message.Label?.Length > 4096)
                return Results.BadRequest(new { error = "Texto inválido." });

            var name = FileNameSafety.Sanitize(message.Label ?? $"Texto {DateTime.Now:yyyy-MM-dd HH-mm}.txt");
            if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name += ".txt";
            if (!_receives.Wait(0)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            var partial = NewPartialPath();
            try
            {
                await File.WriteAllTextAsync(partial, message.Text, new UTF8Encoding(false), token);
                token.ThrowIfCancellationRequested();
                var path = FileNameSafety.MoveToUniquePath(partial, paths.Received, name);
                var record = new TransferRecord(Guid.NewGuid(), Path.GetFileName(path), TransferKind.Text,
                    TransferDirection.Received, TransferStatus.Completed, Encoding.UTF8.GetByteCount(message.Text),
                    peer.Id, peer.Name, path, DateTimeOffset.UtcNow);
                await RecordCommittedAsync(record);
                return Results.Ok(new { record.Id });
            }
            catch (Exception ex)
            {
                await log.WriteAsync("ERROR", "Falha ao receber texto (conteúdo omitido).", ex);
                return Results.Problem("Não foi possível receber o texto.");
            }
            finally { DeletePartial(partial); _receives.Release(); }
        });

        _app = app;
        await app.StartAsync(cancellationToken);
    }

    public async Task AcceptPairAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        RemoveExpiredPairs();
        if (!_pairings.TryGetValue(requestId, out var pending)) return;
        await pending.AcceptGate.WaitAsync(cancellationToken);
        try
        {
        if (pending.Status.Status != "pending" || pending.Expires <= _clock.GetUtcNow()) return;
        var responderReceiveToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var peer = new PairedDevice(
            pending.Message.RequesterId, pending.Message.RequesterName, pending.RemoteAddress,
            pending.Message.RequesterPort, pending.Message.RequesterCertificateFingerprint,
            pending.Message.RequesterReceiveToken, responderReceiveToken, DateTimeOffset.UtcNow);
        await store.UpsertPeerAsync(peer, cancellationToken);
        pending.Status = new PairStatusMessage(
            "accepted", identity.Id, identity.Name, responderReceiveToken);
        }
        finally { pending.AcceptGate.Release(); }
    }

    public void RejectPair(Guid requestId)
    {
        if (_pairings.TryGetValue(requestId, out var pending))
            pending.Status = new PairStatusMessage("rejected", Error: "O outro computador recusou o pareamento.");
    }

    private async Task<PairedDevice?> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Headers["X-Device-Id"].FirstOrDefault(), out var deviceId)) return null;
        var suppliedToken = request.Headers["X-Auth-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(suppliedToken) || suppliedToken.Length > 256) return null;
        var peer = await store.GetPeerAsync(deviceId, cancellationToken);
        if (peer is null) return null;
        return Matches(peer.ReceiveToken, suppliedToken) ? peer : null;
    }

    private static bool Matches(string expected, string? supplied) => supplied is not null && supplied.Length <= 256 &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    private static bool IsFingerprint(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool IsPairToken(string? value)
    {
        if (value?.Length != 44) return false;
        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(value, bytes, out var length) && length == 32;
    }
    private static void LimitBody(HttpRequest request, long bytes)
    {
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = bytes;
    }
    private void RemoveExpiredPairs()
    {
        foreach (var item in _pairings)
            if (item.Value.Expires <= _clock.GetUtcNow()) _pairings.TryRemove(item.Key, out _);
    }
    private string NewPartialPath() => Path.Combine(paths.Received, $"orblink-{Guid.NewGuid():N}.part");
    private static void DeletePartial(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void CleanupOldPartials()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(paths.Received, "orblink-*.part"))
            {
                var name = Path.GetFileName(path);
                if (Guid.TryParseExact(name[8..^5], "N", out _) && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                    DeletePartial(path);
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void ReportActivity(TransferActivity activity)
    {
        try { ReceiveActivity?.Invoke(activity); } catch (Exception ex) { _ = log.WriteAsync("WARN", "Falha de atualização visual.", ex); }
    }
    private async Task RecordCommittedAsync(TransferRecord record)
    {
        // A UI or history failure after the atomic commit must not turn a received file into a failed upload.
        try { await store.AddTransferAsync(record, CancellationToken.None); }
        catch (Exception ex) { await log.WriteAsync("ERROR", "Arquivo salvo; histórico indisponível.", ex); }
        try { if (ItemReceived is not null) await ItemReceived(record); }
        catch (Exception ex) { await log.WriteAsync("WARN", "Arquivo salvo; notificação indisponível.", ex); }
    }

    public async ValueTask DisposeAsync()
    {
        _pairingCleanup?.Dispose();
        _pairings.Clear();
        if (_app is null) return;
        try { await _app.StopAsync(TimeSpan.FromSeconds(3)); } catch { }
        await _app.DisposeAsync();
        _app = null;
    }

    private sealed class PendingPair(PairRequestMessage message, string remoteAddress, DateTimeOffset expires)
    {
        public PairRequestMessage Message { get; } = message;
        public string RemoteAddress { get; } = remoteAddress;
        public PairStatusMessage Status { get; set; } = new("pending");
        public DateTimeOffset Expires { get; } = expires;
        public SemaphoreSlim AcceptGate { get; } = new(1, 1);
    }
}
