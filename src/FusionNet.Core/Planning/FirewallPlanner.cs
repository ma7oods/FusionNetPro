using FusionNet.Core.Models;

namespace FusionNet.Core.Planning;

/// <summary>A Windows Firewall rule that FusionNet owns (all names start with <see cref="FirewallPlanner.RulePrefix"/>).</summary>
public sealed record PlannedFirewallRule(
    string Name,
    string DisplayName,
    string ProgramPath,
    string? ServiceName = null);

/// <summary>An application the plan wants to control, already resolved to something executable.</summary>
public sealed record AppRuleTarget(
    string AppId,
    string DisplayName,
    AppControlAction Action,
    IReadOnlyList<string> ProcessNames,
    IReadOnlyList<string> KnownPaths,
    string? CustomPath = null)
{
    public static AppRuleTarget FromManagedApp(ManagedApp app) =>
        new(app.Id, app.Name, app.DefaultAction, app.ProcessNames, app.Paths);

    public static AppRuleTarget FromCustom(CustomManagedApp app) =>
        new(app.Id, app.Name, app.Action, [app.ProcessName], [], app.ExecutablePath);
}

/// <summary>
/// Pure, testable planner that turns app rules + running processes into firewall rules.
/// Blocking never targets system-critical processes, the running game, or Discord.
/// </summary>
public static class FirewallPlanner
{
    public const string RulePrefix = "FNP_";

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discordptb", "discordcanary",
        "system", "registry", "smss", "csrss", "wininit", "services", "lsass", "svchost",
        "winlogon", "explorer", "dwm", "fontdrvhost", "sihost", "taskhostw", "ctfmon",
        "runtimebroker", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "msmpeng", "nissrv", "securityhealthservice", "audiodg", "conhost", "dllhost"
    };

    public static bool IsProtected(string processName, GameProfile? game)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        if (ProtectedProcessNames.Contains(processName)) return true;
        if (GameCatalog.IsRiotSupportProcess(processName)) return true;
        return game is not null && game.MatchesProcess(processName);
    }

    /// <summary>Builds one outbound+inbound block rule per distinct executable path that should be blocked.</summary>
    public static IReadOnlyList<PlannedFirewallRule> Build(
        IEnumerable<AppRuleTarget> targets,
        IReadOnlyList<ProcessSnapshot> running,
        GameProfile? game)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(running);

        var rules = new Dictionary<string, PlannedFirewallRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets.Where(t => t.Action == AppControlAction.BlockNetwork))
        {
            foreach (var path in ResolvePaths(target, running, game))
            {
                var rule = CreateRule(target.DisplayName, path);
                rules.TryAdd(rule.Name, rule);
            }
        }

        return rules.Values
            .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Block rules for update services (used only when a service cannot be stopped).</summary>
    public static IReadOnlyList<PlannedFirewallRule> BuildServiceRules(
        IEnumerable<ServiceDefinition> services,
        string? svchostPath)
    {
        if (string.IsNullOrWhiteSpace(svchostPath) || !File.Exists(svchostPath)) return [];

        var rules = new Dictionary<string, PlannedFirewallRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in services.Where(service => service is not null &&
                                                          !string.IsNullOrWhiteSpace(service.ServiceName)))
        {
            var rule = new PlannedFirewallRule(
                $"{RulePrefix}SVC_{service.ServiceName.ToUpperInvariant()}",
                $"FusionNet: {service.NameEn}",
                Path.GetFullPath(svchostPath),
                service.ServiceName);
            rules.TryAdd(rule.Name, rule);
        }

        return rules.Values
            .OrderBy(rule => rule.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static PlannedFirewallRule CreateRule(string displayName, string programPath)
    {
        var fullPath = Path.GetFullPath(programPath);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant())))[..16];
        return new PlannedFirewallRule($"{RulePrefix}APP_{hash}", $"FusionNet: {displayName}", fullPath);
    }

    private static IEnumerable<string> ResolvePaths(AppRuleTarget target, IReadOnlyList<ProcessSnapshot> running, GameProfile? game)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(target.CustomPath))
        {
            var custom = Path.GetFullPath(target.CustomPath);
            if (!IsProtected(Path.GetFileNameWithoutExtension(custom), game) && seen.Add(custom))
                yield return custom;
            yield break;
        }

        var processNames = new HashSet<string>(target.ProcessNames, StringComparer.OrdinalIgnoreCase);

        // 1) The application is running: block its real executable path.
        foreach (var process in running.Where(p => processNames.Contains(p.ProcessName)))
        {
            if (string.IsNullOrWhiteSpace(process.ExecutablePath)) continue;
            if (IsProtected(process.ProcessName, game)) continue;
            var full = Path.GetFullPath(process.ExecutablePath);
            if (seen.Add(full)) yield return full;
        }

        // 2) Not running: fall back to known install locations so the rule exists before launch.
        foreach (var hint in target.KnownPaths)
        {
            var expanded = KnownPaths.Expand(hint);
            if (expanded is null || !File.Exists(expanded)) continue;
            if (IsProtected(Path.GetFileNameWithoutExtension(expanded), game)) continue;
            var full = Path.GetFullPath(expanded);
            if (seen.Add(full)) yield return full;
        }
    }
}

/// <summary>Expands %VARS% (including the 32-bit Program Files alias) in catalog paths.</summary>
public static class KnownPaths
{
    public static string? Expand(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded.Contains('%'))
        {
            expanded = expanded
                .Replace("%ProgramFiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)
                .Replace("%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
                .Replace("%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
                .Replace("%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
                .Replace("%SystemRoot%", Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        }

        return expanded.Contains('%') ? null : expanded;
    }

    public static bool TryResolve(string? path, out string resolved)
    {
        resolved = string.Empty;
        var expanded = Expand(path);
        if (expanded is null || !File.Exists(expanded)) return false;
        resolved = Path.GetFullPath(expanded);
        return true;
    }
}
