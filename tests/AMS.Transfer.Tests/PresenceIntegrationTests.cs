using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;

namespace AMS.Transfer.Tests;

public sealed class PresenceIntegrationTests
{
    [Theory]
    [InlineData("heartbeat")]
    [InlineData("send-fallback")]
    [InlineData("refresh-button")]
    public async Task PairedPeerBecomesOnlineAndTransfersWithoutAnyUdpDiscovery(string route)
    {
        using var a = new TestStorage();
        using var b = new TestStorage();
        await a.Store.InitializeAsync(); await b.Store.InitializeAsync();
        var identityA = await a.Store.GetOrCreateIdentityAsync();
        var identityB = await b.Store.GetOrCreateIdentityAsync();
        using var certA = await new CertificateService(a.Paths, a.Store).GetOrCreateAsync();
        using var certB = await new CertificateService(b.Paths, b.Store).GetOrCreateAsync();
        var portA = FreePort(); var portB = FreePort();
        const string tokenAB = "test-send-token-ab";
        const string tokenBA = "test-send-token-ba";
        var stale = DateTimeOffset.UtcNow.AddDays(-1);
        var peerB = new PairedDevice(identityB.Id, "PC 1 Ethernet", "127.0.0.1", portB,
            CertificateService.GetFingerprint(certB), tokenAB, tokenBA, stale);
        var peerA = new PairedDevice(identityA.Id, "PC 2 Wi-Fi", "127.0.0.1", portA,
            CertificateService.GetFingerprint(certA), tokenBA, tokenAB, stale);
        await a.Store.UpsertPeerAsync(peerB); await b.Store.UpsertPeerAsync(peerA);
        await using var serverB = new TransferServer(identityB, certB, b.Store, b.Paths,
            new NetworkOptions { TransferPort = portB }, new LocalLog(b.Paths));
        await serverB.StartAsync();
        var heartbeat = route == "heartbeat";
        await using var coordinator = new NetworkCoordinator(a.Store, a.Paths, new LocalLog(a.Paths),
            new NetworkOptions { TransferPort = portA, EnableDiscovery = false, EnableHeartbeat = heartbeat, HeartbeatInterval = TimeSpan.FromMilliseconds(100) });
        await coordinator.StartAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        IReadOnlyList<PairedDevice> online = [];
        if (!heartbeat)
        {
            Assert.Empty(await coordinator.GetOnlinePeersAsync());
            if (route == "refresh-button")
            {
                await coordinator.RefreshPeersAsync(); // No preceding send/probe populates the cache.
                online = await coordinator.GetOnlinePeersAsync();
            }
            else online = await coordinator.GetAvailablePeersForSendAsync();
        }
        else
        do
        {
            online = await coordinator.GetOnlinePeersAsync();
            if (online.Count > 0) break;
            await Task.Delay(50);
        } while (DateTimeOffset.UtcNow < deadline);
        Assert.Empty(coordinator.DiscoveredDevices);
        Assert.Single(online);
        Assert.Equal(peerB.Id, online[0].Id);
        Assert.True((await a.Store.GetPeerAsync(peerB.Id))!.LastSeenUtc > stale);
        var payloadPath = Path.Combine(a.Paths.Root, "fallback.bin");
        var bytes = new byte[262161]; Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(payloadPath, bytes);
        await coordinator.SendFileAsync(online[0], payloadPath);
        var receivedB = Assert.Single(await b.Store.GetRecentTransfersAsync());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(receivedB.LocalPath!));
        var clientB = new TransferClient(identityB, portB, peerB.CertificateFingerprint, b.Store, new LocalLog(b.Paths));
        await clientB.SendFileAsync(peerA, receivedB.LocalPath!);
        Assert.Equal(bytes, await File.ReadAllBytesAsync((await a.Store.GetRecentTransfersAsync())
            .Single(record => record.Direction == TransferDirection.Received).LocalPath!));
        Assert.Equal(tokenAB, (await a.Store.GetPeerAsync(peerB.Id))!.SendToken);
        Assert.Equal(tokenBA, (await a.Store.GetPeerAsync(peerB.Id))!.ReceiveToken);
    }

    [Fact]
    public async Task RealHealthRejectsInvalidTokenCertificateIdAndUnauthenticatedRequests()
    {
        using var b = new TestStorage(); await b.Store.InitializeAsync();
        var local = new DeviceIdentity(Guid.NewGuid(), "sender");
        var remote = await b.Store.GetOrCreateIdentityAsync();
        using var cert = await new CertificateService(b.Paths, b.Store).GetOrCreateAsync();
        var port = FreePort();
        const string secret = "auth-secret-not-in-log";
        await b.Store.UpsertPeerAsync(new PairedDevice(local.Id, local.Name, "127.0.0.1", 45832,
            new string('A', 64), "reverse-secret-not-in-log", secret, DateTimeOffset.UtcNow));
        var peer = new PairedDevice(remote.Id, "receiver", "127.0.0.1", port,
            CertificateService.GetFingerprint(cert), secret, "reverse-secret-not-in-log", DateTimeOffset.UtcNow);
        await using var server = new TransferServer(remote, cert, b.Store, b.Paths,
            new NetworkOptions { TransferPort = port }, new LocalLog(b.Paths));
        await server.StartAsync();
        var probe = new PeerProbeClient(local, new NetworkOptions(), new LocalLog(b.Paths));
        Assert.True((await probe.ProbeAsync(peer)).Success);
        Assert.Equal(PeerProbeFailure.AuthenticationFailed, (await probe.ProbeAsync(peer with { SendToken = "invalid-secret-not-in-log" })).Failure);
        Assert.Equal(PeerProbeFailure.CertificateMismatch, (await probe.ProbeAsync(peer with { CertificateFingerprint = new string('0', 64) })).Failure);
        Assert.Equal(PeerProbeFailure.DeviceIdMismatch, (await probe.ProbeAsync(peer with { Id = Guid.NewGuid() })).Failure);
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            certificate is not null && CertificateService.GetFingerprint(certificate) == peer.CertificateFingerprint };
        using var http = new HttpClient(handler);
        using var response = await http.GetAsync($"https://127.0.0.1:{port}/api/peer/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await b.Store.GetRecentTransfersAsync());
        var logs = await File.ReadAllTextAsync(Directory.GetFiles(b.Paths.Logs).Single());
        Assert.Contains("TCP connection succeeded", logs); Assert.Contains("OrbLink identity validated", logs);
        Assert.Contains("AuthenticationFailed", logs); Assert.Contains("CertificateMismatch", logs); Assert.Contains("DeviceIdMismatch", logs);
        Assert.DoesNotContain(secret, logs); Assert.DoesNotContain("reverse-secret-not-in-log", logs);
        Assert.DoesNotContain("invalid-secret-not-in-log", logs);
    }

    [Fact]
    public async Task OpenTcpPortAloneNeverCountsAsOrbLinkPresence()
    {
        using var fixture = new TestStorage();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            var peer = new PairedDevice(Guid.NewGuid(), "plain TCP", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
                new string('A', 64), "secret", "secret", DateTimeOffset.UtcNow);
            var probe = new PeerProbeClient(new DeviceIdentity(Guid.NewGuid(), "local"),
                new NetworkOptions { ProbeTimeout = TimeSpan.FromMilliseconds(500) }, new LocalLog(fixture.Paths));
            var pending = probe.ProbeAsync(peer);
            using (var connection = await accept.WaitAsync(TimeSpan.FromSeconds(3)))
                Assert.True(connection.Connected);
            Assert.False((await pending).Success);
        }
        finally { listener.Stop(); }
    }

    internal static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
}
