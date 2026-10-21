using AMS.Transfer.Core;
using AMS.Transfer.Storage;
using Microsoft.Data.Sqlite;

namespace AMS.Transfer.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task PersistsBubblePlacementAndHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ams-store-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteStore(new AppPaths(root), new TestSecretProtector());
            await store.InitializeAsync();
            var placement = new BubblePlacement(100, 200, "DISPLAY2", SnapEdge.Right);
            await store.SaveBubblePlacementAsync(placement);
            Assert.Equal(placement, await store.GetBubblePlacementAsync());

            var record = new TransferRecord(Guid.NewGuid(), "arquivo.txt", TransferKind.File,
                TransferDirection.Received, TransferStatus.Completed, 42, Guid.NewGuid(), "PC Teste",
                Path.Combine(root, "arquivo.txt"), DateTimeOffset.UtcNow);
            await store.AddTransferAsync(record);
            var saved = Assert.Single(await store.GetRecentTransfersAsync());
            Assert.Equal(record.Id, saved.Id);
            Assert.Equal("PC Teste", saved.RemoteDeviceName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProtectsPeerTokenAtRest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ams-peer-{Guid.NewGuid():N}");
        try
        {
            var paths = new AppPaths(root);
            var store = new SqliteStore(paths, new TestSecretProtector());
            await store.InitializeAsync();
            var peer = new PairedDevice(Guid.NewGuid(), "PC B", "127.0.0.1", 45832, "ABC", "secret-token", "receive-token",
                DateTimeOffset.UtcNow);
            await store.UpsertPeerAsync(peer);
            Assert.Equal("secret-token", (await store.GetPeerAsync(peer.Id))!.SendToken);
            SqliteConnection.ClearAllPools();
            var databaseBytes = await File.ReadAllBytesAsync(paths.Database);
            Assert.DoesNotContain("secret-token", System.Text.Encoding.UTF8.GetString(databaseBytes));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string plainText) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test:" + plainText));
        public string Unprotect(string protectedText) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText))[5..];
    }
}
