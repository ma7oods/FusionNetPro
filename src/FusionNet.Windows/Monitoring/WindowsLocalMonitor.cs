using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;
using FusionNet.Windows.Native;
using FusionNet.Windows.Network;

namespace FusionNet.Windows.Monitoring;

public sealed class WindowsLocalMonitor : ILocalMonitor
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<LocalMonitorReport> ScanAsync(bool includeSecurityInventory, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Task.Run(() => CollectAsync(includeSecurityInventory, cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private static async Task<LocalMonitorReport> CollectAsync(bool security, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        var before = ReadProcesses(token);
        var firstTime = watch.Elapsed;
        await Task.Delay(1000, token).ConfigureAwait(false);
        var after = ReadProcesses(token);
        var interval = watch.Elapsed - firstTime;
        var totalMemory = Win32Native.GetMemoryStatus().TotalPhysicalBytes;
        var processes = after.Values.Select(current =>
        {
            before.TryGetValue(current.Pid, out var previous);
            var cpu = ProcessSampling.Cpu(previous?.Identity, current.Identity, previous?.Cpu, current.Cpu, interval, Environment.ProcessorCount);
            return new ProcessObservation(current.Pid, current.Name, current.Identity,
                cpu,
                current.Memory, totalMemory > 0 && current.Memory.HasValue ? current.Memory.Value / (double)totalMemory * 100 : null,
                null, null, current.Identity is null ? "Identity unavailable/access denied/exited" : cpu.HasValue ? "Two CPU samples; network rates unavailable" : "CPU unavailable: first sample, PID reuse or access denied");
        }).OrderByDescending(process => process.CpuPercent).ToArray();
        var sections = new List<InventorySection>
        {
            new("Processes", "Partial", "CPU normalized over all logical processors; ~1 second sample. Access-denied identities remain unavailable. Per-process transfer rates are not yet collected.", [])
        };
        var connections = new List<ConnectionObservation>();
        if(security)sections.Add(ExplorerLaunchReader.Read(token));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        var names = security
            ? new[] { "Connections", "Defender", "Startup", "Services", "Tasks", "Extensions", "Backups", "InstalledApps", "Hardware" }
            : new[] { "Connections" };
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) { sections.Add(new(name, "Unavailable", "Inventory time budget exceeded.", [])); continue; }
            try
            {
                var section = await QueryAsync(name, deadline.Token).ConfigureAwait(false);
                sections.Add(new(name, section.Status, section.Detail, section.Items));
                connections.AddRange(section.Connections.Select(connection => connection with
                { Identity = after.TryGetValue(connection.Pid, out var process) && process.Identity is { } identity && GameBoostWorker.IdentityAlive(identity) ? identity : null }));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { sections.Add(new(name, "Unavailable", "Inventory time budget exceeded.", [])); }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException)
            { sections.Add(new(name, "Unavailable", ex.Message, [])); }
        }
        // Game Boost also needs signatures before it can offer an application as a block target.
        var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var signatureOrder = Enumerable.Range(0, processes.Length).OrderByDescending(index => ManagedAppCatalog.All.Any(app => app.MatchesProcess(processes[index].Name)));
        foreach (var index in signatureOrder)
        {
            token.ThrowIfCancellationRequested();
            var path = processes[index].Identity?.Path;
            if (path is null) continue;
            if (!verified.TryGetValue(path, out var signature) && verified.Count < 40 && watch.Elapsed < TimeSpan.FromSeconds(65))
                verified[path] = signature = OfflineSignature.Verify(path);
            processes[index] = processes[index] with { Signature = signature ?? "NotChecked" };
        }
        sections.Add(new("Signatures", "Partial", $"Offline embedded-signature verification: {verified.Count} distinct executables. Catalog signatures/revocation freshness are not checked; unsigned does not mean malicious.", []));
        return new(started, DateTimeOffset.UtcNow, processes, connections, sections,
            SecurityAssessment.Assess(processes, connections, sections));
    }

    private sealed record RawProcess(int Pid, string Name, ProcessIdentity? Identity, TimeSpan? Cpu, long? Memory);
    private static Dictionary<int, RawProcess> ReadProcesses(CancellationToken token)
    {
        var result = new Dictionary<int, RawProcess>();
        var all = Process.GetProcesses();
        try
        {
            foreach (var process in all)
            {
                token.ThrowIfCancellationRequested();
                var id = process.Id;
                string name;
                try { name = process.ProcessName; } catch { name = "Unavailable"; }
                ProcessIdentity? identity = null;
                TimeSpan? cpu = null;
                long? memory = null;
                try { if (Win32Native.TryQueryProcessPath(id, out var path, out _)) identity = new(id, process.StartTime.ToUniversalTime(), path); } catch { /* unavailable */ }
                try { cpu = process.TotalProcessorTime; } catch { /* unavailable */ }
                try { memory = process.WorkingSet64; } catch { /* unavailable */ }
                result[id] = new(id, name, identity, cpu, memory);
            }
        }
        finally { foreach (var process in all) process.Dispose(); }
        return result;
    }

    internal sealed record QueryResult(string Status, string Detail, InventoryItem[] Items, ConnectionObservation[] Connections);
    internal static QueryResult ParseQuery(string json)
    {
        var result = JsonSerializer.Deserialize<QueryResult>(json, JsonOptions) ?? throw new JsonException("Empty inventory response.");
        if (result.Status is not ("Complete" or "Partial") || result.Items is null || result.Connections is null ||
            result.Detail is null || result.Detail.Length > 8192 || result.Items.Length > 500 || result.Connections.Length > 2000 ||
            result.Items.Any(item => item is null || item.Name is null || item.Name.Length > 256 || item.Source is null || item.Source.Length > 2048 || item.Detail is null || item.Detail.Length > 2048 || item.Pid < 0) ||
            result.Connections.Any(row => row is null || row.Pid < 0 || row.Protocol is not ("TCP" or "UDP") || row.State is null || row.State.Length > 80 ||
                row.LocalPort is < 0 or > 65535 || row.RemotePort is < 0 or > 65535 || !global::System.Net.IPAddress.TryParse(row.LocalAddress, out _) ||
                row.RemoteAddress is not null && !global::System.Net.IPAddress.TryParse(row.RemoteAddress, out _)))
            throw new JsonException("Invalid or oversized inventory response.");
        return result;
    }

    internal static async Task<QueryResult> QueryAsync(string section, CancellationToken token)
    {
        string[] allowed = ["Connections", "Defender", "Startup", "Services", "Tasks", "Extensions", "Backups", "InstalledApps", "Hardware", "SecurityProducts"];
        if (!allowed.Contains(section)) throw new ArgumentException("Unknown inventory section.", nameof(section));
        var script = "& {\n" + ReadScript("InventoryQueries.ps1") + "\n} '" + section + "'";
        var result = await NetworkCommandRunner.RunAsync(PowerShellPath,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], 12000, token).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException("Read-only query failed or timed out: " + section + " (exit " + result.ExitCode + "). No protection verdict is available.");
        return ParseQuery(result.StandardOutput);
    }

    internal static string PowerShellPath => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
    internal static string ReadScript(string file)
    {
        using var stream = typeof(WindowsLocalMonitor).Assembly.GetManifestResourceStream("FusionNet.Windows.Monitoring." + file)
            ?? throw new InvalidOperationException("Embedded script missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
