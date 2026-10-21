using System.Net;
using System.Net.Sockets;
using OrbLink.Core;
using OrbLink.Network;
using OrbLink.Storage;
using Microsoft.Data.Sqlite;

namespace OrbLink.Tests;

public sealed class TransferIntegrationTests
{
    [Fact]
    public async Task PairsAndStreamsFileOverPinnedHttps()
    {
        var rootA = Path.Combine(Path.GetTempPath(), $"orblink-a-{Guid.NewGuid():N}");
        var rootB = Path.Combine(Path.GetTempPath(), $"orblink-b-{Guid.NewGuid():N}");
        TransferServer? server = null;
        try
        {
            var pathsA = new AppPaths(rootA);
            var pathsB = new AppPaths(rootB);
            var protector = new TestSecretProtector();
            var storeA = new SqliteStore(pathsA, protector);
            var storeB = new SqliteStore(pathsB, protector);
            await storeA.InitializeAsync();
            await storeB.InitializeAsync();
            var identityA = await storeA.GetOrCreateIdentityAsync();
            var identityB = await storeB.GetOrCreateIdentityAsync();
            var certA = await new CertificateService(pathsA, storeA, protector).GetOrCreateAsync();
            var certB = await new CertificateService(pathsB, storeB, protector).GetOrCreateAsync();
            var port = FindFreePort();
            var options = new NetworkOptions { TransferPort = port };
            server = new TransferServer(identityB, certB, storeB, pathsB, options, new LocalLog(pathsB));
            server.PairingRequested += request => server.AcceptPairAsync(request.RequestId);
            var received = new TaskCompletionSource<TransferRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.ItemReceived += record => { received.TrySetResult(record); return Task.CompletedTask; };
            await server.StartAsync();

            var deviceB = new DiscoveredDevice(identityB.Id, identityB.Name, "127.0.0.1", port,
                CertificateService.GetFingerprint(certB), DateTimeOffset.UtcNow);
            var client = new TransferClient(identityA, FindFreePort(), CertificateService.GetFingerprint(certA),
                storeA, new LocalLog(pathsA));
            var peerB = await client.PairAsync(deviceB);

            var sourcePath = Path.Combine(rootA, "payload.bin");
            var payload = new byte[32 * 1024 * 1024 + 17];
            Random.Shared.NextBytes(payload);
            await File.WriteAllBytesAsync(sourcePath, payload);
            var progressValues = new List<double>();
            await client.SendFileAsync(peerB, sourcePath, new Progress<double>(value => progressValues.Add(value)));
            var record = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(record.LocalPath);
            Assert.Equal(payload, await File.ReadAllBytesAsync(record.LocalPath!));
            Assert.Equal(payload.LongLength, record.Size);
        }
        finally
        {
            if (server is not null) await server.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(rootA)) Directory.Delete(rootA, true);
            if (Directory.Exists(rootB)) Directory.Delete(rootB, true);
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string plainText) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test:" + plainText));
        public string Unprotect(string protectedText) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText))[5..];
    }
}
