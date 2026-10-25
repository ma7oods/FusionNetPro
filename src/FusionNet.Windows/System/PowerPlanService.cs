using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Security;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.System;

/// <summary>
/// Reads and switches the active Windows power plan through <c>powercfg</c>. Parsing is GUID
/// based so localised powercfg output (Arabic Windows included) cannot break it.
/// </summary>
public sealed class PowerPlanService : IPowerPlanService
{
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    /// <summary>
    /// powercfg's language-neutral alias for the High performance plan (SCHEME_MAX). It must not
    /// be confused with SCHEME_MIN, which is the *Power saver* plan.
    /// </summary>
    public const string MaximumSchemeAlias = "SCHEME_MAX";

    private const string Category = "Fusion";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QueryCacheLifetime = TimeSpan.FromSeconds(2);
    private static readonly Regex GuidPattern = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant);
    private static readonly Regex NamePattern = new(@"\(([^)]*)\)", RegexOptions.CultureInvariant);

    private readonly IActivityLog _activity;
    private readonly object _cacheGate = new();
    private ActivePlan _cache = new(null, null);
    private DateTimeOffset _cacheValidUntilUtc = DateTimeOffset.MinValue;

    public PowerPlanService(IActivityLog activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public string? ActivePlanName => QueryActivePlan().Name;

    public string? ActivePlanGuid => QueryActivePlan().Guid;

    public bool IsHighPerformanceActive =>
        string.Equals(ActivePlanGuid, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ActivePlanGuid, UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase);

    public OperationResult SwitchToHighPerformance(IJournal journal)
    {
        if (!ElevationService.CheckIsElevated()) return OperationResult.NotElevated("Switching the power plan");

        var current = QueryActivePlan(forceRefresh: true);
        if (string.Equals(current.Guid, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("High performance plan is already active.");
        }

        var entry = journal is null
            ? null
            : SafeRecord(journal, JournalKind.PowerPlan, "powerplan", "Switched to the High performance power plan",
                current.Guid ?? "unknown", HighPerformanceGuid);

        if (TrySetActive(HighPerformanceGuid) ||
            TrySetActive(UltimatePerformanceGuid) ||
            TrySetActive(MaximumSchemeAlias))
        {
            _activity.Success(Category, "Power plan switched to High performance.");
            return OperationResult.Ok("Power plan switched to High performance.");
        }

        if (entry is not null) SafeMarkRestored(journal!, entry.Id);
        _activity.Warning(Category, "Could not switch to the High performance power plan.");
        return OperationResult.Fail("Could not switch to the High performance power plan.");
    }

    public OperationResult Restore(string? planGuid, IJournal journal)
    {
        if (string.IsNullOrWhiteSpace(planGuid)) return OperationResult.Ok("No previous power plan was recorded.");
        if (!Guid.TryParse(planGuid.Trim(), out var parsed)) return OperationResult.Fail("The recorded power plan identifier is invalid.");

        var target = parsed.ToString("D").ToLowerInvariant();
        var current = QueryActivePlan(forceRefresh: true);
        if (string.Equals(current.Guid, target, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("The previous power plan is already active.");
        }

        if (!ElevationService.CheckIsElevated()) return OperationResult.NotElevated("Restoring the power plan");

        if (TrySetActive(target))
        {
            _activity.Success(Category, "Previous power plan restored.");
            return OperationResult.Ok("Previous power plan restored.");
        }

        _activity.Warning(Category, $"Could not restore the previous power plan ({target}).");
        return OperationResult.Fail("Could not restore the previous power plan.");
    }

    private bool TrySetActive(string scheme)
    {
        var result = RunPowerCfg(["/setactive", scheme]);
        if (result.TimedOut) return false;

        // Verify against the real active scheme rather than trusting the exit code.
        var active = QueryActivePlan(forceRefresh: true).Guid;
        if (string.Equals(active, scheme, StringComparison.OrdinalIgnoreCase)) return true;

        // SCHEME_MAX is only an alias: powercfg reports the real High performance GUID.
        return string.Equals(scheme, MaximumSchemeAlias, StringComparison.OrdinalIgnoreCase) &&
               IsHighPerformancePlan(active);
    }

    /// <summary>True when the GUID is one of the two high-performance power schemes.</summary>
    private static bool IsHighPerformancePlan(string? guid) =>
        string.Equals(guid, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase);

    private ActivePlan QueryActivePlan(bool forceRefresh = false)
    {
        lock (_cacheGate)
        {
            if (!forceRefresh && DateTimeOffset.UtcNow < _cacheValidUntilUtc) return _cache;
        }

        var plan = ReadActivePlan();
        lock (_cacheGate)
        {
            _cache = plan;
            _cacheValidUntilUtc = DateTimeOffset.UtcNow + QueryCacheLifetime;
        }

        return plan;
    }

    private ActivePlan ReadActivePlan()
    {
        var result = RunPowerCfg(["/getactivescheme"]);
        if (result.TimedOut || string.IsNullOrWhiteSpace(result.Output)) return new ActivePlan(null, null);

        var guidMatch = GuidPattern.Match(result.Output);
        if (!guidMatch.Success) return new ActivePlan(null, null);

        var guid = guidMatch.Value.ToLowerInvariant();
        string? name = null;
        var nameMatch = NamePattern.Match(result.Output[guidMatch.Index..]);
        if (nameMatch.Success)
        {
            var value = nameMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value)) name = value;
        }

        return new ActivePlan(guid, name ?? guid);
    }

    private CommandResult RunPowerCfg(IEnumerable<string> arguments)
    {
        var executable = ResolvePowerCfgPath();
        if (executable is null) return new CommandResult(-1, string.Empty, "powercfg.exe was not found.", false);
        return ProcessRunner.Run(executable, arguments, QueryTimeout);
    }

    private static string? ResolvePowerCfgPath()
    {
        try
        {
            var systemDirectory = Environment.SystemDirectory;
            if (string.IsNullOrWhiteSpace(systemDirectory)) return null;
            var path = Path.Combine(systemDirectory, "powercfg.exe");
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SecurityException)
        {
            return null;
        }
    }

    private static JournalEntry? SafeRecord(IJournal journal, JournalKind kind, string target, string description,
        string? previousValue, string appliedValue)
    {
        try
        {
            return journal.Record(kind, target, description, previousValue, appliedValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine("Power plan journal record failed: " + ex.Message);
            return null;
        }
    }

    private static void SafeMarkRestored(IJournal journal, string entryId)
    {
        try { journal.MarkRestored(entryId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine("Power plan journal mark-restored failed: " + ex.Message);
        }
    }

    private readonly record struct ActivePlan(string? Guid, string? Name);
}
