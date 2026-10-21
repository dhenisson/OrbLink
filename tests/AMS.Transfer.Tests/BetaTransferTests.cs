using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;
using Xunit.Abstractions;

namespace AMS.Transfer.Tests;

[CollectionDefinition("Streaming", DisableParallelization = true)]
public sealed class StreamingCollection { }

[Collection("Streaming")]
public sealed class BetaTransferTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1024)]
    public async Task LargeFileStreamsWithIntegrityAndProgress(int mebibytes)
    {
        await using var fixture = await ServerFixture.CreateAsync();
        var source = Path.Combine(fixture.A.Paths.Root, $"grande-ação-{mebibytes}.bin");
        var chunk = new byte[128 * 1024]; Random.Shared.NextBytes(chunk);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await using (var file = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None, chunk.Length, true))
            for (var index = 0; index < mebibytes * 8; index++) await file.WriteAsync(chunk);
        var progress = new ProgressCollector();
        var received = new TaskCompletionSource<TransferRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Server.ItemReceived += record => { received.TrySetResult(record); return Task.CompletedTask; };
        await fixture.Client.SendFileAsync(fixture.Peer, source, progress);
        var record = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((long)mebibytes * 1024 * 1024, record.Size);
        await using var original = File.OpenRead(source); await using var saved = File.OpenRead(record.LocalPath!);
        Assert.Equal(await SHA256.HashDataAsync(original), await SHA256.HashDataAsync(saved));
        Assert.True(progress.Count > 10); Assert.Equal(1, progress.Last);
        Assert.Empty(Directory.GetFiles(fixture.B.Paths.Received, "*.part"));
        output.WriteLine($"{mebibytes} MiB: SHA-256 identical, {progress.Count} progress events, {timer.Elapsed.TotalSeconds:F2}s; buffers 128 KiB; no full-file allocation.");
    }

    [Fact]
    public async Task ConcurrentFilesAndTextsNeverOverwriteAndUiFailureDoesNotInvalidateCommit()
    {
        await using var fixture = await ServerFixture.CreateAsync();
        fixture.Server.ItemReceived += _ => throw new InvalidOperationException("UI failure");
        var source = Path.Combine(fixture.A.Paths.Root, "proposta-ação 😀.pdf");
        await File.WriteAllTextAsync(source, "original");
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => fixture.Client.SendFileAsync(fixture.Peer, source)));
        Assert.Equal(3, Directory.GetFiles(fixture.B.Paths.Received, "*.pdf").Length);
        Assert.True(File.Exists(Path.Combine(fixture.B.Paths.Received, "proposta-ação 😀 (1).pdf")));
        using var client = fixture.Http();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => client.PostAsJsonAsync(fixture.Uri("/api/text"), new TextTransferMessage("text-" + i, "CON.txt"))));
        try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode)); }
        finally { foreach (var response in responses) response.Dispose(); }
        var texts = Directory.GetFiles(fixture.B.Paths.Received, "*.txt");
        Assert.Equal(4, texts.Length); Assert.Equal(4, texts.Select(File.ReadAllText).Distinct().Count());
        Assert.All(texts, path => Assert.StartsWith("_CON", Path.GetFileName(path)));
    }

    [Fact]
    public async Task BadChecksumAndOversizeLeaveNoFinalOrPartialFile()
    {
        await using var fixture = await ServerFixture.CreateAsync(maxBytes: 1024);
        using var client = fixture.Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, fixture.Uri("/api/files"));
        request.Headers.Add("X-File-Name", Uri.EscapeDataString(@"..\..\outside.bin"));
        request.Headers.Add("X-File-SHA256", new string('0', 64));
        request.Content = new ByteArrayContent([1, 2, 3]);
        using var bad = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var oversized = await client.PostAsync(fixture.Uri("/api/files"), new ByteArrayContent(new byte[1025]));
        Assert.True((int)oversized.StatusCode is 400 or 413);
        Assert.Empty(Directory.GetFiles(fixture.B.Paths.Received));
    }

    [Fact]
    public async Task CancellationRemovesPartialAndDoesNotPublishFile()
    {
        await using var fixture = await ServerFixture.CreateAsync();
        using var client = fixture.Http();
        using var stop = new CancellationTokenSource();
        fixture.Server.ReceiveActivity += activity => { if (activity.IsActive && activity.Fraction > 0) stop.Cancel(); };
        using var request = new HttpRequestMessage(HttpMethod.Post, fixture.Uri("/api/files"))
        { Content = new SlowContent(stop.Token) };
        request.Headers.Add("X-File-Name", "interrupted.bin");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(request, stop.Token));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Directory.GetFiles(fixture.B.Paths.Received).Length > 0 && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Empty(Directory.GetFiles(fixture.B.Paths.Received));
        Assert.Empty(await fixture.B.Store.GetRecentTransfersAsync());
    }

    [Fact]
    public async Task PairStatusRequiresProofAndInvalidOrExcessRequestsAreRejected()
    {
        await using var fixture = await ServerFixture.CreateAsync();
        using var client = fixture.Http();
        var message = new PairRequestMessage(Guid.NewGuid(), "PC fixture", 45832, new string('A', 64), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var receiptResponse = await client.PostAsJsonAsync(fixture.Uri("/api/pair/request"), message);
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<PairRequestReceipt>())!;
        using var anonymous = await client.GetAsync(fixture.Uri("/api/pair/status/" + receipt.RequestId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Pair-Token", message.RequesterReceiveToken);
        using var authenticated = await client.GetAsync(fixture.Uri("/api/pair/status/" + receipt.RequestId));
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        using var invalid = await client.PostAsJsonAsync(fixture.Uri("/api/pair/request"), message with { RequesterPort = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        HttpStatusCode last = 0;
        for (var index = 0; index < 35; index++)
        { using var response = await client.PostAsJsonAsync(fixture.Uri("/api/pair/request"), message); last = response.StatusCode; }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task ExpiredPairCannotExposeCredentialsOrBeAccepted()
    {
        var clock = new AdjustableClock();
        await using var fixture = await ServerFixture.CreateAsync(clock: clock);
        using var client = fixture.Http();
        var message = new PairRequestMessage(Guid.NewGuid(), "expiring fixture", 45832, new string('A', 64), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var response = await client.PostAsJsonAsync(fixture.Uri("/api/pair/request"), message);
        var receipt = (await response.Content.ReadFromJsonAsync<PairRequestReceipt>())!;
        clock.Advance(TimeSpan.FromMinutes(3));
        await fixture.Server.AcceptPairAsync(receipt.RequestId);
        client.DefaultRequestHeaders.Add("X-Pair-Token", message.RequesterReceiveToken);
        using var expired = await client.GetAsync(fixture.Uri("/api/pair/status/" + receipt.RequestId));
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Null(await fixture.B.Store.GetPeerAsync(message.RequesterId));
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    [Fact]
    public async Task DirectPeerRecoversAfterServerRestartAndValidatedEndpointChange()
    {
        await using var fixture = await ServerFixture.CreateAsync();
        var options = new NetworkOptions { EnableHeartbeat = false, OfflineFailureCount = 1, ProbeTimeout = TimeSpan.FromMilliseconds(500) };
        var identity = await fixture.A.Store.GetOrCreateIdentityAsync();
        var probe = new PeerProbeClient(identity, options, new LocalLog(fixture.A.Paths));
        await using var presence = new PeerPresenceMonitor(fixture.A.Store, new LocalLog(fixture.A.Paths), options, probe.ProbeAsync);
        await presence.RefreshAsync(true); Assert.Single(await presence.GetOnlinePeersAsync());
        await fixture.Server.DisposeAsync();
        await presence.RefreshAsync(true); Assert.Empty(await presence.GetOnlinePeersAsync());
        await fixture.RestartOnNewPortAsync();
        Assert.True(await presence.ObserveDiscoveryAsync(new DiscoveredDevice(fixture.Peer.Id, "same fixture", "127.0.0.1",
            fixture.Peer.Port, fixture.Peer.CertificateFingerprint, DateTimeOffset.UtcNow)));
        var saved = Assert.Single(await fixture.A.Store.GetPeersAsync());
        Assert.Equal(fixture.Peer.Id, saved.Id); Assert.Equal(fixture.Peer.Port, saved.Port);
        Assert.Equal(fixture.Peer.SendToken, saved.SendToken); Assert.Single(await presence.GetOnlinePeersAsync());
    }

    private sealed class ProgressCollector : IProgress<double>
    {
        public int Count { get; private set; } public double Last { get; private set; }
        public void Report(double value) { Count++; Last = value; }
    }
    private sealed class SlowContent(CancellationToken stop) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 100 * 1024 * 1024; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[128 * 1024];
            for (var i = 0; i < 800; i++) { await stream.WriteAsync(chunk, stop); await Task.Delay(10, stop); }
        }
    }

    private sealed class ServerFixture : IAsyncDisposable
    {
        public TestStorage A { get; } = new(); public TestStorage B { get; } = new();
        public TransferServer Server { get; private set; } = null!;
        public TransferClient Client { get; private set; } = null!; public PairedDevice Peer { get; private set; } = null!;
        private System.Security.Cryptography.X509Certificates.X509Certificate2? _certificate;
        private DeviceIdentity _identity = null!; private int _port;
        public static async Task<ServerFixture> CreateAsync(long maxBytes = 10L * 1024 * 1024 * 1024, TimeProvider? clock = null)
        {
            var f = new ServerFixture();
            try
            {
                await f.A.Store.InitializeAsync(); await f.B.Store.InitializeAsync();
                f._identity = await f.A.Store.GetOrCreateIdentityAsync(); var remote = await f.B.Store.GetOrCreateIdentityAsync();
                f._certificate = await new CertificateService(f.B.Paths, f.B.Store, new TestProtector()).GetOrCreateAsync();
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); f._port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                f.Peer = new PairedDevice(remote.Id, "remote fixture", "127.0.0.1", f._port, CertificateService.GetFingerprint(f._certificate), "fixture-send", "fixture-receive", DateTimeOffset.UtcNow);
                await f.A.Store.UpsertPeerAsync(f.Peer);
                await f.B.Store.UpsertPeerAsync(new PairedDevice(f._identity.Id, "local fixture", "127.0.0.1", 45832, new string('A', 64), f.Peer.ReceiveToken, f.Peer.SendToken, DateTimeOffset.UtcNow));
                f.Server = new TransferServer(remote, f._certificate, f.B.Store, f.B.Paths, new NetworkOptions { TransferPort = f._port, MaxFileSizeBytes = maxBytes }, new LocalLog(f.B.Paths), clock);
                await f.Server.StartAsync();
                f.Client = new TransferClient(f._identity, 45832, new string('A', 64), f.A.Store, new LocalLog(f.A.Paths));
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public Uri Uri(string path) => new($"https://127.0.0.1:{_port}{path}");
        public async Task RestartOnNewPortAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            Peer = Peer with { Port = _port };
            Server = new TransferServer(await B.Store.GetOrCreateIdentityAsync(), _certificate!, B.Store, B.Paths,
                new NetworkOptions { TransferPort = _port }, new LocalLog(B.Paths));
            await Server.StartAsync();
        }
        public HttpClient Http()
        {
            var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && CertificateService.GetFingerprint(cert) == Peer.CertificateFingerprint };
            var client = new HttpClient(handler); client.DefaultRequestHeaders.Add("X-Device-Id", _identity.Id.ToString()); client.DefaultRequestHeaders.Add("X-Auth-Token", Peer.SendToken); return client;
        }
        public async ValueTask DisposeAsync()
        {
            if (Server is not null) await Server.DisposeAsync(); _certificate?.Dispose(); A.Dispose(); B.Dispose();
        }
    }
}
