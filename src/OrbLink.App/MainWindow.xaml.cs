using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrbLink.Core;
using OrbLink.Network;
using OrbLink.Storage;
using OrbLink.App.Services;

namespace OrbLink.App;

public partial class MainWindow : Window
{
    private readonly SqliteStore _store;
    private readonly NetworkCoordinator _network;
    private readonly AppearanceService _appearance;
    private readonly ClipboardImageSender _imageSender;
    private readonly ObservableCollection<InboxRow> _items = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private Point _dragStart;
    private InboxRow? _dragItem;
    private bool _dragging;
    private bool _sending;
    private bool _childOpen;
    private bool _reloading;
    private bool _closing;

    public MainWindow(SqliteStore store, NetworkCoordinator network, AppearanceService appearance, ClipboardImageSender imageSender)
    {
        InitializeComponent();
        _store = store;
        _network = network;
        _appearance = appearance;
        _imageSender = imageSender;
        InboxList.ItemsSource = _items;
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            async (_, _) => await UpdateConnectionAsync(), Dispatcher);
        Loaded += async (_, _) => { await ReloadAsync(); await UpdateConnectionAsync(); Focus(); };
        Closed += (_, _) => { _statusTimer.Stop(); _lifetime.Cancel(); };
    }

    public void ShowNear(Window orb)
    {
        ScreenGeometry.PlacePanel(this, orb);
        Show();
        ScreenGeometry.PlacePanel(this, orb);
        Activate();
        Focus();
    }

    public void Reload() => _ = ReloadAsync();

    public async Task ReloadAsync()
    {
        if (_reloading) return;
        _reloading = true;
        try
        {
            var transfers = await _store.GetRecentTransfersAsync(50);
            if (_lifetime.IsCancellationRequested) return;
            _items.Clear();
            foreach (var record in transfers.Where(item => item.Direction == TransferDirection.Received &&
                item.Status == TransferStatus.Completed && item.LocalPath is not null && File.Exists(item.LocalPath)))
                _items.Add(new InboxRow(record));
            EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ItemCount.Text = _items.Count == 0 ? "" : _items.Count.ToString();
        }
        catch (Exception ex) { ShowFeedback($"Não foi possível carregar os recebidos: {ex.Message}"); }
        finally { _reloading = false; }
    }

    private async Task UpdateConnectionAsync()
    {
        try
        {
            var peers = await _network.GetOnlinePeersAsync();
            if (_lifetime.IsCancellationRequested) return;
            ConnectionText.Text = peers.Count switch
            {
                0 => "Nenhum computador online", 1 => "1 computador online",
                _ => $"{peers.Count} computadores online"
            };
            ConnectionDot.SetResourceReference(ShapeFillProperty, peers.Count > 0 ? "OnlineBrush" : "SubtleBrush");
        }
        catch { ConnectionText.Text = "Rede indisponível"; }
    }

    private static readonly DependencyProperty ShapeFillProperty = System.Windows.Shapes.Shape.FillProperty;

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_sending) { Close(); e.Handled = true; return; }
        if (!PanelShortcuts.ShouldPasteImage(e.Key, Keyboard.Modifiers, IsActive, _childOpen)) return;
        e.Handled = true;
        await PasteAndSendImageAsync();
    }

    private async void OnPasteImage(object sender, RoutedEventArgs e) => await PasteAndSendImageAsync();

    public async Task PasteAndSendImageAsync()
    {
        if (_sending) { ShowFeedback("Uma imagem já está sendo enviada."); return; }
        BitmapSource? image;
        try { image = ClipboardImages.ReadCurrentImage(); }
        catch (ExternalException) { ShowFeedback("A área de transferência está ocupada. Tente novamente."); return; }
        catch (Exception) { ShowFeedback("Não foi possível ler essa imagem. Copie uma nova captura."); return; }
        if (image is null)
        {
            ShowFeedback("Nenhuma imagem encontrada na área de transferência");
            return;
        }
        _sending = true;
        PasteButton.IsEnabled = false;
        PastePreview.Source = image;
        PastePreview.Visibility = Visibility.Visible;
        SendProgress.Visibility = Visibility.Visible;
        SendProgress.Value = 0;
        ShowFeedback("Imagem capturada · enviando…");
        try
        {
            var progress = new Progress<double>(value =>
            {
                SendProgress.Value = value * 100;
                ShowFeedback($"Enviando imagem · {value:P0}");
            });
            await _imageSender.SendAsync(image, progress, _lifetime.Token);
            ShowFeedback("Imagem enviada");
        }
        catch (OperationCanceledException) { ShowFeedback("Envio cancelado."); }
        catch (Exception ex) { ShowFeedback(ex.Message); }
        finally
        {
            _sending = false;
            PasteButton.IsEnabled = true;
            SendProgress.Visibility = Visibility.Collapsed;
            if (!IsActive && !_childOpen) Close();
        }
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) { _dragItem = null; return; }
        _dragStart = e.GetPosition(InboxList);
        _dragItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as InboxRow;
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _dragging) return;
        var current = e.GetPosition(InboxList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        StartDrag(_dragItem);
        _dragItem = null;
    }

    private void OnDragButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(InboxList);
        _dragItem = (sender as FrameworkElement)?.DataContext as InboxRow;
    }
    private void OnDragButtonMove(object sender, MouseEventArgs e) => OnItemMouseMove(sender, e);

    private void StartDrag(InboxRow row)
    {
        if (row.Record.LocalPath is null) return;
        _dragging = true;
        try { NativeFileDrag.Start(InboxList, row.Record.LocalPath); }
        catch (Exception ex) { ShowFeedback(ex.Message); }
        finally
        {
            _dragging = false;
            if (!IsActive && !_childOpen && !_sending) Dispatcher.BeginInvoke(Close);
        }
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InboxRow row || row.Record.LocalPath is null) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (row.Record.Kind == TransferKind.Image)
                    ClipboardImages.CopyImage(LoadBitmap(row.Record.LocalPath));
                else if (row.Record.Kind == TransferKind.Text)
                    Clipboard.SetText(await File.ReadAllTextAsync(row.Record.LocalPath));
                else Clipboard.SetDataObject(NativeFileDrag.CreateDataObject(row.Record.LocalPath), true);
                ShowFeedback(row.Record.Kind == TransferKind.Image ? "Imagem copiada · use Ctrl+V" : "Item copiado");
                return;
            }
            catch (ExternalException) when (attempt < 2) { await Task.Delay(80); }
            catch (Exception ex) { ShowFeedback($"Não foi possível copiar: {ex.Message}"); return; }
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InboxRow row || row.Record.LocalPath is null) return;
        try { Process.Start(new ProcessStartInfo(row.Record.LocalPath) { UseShellExecute = true }); }
        catch (Exception ex) { ShowFeedback($"Não foi possível abrir: {ex.Message}"); }
    }

    private void ShowChild(Window window)
    {
        _childOpen = true;
        try
        {
            window.Owner = this;
            window.Topmost = true;
            window.ShowDialog();
        }
        finally { _childOpen = false; Activate(); Focus(); }
    }
    private void OnDevices(object sender, RoutedEventArgs e) => ShowChild(new DevicesWindow(_network, _store));
    private void OnHistory(object sender, RoutedEventArgs e) => ShowChild(new HistoryWindow(_store));
    private void OnSettings(object sender, RoutedEventArgs e) => ShowChild(new SettingsWindow(_appearance, _network));
    private void OnSendText(object sender, RoutedEventArgs e) => ShowChild(new TextSendWindow(_network));
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // WM_ACTIVATE can raise Deactivated from inside Window.Close().
        _closing = true;
        base.OnClosing(e);
        if (e.Cancel) _closing = false;
    }

    internal void OnDeactivated(object sender, EventArgs e)
    {
        // OLE runs a nested message loop. Keep the drag source alive until the drop ends.
        if (!_closing && !_lifetime.IsCancellationRequested && !_dragging && !_childOpen && !_sending) Close();
    }
    public void ShowFeedback(string text) => FeedbackText.Text = text;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T result) return result;
            current = current is Visual ? VisualTreeHelper.GetParent(current) :
                LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    public static BitmapSource LoadBitmap(string path, int? thumbnailWidth = null)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (thumbnailWidth is not null) image.DecodePixelWidth = thumbnailWidth.Value;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public sealed class InboxRow
    {
        public InboxRow(TransferRecord record)
        {
            Record = record;
            var extension = Path.GetExtension(record.Name).ToLowerInvariant();
            Glyph = record.Kind == TransferKind.Image ? "\uE91B" :
                record.Kind == TransferKind.Text ? "\uE8A5" : extension switch
                {
                    ".pdf" => "\uEA90", ".zip" or ".rar" => "\uF012",
                    ".xlsx" or ".xls" => "\uE9F9", ".mp4" or ".avi" => "\uE714", _ => "\uE8A5"
                };
            Origin = $"De {record.RemoteDeviceName}";
            var local = record.CreatedUtc.ToLocalTime();
            var date = local.Date == DateTime.Today ? $"Hoje, {local:HH:mm}" : local.ToString("dd/MM, HH:mm");
            Details = $"{FormatSize(record.Size)}   ·   {date}";
            CopyLabel = record.Kind switch { TransferKind.Image => "Copiar imagem", TransferKind.Text => "Copiar texto", _ => "Copiar arquivo" };
            if (record.Kind == TransferKind.Image && record.LocalPath is not null)
                try { Thumbnail = LoadBitmap(record.LocalPath, 96); } catch { }
        }
        public TransferRecord Record { get; }
        public string Glyph { get; }
        public string Origin { get; }
        public string Details { get; }
        public string CopyLabel { get; }
        public ImageSource? Thumbnail { get; }
        public static string FormatSize(long size) => size >= 1024 * 1024 * 1024 ? $"{size / (1024d * 1024 * 1024):0.#} GB" :
            size >= 1024 * 1024 ? $"{size / (1024d * 1024):0.#} MB" : size >= 1024 ? $"{size / 1024d:0.#} KB" : $"{size} B";
    }
}
