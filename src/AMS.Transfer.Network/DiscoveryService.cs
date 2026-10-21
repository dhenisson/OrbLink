using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AMS.Transfer.Core;
using AMS.Transfer.Storage;

namespace AMS.Transfer.Network;

public sealed class DiscoveryService(
    DeviceIdentity identity,
    string certificateFingerprint,
    NetworkOptions options,
    LocalLog log) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Guid, DiscoveredDevice> _devices = new();
    private UdpClient? _receiver;
    private Task? _receiveTask;
    private Task? _broadcastTask;
    private readonly SemaphoreSlim _announceGate = new(1, 1);
    private string? _lastInterfaces;

    public event Action<DiscoveredDevice>? DeviceSeen;
    public IReadOnlyCollection<DiscoveredDevice> Devices => _devices.Values.ToArray();

    public void Start()
    {
        if (_receiveTask is not null) return;
        // Bind/rebind in the worker: an unavailable UDP socket must not prevent the visible app starting.
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_stop.Token));
        _broadcastTask = Task.Run(() => BroadcastLoopAsync(_stop.Token));
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await RefreshAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { await log.WriteAsync("WARN", "Discovery indisponível; nova tentativa no próximo ciclo.", ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        cancellationToken = lifetime.Token;
        await _announceGate.WaitAsync(cancellationToken);
        try
        {
            var announcement = new Announcement(
                "AMS_TRANSFER_V1", identity.Id, identity.Name, options.TransferPort, certificateFingerprint);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(announcement);
            var interfaces = LanInterfaceSelector.GetLocalAddresses();
            var signature = string.Join(", ", interfaces.Select(item => $"{item.Name} / {item.Address}"));
            if (signature != _lastInterfaces)
            {
                _lastInterfaces = signature;
                await log.WriteAsync("INFO", interfaces.Count == 0 ? "LAN discovery: no eligible physical RFC1918 interface; direct peer probes remain available."
                    : $"LAN discovery interfaces (priority order): {signature}");
            }
            foreach (var local in interfaces)
            {
                try
                {
                    // Bind explicitly: Windows must not choose Radmin/Tailscale as the source route.
                    using var sender = new UdpClient(new IPEndPoint(local.Address, 0)) { EnableBroadcast = true };
                    foreach (var target in new[] { LanInterfaceSelector.BroadcastAddress(local), IPAddress.Broadcast }.Distinct())
                        await sender.SendAsync(bytes, new IPEndPoint(target, options.DiscoveryPort), cancellationToken);
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        await log.WriteAsync("WARN", "Falha ao anunciar dispositivo na rede local.", ex);
                }
            }
        }
        finally { _announceGate.Release(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_receiver is null)
                {
                    _receiver = new UdpClient(AddressFamily.InterNetwork);
                    _receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _receiver.Client.ExclusiveAddressUse = false;
                    _receiver.Client.Bind(new IPEndPoint(IPAddress.Any, options.DiscoveryPort));
                    _receiver.EnableBroadcast = true;
                }
                var result = await _receiver.ReceiveAsync(cancellationToken);
                if (result.Buffer.Length > 4096) continue;
                var announcement = JsonSerializer.Deserialize<Announcement>(result.Buffer);
                if (announcement is null || announcement.Protocol != "AMS_TRANSFER_V1" || announcement.Id == identity.Id ||
                    announcement.Id == Guid.Empty || announcement.Port is < 1 or > 65535 ||
                    string.IsNullOrWhiteSpace(announcement.Name) || announcement.Name.Length > 128 || announcement.Name.Any(char.IsControl) ||
                    announcement.CertificateFingerprint?.Length != 64 || !announcement.CertificateFingerprint.All(Uri.IsHexDigit) ||
                    !LanInterfaceSelector.IsPrivateLanIPv4(result.RemoteEndPoint.Address))
                    continue;

                var device = new DiscoveredDevice(
                    announcement.Id, announcement.Name, result.RemoteEndPoint.Address.ToString(),
                    announcement.Port, announcement.CertificateFingerprint, DateTimeOffset.UtcNow);
                _devices[device.Id] = device;
                foreach (var item in _devices)
                    if (item.Value.LastSeenUtc < DateTimeOffset.UtcNow.AddSeconds(-30)) _devices.TryRemove(item.Key, out _);
                if (_devices.Count > 256) _devices.TryRemove(device.Id, out _);
                DeviceSeen?.Invoke(device);
            }
            catch (OperationCanceledException) { break; }
            catch (JsonException) { /* Untrusted UDP payload; do not flood the logs. */ }
            catch (Exception ex)
            {
                _receiver?.Dispose(); _receiver = null;
                if (cancellationToken.IsCancellationRequested) break;
                await log.WriteAsync("WARN", "Falha ao processar descoberta UDP.", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _receiver?.Dispose();
        var tasks = new[] { _receiveTask, _broadcastTask }.Where(task => task is not null).Cast<Task>();
        try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
        // Also wait for a manual Refresh to release its sender before disposing the gate.
        await _announceGate.WaitAsync();
        _receiver?.Dispose();
        _stop.Dispose();
        _announceGate.Dispose();
    }

    private sealed record Announcement(
        string Protocol, Guid Id, string Name, int Port, string CertificateFingerprint);
}
