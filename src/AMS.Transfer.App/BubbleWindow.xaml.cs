using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;
using AMS.Transfer.App.Services;
using Forms = System.Windows.Forms;

namespace AMS.Transfer.App;

public partial class BubbleWindow : Window
{
    private readonly SqliteStore _store;
    private readonly NetworkCoordinator _network;
    private readonly LocalLog _log;
    private readonly AppearanceService _appearance;
    private readonly ClipboardImageSender _imageSender;
    private readonly DispatcherTimer _onlineTimer;
    private readonly DispatcherTimer _hideTimer;
    private readonly Dictionary<Guid, TransferActivity> _receiving = [];
    private MainWindow? _tray;
    private Point _mouseStart;
    private Point _dragOriginPixels;
    private bool _moving;
    private bool _dragStarted;
    private bool _expandedForDrop;
    private bool _sending;
    private bool _closed;
    private BubblePlacement? _placement;
    private int _unread;
    private double OrbSize => _appearance.Current.OrbSize;

    public BubbleWindow(SqliteStore store, NetworkCoordinator network, LocalLog log,
        AppearanceService appearance, ClipboardImageSender imageSender)
    {
        InitializeComponent();
        _store = store; _network = network; _log = log;
        _appearance = appearance; _imageSender = imageSender;
        _network.ItemReceived += OnItemReceivedAsync;
        _network.ReceiveActivity += OnReceiveActivity;
        _appearance.Changed += OnAppearanceChanged;
        _onlineTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            async (_, _) => await UpdateOnlineState(), Dispatcher);
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hideTimer.Tick += (_, _) => HidePartially();
        Loaded += OnLoaded;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await RecoverPositionAsync()));
        Closed += (_, _) =>
        {
            _closed = true;
            _onlineTimer.Stop(); _hideTimer.Stop();
            _network.ItemReceived -= OnItemReceivedAsync;
            _network.ReceiveActivity -= OnReceiveActivity;
            _appearance.Changed -= OnAppearanceChanged;
            _tray?.Close();
        };
        ApplyAppearance();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _placement = await _store.GetBubblePlacementAsync();
            _placement = OrbPlacementRecovery.Resolve(_placement, ScreenGeometry.Monitors(this), OrbSize);
            Left = _placement.Left; Top = _placement.Top;
            await ClampAndSnapAsync(false, true);
            await UpdateOnlineState();
        }
        catch (Exception ex)
        {
            var primary = ScreenGeometry.WorkArea(this, Forms.Screen.PrimaryScreen);
            Left = Math.Max(primary.Left, primary.Right - OrbSize); Top = primary.Top + Math.Max(0, (primary.Height - OrbSize) / 2);
            await _log.WriteAsync("ERROR", "Falha ao restaurar a orb; posição principal recuperada.", ex);
        }
        finally { if (!_closed) _onlineTimer.Start(); }
    }

    public async Task RecoverPositionAsync()
    {
        if (_closed || !IsLoaded || _dragStarted || _expandedForDrop) return;
        try
        {
            var current = new BubblePlacement(Left, Top, _placement?.MonitorName);
            var monitors = ScreenGeometry.Monitors(this);
            if (!OrbPlacementRecovery.NeedsRecovery(current, monitors, OrbSize)) return;
            _placement = OrbPlacementRecovery.Resolve(null, monitors, OrbSize);
            StopPositionAnimation(); Left = _placement.Left; Top = _placement.Top;
            await ClampAndSnapAsync(false, true);
            await _log.WriteAsync("INFO", "Orb recuperada no monitor principal.");
        }
        catch (Exception ex) { await _log.WriteAsync("WARN", "Falha ao recuperar monitor da orb.", ex); }
    }

    public void ToggleTray()
    {
        if (_tray is { IsVisible: true }) { _tray.Close(); return; }
        _hideTimer.Stop(); Reveal();
        _unread = 0; UpdateBadge();
        var tray = new MainWindow(_store, _network, _appearance, _imageSender);
        _tray = tray;
        tray.Closed += (_, _) =>
        {
            _tray = null;
            Halo.StrokeThickness = 1.5;
            ScheduleHide();
        };
        Halo.StrokeThickness = 3;
        tray.ShowNear(this);
    }

    private void ApplyAppearance()
    {
        if (!_expandedForDrop) { Width = OrbSize; Height = OrbSize; }
        OrbIcon.Source = _appearance.GetIcon();
        OrbIcon.Width = OrbIcon.Height = Math.Min(_appearance.Current.IconSize, OrbSize - 28);
        Bubble.BorderThickness = new Thickness(_appearance.Current.BorderEnabled ? 1 : 0);
        Glow.Color = (Color)ColorConverter.ConvertFromString(_appearance.Current.AccentColor);
        Glow.Opacity = _appearance.Current.ShadowIntensity;
    }

    private async void OnAppearanceChanged()
    {
        ApplyAppearance();
        if (IsLoaded) await ClampAndSnapAsync(true);
    }

    private async Task UpdateOnlineState()
    {
        await RecoverPositionAsync();
        try
        {
            var online = await _network.GetOnlinePeersAsync();
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, online.Count > 0 ? "OnlineBrush" : "SubtleBrush");
            ToolTip = online.Count switch
            {
                0 => "OrbLink · offline · clique para abrir",
                1 => $"OrbLink · {online[0].Name} online",
                _ => $"OrbLink · {online.Count} computadores online"
            };
        }
        catch { ToolTip = "OrbLink · rede indisponível"; }
    }

    private Task OnItemReceivedAsync(TransferRecord record) => Dispatcher.InvokeAsync(() =>
    {
        if (_tray is not { IsVisible: true }) _unread++;
        _hideTimer.Stop(); Reveal();
        UpdateBadge();
        if (_appearance.Current.SoundEnabled) System.Media.SystemSounds.Asterisk.Play();
        if (_appearance.Current.AnimationsEnabled)
        {
            var pulse = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(140))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        _tray?.Reload();
        _tray?.ShowFeedback($"Recebido: {record.Name}");
    }).Task;

    private void OnReceiveActivity(TransferActivity activity) => Dispatcher.InvokeAsync(() =>
    {
        if (activity.IsActive) _receiving[activity.Id] = activity;
        else _receiving.Remove(activity.Id);
        if (activity.IsActive) { _hideTimer.Stop(); Reveal(); }
        else ScheduleHide();
        if (_sending) return;
        ProgressLabel.Visibility = _receiving.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OrbIcon.Visibility = _receiving.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_receiving.Count > 0) ProgressLabel.Text = $"{_receiving.Values.Last().Fraction:P0}";
    });

    private void UpdateBadge()
    {
        Badge.Visibility = _unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = _unread > 99 ? "99+" : _unread.ToString();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _hideTimer.Stop();
        StopPositionAnimation();
        var cursor = Forms.Cursor.Position;
        _mouseStart = new Point(cursor.X, cursor.Y);
        _dragOriginPixels = PointToScreen(new Point(0, 0));
        _moving = false; _dragStarted = true;
        Root.CaptureMouse(); e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Root.IsMouseCaptured || !_dragStarted) return;
        var cursor = Forms.Cursor.Position;
        var dx = cursor.X - _mouseStart.X;
        var dy = cursor.Y - _mouseStart.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 4) _moving = true;
        if (_moving)
            ScreenGeometry.MoveInPixels(this, (int)(_dragOriginPixels.X + dx), (int)(_dragOriginPixels.Y + dy));
    }

    private async void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        Root.ReleaseMouseCapture();
        _dragStarted = false;
        if (_moving) await ClampAndSnapAsync(true); else ToggleTray();
        e.Handled = true;
    }

    private async Task ClampAndSnapAsync(bool animate, bool useSavedMonitor = false)
    {
        StopPositionAnimation();
        var screen = useSavedMonitor && !string.IsNullOrWhiteSpace(_placement?.MonitorName)
            ? Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName == _placement.MonitorName)
              ?? ScreenGeometry.ForWindow(this)
            : ScreenGeometry.ForWindow(this);
        var area = ScreenGeometry.WorkArea(this, screen);
        var snapped = SnapCalculator.Snap(Left, Top, OrbSize, OrbSize, area, screen.DeviceName);
        _placement = snapped;
        if (animate) AnimateTo(snapped.Left, snapped.Top); else { Left = snapped.Left; Top = snapped.Top; }
        try { await _store.SaveBubblePlacementAsync(snapped); }
        catch (Exception ex) { await _log.WriteAsync("WARN", "Não foi possível salvar a posição da orb.", ex); }
        ScheduleHide();
    }

    private void StopPositionAnimation()
    {
        var left = Left; var top = Top;
        BeginAnimation(LeftProperty, null); BeginAnimation(TopProperty, null);
        Left = left; Top = top;
    }

    private void AnimateTo(double left, double top)
    {
        var fromLeft = Left; var fromTop = Top;
        StopPositionAnimation();
        Left = left; Top = top;
        if (!_appearance.Current.AnimationsEnabled) return;
        var duration = TimeSpan.FromMilliseconds(150);
        // Set the base values first: cancelled animations cannot leave stale coordinates.
        BeginAnimation(LeftProperty, new DoubleAnimation(fromLeft, left, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        BeginAnimation(TopProperty, new DoubleAnimation(fromTop, top, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void ScheduleHide()
    {
        _hideTimer.Stop();
        if (_appearance.Current.AutoHide && _placement?.Edge is SnapEdge.Left or SnapEdge.Right) _hideTimer.Start();
    }

    private void HidePartially()
    {
        _hideTimer.Stop();
        if (_placement is null || IsMouseOver || _tray is { IsVisible: true } || _expandedForDrop || _sending || _unread > 0 || _receiving.Count > 0) return;
        if (_placement.Edge is not (SnapEdge.Left or SnapEdge.Right)) return;
        AnimateTo(_placement.Left + (_placement.Edge == SnapEdge.Left ? -1 : 1) * OrbSize * 0.42, _placement.Top);
    }

    private void Reveal()
    {
        if (_placement is not null && !_dragStarted && !_expandedForDrop) AnimateTo(_placement.Left, _placement.Top);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop(); Reveal();
        if (_appearance.Current.AnimationsEnabled)
        {
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(100)));
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(100)));
        }
    }
    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ScheduleHide();
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || _sending) return;
        _hideTimer.Stop(); Reveal();
        e.Effects = DragDropEffects.Copy; SetDropExpanded(true);
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_sending ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDragLeave(object sender, DragEventArgs e) => SetDropExpanded(false);

    private async void OnDrop(object sender, DragEventArgs e)
    {
        SetDropExpanded(false); e.Handled = true;
        if (_sending || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        _sending = true;
        try
        {
            var peers = await _network.GetAvailablePeersForSendAsync();
            if (peers.Count == 0)
            {
                MessageBox.Show("Nenhum computador pareado está online. Abra Computadores para conectar.", "OrbLink",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            foreach (var path in paths.Where(File.Exists))
            {
                try
                {
                    ProgressLabel.Text = "0%"; ProgressLabel.Visibility = Visibility.Visible; OrbIcon.Visibility = Visibility.Collapsed;
                    var progress = new Progress<double>(value => ProgressLabel.Text = $"{value:P0}");
                    await _network.SendFileAsync(peers[0], path, progress);
                    _tray?.ShowFeedback($"Enviado: {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    await _log.WriteAsync("ERROR", "Falha no envio pela orb.", ex);
                    MessageBox.Show($"Não foi possível enviar o arquivo.\n\n{ex.Message}", "OrbLink", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            await _log.WriteAsync("ERROR", "Falha ao verificar computadores para envio.", ex);
            MessageBox.Show("Não foi possível verificar os computadores. Tente Atualizar em Computadores.", "OrbLink",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _sending = false;
            ProgressLabel.Visibility = _receiving.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OrbIcon.Visibility = _receiving.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_receiving.Count > 0) ProgressLabel.Text = $"{_receiving.Values.Last().Fraction:P0}";
            ScheduleHide();
        }
    }

    private void SetDropExpanded(bool expanded)
    {
        if (_expandedForDrop == expanded) return;
        StopPositionAnimation();
        var centerX = Left + Width / 2; var centerY = Top + Height / 2;
        _expandedForDrop = expanded;
        Width = expanded ? 146 : OrbSize; Height = expanded ? 86 : OrbSize;
        Left = centerX - Width / 2; Top = centerY - Height / 2;
        Bubble.CornerRadius = new CornerRadius(expanded ? 22 : 100);
        DropContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        OrbIcon.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        StatusDot.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        if (!expanded) ScheduleHide();
    }
}
