using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using Microsoft.Win32;

namespace FusionNet.Windows.Network;

/// <summary>
/// Real DNS management: raw-UDP latency benchmark against each provider server, journaled
/// <c>netsh</c> apply/reset and a resolver cache flush.
///
/// Everything that mutates the system is journaled first and requires elevation; read-only
/// operations (benchmark) work for standard users. No method throws across the service boundary.
/// </summary>
public sealed class DnsService : IDnsService
{
    public const string DefaultBenchmarkDomain = "example.com";
    public const int QueriesPerServer = 3;
    public const int MaxConcurrency = 4;
    public const int QueryTimeoutMs = 1_500;
    public const int MaxServers = 4;

    private readonly IActivityLog _activity;

    public DnsService(IActivityLog activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public async Task<IReadOnlyList<DnsBenchmarkResult>> BenchmarkAsync(
        IEnumerable<DnsProvider> providers,
        IProgress<DnsBenchmarkResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var work = new List<(DnsProvider Provider, string Server)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (providers is not null)
        {
            foreach (var provider in providers)
            {
                if (provider?.Servers is null) continue;
                foreach (var server in provider.Servers)
                {
                    if (string.IsNullOrWhiteSpace(server)) continue;
                    if (!DnsCatalog.IsValidIPv4(server)) continue;
                    if (!seen.Add(server.Trim())) continue;
                    work.Add((provider, server.Trim()));
                }
            }
        }

        if (work.Count == 0) return [];

        _activity.Info("DNS", $"DNS benchmark started: {work.Count} servers x {QueriesPerServer} {DefaultBenchmarkDomain} queries.");

        var results = new ConcurrentBag<DnsBenchmarkResult>();
        using var gate = new SemaphoreSlim(Math.Min(MaxConcurrency, work.Count));
        var tasks = work.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await BenchmarkServerAsync(item.Provider, item.Server, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                progress?.Report(result);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var ordered = results
            .OrderBy(result => result.Success ? 0 : 1)
            .ThenBy(result => result.LatencyMs)
            .ThenBy(result => result.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Server, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fastest = ordered.FirstOrDefault(result => result.Success);
        _activity.Info("DNS", fastest is null
            ? "DNS benchmark finished: no server responded."
            : $"DNS benchmark finished: {ordered.Count(result => result.Success)}/{ordered.Length} servers responded. Fastest: {fastest.ProviderName} ({fastest.Server}) at {fastest.LatencyMs:0.#} ms.");

        return ordered;
    }

    public OperationResult ApplyServers(IReadOnlyList<string> servers, string? adapterId, IJournal journal)
    {
        if (journal is null)
            return OperationResult.Fail("The journal is unavailable; refusing to change DNS without a restore record.");
        if (servers is null || servers.Count == 0)
            return OperationResult.Fail("No DNS servers were supplied.");

        var cleaned = servers
            .Where(server => DnsCatalog.IsValidIPv4(server))
            .Select(server => server.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxServers)
            .ToArray();

        if (cleaned.Length == 0)
            return OperationResult.Fail("No valid IPv4 DNS servers were supplied.");

        if (!NetworkInterop.IsElevated)
        {
            _activity.Warning("DNS", "DNS change blocked: the process is not elevated.");
            return OperationResult.NotElevated("Changing DNS servers");
        }

        var adapter = ResolveAdapter(adapterId);
        if (adapter is null)
            return OperationResult.Fail("No suitable network adapter was found for the DNS change.");

        var previous = DescribeCurrentDns(adapter);
        var applied = string.Join(",", cleaned);

        JournalEntry? journalEntry;
        try
        {
            journalEntry = journal.Record(JournalKind.Dns, adapter.Id,
                $"DNS servers for {adapter.Name}: {applied}", previous, applied);
        }
        catch (Exception ex)
        {
            _activity.Error("DNS", "Could not journal the DNS change; aborting before touching the adapter.", ex.Message);
            return OperationResult.Fail("The DNS change could not be journaled, so nothing was modified.", [ex.Message]);
        }

        var details = new List<string>();
        var netsh = NetworkCommandRunner.SystemTool("netsh.exe");

        // Primary server replaces the current list.
        var setResult = NetworkCommandRunner.Run(netsh,
        [
            "interface", "ip", "set", "dns",
            $"name={adapter.Name}",
            "static", cleaned[0], "validate=no"
        ], timeoutMs: 8_000);

        details.Add(SummarizeNetsh("primary", cleaned[0], setResult));
        if (!setResult.Success)
        {
            _activity.Error("DNS", $"Failed to set the primary DNS server on {adapter.Name}.", NetworkCommandRunner.Summarize(setResult.CombinedOutput));
            return OperationResult.Fail(
                $"Windows rejected the primary DNS server on {adapter.Name}.",
                [.. details, DescribeFailure(setResult)]);
        }

        // Remaining servers are appended as additional resolvers.
        for (var index = 1; index < cleaned.Length; index++)
        {
            var addResult = NetworkCommandRunner.Run(netsh,
            [
                "interface", "ip", "add", "dns",
                $"name={adapter.Name}",
                cleaned[index],
                $"index={index + 1}",
                "validate=no"
            ], timeoutMs: 8_000);

            details.Add(SummarizeNetsh($"index {index + 1}", cleaned[index], addResult));
            if (!addResult.Success)
            {
                // A primary was already applied: roll the adapter back to its journaled state
                // instead of leaving it with a half-applied server list.
                var rollback = RestorePreviousDns(adapter, previous);
                if (rollback.Success)
                {
                    MarkRestored(journal, journalEntry);
                    details.Add("Rolled back to the previous DNS configuration.");
                }
                else
                {
                    details.Add("Rollback failed, the journal entry stays open for recovery: " + rollback.Message);
                }

                _activity.Error("DNS", $"Failed to add DNS server {cleaned[index]} on {adapter.Name}.", NetworkCommandRunner.Summarize(addResult.CombinedOutput));
                return OperationResult.Fail(
                    $"Windows rejected DNS server {cleaned[index]} (index {index + 1}) on {adapter.Name}.",
                    [.. details, DescribeFailure(addResult)]);
            }
        }

        TryFlushCacheQuietly();

        _activity.Success("DNS", $"DNS servers applied on {adapter.Name}: {applied}.", $"Previous: {previous}");
        return OperationResult.Ok($"DNS servers on {adapter.Name} are now {applied}.", details);
    }

    public OperationResult ResetToAutomatic(string? adapterId, IJournal journal)
    {
        if (journal is null)
            return OperationResult.Fail("The journal is unavailable; refusing to change DNS without a restore record.");

        if (!NetworkInterop.IsElevated)
        {
            _activity.Warning("DNS", "DNS reset blocked: the process is not elevated.");
            return OperationResult.NotElevated("Resetting DNS to automatic");
        }

        var adapter = ResolveAdapter(adapterId);
        if (adapter is null)
            return OperationResult.Fail("No suitable network adapter was found for the DNS reset.");

        var previous = DescribeCurrentDns(adapter);

        try
        {
            journal.Record(JournalKind.Dns, adapter.Id,
                $"DNS servers for {adapter.Name}: automatic (DHCP)", previous, "dhcp");
        }
        catch (Exception ex)
        {
            _activity.Error("DNS", "Could not journal the DNS reset; aborting before touching the adapter.", ex.Message);
            return OperationResult.Fail("The DNS reset could not be journaled, so nothing was modified.", [ex.Message]);
        }

        var netsh = NetworkCommandRunner.SystemTool("netsh.exe");
        var result = NetworkCommandRunner.Run(netsh,
        [
            "interface", "ip", "set", "dns",
            $"name={adapter.Name}",
            "source=dhcp"
        ], timeoutMs: 8_000);

        if (!result.Success)
        {
            _activity.Error("DNS", $"Failed to reset DNS to DHCP on {adapter.Name}.", NetworkCommandRunner.Summarize(result.CombinedOutput));
            return OperationResult.Fail(
                $"Windows could not reset DNS on {adapter.Name} to automatic.",
                [DescribeFailure(result)]);
        }

        TryFlushCacheQuietly();

        _activity.Success("DNS", $"DNS reset to automatic on {adapter.Name}.", $"Previous: {previous}");
        return OperationResult.Ok($"DNS on {adapter.Name} is automatic (DHCP) again.", [$"Previous: {previous}"]);
    }

    public OperationResult FlushCache()
    {
        if (NetworkInterop.TryFlushResolverCache())
        {
            _activity.Success("DNS", "DNS resolver cache flushed through dnsapi.");
            return OperationResult.Ok("DNS resolver cache flushed.");
        }

        var result = NetworkCommandRunner.Run(
            NetworkCommandRunner.SystemTool("ipconfig.exe"),
            ["/flushdns"],
            timeoutMs: 8_000);

        if (result.Success)
        {
            _activity.Success("DNS", "DNS resolver cache flushed through ipconfig.");
            return OperationResult.Ok("DNS resolver cache flushed.", ["Fallback: ipconfig /flushdns"]);
        }

        var output = NetworkCommandRunner.Summarize(result.CombinedOutput);
        _activity.Warning("DNS", "DNS cache flush failed.", output);
        return OperationResult.Fail(
            "Could not flush the DNS resolver cache.",
            string.IsNullOrWhiteSpace(output) ? null : [output]);
    }

    private static async Task<DnsBenchmarkResult> BenchmarkServerAsync(
        DnsProvider provider,
        string server,
        CancellationToken cancellationToken)
    {
        var roundTrips = new List<double>(QueriesPerServer);
        var errors = new List<string>(QueriesPerServer);

        for (var attempt = 0; attempt < QueriesPerServer; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await DnsQueryClient
                .QueryAsync(server, DefaultBenchmarkDomain, QueryTimeoutMs, cancellationToken)
                .ConfigureAwait(false);

            if (response.Success) roundTrips.Add(response.RoundTripMs);
            else errors.Add(response.ErrorText ?? "failed");
        }

        if (roundTrips.Count == 0)
        {
            var errorText = errors.Count == 0 ? "no response" : string.Join("; ", errors.Distinct(StringComparer.OrdinalIgnoreCase));
            return new DnsBenchmarkResult(provider.Id, provider.Name, server, 0, false, errorText) { Score = 0 };
        }

        roundTrips.Sort();
        var median = roundTrips.Count % 2 == 1
            ? roundTrips[roundTrips.Count / 2]
            : (roundTrips[roundTrips.Count / 2 - 1] + roundTrips[roundTrips.Count / 2]) / 2;

        return new DnsBenchmarkResult(
            provider.Id,
            provider.Name,
            server,
            Math.Round(median, 1),
            true,
            errors.Count == 0 ? null : $"{errors.Count}/{QueriesPerServer} queries failed")
        {
            Score = ScoreFor(median)
        };
    }

    /// <summary>Maps resolver latency to a 0-100 score: 5 ms = 100, 200 ms = 0.</summary>
    public static int ScoreFor(double latencyMs)
    {
        if (double.IsNaN(latencyMs) || latencyMs <= 5) return 100;
        if (latencyMs >= 200) return 0;
        return (int)Math.Round(Math.Clamp(100 - ((latencyMs - 5) * 100d / 195d), 0, 100));
    }

    private static NetworkInterface? ResolveAdapter(string? adapterId)
    {
        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception)
        {
            return null;
        }

        var candidates = all.Where(NetworkProbeHelper.IsPhysicalAdapter).ToArray();
        if (candidates.Length == 0) return null;

        if (!string.IsNullOrWhiteSpace(adapterId))
        {
            var key = adapterId.Trim();
            var normalized = NetworkProbeHelper.NormalizeId(key);
            var match = candidates.FirstOrDefault(candidate =>
                    normalized.Length > 0 && NetworkProbeHelper.NormalizeId(candidate.Id) == normalized)
                ?? candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Description, key, StringComparison.OrdinalIgnoreCase));

            // An explicit choice must never change a different adapter after a disconnect.
            return match;
        }

        var defaultIndex = NetworkInterop.GetDefaultInterfaceIndex();
        if (defaultIndex.HasValue)
        {
            var byRoute = candidates.FirstOrDefault(candidate =>
                NetworkProbeHelper.GetInterfaceIndex(candidate) == defaultIndex.Value);
            if (byRoute is not null) return byRoute;
        }

        return candidates.FirstOrDefault(candidate => candidate.OperationalStatus == OperationalStatus.Up)
               ?? candidates[0];
    }

    private static string DescribeCurrentDns(NetworkInterface adapter)
    {
        // The managed API cannot tell DHCP-provided DNS from a static list (DnsAddresses contains
        // both), and restoring "dhcp" as concrete servers would silently convert the adapter to
        // static DNS. The TCP/IP registry sub-key is authoritative: a non-empty "NameServer" is
        // the static list, an empty/missing one means the servers come from DHCP / are automatic.
        var configured = ReadConfiguredDnsList(adapter);
        if (configured is not null) return configured;

        try
        {
            var servers = adapter.GetIPProperties().DnsAddresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .ToArray();

            return servers.Length > 0 ? string.Join(",", servers) : "dhcp";
        }
        catch (Exception)
        {
            // Adapter vanished or has no IPv4 stack — the safe journal value is DHCP.
            return "dhcp";
        }
    }

    /// <summary>
    /// Reads the static DNS list from the interface's TCP/IP registry key. Returns the normalised
    /// server list, "dhcp" when DNS is automatic, or null when the registry cannot be read.
    /// </summary>
    private static string? ReadConfiguredDnsList(NetworkInterface adapter)
    {
        const string interfaceRoot = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(interfaceRoot, writable: false);
            if (interfaces is null) return null;

            var keyName = interfaces.GetSubKeyNames().FirstOrDefault(name =>
                NormalizeGuid(name) == NormalizeGuid(adapter.Id));
            if (keyName is null) return null;

            using var key = interfaces.OpenSubKey(keyName, writable: false);
            if (key is null) return null;

            var nameServer = key.GetValue("NameServer", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (string.IsNullOrWhiteSpace(nameServer)) return "dhcp";

            var servers = nameServer
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(DnsCatalog.IsValidIPv4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return servers.Length > 0 ? string.Join(",", servers) : nameServer.Trim();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException
                                       or ArgumentException or NotSupportedException)
        {
            // Registry not readable: fall back to the managed API instead of guessing.
            return null;
        }
    }

    private static string NormalizeGuid(string value) =>
        value.Trim().Trim('{', '}').ToLowerInvariant();

    /// <summary>
    /// Best-effort rollback of <see cref="ApplyServers"/> after a mid-way failure: re-applies the
    /// previously journaled DNS state (automatic/DHCP or the recorded static server list).
    /// </summary>
    private static OperationResult RestorePreviousDns(NetworkInterface adapter, string? previous)
    {
        var netsh = NetworkCommandRunner.SystemTool("netsh.exe");
        var isDhcp = string.IsNullOrWhiteSpace(previous) ||
                     string.Equals(previous.Trim(), "dhcp", StringComparison.OrdinalIgnoreCase);

        if (isDhcp)
        {
            var reset = NetworkCommandRunner.Run(netsh,
            [
                "interface", "ip", "set", "dns",
                $"name={adapter.Name}",
                "source=dhcp"
            ], timeoutMs: 8_000);

            return reset.Success
                ? OperationResult.Ok("DNS rolled back to automatic.")
                : OperationResult.Fail("DNS rollback to automatic failed.", [DescribeFailure(reset)]);
        }

        var servers = previous!
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(DnsCatalog.IsValidIPv4)
            .Take(MaxServers)
            .ToArray();
        if (servers.Length == 0) return OperationResult.Fail("The journaled DNS server list is invalid; cannot roll back.");

        var set = NetworkCommandRunner.Run(netsh,
        [
            "interface", "ip", "set", "dns",
            $"name={adapter.Name}",
            "static", servers[0], "validate=no"
        ], timeoutMs: 8_000);
        if (!set.Success) return OperationResult.Fail("DNS rollback could not set the primary server.", [DescribeFailure(set)]);

        for (var index = 1; index < servers.Length; index++)
        {
            var add = NetworkCommandRunner.Run(netsh,
            [
                "interface", "ip", "add", "dns",
                $"name={adapter.Name}",
                servers[index],
                $"index={index + 1}",
                "validate=no"
            ], timeoutMs: 8_000);

            if (!add.Success) return OperationResult.Fail("DNS rollback could not add a secondary server.", [DescribeFailure(add)]);
        }

        return OperationResult.Ok("DNS rolled back to the previous servers.");
    }

    private static void MarkRestored(IJournal journal, JournalEntry? entry)
    {
        if (entry is null) return;

        try
        {
            journal.MarkRestored(entry.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The rollback succeeded; a stale open entry is harmless and will be retried.
        }
    }

    private void TryFlushCacheQuietly()
    {
        try
        {
            if (NetworkInterop.TryFlushResolverCache()) return;

            var result = NetworkCommandRunner.Run(
                NetworkCommandRunner.SystemTool("ipconfig.exe"),
                ["/flushdns"],
                timeoutMs: 8_000);

            if (!result.Success)
            {
                _activity.Warning("DNS", "DNS cache flush after applying servers failed.",
                    NetworkCommandRunner.Summarize(result.CombinedOutput));
            }
        }
        catch (Exception ex)
        {
            // Flushing is a convenience after a successful change; never turn it into a failure.
            _activity.Warning("DNS", "DNS cache flush after applying servers threw.", ex.Message);
        }
    }

    private static string SummarizeNetsh(string label, string server, NetworkCommandResult result)
    {
        var output = NetworkCommandRunner.Summarize(result.CombinedOutput);
        return string.IsNullOrWhiteSpace(output)
            ? $"{label} {server}: exit {result.ExitCode}"
            : $"{label} {server}: exit {result.ExitCode} — {output}";
    }

    private static string DescribeFailure(NetworkCommandResult result)
    {
        if (result.TimedOut) return "netsh timed out.";
        var output = NetworkCommandRunner.Summarize(result.CombinedOutput);
        return string.IsNullOrWhiteSpace(output) ? $"netsh exited with code {result.ExitCode}." : output;
    }
}
