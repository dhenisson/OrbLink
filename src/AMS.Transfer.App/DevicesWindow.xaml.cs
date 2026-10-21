using System.Collections.ObjectModel;
using System.Windows;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;

namespace AMS.Transfer.App;

public partial class DevicesWindow : Window
{
    private readonly NetworkCoordinator _network;
    private readonly SqliteStore _store;
    private readonly ObservableCollection<DeviceRow> _rows = [];
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading;
    private bool _refreshing;
    private readonly CancellationTokenSource _lifetime = new();

    public DevicesWindow(NetworkCoordinator network, SqliteStore store)
    {
        InitializeComponent();
        _network = network;
        _store = store;
        DevicesList.ItemsSource = _rows;
        Loaded += async (_, _) => { await RefreshPresenceAsync(); if (!_lifetime.IsCancellationRequested) _timer.Start(); };
        _timer.Tick += async (_, _) => { if (!_refreshing) await ReloadAsync(); };
        Closed += (_, _) => { _timer.Stop(); _lifetime.Cancel(); };
    }

    private async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var selectedId = (DevicesList.SelectedItem as DeviceRow)?.Device.Id;
            var peers = await _store.GetPeersAsync();
            var onlineIds = (await _network.GetOnlinePeersAsync()).Select(peer => peer.Id).ToHashSet();
            var peerIds = peers.Select(peer => peer.Id).ToHashSet();
            _rows.Clear();
            foreach (var peer in peers)
                _rows.Add(new DeviceRow(new DiscoveredDevice(peer.Id, peer.Name, peer.Address, peer.Port,
                    peer.CertificateFingerprint, peer.LastSeenUtc), true, onlineIds.Contains(peer.Id),
                    !string.IsNullOrEmpty(peer.SendToken) && !string.IsNullOrEmpty(peer.ReceiveToken)));
            foreach (var device in _network.DiscoveredDevices.Where(item => !peerIds.Contains(item.Id)).OrderBy(item => item.Name))
                _rows.Add(new DeviceRow(device, false, DateTimeOffset.UtcNow - device.LastSeenUtc < TimeSpan.FromSeconds(8)));
            StatusText.Text = _rows.Count == 0 ? "Nenhum dispositivo encontrado" : $"{_rows.Count} encontrado(s)";
            DevicesList.SelectedItem = _rows.FirstOrDefault(row => row.Device.Id == selectedId);
        }
        catch { StatusText.Text = "Não foi possível atualizar. Tente novamente."; }
        finally { _loading = false; }
    }

    private async void OnPair(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not DeviceRow row || row.IsPaired) return;
        PairButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(text => StatusText.Text = text);
            await _network.PairAsync(row.Device, progress);
            StatusText.Text = "Pareamento concluído";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "OrbLink · Pareamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "Não foi possível parear";
        }
        finally { PairButton.IsEnabled = true; }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshPresenceAsync();

    private async Task RefreshPresenceAsync()
    {
        if (_refreshing) return;
        _refreshing = true; RefreshButton.IsEnabled = false;
        StatusText.Text = "Verificando computadores…";
        try
        {
            await _network.RefreshPeersAsync(_lifetime.Token);
            if (!_lifetime.IsCancellationRequested) await ReloadAsync();
        }
        catch (OperationCanceledException) { }
        catch { StatusText.Text = "Falha na verificação. Tente novamente."; }
        finally { _refreshing = false; RefreshButton.IsEnabled = true; }
    }

    internal sealed class DeviceRow
    {
        public DeviceRow(DiscoveredDevice device, bool isPaired, bool isOnline, bool hasCredentials = true)
        {
            Device = device;
            IsPaired = isPaired;
            Display = $"{device.Name}    {(isOnline ? "● Online" : "○ Offline")}{(isPaired ? "    Pareado" : string.Empty)}";
            if (!hasCredentials) Display += " · Credenciais indisponíveis; restaure um backup";
        }
        public DiscoveredDevice Device { get; }
        public bool IsPaired { get; }
        public string Display { get; }
    }
}
