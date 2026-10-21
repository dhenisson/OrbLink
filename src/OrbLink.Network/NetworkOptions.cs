namespace OrbLink.Network;

public sealed class NetworkOptions
{
    public bool EnableDiscovery { get; init; } = true;
    public int DiscoveryPort { get; init; } = 45831;
    public int TransferPort { get; init; } = 45832;
    public long MaxFileSizeBytes { get; init; } = 10L * 1024 * 1024 * 1024;
    public TimeSpan OnlineWindow { get; init; } = TimeSpan.FromSeconds(8);
    public bool EnableHeartbeat { get; init; } = true;
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PresenceTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int OfflineFailureCount { get; init; } = 3;
}
