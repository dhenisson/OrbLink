using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AMS.Transfer.App.Services;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using Microsoft.Win32;

namespace AMS.Transfer.App;

public partial class SettingsWindow : Window
{
    private readonly AppearanceService _appearance;
    private readonly NetworkCoordinator _network;
    private string? _pendingIcon;
    private bool _ready;

    public SettingsWindow(AppearanceService appearance, NetworkCoordinator network)
    {
        InitializeComponent();
        _appearance = appearance;
        _network = network;
        var settings = appearance.Current;
        _pendingIcon = settings.CustomIconFile;
        OrbSizeSlider.Value = settings.OrbSize;
        IconSizeSlider.Value = settings.IconSize;
        OpacitySlider.Value = settings.PanelOpacity;
        ShadowSlider.Value = settings.ShadowIntensity;
        AccentText.Text = settings.AccentColor;
        BackgroundText.Text = settings.BackgroundColor;
        BorderCheck.IsChecked = settings.BorderEnabled;
        AutoHideCheck.IsChecked = settings.AutoHide;
        AnimationsCheck.IsChecked = settings.AnimationsEnabled;
        SoundCheck.IsChecked = settings.SoundEnabled;
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        LocalDeviceText.Text = network.Identity.Name;
        Sections.SelectedIndex = 1;
        _ready = true;
        UpdatePreview();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppearanceSection is null) return;
        var sections = new[] { GeneralSection, AppearanceSection, ShortcutsSection, DevicesSection, AboutSection };
        for (var index = 0; index < sections.Length; index++)
            sections[index].Visibility = Sections.SelectedIndex == index ? Visibility.Visible : Visibility.Collapsed;
    }

    private AppearanceSettings ReadSettings() => new()
    {
        OrbSize = OrbSizeSlider.Value, IconSize = IconSizeSlider.Value,
        AccentColor = AccentText.Text, BackgroundColor = BackgroundText.Text,
        PanelOpacity = OpacitySlider.Value, ShadowIntensity = ShadowSlider.Value,
        BorderEnabled = BorderCheck.IsChecked == true, AutoHide = AutoHideCheck.IsChecked == true,
        AnimationsEnabled = AnimationsCheck.IsChecked == true, SoundEnabled = SoundCheck.IsChecked == true,
        CustomIconFile = _pendingIcon
    };

    private void UpdatePreview()
    {
        if (!_ready) return;
        var settings = ReadSettings().Normalize();
        PreviewOrb.Width = PreviewOrb.Height = Math.Min(settings.OrbSize, 84);
        PreviewOrb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.BackgroundColor));
        PreviewOrb.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.AccentColor));
        PreviewOrb.BorderThickness = new Thickness(settings.BorderEnabled ? 1 : 0);
        PreviewShadow.Color = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
        PreviewShadow.Opacity = settings.ShadowIntensity;
        PreviewIcon.Width = PreviewIcon.Height = Math.Min(settings.IconSize, settings.OrbSize - 28);
        PreviewIcon.Source = _appearance.Icons.TryLoad(_pendingIcon) ?? (ImageSource)FindResource("OrbLinkLogo");
        IconDescription.Text = _pendingIcon is null ? "Ícone padrão do OrbLink" : "Ícone personalizado";
    }

    private async void OnChooseIcon(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Escolher ícone da orb", Filter = "Ícones e imagens|*.png;*.ico|PNG|*.png|ICO|*.ico" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _pendingIcon = await _appearance.Icons.ImportAsync(dialog.FileName);
            StatusText.Text = "Ícone pronto. Salve para aplicar à orb.";
        }
        catch (Exception ex)
        {
            _pendingIcon = null;
            StatusText.Text = $"Ícone inválido. Usando o padrão. {ex.Message}";
        }
        UpdatePreview();
    }

    private void OnResetIcon(object sender, RoutedEventArgs e)
    {
        _pendingIcon = null;
        UpdatePreview();
        StatusText.Text = "Ícone padrão selecionado. Salve para aplicar.";
    }
    private void OnAccent(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) AccentText.Text = color;
    }
    private void OnPreviewChanged(object sender, RoutedEventArgs e) => UpdatePreview();
    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            var settings = ReadSettings();
            var normalized = settings.Normalize();
            if (!string.Equals(settings.AccentColor, normalized.AccentColor, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(settings.BackgroundColor, normalized.BackgroundColor, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "Use cores no formato #RRGGBB, por exemplo #55A5FF.";
                return;
            }
            await _appearance.SaveAsync(settings);
            if (StartupCheck.IsChecked == true || StartupManager.IsEnabled())
                StartupManager.SetEnabled(StartupCheck.IsChecked == true);
            StatusText.Text = "Salvo. Sua aparência será mantida ao reiniciar.";
        }
        catch (Exception ex) { StatusText.Text = $"Não foi possível salvar: {ex.Message}"; }
        finally { SaveButton.IsEnabled = true; }
    }
    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is Button) return;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnDevices(object sender, RoutedEventArgs e) => new DevicesWindow(_network, ((App)Application.Current).Store)
        { Owner = this, Topmost = true }.ShowDialog();
}
