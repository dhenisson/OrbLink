using AMS.Transfer.Core;
using Microsoft.Data.Sqlite;

namespace AMS.Transfer.Storage;

public sealed class SqliteStore(AppPaths paths, ISecretProtector protector)
{
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = paths.Database,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Peers (
                DeviceId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Address TEXT NOT NULL,
                Port INTEGER NOT NULL,
                CertificateFingerprint TEXT NOT NULL,
                ProtectedSendToken TEXT NOT NULL,
                ProtectedReceiveToken TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Transfers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                Direction INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                Size INTEGER NOT NULL,
                RemoteDeviceId TEXT NOT NULL,
                RemoteDeviceName TEXT NOT NULL,
                LocalPath TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                Error TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Transfers_CreatedUtc ON Transfers(CreatedUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await MigratePeerTokensAsync(connection, cancellationToken);
    }

    private static async Task MigratePeerTokensAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(Peers)";
        await using (var reader = await inspect.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));

        if (columns.Contains("ProtectedReceiveToken")) return;
        var migrate = connection.CreateCommand();
        migrate.CommandText = """
            ALTER TABLE Peers ADD COLUMN ProtectedReceiveToken TEXT NOT NULL DEFAULT '';
            """;
        await migrate.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DeviceIdentity> GetOrCreateIdentityAsync(CancellationToken cancellationToken = default)
    {
        var idText = await GetSettingAsync("DeviceId", cancellationToken);
        var name = await GetSettingAsync("DeviceName", cancellationToken);
        if (!Guid.TryParse(idText, out var id))
        {
            id = Guid.NewGuid();
            await SetSettingAsync("DeviceId", id.ToString("D"), cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = Environment.MachineName;
            await SetSettingAsync("DeviceName", name, cancellationToken);
        }

        return new DeviceIdentity(id, name);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings(Key, Value) VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<BubblePlacement?> GetBubblePlacementAsync(CancellationToken cancellationToken = default)
    {
        var value = await GetSettingAsync("BubblePlacement", cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<BubblePlacement>(value); }
        catch { return null; }
    }

    public Task SaveBubblePlacementAsync(BubblePlacement placement, CancellationToken cancellationToken = default) =>
        SetSettingAsync("BubblePlacement", System.Text.Json.JsonSerializer.Serialize(placement), cancellationToken);

    public async Task UpsertPeerAsync(PairedDevice peer, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Peers(DeviceId, Name, Address, Port, CertificateFingerprint, ProtectedSendToken, ProtectedReceiveToken, LastSeenUtc)
            VALUES($id, $name, $address, $port, $fingerprint, $sendToken, $receiveToken, $lastSeen)
            ON CONFLICT(DeviceId) DO UPDATE SET
                Name = excluded.Name,
                Address = excluded.Address,
                Port = excluded.Port,
                CertificateFingerprint = excluded.CertificateFingerprint,
                ProtectedSendToken = excluded.ProtectedSendToken,
                ProtectedReceiveToken = excluded.ProtectedReceiveToken,
                LastSeenUtc = excluded.LastSeenUtc
            """;
        command.Parameters.AddWithValue("$id", peer.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", peer.Name);
        command.Parameters.AddWithValue("$address", peer.Address);
        command.Parameters.AddWithValue("$port", peer.Port);
        command.Parameters.AddWithValue("$fingerprint", peer.CertificateFingerprint);
        command.Parameters.AddWithValue("$sendToken", protector.Protect(peer.SendToken));
        command.Parameters.AddWithValue("$receiveToken", protector.Protect(peer.ReceiveToken));
        command.Parameters.AddWithValue("$lastSeen", peer.LastSeenUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PairedDevice?> GetPeerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, Name, Address, Port, CertificateFingerprint, ProtectedSendToken, ProtectedReceiveToken, LastSeenUtc FROM Peers WHERE DeviceId = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPeer(reader) : null;
    }

    public async Task<bool> UpdatePeerPresenceAsync(Guid id, string fingerprint, string address, int port,
        DateTimeOffset lastSeen, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        // Never replace protected tokens or other pairing fields with a stale probe snapshot.
        command.CommandText = """
            UPDATE Peers SET Address=$address, Port=$port, LastSeenUtc=$seen
            WHERE DeviceId=$id AND CertificateFingerprint=$fingerprint
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$address", address);
        command.Parameters.AddWithValue("$port", port);
        command.Parameters.AddWithValue("$seen", lastSeen.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<PairedDevice>> GetPeersAsync(CancellationToken cancellationToken = default)
    {
        var peers = new List<PairedDevice>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, Name, Address, Port, CertificateFingerprint, ProtectedSendToken, ProtectedReceiveToken, LastSeenUtc FROM Peers ORDER BY Name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) peers.Add(ReadPeer(reader));
        return peers;
    }

    private PairedDevice ReadPeer(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
        reader.GetString(4), UnprotectToken(reader.GetString(5)), UnprotectToken(reader.GetString(6)),
        DateTimeOffset.Parse(reader.GetString(7)));

    private string UnprotectToken(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try { return protector.Unprotect(value); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or ArgumentException)
        { return string.Empty; } // Keep the row/protected value intact; an empty credential can never authenticate.
    }

    public async Task AddTransferAsync(TransferRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO Transfers
            (Id, Name, Kind, Direction, Status, Size, RemoteDeviceId, RemoteDeviceName, LocalPath, CreatedUtc, Error)
            VALUES($id, $name, $kind, $direction, $status, $size, $remoteId, $remoteName, $path, $created, $error)
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$kind", (int)record.Kind);
        command.Parameters.AddWithValue("$direction", (int)record.Direction);
        command.Parameters.AddWithValue("$status", (int)record.Status);
        command.Parameters.AddWithValue("$size", record.Size);
        command.Parameters.AddWithValue("$remoteId", record.RemoteDeviceId.ToString("D"));
        command.Parameters.AddWithValue("$remoteName", record.RemoteDeviceName);
        command.Parameters.AddWithValue("$path", (object?)record.LocalPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", record.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$error", (object?)record.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TransferRecord>> GetRecentTransfersAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var items = new List<TransferRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Direction, Status, Size, RemoteDeviceId, RemoteDeviceName, LocalPath, CreatedUtc, Error
            FROM Transfers ORDER BY CreatedUtc DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new TransferRecord(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), (TransferKind)reader.GetInt32(2),
                (TransferDirection)reader.GetInt32(3), (TransferStatus)reader.GetInt32(4), reader.GetInt64(5),
                Guid.Parse(reader.GetString(6)), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9)), reader.IsDBNull(10) ? null : reader.GetString(10)));
        }
        return items;
    }
}
