using AMS.Transfer.Core;
using AMS.Transfer.Storage;
using Microsoft.Data.Sqlite;

namespace AMS.Transfer.Tests;

public sealed class AppearanceTests
{
    [Fact]
    public void InvalidValuesUseSafeDefaultsAndLimits()
    {
        var value = new AppearanceSettings
        {
            OrbSize = double.NaN, IconSize = 900, PanelOpacity = -1,
            ShadowIntensity = double.PositiveInfinity, AccentColor = "red",
            BackgroundColor = "#aabbcc", CustomIconFile = "../escape.png"
        }.Normalize();
        Assert.Equal(68, value.OrbSize);
        Assert.Equal(56, value.IconSize);
        Assert.Equal(0.82, value.PanelOpacity);
        Assert.Equal(0.5, value.ShadowIntensity);
        Assert.Equal("#55A5FF", value.AccentColor);
        Assert.Equal("#AABBCC", value.BackgroundColor);
        Assert.Null(value.CustomIconFile);
    }

    [Fact]
    public async Task PersistsAppearanceWithoutChangingV1IdentityPeersAndPlacement()
    {
        using var fixture = new TestStorage();
        var store = fixture.Store;
        await store.InitializeAsync();
        var identity = await store.GetOrCreateIdentityAsync();
        var position = new BubblePlacement(-900, 100, "monitor2", SnapEdge.Left);
        await store.SaveBubblePlacementAsync(position);
        var peer = new PairedDevice(Guid.NewGuid(), "PC 2", "10.0.0.2", 45832, "fingerprint", "send", "receive", DateTimeOffset.UtcNow);
        await store.UpsertPeerAsync(peer);
        var settings = new AppearanceSettings { CustomIconFile = "personal.png", OrbSize = 80, AccentColor = "#9785FF", SoundEnabled = true };
        await new AppearanceStore(store).SaveAsync(settings);
        var reopened = new SqliteStore(fixture.Paths, new TestProtector());
        await reopened.InitializeAsync();
        Assert.Equal(settings, await new AppearanceStore(reopened).LoadAsync());
        Assert.Equal(identity, await reopened.GetOrCreateIdentityAsync());
        Assert.Equal(position, await reopened.GetBubblePlacementAsync());
        Assert.Equal(peer, await reopened.GetPeerAsync(peer.Id));
        await reopened.SetSettingAsync(AppearanceStore.SettingKey, "{broken");
        Assert.Equal(new AppearanceSettings(), await new AppearanceStore(reopened).LoadAsync());
    }

    [Theory]
    [InlineData(1850, 1040, 0, 0, 1920, 1080)]
    [InlineData(-1910, -190, -1920, -200, 1920, 1080)]
    [InlineData(250, 190, 0, 0, 300, 240)]
    [InlineData(100, 100, 0, 0, 768, 600)]
    public void PanelAlwaysFitsCurrentMonitor(double orbX, double orbY, double x, double y, double width, double height)
    {
        var area = new ScreenArea(x, y, width, height);
        var panel = PanelPlacement.NearOrb(orbX, orbY, 68, 68, 414, 570, area);
        Assert.InRange(panel.Left, area.Left, area.Right - panel.Width);
        Assert.InRange(panel.Top, area.Top, area.Bottom - panel.Height);
        Assert.True(panel.Width <= area.Width && panel.Height <= area.Height);
    }
}

internal sealed class TestProtector : ISecretProtector
{
    public string Protect(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
    public string Unprotect(string value) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

internal sealed class TestStorage : IDisposable
{
    public AppPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "orblink-test-" + Guid.NewGuid().ToString("N")));
    public SqliteStore Store { get; }
    public TestStorage() => Store = new(Paths, new TestProtector());
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Paths.Root, true);
    }
}
