using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AMS.Transfer.Core;
using AMS.Transfer.Storage;

namespace AMS.Transfer.Network;

public sealed record PeerHealthResponse(string Protocol, Guid DeviceId, string Name)
{
    public const string ProtocolName = "ORBLINK_PEER_V1";
}

public enum PeerProbeFailure
{
    None, Timeout, ConnectionFailed, AuthenticationFailed, CertificateMismatch,
    DeviceIdMismatch, InvalidService, UnsupportedEndpoint
}

public sealed record PeerProbeResult(bool Success, PeerProbeFailure Failure = PeerProbeFailure.None)
{
    public bool IsSecurityFailure => Failure is PeerProbeFailure.AuthenticationFailed or
        PeerProbeFailure.CertificateMismatch or PeerProbeFailure.DeviceIdMismatch or
        PeerProbeFailure.InvalidService or PeerProbeFailure.UnsupportedEndpoint;
}

public sealed class PeerProbeClient(DeviceIdentity identity, NetworkOptions options, LocalLog log)
{
    public async Task<PeerProbeResult> ProbeAsync(PairedDevice peer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(peer.SendToken) || string.IsNullOrEmpty(peer.ReceiveToken))
        {
            await log.WriteAsync("WARN", "Credenciais de um peer indisponíveis; restaure backup, sem reset automático.");
            return new(false, PeerProbeFailure.AuthenticationFailed);
        }
        var label = $"{peer.Name} / {peer.Address}:{peer.Port}";
        await log.WriteAsync("DEBUG", $"Peer probe started: {label}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ProbeTimeout);
        var certificateSeen = false;
        var certificateMismatch = false;
        using var client = PinnedHttp.Create(peer.CertificateFingerprint, matches =>
        {
            certificateSeen = true;
            certificateMismatch = !matches;
        });
        PeerProbeResult result;
        try
        {
            var host = peer.Address.Contains(':') ? $"[{peer.Address}]" : peer.Address;
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{host}:{peer.Port}/api/peer/health"));
            request.Headers.TryAddWithoutValidation("X-Device-Id", identity.Id.ToString("D"));
            request.Headers.TryAddWithoutValidation("X-Auth-Token", peer.SendToken);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                result = new(false, PeerProbeFailure.AuthenticationFailed);
            else if (response.StatusCode == HttpStatusCode.NotFound)
                result = new(false, PeerProbeFailure.UnsupportedEndpoint);
            else if (!response.IsSuccessStatusCode)
                result = new(false, PeerProbeFailure.InvalidService);
            else
            {
                await response.Content.LoadIntoBufferAsync(8192, timeout.Token).ConfigureAwait(false);
                var health = await response.Content.ReadFromJsonAsync<PeerHealthResponse>(cancellationToken: timeout.Token).ConfigureAwait(false);
                result = health is null || health.Protocol != PeerHealthResponse.ProtocolName
                    ? new(false, PeerProbeFailure.InvalidService)
                    : health.DeviceId != peer.Id ? new(false, PeerProbeFailure.DeviceIdMismatch) : new(true);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { result = new(false, PeerProbeFailure.Timeout); }
        catch (HttpRequestException)
        { result = new(false, certificateMismatch ? PeerProbeFailure.CertificateMismatch : PeerProbeFailure.ConnectionFailed); }
        catch (IOException)
        { result = new(false, PeerProbeFailure.ConnectionFailed); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or UriFormatException or ArgumentException)
        { result = new(false, PeerProbeFailure.InvalidService); }

        if (certificateSeen) await log.WriteAsync("DEBUG", $"TCP connection succeeded: {label}");
        if (certificateSeen && !certificateMismatch)
            await log.WriteAsync("DEBUG", $"Certificate fingerprint validated: {label}");
        await log.WriteAsync(result.Success ? "DEBUG" : "WARN", result.Success
            ? $"OrbLink identity validated: {label}"
            : $"Peer probe failed: {label} / {result.Failure}");
        // Deliberately do not log HTTP headers, tokens, response bodies or exception messages.
        return result;
    }
}
