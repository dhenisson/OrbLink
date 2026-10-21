using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;

namespace AMS.Transfer.Tests;

public sealed class PeerPresenceTests
{
    private static PairedDevice Peer(string address = "192.168.10.20") => new(Guid.NewGuid(), "PC fixture",
        address, 45832, new string('A', 64), "send-secret-never-log", "receive-secret-never-log", DateTimeOffset.UtcNow.AddDays(-1));

    [Fact]
    public async Task UsesSavedEndpointAndRequiresFreshSuccessNotDatabaseTimestamp()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        var peer = Peer() with { LastSeenUtc = DateTimeOffset.UtcNow };
        await fixture.Store.UpsertPeerAsync(peer);
        PairedDevice? attempted = null;
        await using var monitor = new PeerPresenceMonitor(fixture.Store, new LocalLog(fixture.Paths), new NetworkOptions(),
            (target, _) => { attempted = target; return Task.FromResult(new PeerProbeResult(true)); });
        Assert.Empty(await monitor.GetOnlinePeersAsync());
        await monitor.RefreshAsync();
        var online = Assert.Single(await monitor.GetOnlinePeersAsync());
        Assert.Equal(peer.Address, attempted!.Address); Assert.Equal(peer.Port, attempted.Port);
        Assert.Equal(peer.Id, online.Id);
        var reopened = new SqliteStore(fixture.Paths, new TestProtector());
        Assert.Equal(peer.SendToken, (await reopened.GetPeerAsync(peer.Id))!.SendToken);
        Assert.Equal(peer.ReceiveToken, (await reopened.GetPeerAsync(peer.Id))!.ReceiveToken);
    }

    [Fact]
    public async Task ToleratesTwoTransientFailuresThenGoesOfflineAndRecovers()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        await fixture.Store.UpsertPeerAsync(Peer());
        var result = new PeerProbeResult(true);
        await using var monitor = new PeerPresenceMonitor(fixture.Store, new LocalLog(fixture.Paths), new NetworkOptions(),
            (_, _) => Task.FromResult(result));
        await monitor.RefreshAsync();
        result = new(false, PeerProbeFailure.Timeout);
        await monitor.RefreshAsync(); Assert.Single(await monitor.GetOnlinePeersAsync());
        await monitor.RefreshAsync(); Assert.Single(await monitor.GetOnlinePeersAsync());
        await monitor.RefreshAsync(); Assert.Empty(await monitor.GetOnlinePeersAsync());
        result = new(true); await monitor.RefreshAsync(); Assert.Single(await monitor.GetOnlinePeersAsync());
        var log = await File.ReadAllTextAsync(Directory.GetFiles(fixture.Paths.Logs).Single());
        Assert.Contains("Offline -> Online", log); Assert.Contains("Online -> Offline", log);
        Assert.DoesNotContain("send-secret-never-log", log); Assert.DoesNotContain("receive-secret-never-log", log);
    }

    [Fact]
    public async Task SuccessExpiresAfterFifteenSecondsEvenWithoutThreeFailures()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        await fixture.Store.UpsertPeerAsync(Peer());
        var clock = new TestClock();
        await using var monitor = new PeerPresenceMonitor(fixture.Store, new LocalLog(fixture.Paths), new NetworkOptions(),
            (_, _) => Task.FromResult(new PeerProbeResult(true)), clock);
        await monitor.RefreshAsync(); clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Single(await monitor.GetOnlinePeersAsync());
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.Empty(await monitor.GetOnlinePeersAsync());
    }

    [Theory]
    [InlineData(PeerProbeFailure.AuthenticationFailed)]
    [InlineData(PeerProbeFailure.CertificateMismatch)]
    [InlineData(PeerProbeFailure.DeviceIdMismatch)]
    [InlineData(PeerProbeFailure.InvalidService)]
    public async Task SecurityFailureDoesNotGetTransientOfflineGrace(PeerProbeFailure failure)
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        await fixture.Store.UpsertPeerAsync(Peer());
        var result = new PeerProbeResult(true);
        await using var monitor = new PeerPresenceMonitor(fixture.Store, new LocalLog(fixture.Paths), new NetworkOptions(),
            (_, _) => Task.FromResult(result));
        await monitor.RefreshAsync(); result = new(false, failure);
        await monitor.RefreshAsync(); Assert.Empty(await monitor.GetOnlinePeersAsync());
        result = new(false, PeerProbeFailure.Timeout);
        await monitor.RefreshAsync(); Assert.Empty(await monitor.GetOnlinePeersAsync());
        result = new(true);
        await monitor.RefreshAsync(); Assert.Single(await monitor.GetOnlinePeersAsync());
    }

    [Fact]
    public async Task ConcurrentRefreshesShareOneProbeAndCallerCancellationDoesNotCancelIt()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        await fixture.Store.UpsertPeerAsync(Peer());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<PeerProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var monitor = new PeerPresenceMonitor(fixture.Store, new LocalLog(fixture.Paths), new NetworkOptions(),
            async (_, token) => { Interlocked.Increment(ref calls); entered.TrySetResult(); return await release.Task.WaitAsync(token); });
        using var canceled = new CancellationTokenSource();
        var first = monitor.RefreshAsync(true, canceled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = monitor.RefreshAsync();
        canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, calls);
        release.SetResult(new(true)); await second;
        Assert.Equal(1, calls); Assert.Single(await monitor.GetOnlinePeersAsync());
    }

    [Fact]
    public async Task ValidatedDiscoveryUpdatesAddressWithoutDuplicatingOrReplacingPairing()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        var peer = Peer(); await fixture.Store.UpsertPeerAsync(peer);
        var result = new PeerProbeResult(true);
        var clock = new TestClock();
        await using var monitor = new PeerPresenceMonitor(fixture.Store, new LocalLog(fixture.Paths), new NetworkOptions(),
            (_, _) => Task.FromResult(result), clock);
        await monitor.RefreshAsync();
        var update = new DiscoveredDevice(peer.Id, "same identity", "192.168.0.210", 45833, peer.CertificateFingerprint, clock.GetUtcNow());
        Assert.True(await monitor.ObserveDiscoveryAsync(update));
        var saved = Assert.Single(await fixture.Store.GetPeersAsync());
        Assert.Equal(update.Address, saved.Address); Assert.Equal(update.Port, saved.Port);
        Assert.Equal(peer.SendToken, saved.SendToken); Assert.Equal(peer.ReceiveToken, saved.ReceiveToken);
        Assert.Equal(saved.Address, Assert.Single(await monitor.GetOnlinePeersAsync()).Address);
        result = new(false, PeerProbeFailure.CertificateMismatch);
        Assert.False(await monitor.ObserveDiscoveryAsync(update with { Address = "192.168.0.199" }));
        Assert.Equal(update.Address, (await fixture.Store.GetPeerAsync(peer.Id))!.Address);
        Assert.Single(await monitor.GetOnlinePeersAsync());
        Assert.False(await monitor.ObserveDiscoveryAsync(update with { CertificateFingerprint = new string('B', 64) }));
        Assert.Single(await fixture.Store.GetPeersAsync());
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
