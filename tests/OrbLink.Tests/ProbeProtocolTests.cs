using OrbLink.Core;
using OrbLink.Network;
using OrbLink.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace OrbLink.Tests;

public sealed class ProbeProtocolTests
{
    [Theory]
    [InlineData("wrong-protocol", PeerProbeFailure.InvalidService)]
    [InlineData("redirect", PeerProbeFailure.InvalidService)]
    [InlineData("timeout", PeerProbeFailure.Timeout)]
    [InlineData("not-found", PeerProbeFailure.UnsupportedEndpoint)]
    public async Task RejectsWrongServiceRedirectTimeoutAndOldEndpoint(string mode, PeerProbeFailure expected)
    {
        using var fixture = new TestStorage(); await fixture.Store.InitializeAsync();
        var identity = await fixture.Store.GetOrCreateIdentityAsync();
        using var cert = await new CertificateService(fixture.Paths, fixture.Store).GetOrCreateAsync();
        var port = PresenceIntegrationTests.FreePort();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port, listen => listen.UseHttps(cert)));
        await using var server = builder.Build();
        server.MapGet("/api/peer/health", async (CancellationToken token) =>
        {
            if (mode == "timeout") await Task.Delay(TimeSpan.FromSeconds(5), token);
            return mode switch
            {
                "redirect" => Results.Redirect("https://example.invalid/never-follow-authenticated-redirect"),
                "not-found" => Results.NotFound(),
                _ => Results.Json(new PeerHealthResponse("NOT_ORBLINK", identity.Id, "not OrbLink"))
            };
        });
        await server.StartAsync();
        var peer = new PairedDevice(identity.Id, "receiver", "127.0.0.1", port, CertificateService.GetFingerprint(cert),
            "never-log-or-redirect-this", "reverse", DateTimeOffset.UtcNow);
        var client = new PeerProbeClient(new DeviceIdentity(Guid.NewGuid(), "sender"), new NetworkOptions(), new LocalLog(fixture.Paths));
        var result = await client.ProbeAsync(peer);
        Assert.False(result.Success); Assert.Equal(expected, result.Failure);
        Assert.DoesNotContain(peer.SendToken, await File.ReadAllTextAsync(Directory.GetFiles(fixture.Paths.Logs).Single()));
        await server.StopAsync();
    }
}
