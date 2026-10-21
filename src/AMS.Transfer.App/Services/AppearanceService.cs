using System.Windows;
using System.Windows.Media;
using AMS.Transfer.Core;
using AMS.Transfer.Storage;

namespace AMS.Transfer.App.Services;

public sealed class AppearanceService(AppPaths paths, SqliteStore store)
{
    private readonly AppearanceStore _store = new(store);
    public IconStore Icons { get; } = new(paths);
    public AppearanceSettings Current { get; private set; } = new();
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        Current = await _store.LoadAsync();
        ApplyResources();
    }

    public async Task SaveAsync(AppearanceSettings settings)
    {
        var normalized = settings.Normalize();
        await _store.SaveAsync(normalized);
        Current = normalized;
        ApplyResources();
        Changed?.Invoke();
    }

    public ImageSource GetIcon() =>
        Icons.TryLoad(Current.CustomIconFile) ??
        (ImageSource)Application.Current.FindResource("OrbLinkLogo");

    private void ApplyResources()
    {
        if (Application.Current is null) return;
        var accent = (Color)ColorConverter.ConvertFromString(Current.AccentColor);
        Application.Current.Resources["AccentColor"] = accent;
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
        Application.Current.Resources["AccentSoftBrush"] = new SolidColorBrush(
            Color.FromArgb(30, accent.R, accent.G, accent.B));
        Application.Current.Resources["OrbBackgroundBrush"] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(Current.BackgroundColor));
        Application.Current.Resources["PanelBrush"] = new SolidColorBrush(
            Color.FromArgb((byte)Math.Round(Current.PanelOpacity * 255), 16, 24, 36));
    }
}
