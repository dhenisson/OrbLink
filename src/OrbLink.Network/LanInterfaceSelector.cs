using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OrbLink.Network;

public sealed record LanAddress(string InterfaceId, string Name, string Description,
    NetworkInterfaceType Type, bool IsUp, IPAddress Address, IPAddress Mask, bool HasGateway);

public static class LanInterfaceSelector
{
    private static readonly string[] VirtualMarkers =
        ["radmin", "tailscale", "vpn", "virtual", "vethernet", "hyper-v", "vmware", "wireguard", "wintun", "zerotier", "hamachi", "tunnel", "loopback", "tap-windows", "docker"];

    public static bool IsPrivateLanIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }

    public static IReadOnlyList<LanAddress> Select(IEnumerable<LanAddress> candidates) => candidates
        .Where(item => item.IsUp && item.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
            IsPrivateLanIPv4(item.Address) && item.Mask.AddressFamily == AddressFamily.InterNetwork &&
            !VirtualMarkers.Any(marker => (item.Name + " " + item.Description).Contains(marker, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(item => item.HasGateway ? 0 : 1)
        .ThenBy(item => item.Type == NetworkInterfaceType.Ethernet ? 0 : 1)
        .ThenBy(item => item.InterfaceId, StringComparer.Ordinal)
        .ThenBy(item => item.Address.ToString(), StringComparer.Ordinal)
        .DistinctBy(item => item.Address.ToString()).ToArray();

    public static IReadOnlyList<LanAddress> GetLocalAddresses()
    {
        var candidates = new List<LanAddress>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !item.Address.Equals(IPAddress.Any));
                foreach (var address in properties.UnicastAddresses.Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork))
                    candidates.Add(new LanAddress(adapter.Id, adapter.Name, adapter.Description, adapter.NetworkInterfaceType,
                        adapter.OperationalStatus == OperationalStatus.Up, address.Address, address.IPv4Mask, hasGateway));
            }
            catch (NetworkInformationException) { }
        }
        return Select(candidates);
    }

    public static IPAddress BroadcastAddress(LanAddress item)
    {
        var address = item.Address.GetAddressBytes(); var mask = item.Mask.GetAddressBytes();
        return new IPAddress(address.Zip(mask, (a, m) => (byte)(a | ~m)).ToArray());
    }
}
