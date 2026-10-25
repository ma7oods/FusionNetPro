using System.Net.NetworkInformation;

namespace FusionNet.Windows.Network;

/// <summary>
/// Shared adapter classification helpers. Kept separate so ping, throughput, adapter and
/// diagnostics services agree on what a "real" adapter is.
/// </summary>
internal static class NetworkProbeHelper
{
    private static readonly string[] PseudoMarkers =
    [
        "pseudo", "loopback", "teredo", "isatap", "6to4", "wan miniport", "kernel debug",
        // NDIS lightweight-filter interfaces are exposed as separate NetworkInterface objects
        // (e.g. "Ethernet-QoS Packet Scheduler-0000"); they duplicate a real NIC and must not
        // pollute the adapter list.
        "-qos packet scheduler", "wfp ", "native wifi filter", "virtual wifi filter",
        // Wi-Fi Direct virtual adapters ("Local Area Connection* 1") are not usable NICs.
        "wi-fi direct", "wifi direct"
    ];

    private static readonly string[] WirelessMarkers =
    [
        "wi-fi", "wifi", "wireless", "wlan", "802.11", "ax2", "ac 3165"
    ];

    private static readonly string[] VpnMarkers =
    [
        "vpn", "tap-windows", "tap adapter", "wireguard", "tailscale", "zerotier",
        "openvpn", "wintun", "nordlynx", "proton", "mullvad", "softether", "pptp", "l2tp"
    ];

    /// <summary>Loopback, tunnel and pseudo interfaces are never physical adapters.</summary>
    public static bool IsPhysicalAdapter(NetworkInterface networkInterface) =>
        networkInterface.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && !LooksPseudo(networkInterface);

    /// <summary>Pseudo/VPN wrapper interfaces that Windows exposes but no user thinks of as a NIC.</summary>
    public static bool LooksPseudo(NetworkInterface networkInterface)
    {
        var name = networkInterface.Name;
        var description = networkInterface.Description;
        var text = $"{name} {description}";

        // Filter-miniport duplicates always end with the filter's 4-digit index.
        if (description.EndsWith("-0000", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("-0000", StringComparison.OrdinalIgnoreCase)) return true;

        return PseudoMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksWireless(NetworkInterface networkInterface)
    {
        if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return true;

        var text = $"{networkInterface.Name} {networkInterface.Description}";
        return WirelessMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksLikeVpnOrTunnel(NetworkInterface networkInterface)
    {
        if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return true;

        var text = $"{networkInterface.Name} {networkInterface.Description}";
        return VpnMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatMac(PhysicalAddress? address)
    {
        if (address is null) return string.Empty;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 0) return string.Empty;
        return string.Join(":", bytes.Select(static value => value.ToString("X2")));
    }

    /// <summary>GUID form used by Windows adapter ids, without braces and lower-cased.</summary>
    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        return id.Trim().Trim('{', '}').ToLowerInvariant();
    }

    public static uint? GetInterfaceIndex(NetworkInterface networkInterface)
    {
        try
        {
            var properties = networkInterface.GetIPProperties().GetIPv4Properties();
            return properties is null ? null : (uint)properties.Index;
        }
        catch (Exception)
        {
            // Interface disappears while being queried or has no IPv4 stack — treat as unknown.
            return null;
        }
    }

    public static string DescribeStatus(OperationalStatus status) => status switch
    {
        OperationalStatus.Up => "Up",
        OperationalStatus.Down => "Down",
        OperationalStatus.Dormant => "Dormant",
        OperationalStatus.NotPresent => "Not present",
        OperationalStatus.LowerLayerDown => "Lower layer down",
        OperationalStatus.Testing => "Testing",
        OperationalStatus.Unknown => "Unknown",
        _ => status.ToString()
    };
}
