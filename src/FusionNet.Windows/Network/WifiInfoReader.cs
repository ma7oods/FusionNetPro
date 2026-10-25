using System.Text.RegularExpressions;

namespace FusionNet.Windows.Network;

/// <summary>One wireless interface as reported by <c>netsh wlan show interfaces</c>.</summary>
public sealed record WifiInterfaceInfo(
    string InterfaceGuid,
    string Name,
    string Description,
    string State,
    string Ssid,
    int? SignalPercent,
    string MacAddress);

/// <summary>
/// Reads Wi-Fi SSID and signal strength from netsh and parses it defensively.
///
/// Localized Windows translates the prose and even some keys, so the parser never matches
/// sentence text: it only recognises stable key names (<c>SSID</c>, <c>Signal</c>, <c>GUID</c>, ...),
/// starts a new record on a GUID value (language neutral) and extracts numbers with a regex.
/// When the WLAN service is stopped netsh returns an error string and this reader simply
/// returns an empty list — no exception, no Wi-Fi required.
/// </summary>
public sealed class WifiInfoReader
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);
    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PercentPattern = new(
        @"(\d{1,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whole value is a percentage ("87%"), used to recognise the localised signal key.</summary>
    private static readonly Regex PercentValuePattern = new(
        @"^\s*\d{1,3}\s*%\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whole value is a MAC address ("AA-BB-CC-DD-EE-FF"), used for the localised label.</summary>
    private static readonly Regex MacAddressPattern = new(
        @"^([0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private IReadOnlyList<WifiInterfaceInfo> _cache = [];
    private DateTime _cacheUtc = DateTime.MinValue;

    /// <summary>Returns cached wireless interface details (refreshed at most every 3 seconds).</summary>
    public IReadOnlyList<WifiInterfaceInfo> GetInterfaces(bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (!forceRefresh && DateTime.UtcNow - _cacheUtc < CacheTtl) return _cache;

            var result = NetworkCommandRunner.Run(
                NetworkCommandRunner.SystemTool("netsh.exe"),
                ["wlan", "show", "interfaces"],
                timeoutMs: 3_000);

            _cache = Parse(result.StandardOutput + Environment.NewLine + result.StandardError);
            _cacheUtc = DateTime.UtcNow;
            return _cache;
        }
    }

    /// <summary>
    /// Parses any number of interface blocks. Each block is delimited by its GUID line (or an
    /// English <c>Name</c> line when netsh omits the GUID); unrelated lines are ignored.
    /// </summary>
    public static IReadOnlyList<WifiInterfaceInfo> Parse(string? output)
    {
        var results = new List<WifiInterfaceInfo>();
        if (string.IsNullOrWhiteSpace(output)) return results;

        Builder? current = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var separator = line.IndexOf(':');
            if (separator <= 0 || separator > 40) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0) continue;

            if (IsKey(key, "Name"))
            {
                current = Flush(results, current) ?? new Builder();
                current.Name = value;
                continue;
            }

            if (IsKey(key, "Description"))
            {
                current ??= new Builder();
                current.Description = value;
                continue;
            }

            if (IsKey(key, "GUID"))
            {
                var match = GuidPattern.Match(value);
                if (match.Success)
                {
                    if (current?.InterfaceGuid.Length > 0) current = Flush(results, current) ?? new Builder();
                    current ??= new Builder();
                    current.InterfaceGuid = match.Value.ToLowerInvariant();
                }
                continue;
            }

            if (IsKey(key, "Physical address"))
            {
                current ??= new Builder();
                current.MacAddress = value;
                continue;
            }

            if (IsKey(key, "State"))
            {
                current ??= new Builder();
                current.State = value;
                continue;
            }

            if (IsKey(key, "SSID"))
            {
                current ??= new Builder();
                current.Ssid = value;
                continue;
            }

            if (IsKey(key, "Signal"))
            {
                current ??= new Builder();
                current.SignalPercent = ParsePercent(value);
                continue;
            }

            // Localised Windows translates the *key names* ("Signal", "Physical address", ...),
            // while the values stay language neutral: signal strength is still "NN%" and the MAC
            // address is still hex. Recover those two so Arabic/other localised Windows still
            // reports signal quality and adapter identity.
            if (current is not null)
            {
                if (current.SignalPercent is null && PercentValuePattern.IsMatch(value))
                {
                    current.SignalPercent = ParsePercent(value);
                    continue;
                }

                if (current.MacAddress.Length == 0 && MacAddressPattern.IsMatch(value))
                {
                    current.MacAddress = value;
                }
            }
        }

        Flush(results, current);
        return results;
    }

    private static Builder? Flush(List<WifiInterfaceInfo> results, Builder? current)
    {
        if (current is null) return null;
        if (current.HasData)
        {
            results.Add(current.ToRecord());
        }

        return null;
    }

    private static bool IsKey(string key, string expected) =>
        string.Equals(key.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static int? ParsePercent(string value)
    {
        var match = PercentPattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var percent)) return null;
        return Math.Clamp(percent, 0, 100);
    }

    private sealed class Builder
    {
        public string InterfaceGuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Ssid { get; set; } = string.Empty;
        public int? SignalPercent { get; set; }
        public string MacAddress { get; set; } = string.Empty;

        public bool HasData =>
            InterfaceGuid.Length > 0 || Name.Length > 0 || Ssid.Length > 0 ||
            SignalPercent.HasValue || MacAddress.Length > 0;

        public WifiInterfaceInfo ToRecord() => new(
            InterfaceGuid, Name, Description, State, Ssid, SignalPercent, MacAddress);
    }
}
