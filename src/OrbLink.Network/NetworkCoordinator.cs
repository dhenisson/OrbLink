using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using OrbLink.Core;
using OrbLink.Storage;

namespace OrbLink.Network;

public sealed class NetworkCoordinator : IAsyncDisposable
{
    private readonly SqliteStore _store;
    private readonly AppPaths _paths;
    private readonly LocalLog _log;
    private readonly NetworkOptions _options;
    private readonly ConcurrentDictionary<Guid, DiscoveredDevice> _discovered = new();
    private DiscoveryService? _discovery;
    private TransferServer? _server;
    private TransferClient? _client;
    private PeerPresenceMonitor? _presence;
    private X509Certificate2? _certificate;

    public NetworkCoordinator(SqliteStore store, AppPaths paths, LocalLog log, NetworkOptions? options = null)
    {
        _store = store;
        _paths = paths;
        _log = log;
        _options = options ?? new NetworkOptions();
    }

    public DeviceIdentity Identity { get; private set; } = new(Guid.Empty, string.Empty);
    public IReadOnlyCollection<DiscoveredDevice> DiscoveredDevices => _discovered.Values.ToArray();
    public event Action? DevicesChanged;
    public event Func<IncomingPairRequest, Task>? PairingRequested;
    public event Func<TransferRecord, Task>? ItemReceived;
    public event Action<TransferActivity>? ReceiveActivity;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Identity = await _store.GetOrCreateIdentityAsync(cancellationToken);
        X509Certificate2 certificate = await new CertificateService(_paths, _store).GetOrCreateAsync(cancellationToken);
        _certificate = certificate;
        var fingerprint = CertificateService.GetFingerprint(certificate);
        _server = new TransferServer(Identity, certificate, _store, _paths, _options, _log);
        _server.PairingRequested += request => PairingRequested?.Invoke(request) ?? Task.CompletedTask;
        _server.ItemReceived += record => ItemReceived?.Invoke(record) ?? Task.CompletedTask;
        _server.ReceiveActivity += activity => ReceiveActivity?.Invoke(activity);
        await _server.StartAsync(cancellationToken);

        _client = new TransferClient(Identity, _options.TransferPort, fingerprint, _store, _log);
        var probe = new PeerProbeClient(Identity, _options, _log);
        _presence = new PeerPresenceMonitor(_store, _log, _options, probe.ProbeAsync);
        _presence.Changed += () => DevicesChanged?.Invoke();
        _presence.Start();
        if (_options.EnableDiscovery)
        {
            _discovery = new DiscoveryService(Identity, fingerprint, _options, _log);
            _discovery.DeviceSeen += OnDeviceSeen;
            _discovery.Start();
        }
    }

    private async void OnDeviceSeen(DiscoveredDevice device)
    {
        _discovered[device.Id] = device;
        foreach (var item in _discovered)
            if (item.Value.LastSeenUtc < DateTimeOffset.UtcNow.AddSeconds(-30)) _discovered.TryRemove(item.Key, out _);
        if (_discovered.Count > 256) _discovered.TryRemove(device.Id, out _);
        try
        {
            if (_presence is not null) await _presence.ObserveDiscoveryAsync(device);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await _log.WriteAsync("WARN", "Falha ao atualizar endereço do par.", ex); }
        DevicesChanged?.Invoke();
    }

    public Task<IReadOnlyList<PairedDevice>> GetOnlinePeersAsync(CancellationToken cancellationToken = default) =>
        _presence?.GetOnlinePeersAsync(cancellationToken) ?? Task.FromResult<IReadOnlyList<PairedDevice>>([]);

    public async Task<IReadOnlyList<PairedDevice>> GetAvailablePeersForSendAsync(CancellationToken cancellationToken = default)
    {
        var peers = await GetOnlinePeersAsync(cancellationToken);
        if (peers.Count == 0 && _presence is not null)
        {
            await _presence.RefreshAsync(true, cancellationToken);
            peers = await GetOnlinePeersAsync(cancellationToken);
        }
        return peers;
    }

    public async Task RefreshPeersAsync(CancellationToken cancellationToken = default)
    {
        if (_discovery is not null)
        {
            try { await _discovery.RefreshAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { await _log.WriteAsync("WARN", "LAN discovery refresh failed; probing saved peers directly."); }
        }
        if (_presence is not null) await _presence.RefreshAsync(true, cancellationToken);
        DevicesChanged?.Invoke();
    }

    public async Task<PairedDevice> PairAsync(DiscoveredDevice device, IProgress<string>? status = null,
        CancellationToken cancellationToken = default) =>
        await (_client ?? throw new InvalidOperationException("Rede não iniciada."))
            .PairAsync(device, status, cancellationToken);

    public Task AcceptPairAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        (_server ?? throw new InvalidOperationException("Rede não iniciada."))
        .AcceptPairAsync(requestId, cancellationToken);

    public void RejectPair(Guid requestId) => _server?.RejectPair(requestId);

    public Task<TransferRecord> SendFileAsync(PairedDevice peer, string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        (_client ?? throw new InvalidOperationException("Rede não iniciada."))
        .SendFileAsync(peer, path, progress, cancellationToken);

    public Task SendTextAsync(PairedDevice peer, string text, CancellationToken cancellationToken = default) =>
        (_client ?? throw new InvalidOperationException("Rede não iniciada."))
        .SendTextAsync(peer, text, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_discovery is not null)
        {
            _discovery.DeviceSeen -= OnDeviceSeen;
            await _discovery.DisposeAsync();
        }
        if (_presence is not null)
        {
            await _log.WriteAsync("DEBUG", "Stopping peer presence monitor.");
            await _presence.DisposeAsync();
            await _log.WriteAsync("DEBUG", "Peer presence monitor stopped.");
        }
        if (_server is not null)
        {
            await _log.WriteAsync("DEBUG", "Stopping OrbLink HTTPS server.");
            await _server.DisposeAsync();
            await _log.WriteAsync("DEBUG", "OrbLink HTTPS server stopped.");
        }
        _certificate?.Dispose();
        _certificate = null;
    }
}
