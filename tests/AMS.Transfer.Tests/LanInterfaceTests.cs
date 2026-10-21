using System.Net;
using System.Net.NetworkInformation;
using AMS.Transfer.Network;

namespace AMS.Transfer.Tests;

public sealed class LanInterfaceTests
{
    [Theory]
    [InlineData("192.168.10.20", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.3.4", false)]
    [InlineData("26.1.2.3", false)]
    [InlineData("100.64.1.1", false)]
    [InlineData("::1", false)]
    public void OnlyRfc1918Ipv4IsAutomaticLan(string ip, bool expected) =>
        Assert.Equal(expected, LanInterfaceSelector.IsPrivateLanIPv4(IPAddress.Parse(ip)));

    [Fact]
    public void PrioritizesPhysicalEthernetWifiAndFiltersActiveVirtualPrivateAdapters()
    {
        static LanAddress Address(string name, string ip, NetworkInterfaceType type = NetworkInterfaceType.Ethernet, bool up = true) =>
            new(name, name, name, type, up, IPAddress.Parse(ip), IPAddress.Parse("255.255.255.0"), true);
        var selected = LanInterfaceSelector.Select([
            Address("Wi-Fi", "192.168.10.21", NetworkInterfaceType.Wireless80211),
            Address("Radmin VPN", "26.1.2.3"), Address("Tailscale", "169.254.12.3"),
            Address("vEthernet Default Switch", "172.20.0.1"), Address("WireGuard", "10.8.0.2"),
            Address("Ethernet", "192.168.10.20"), Address("Ethernet disabled", "192.168.1.2", up: false),
            Address("Tunnel", "10.9.0.2", NetworkInterfaceType.Tunnel)
        ]);
        Assert.Equal(new[] { "Ethernet", "Wi-Fi" }, selected.Select(item => item.Name));
        Assert.Equal(IPAddress.Parse("192.168.10.255"), LanInterfaceSelector.BroadcastAddress(selected[0]));
    }
}
