using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AMS.Transfer.Core;
using AMS.Transfer.Storage;

namespace AMS.Transfer.Network;

public sealed class TransferClient(
    DeviceIdentity identity,
    int localPort,
    string localCertificateFingerprint,
    SqliteStore store,
    LocalLog log)
{
    public async Task<PairedDevice> PairAsync(
        DiscoveredDevice device,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var localReceiveToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var client = CreatePinnedClient(device.CertificateFingerprint);
        using var pairTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pairTimeout.CancelAfter(TimeSpan.FromMinutes(2));
        cancellationToken = pairTimeout.Token;
        client.DefaultRequestHeaders.Add("X-Pair-Token", localReceiveToken);
        var request = new PairRequestMessage(identity.Id, identity.Name, localPort,
            localCertificateFingerprint, localReceiveToken);
        status?.Report("Solicitando autorização...");
        using var response = await client.PostAsJsonAsync(BuildUri(device, "/api/pair/request"), request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<PairRequestReceipt>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Resposta de pareamento inválida.");

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(1000, cancellationToken);
            var pairStatus = await client.GetFromJsonAsync<PairStatusMessage>(
                BuildUri(device, $"/api/pair/status/{receipt.RequestId}"), cancellationToken)
                ?? throw new InvalidDataException("Status de pareamento inválido.");
            if (pairStatus.Status == "pending") continue;
            if (pairStatus.Status != "accepted" || pairStatus.ResponderId is null ||
                pairStatus.ResponderId != device.Id || string.IsNullOrWhiteSpace(pairStatus.ResponderReceiveToken))
                throw new InvalidOperationException(pairStatus.Error ?? "Pareamento recusado.");

            var peer = new PairedDevice(pairStatus.ResponderId.Value,
                pairStatus.ResponderName ?? device.Name, device.Address, device.Port,
                device.CertificateFingerprint, pairStatus.ResponderReceiveToken, localReceiveToken, DateTimeOffset.UtcNow);
            await store.UpsertPeerAsync(peer, cancellationToken);
            status?.Report("Pareado");
            return peer;
        }

        throw new TimeoutException("O pedido de pareamento expirou.");
    }

    public async Task<TransferRecord> SendFileAsync(
        PairedDevice peer,
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Arquivo não encontrado.", path);
        var id = Guid.NewGuid();
        var size = info.Length;
        try
        {
            using var client = CreatePinnedClient(peer.CertificateFingerprint);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            size = input.Length;
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(peer, "/api/files"));
            request.Headers.TryAddWithoutValidation("X-Device-Id", identity.Id.ToString("D"));
            request.Headers.TryAddWithoutValidation("X-Auth-Token", peer.SendToken);
            request.Headers.TryAddWithoutValidation("X-File-Name", Uri.EscapeDataString(info.Name));
            progress?.Report(0);
            var hash = await SHA256.HashDataAsync(input, cancellationToken);
            input.Position = 0;
            request.Headers.TryAddWithoutValidation("X-File-SHA256", Convert.ToHexString(hash));
            request.Content = new ProgressStreamContent(input, input.Length, progress, cancellationToken);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var record = new TransferRecord(id, info.Name, FileNameSafety.DetectKind(info.Name),
                TransferDirection.Sent, TransferStatus.Completed, size, peer.Id, peer.Name,
                path, DateTimeOffset.UtcNow);
            await store.AddTransferAsync(record, cancellationToken);
            return record;
        }
        catch (Exception ex)
        {
            await log.WriteAsync("ERROR", $"Falha ao enviar {info.Name}.", ex);
            var record = new TransferRecord(id, info.Name, FileNameSafety.DetectKind(info.Name),
                TransferDirection.Sent, cancellationToken.IsCancellationRequested ? TransferStatus.Cancelled : TransferStatus.Failed,
                size, peer.Id, peer.Name, path, DateTimeOffset.UtcNow, ex.Message);
            await store.AddTransferAsync(record, CancellationToken.None);
            throw;
        }
    }

    public async Task SendTextAsync(PairedDevice peer, string text, CancellationToken cancellationToken = default)
    {
        using var client = CreatePinnedClient(peer.CertificateFingerprint);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(peer, "/api/text"));
        request.Headers.TryAddWithoutValidation("X-Device-Id", identity.Id.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Auth-Token", peer.SendToken);
        request.Content = JsonContent.Create(new TextTransferMessage(text));
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static HttpClient CreatePinnedClient(string expectedFingerprint)
    {
        return PinnedHttp.Create(expectedFingerprint);
    }

    private static Uri BuildUri(DiscoveredDevice device, string path) =>
        new($"https://{FormatHost(device.Address)}:{device.Port}{path}");

    private static Uri BuildUri(PairedDevice device, string path) =>
        new($"https://{FormatHost(device.Address)}:{device.Port}{path}");

    private static string FormatHost(string address) => address.Contains(':') ? $"[{address}]" : address;
}
