using System.Collections.Concurrent;
using OrbLink.Core;
using OrbLink.Storage;

namespace OrbLink.Network;

public sealed class PeerPresenceMonitor : IAsyncDisposable
{
    private readonly SqliteStore _store;
    private readonly LocalLog _log;
    private readonly NetworkOptions _options;
    private readonly Func<PairedDevice, CancellationToken, Task<PeerProbeResult>> _probe;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<Guid, Slot> _slots = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _parallel = new(4);
    private Task? _heartbeat;

    public PeerPresenceMonitor(SqliteStore store, LocalLog log, NetworkOptions options,
        Func<PairedDevice, CancellationToken, Task<PeerProbeResult>> probe, TimeProvider? clock = null)
    {
        _store = store; _log = log; _options = options; _probe = probe;
        _clock = clock ?? TimeProvider.System;
    }

    public event Action? Changed;

    public void Start()
    {
        if (_heartbeat is null && _options.EnableHeartbeat)
            _heartbeat = Task.Run(HeartbeatAsync);
    }

    private async Task HeartbeatAsync()
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        do
        {
            try { await RefreshAsync(false, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch { await _log.WriteAsync("WARN", "Peer heartbeat cycle failed; retrying next cycle."); }
        } while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
    }

    public async Task RefreshAsync(bool force = true, CancellationToken cancellationToken = default)
    {
        var peers = await _store.GetPeersAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(peers.Select(peer => RequestProbeAsync(peer, false, force, cancellationToken))).ConfigureAwait(false);
    }

    public async Task<bool> ObserveDiscoveryAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        if (device.Port is < 1 or > 65535) return false;
        var peer = await _store.GetPeerAsync(device.Id, cancellationToken).ConfigureAwait(false);
        if (peer is null || !string.Equals(peer.CertificateFingerprint, device.CertificateFingerprint, StringComparison.OrdinalIgnoreCase))
            return false;
        var attempt = await RequestProbeAsync(peer with { Address = device.Address, Port = device.Port },
            true, false, cancellationToken).ConfigureAwait(false);
        return attempt.Result.Success;
    }

    public async Task<IReadOnlyList<PairedDevice>> GetOnlinePeersAsync(CancellationToken cancellationToken = default)
    {
        var online = new List<PairedDevice>();
        foreach (var peer in await _store.GetPeersAsync(cancellationToken).ConfigureAwait(false))
        {
            var slot = _slots.GetOrAdd(peer.Id, _ => new Slot());
            await PublishAsync(peer, slot).ConfigureAwait(false);
            lock (slot.Sync) { if (IsOnline(peer, slot)) online.Add(peer); }
        }
        return online;
    }

    private bool IsOnline(PairedDevice peer, Slot slot) => slot.VerifiedPeer is not null &&
        SameEndpoint(peer, slot.VerifiedPeer) && !slot.SecurityFailure &&
        slot.Failures < _options.OfflineFailureCount && slot.LastSuccess is { } seen &&
        _clock.GetUtcNow() - seen < _options.PresenceTimeout;

    private async Task<Attempt> RequestProbeAsync(PairedDevice peer, bool candidate, bool force, CancellationToken cancellationToken)
    {
        _stop.Token.ThrowIfCancellationRequested();
        var slot = _slots.GetOrAdd(peer.Id, _ => new Slot());
        while (true)
        {
            Task<Attempt> pending;
            lock (slot.Sync)
            {
                if (slot.Pending is { IsCompleted: false }) pending = slot.Pending;
                else if (!force && slot.LastAttempt is { } recent && SameEndpoint(peer, recent.Peer) &&
                    _clock.GetUtcNow() - slot.LastAttemptTime < _options.HeartbeatInterval)
                    return recent;
                else pending = slot.Pending = Task.Run(() => RunProbeAsync(peer, slot));
            }
            // Cancelling a UI caller does not cancel a shared heartbeat/probe for another caller.
            var attempt = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (SameEndpoint(peer, attempt.Peer)) return attempt;
            if (!candidate)
            {
                var current = await _store.GetPeerAsync(peer.Id, cancellationToken).ConfigureAwait(false);
                if (current is null) return attempt;
                peer = current; // Discovery may have updated the endpoint while this caller waited.
                if (attempt.Result.Success && SameCredentials(peer, attempt.Peer)) return attempt;
            }
        }
    }

    private async Task<Attempt> RunProbeAsync(PairedDevice peer, Slot slot)
    {
        await _parallel.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            PeerProbeResult result;
            try { result = await _probe(peer, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
            catch { result = new(false, PeerProbeFailure.ConnectionFailed); }
            var now = _clock.GetUtcNow();
            var current = await _store.GetPeerAsync(peer.Id, _stop.Token).ConfigureAwait(false);
            var attempt = new Attempt(peer, result);
            if (current is not null && SameCredentials(current, peer))
            {
                if (result.Success)
                {
                    if (await _store.UpdatePeerPresenceAsync(peer.Id, current.CertificateFingerprint, peer.Address,
                        peer.Port, now, _stop.Token).ConfigureAwait(false))
                    {
                        lock (slot.Sync)
                        {
                            slot.VerifiedPeer = peer; slot.LastSuccess = now;
                            slot.Failures = 0; slot.SecurityFailure = false;
                        }
                        if (current.Address != peer.Address || current.Port != peer.Port)
                            await _log.WriteAsync("INFO", $"Peer address updated after identity validation: {peer.Name} / {peer.Address}:{peer.Port}");
                        current = current with { Address = peer.Address, Port = peer.Port, LastSeenUtc = now };
                    }
                }
                else if (SameEndpoint(current, peer))
                {
                    // A bogus discovery candidate must not poison a verified saved endpoint.
                    lock (slot.Sync) { slot.Failures++; slot.SecurityFailure |= result.IsSecurityFailure; }
                }
                await PublishAsync(current, slot).ConfigureAwait(false);
            }
            lock (slot.Sync) { slot.LastAttempt = attempt; slot.LastAttemptTime = now; }
            return attempt;
        }
        finally { _parallel.Release(); }
    }

    private async Task PublishAsync(PairedDevice peer, Slot slot)
    {
        bool changed, online;
        lock (slot.Sync)
        {
            online = IsOnline(peer, slot);
            changed = online != slot.PublishedOnline;
            slot.PublishedOnline = online;
        }
        if (!changed) return;
        await _log.WriteAsync("INFO", $"Peer status changed: {peer.Name} / {(online ? "Offline -> Online" : "Online -> Offline")}");
        Changed?.Invoke();
    }

    private static bool SameCredentials(PairedDevice a, PairedDevice b) => a.Id == b.Id &&
        string.Equals(a.CertificateFingerprint, b.CertificateFingerprint, StringComparison.OrdinalIgnoreCase) &&
        a.SendToken == b.SendToken;
    private static bool SameEndpoint(PairedDevice a, PairedDevice b) => SameCredentials(a, b) &&
        a.Address == b.Address && a.Port == b.Port;

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_heartbeat is not null)
            try { await _heartbeat.ConfigureAwait(false); } catch (OperationCanceledException) { }
        var pending = new List<Task>();
        foreach (var slot in _slots.Values)
            lock (slot.Sync) { if (slot.Pending is not null) pending.Add(slot.Pending); }
        try { await Task.WhenAll(pending).ConfigureAwait(false); } catch (OperationCanceledException) { }
        _parallel.Dispose(); _stop.Dispose();
    }

    private sealed record Attempt(PairedDevice Peer, PeerProbeResult Result);
    private sealed class Slot
    {
        public object Sync { get; } = new();
        public Task<Attempt>? Pending;
        public Attempt? LastAttempt;
        public DateTimeOffset LastAttemptTime;
        public PairedDevice? VerifiedPeer;
        public DateTimeOffset? LastSuccess;
        public int Failures;
        public bool SecurityFailure;
        public bool PublishedOnline;
    }
}
