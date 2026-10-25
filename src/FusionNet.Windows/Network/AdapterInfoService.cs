using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.Network;

/// <summary>
/// Reads real network adapters (never loopback/tunnel/pseudo interfaces) with their addresses,
/// DNS servers, gateway, default-route flag, counters and, for Wi-Fi, SSID + signal strength.
///
/// All Windows calls are individually guarded: a disconnecting adapter or a disabled Wi-Fi radio
/// produces a partial but valid <see cref="AdapterInfo"/> list, never an exception.
/// </summary>
public sealed class AdapterInfoService : IAdapterInfoService
{
    private readonly WifiInfoReader _wifi = new();

    public IReadOnlyList<AdapterInfo> GetAdapters()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception)
        {
            // Network stack is reconfiguring — report "no adapters" instead of throwing at the UI.
            return [];
        }

        var wifi = SafeReadWifi();
        var defaultRouteIndex = NetworkInterop.GetDefaultInterfaceIndex();
        var adapters = new List<AdapterInfo>(interfaces.Length);

        foreach (var networkInterface in interfaces)
        {
            if (!NetworkProbeHelper.IsPhysicalAdapter(networkInterface)) continue;

            try
            {
                adapters.Add(BuildAdapter(networkInterface, wifi, defaultRouteIndex));
            }
            catch (Exception)
            {
                // Adapter was removed while being inspected; skip it rather than failing the whole list.
            }
        }

        return adapters
            .OrderByDescending(adapter => adapter.IsDefaultRoute)
            .ThenByDescending(adapter => string.Equals(adapter.Status, "Up", StringComparison.OrdinalIgnoreCase))
            .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AdapterInfo? GetDefaultAdapter() => SelectDefault(GetAdapters());

    public AdapterInfo? GetAdapter(string? adapterId)
    {
        var adapters = GetAdapters();
        if (string.IsNullOrWhiteSpace(adapterId)) return SelectDefault(adapters);

        var key = adapterId.Trim();
        var normalized = NetworkProbeHelper.NormalizeId(key);

        return adapters.FirstOrDefault(adapter =>
                   normalized.Length > 0 && NetworkProbeHelper.NormalizeId(adapter.Id) == normalized)
               ?? adapters.FirstOrDefault(adapter =>
                   string.Equals(adapter.Name, key, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(adapter.Description, key, StringComparison.OrdinalIgnoreCase));
    }

    private static AdapterInfo? SelectDefault(IReadOnlyList<AdapterInfo> adapters) =>
        adapters.FirstOrDefault(adapter => adapter.IsDefaultRoute && IsUp(adapter))
        ?? adapters.FirstOrDefault(IsUp)
        ?? adapters.FirstOrDefault();

    private AdapterInfo BuildAdapter(
        NetworkInterface networkInterface,
        IReadOnlyList<WifiInterfaceInfo> wifi,
        uint? defaultRouteIndex)
    {
        var ipProperties = TryGet(() => networkInterface.GetIPProperties(), null);

        var ipv4Addresses = new List<string>();
        var dnsServers = new List<string>();
        string? gateway = null;

        if (ipProperties is not null)
        {
            try
            {
                ipv4Addresses.AddRange(ipProperties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString()));
            }
            catch (Exception)
            {
                // Address list unavailable for this adapter.
            }

            try
            {
                dnsServers.AddRange(ipProperties.DnsAddresses
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.ToString()));
            }
            catch (Exception)
            {
                // DNS list unavailable for this adapter.
            }

            try
            {
                var gateways = ipProperties.GatewayAddresses
                    .Select(entry => entry.Address)
                    .Where(address => address is not null && !address.Equals(IPAddress.Any))
                    .ToArray();

                // Prefer the IPv4 default gateway: it is what users mean and it is pingable.
                gateway = (gateways.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                           ?? gateways.FirstOrDefault())?.ToString();
            }
            catch (Exception)
            {
                // Gateway list unavailable for this adapter.
            }
        }

        var wireless = NetworkProbeHelper.LooksWireless(networkInterface);
        var wifiInfo = wireless ? MatchWifiInfo(wifi, networkInterface) : null;

        long linkSpeed = 0;
        try
        {
            linkSpeed = networkInterface.Speed;
        }
        catch (Exception)
        {
            // Link speed is not reported for some virtual/disconnected adapters.
        }

        ulong receivedBytes = 0;
        ulong sentBytes = 0;
        try
        {
            var statistics = networkInterface.GetIPv4Statistics();
            receivedBytes = (ulong)Math.Max(0, statistics.BytesReceived);
            sentBytes = (ulong)Math.Max(0, statistics.BytesSent);
        }
        catch (Exception)
        {
            // Counters unavailable while the adapter is down.
        }

        var interfaceIndex = NetworkProbeHelper.GetInterfaceIndex(networkInterface);
        var isDefaultRoute = interfaceIndex.HasValue && defaultRouteIndex.HasValue &&
                             interfaceIndex.Value == defaultRouteIndex.Value;

        var ssid = string.IsNullOrWhiteSpace(wifiInfo?.Ssid) ? null : wifiInfo!.Ssid.Trim();

        return new AdapterInfo(
            Id: networkInterface.Id,
            Name: networkInterface.Name,
            Description: networkInterface.Description,
            MacAddress: NetworkProbeHelper.FormatMac(TryGet(() => networkInterface.GetPhysicalAddress(), null)),
            Status: NetworkProbeHelper.DescribeStatus(networkInterface.OperationalStatus),
            LinkSpeedBps: linkSpeed,
            IPv4Addresses: ipv4Addresses,
            DnsServers: dnsServers,
            Gateway: string.IsNullOrWhiteSpace(gateway) ? null : gateway,
            IsWireless: wireless,
            Ssid: ssid,
            SignalPercent: wifiInfo?.SignalPercent,
            IsDefaultRoute: isDefaultRoute,
            ReceivedBytes: receivedBytes,
            SentBytes: sentBytes);
    }

    private IReadOnlyList<WifiInterfaceInfo> SafeReadWifi()
    {
        try
        {
            return _wifi.GetInterfaces();
        }
        catch (Exception)
        {
            // netsh missing or WLAN service unavailable — Ethernet-only view is still correct.
            return [];
        }
    }

    private static WifiInterfaceInfo? MatchWifiInfo(IReadOnlyList<WifiInterfaceInfo> entries, NetworkInterface networkInterface)
    {
        if (entries.Count == 0) return null;

        var interfaceId = NetworkProbeHelper.NormalizeId(networkInterface.Id);
        var mac = NetworkProbeHelper.FormatMac(TryGet(() => networkInterface.GetPhysicalAddress(), null));

        return entries.FirstOrDefault(entry =>
                   interfaceId.Length > 0 && NetworkProbeHelper.NormalizeId(entry.InterfaceGuid) == interfaceId)
               ?? entries.FirstOrDefault(entry =>
                   mac.Length > 0 && string.Equals(entry.MacAddress.Replace("-", ":"), mac, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(entry =>
                   string.Equals(entry.Name, networkInterface.Name, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Description, networkInterface.Description, StringComparison.OrdinalIgnoreCase))
               ?? (entries.Count == 1 ? entries[0] : null);
    }

    private static bool IsUp(AdapterInfo adapter) =>
        string.Equals(adapter.Status, "Up", StringComparison.OrdinalIgnoreCase);

    private static T TryGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            // Property not supported for this adapter/state.
            return fallback;
        }
    }
}
