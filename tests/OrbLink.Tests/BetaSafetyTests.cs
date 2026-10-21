using OrbLink.App;
using OrbLink.Core;
using OrbLink.Network;
using OrbLink.Storage;
using Microsoft.Data.Sqlite;

namespace OrbLink.Tests;

public sealed class BetaSafetyTests
{
    private static readonly MonitorArea[] Monitors =
    [new("primary", new ScreenArea(0, 0, 1920, 1040), true), new("secondary", new ScreenArea(-1920, -200, 1920, 1080))];

    [Theory]
    [InlineData(double.NaN, 50, "primary")]
    [InlineData(double.PositiveInfinity, 20, "primary")]
    [InlineData(6000, -5000, "primary")]
    [InlineData(-800, 200, "disconnected")]
    public void InvalidPlacementRecoversOnPrimaryRight(double left, double top, string monitor)
    {
        var recovered = OrbPlacementRecovery.Resolve(new(left, top, monitor), Monitors, 68);
        Assert.Equal(new BubblePlacement(1852, 486, "primary", SnapEdge.Right), recovered);
    }

    [Fact]
    public void SemiHiddenAndNegativeMonitorPlacementsAreStillReachable()
    {
        Assert.False(OrbPlacementRecovery.NeedsRecovery(new(1880, 300, "primary", SnapEdge.Right), Monitors, 68));
        var saved = new BubblePlacement(-900, 200, "secondary");
        Assert.Equal(saved, OrbPlacementRecovery.Resolve(saved, Monitors, 68));
        Assert.Equal("primary", OrbPlacementRecovery.Resolve(saved, Monitors[..1], 68).MonitorName);
    }

    [Fact]
    public void StartupCommandQuotesPublishedBinaryAndRejectsAmbiguousCommands()
    {
        var path = Path.Combine(Path.GetTempPath(), "folder with spaces", "OrbLink.exe");
        Assert.Equal($"\"{path}\"", StartupManager.BuildCommand(path));
        Assert.Throws<InvalidOperationException>(() => StartupManager.BuildCommand("OrbLink.exe"));
        Assert.Throws<InvalidOperationException>(() => StartupManager.BuildCommand(Path.Combine(Path.GetTempPath(), "dotnet.exe")));
        Assert.Throws<InvalidOperationException>(() => StartupManager.BuildCommand(path + "\" --argument"));
    }

    [Fact]
    public async Task CorruptedCertificateDoesNotOverwriteExistingIdentityOrPassword()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        var service = new CertificateService(fixture.Paths, fixture.Store, new TestProtector());
        using var certificate = await service.GetOrCreateAsync();
        var identity = await fixture.Store.GetOrCreateIdentityAsync();
        var password = await fixture.Store.GetSettingAsync("CertificatePassword");
        await File.WriteAllBytesAsync(fixture.Paths.Certificate, [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetOrCreateAsync());
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(fixture.Paths.Certificate));
        Assert.Equal(password, await fixture.Store.GetSettingAsync("CertificatePassword"));
        Assert.Equal(identity, await fixture.Store.GetOrCreateIdentityAsync());
    }

    [Fact]
    public async Task LogsRedactSecretsOmitExceptionContentsRotateAndExpire()
    {
        using var fixture = new TestStorage();
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var today = Path.Combine(fixture.Paths.Logs, $"orblink-{DateTime.UtcNow:yyyy-MM-dd}.log");
        await using (var file = File.Create(today)) file.SetLength(8 * 1024 * 1024);
        var old = Path.Combine(fixture.Paths.Logs, "orblink-2000-01-01.log");
        await File.WriteAllTextAsync(old, "old"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-20));
        var log = new LocalLog(fixture.Paths);
        await log.WriteAsync("WARN", $"ProtectedSendToken={token}\npeer online", new IOException("PRIVATE_CLIPBOARD " + token));
        var text = await File.ReadAllTextAsync(today);
        Assert.DoesNotContain(token, text); Assert.DoesNotContain("PRIVATE_CLIPBOARD", text);
        Assert.Contains("[redacted]", text); Assert.Contains("IOException", text);
        Assert.True(File.Exists(today + ".1")); Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task LegacyMigrationPreservesPeerInsteadOfDeletingIt()
    {
        using var fixture = new TestStorage();
        var id = Guid.NewGuid();
        await using (var connection = new SqliteConnection($"Data Source={fixture.Paths.Database}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Peers (DeviceId TEXT PRIMARY KEY, Name TEXT, Address TEXT, Port INTEGER,
                    CertificateFingerprint TEXT, ProtectedSendToken TEXT, LastSeenUtc TEXT);
                INSERT INTO Peers VALUES ($id, 'legacy fixture', '127.0.0.1', 45832, 'ABC', $token, $seen);
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$token", new TestProtector().Protect("legacy-synthetic"));
            command.Parameters.AddWithValue("$seen", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        await fixture.Store.InitializeAsync();
        var peer = Assert.Single(await fixture.Store.GetPeersAsync());
        Assert.Equal(id, peer.Id); Assert.Equal("legacy-synthetic", peer.SendToken); Assert.Empty(peer.ReceiveToken);
        var result = await new PeerProbeClient(new DeviceIdentity(Guid.NewGuid(), "local"), new NetworkOptions(), new LocalLog(fixture.Paths)).ProbeAsync(peer);
        Assert.Equal(PeerProbeFailure.AuthenticationFailed, result.Failure);
    }

    [Fact]
    public async Task CorruptPeerCredentialDoesNotHideHealthyPeersOrEraseProtectedValue()
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        var peer = new PairedDevice(Guid.NewGuid(), "healthy", "127.0.0.1", 45832, new string('A', 64), "send", "receive", DateTimeOffset.UtcNow);
        await fixture.Store.UpsertPeerAsync(peer);
        var invalid = peer with { Id = Guid.NewGuid(), Name = "damaged" }; await fixture.Store.UpsertPeerAsync(invalid);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Paths.Database}"))
        {
            await connection.OpenAsync(); var command = connection.CreateCommand();
            command.CommandText = "UPDATE Peers SET ProtectedReceiveToken='not-base64!' WHERE DeviceId=$id";
            command.Parameters.AddWithValue("$id", invalid.Id.ToString()); await command.ExecuteNonQueryAsync();
        }
        var peers = await fixture.Store.GetPeersAsync(); Assert.Equal(2, peers.Count);
        Assert.Equal(peer, peers.Single(p => p.Id == peer.Id)); Assert.Empty(peers.Single(p => p.Id == invalid.Id).ReceiveToken);
        await fixture.Store.InitializeAsync(); Assert.Equal(2, (await fixture.Store.GetPeersAsync()).Count);
    }

    [Fact]
    public Task NamedMutexPreventsSecondOwnerAndCanBeAcquiredAfterRelease() => Task.Run(() =>
    {
        var name = "Local\\OrbLink.Test." + Guid.NewGuid().ToString("N");
        using var first = new Mutex(true, name);
        var blocked = false;
        var thread = new Thread(() => { using var second = new Mutex(false, name); blocked = !second.WaitOne(0); if (!blocked) second.ReleaseMutex(); });
        thread.IsBackground = true;
        try { thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(3))); Assert.True(blocked); }
        finally { first.ReleaseMutex(); }
        using var next = new Mutex(false, name);
        Assert.True(next.WaitOne(0)); next.ReleaseMutex();
    });
}
