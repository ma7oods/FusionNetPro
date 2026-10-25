using System.Text.RegularExpressions;
using System.Security;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Planning;

namespace FusionNet.Windows.System;

/// <summary>
/// Owns the temporary Windows Firewall block rules Fusion Mode creates. Every rule lives in
/// an outbound/inbound pair named <c>FNP_..._OUT</c> / <c>FNP_..._IN</c>, so cleanup can never
/// touch a rule the user created by hand. All netsh calls are argument-list based.
/// </summary>
public sealed class FirewallService : IFirewallService
{
    private const string Category = "Firewall";
    private const string OutSuffix = "_OUT";
    private const string InSuffix = "_IN";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly Regex OwnedRuleNamePattern = new(@"FNP_[A-Za-z0-9_]+", RegexOptions.CultureInvariant);

    /// <summary>System executables that must never be blocked directly.</summary>
    private static readonly HashSet<string> CriticalExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "lsass.exe", "csrss.exe", "wininit.exe", "services.exe", "smss.exe",
        "winlogon.exe", "explorer.exe", "dwm.exe", "fontdrvhost.exe", "sihost.exe", "taskhostw.exe",
        "ctfmon.exe", "runtimebroker.exe", "searchhost.exe", "startmenuexperiencehost.exe",
        "shellexperiencehost.exe", "audiodg.exe", "conhost.exe", "dllhost.exe", "msmpeng.exe",
        "nissrv.exe", "securityhealthservice.exe", "wudfhost.exe", "system", "registry"
    };

    private readonly IActivityLog _activity;
    private readonly string? _netshPath;

    public FirewallService(IActivityLog activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _netshPath = ResolveNetshPath();
    }

    public bool IsSupported => _netshPath is not null;

    public OperationResult ApplyRules(IReadOnlyList<PlannedFirewallRule> rules)
    {
        if (rules is null || rules.Count == 0) return OperationResult.Ok("No firewall rules were needed.");

        if (!IsSupported) return OperationResult.Fail("Windows Firewall (netsh) is not available on this machine.");
        if (!ElevationService.CheckIsElevated()) return OperationResult.NotElevated("Applying firewall rules");

        var created = new List<string>();
        var problems = new List<string>();
        var skippedRules = new List<string>();

        foreach (var rule in rules.DistinctBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase))
        {
            var skipReason = ValidateRule(rule);
            if (skipReason is not null)
            {
                // A stale managed-app path (app uninstalled) or a protected executable is a
                // per-rule skip, not a failure: aborting the whole Fusion Mode session because
                // one optional rule is no longer applicable would be worse for the user.
                skippedRules.Add($"{rule.DisplayName}: {skipReason}");
                _activity.Warning(Category, $"Skipped firewall rule for {rule.DisplayName}: {skipReason}");
                continue;
            }

            foreach (var (suffix, direction) in new[] { (OutSuffix, "out"), (InSuffix, "in") })
            {
                var fullName = rule.Name + suffix;

                // Removing first makes re-apply idempotent; netsh errors when the name exists.
                RunNetsh(["advfirewall", "firewall", "delete", "rule", "name=" + fullName]);

                var add = RunNetsh(BuildAddArguments(rule, fullName, direction));
                if (add.Succeeded)
                {
                    created.Add(fullName);
                }
                else
                {
                    problems.Add($"{fullName}: {FirstLine(add)}");
                    _activity.Warning(Category, $"Could not create firewall rule {fullName}: {FirstLine(add)}");
                }
            }
        }

        var skippedNote = skippedRules.Count > 0
            ? $" {skippedRules.Count} rule(s) were skipped."
            : string.Empty;

        if (problems.Count == 0)
        {
            _activity.Success(Category,
                $"Created {created.Count} temporary firewall rules ({created.Count / 2} blocked programs).{skippedNote}");
            return OperationResult.Ok($"Created {created.Count} temporary firewall rules.{skippedNote}",
                skippedRules.Count > 0 ? skippedRules : null);
        }

        var message = created.Count == 0
            ? $"Firewall rules could not be created ({problems.Count} problem(s))."
            : $"Created {created.Count} firewall rules with {problems.Count} problem(s).";
        if (skippedRules.Count > 0)
        {
            problems.AddRange(skippedRules);
            _activity.Warning(Category, skippedNote.Trim());
        }

        return OperationResult.Fail(message, problems);
    }

    public OperationResult RemoveRules(IEnumerable<string> ruleNames)
    {
        if (ruleNames is null) return OperationResult.Ok("No rules to remove.");
        if (!IsSupported) return OperationResult.Ok("Windows Firewall is not available; no rules to remove.");
        if (!ElevationService.CheckIsElevated()) return OperationResult.NotElevated("Removing firewall rules");

        var names = ruleNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !name.EndsWith(OutSuffix, StringComparison.OrdinalIgnoreCase) &&
                           !name.EndsWith(InSuffix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0) return OperationResult.Ok("No FusionNet firewall rules to remove.");

        var failures = new List<string>();
        var removed = 0;

        foreach (var name in names)
        {
            foreach (var suffix in new[] { OutSuffix, InSuffix })
            {
                var fullName = name + suffix;
                var result = RunNetsh(["advfirewall", "firewall", "delete", "rule", "name=" + fullName]);
                // "No rules match the specified criteria." is a success for an idempotent cleanup.
                if (result.TimedOut || result.ExitCode < 0)
                {
                    failures.Add($"{fullName}: {FirstLine(result)}");
                }
                else
                {
                    removed++;
                }
            }
        }

        if (failures.Count > 0)
        {
            _activity.Warning(Category, $"Some firewall rules could not be removed: {string.Join("; ", failures)}");
            return OperationResult.Fail($"Could not remove {failures.Count} firewall rule(s).", failures);
        }

        _activity.Info(Category, $"Removed {removed} temporary firewall rules.");
        return OperationResult.Ok($"Removed {removed} temporary firewall rules.");
    }

    public int RemoveAllOwnedRules()
    {
        if (!IsSupported) return 0;

        if (!ElevationService.CheckIsElevated())
        {
            _activity.Warning(Category, "Firewall cleanup needs administrator rights; no rules were removed.");
            return 0;
        }

        var owned = GetOwnedRuleNames();
        if (owned.Count == 0) return 0;

        var result = RemoveRules(owned);
        if (result.Success)
        {
            _activity.Success(Category, $"Cleaned up all {owned.Count} FusionNet firewall rules.");
            return owned.Count;
        }

        // Report only rules that are actually gone; a failed delete must stay visible so the
        // caller/coordinator can keep the journal pending instead of claiming a clean restore.
        var remaining = GetOwnedRuleNames();
        var removed = Math.Max(0, owned.Count - remaining.Count);
        _activity.Warning(Category,
            $"Firewall cleanup was incomplete: {remaining.Count} of {owned.Count} rule(s) are still active. " + result.Message);
        return removed;
    }

    public IReadOnlyList<string> GetOwnedRuleNames()
    {
        if (!IsSupported) return [];

        var result = RunNetsh(["advfirewall", "firewall", "show", "rule", "name=all"]);
        if (result.TimedOut) return [];

        var output = result.Output;
        if (string.IsNullOrWhiteSpace(output)) return [];

        // Rule names are ASCII and FusionNet-owned, so matching the prefix is localisation safe
        // (netsh translates the "Rule Name:" labels on non-English Windows).
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in OwnedRuleNamePattern.Matches(output))
        {
            var value = match.Value;
            if (value.EndsWith(OutSuffix, StringComparison.OrdinalIgnoreCase))
                value = value[..^OutSuffix.Length];
            else if (value.EndsWith(InSuffix, StringComparison.OrdinalIgnoreCase))
                value = value[..^InSuffix.Length];

            if (!string.IsNullOrWhiteSpace(value)) names.Add(value);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildAddArguments(PlannedFirewallRule rule, string fullName, string direction)
    {
        var arguments = new List<string>
        {
            "advfirewall",
            "firewall",
            "add",
            "rule",
            "name=" + fullName,
            "dir=" + direction,
            "action=block",
            "program=" + rule.ProgramPath,
            "enable=yes",
            "profile=any",
            "description=" + BuildDescription(rule, direction)
        };

        if (!string.IsNullOrWhiteSpace(rule.ServiceName)) arguments.Add("service=" + rule.ServiceName);
        return arguments;
    }

    private static string BuildDescription(PlannedFirewallRule rule, string direction)
    {
        var safeName = new string(rule.DisplayName
            .Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.' ? character : ' ')
            .ToArray());
        safeName = string.Join(' ', safeName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (safeName.Length > 120) safeName = safeName[..120];
        return $"FusionNet Pro - {safeName} {direction}bound block";
    }

    private string? ValidateRule(PlannedFirewallRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.ProgramPath)) return "no executable path";
        if (string.IsNullOrWhiteSpace(rule.Name)) return "no rule name";
        if (!rule.Name.StartsWith(FirewallPlanner.RulePrefix, StringComparison.OrdinalIgnoreCase))
            return "rule name does not use the FusionNet ownership prefix";

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rule.ProgramPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "invalid executable path";
        }

        if (!File.Exists(fullPath)) return "the executable no longer exists";
        if (IsSamePath(fullPath, Environment.ProcessPath)) return "refusing to block FusionNet Pro itself";
        if (IsCriticalExecutable(fullPath, rule.ServiceName)) return "refusing to block a system-critical executable";

        return null;
    }

    /// <summary>System32 executables are blocked only through an explicit service rule.</summary>
    private static bool IsCriticalExecutable(string fullPath, string? serviceName)
    {
        if (!string.IsNullOrWhiteSpace(serviceName)) return false;

        var fileName = Path.GetFileName(fullPath);
        if (!CriticalExecutables.Contains(fileName)) return false;

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory)) return true;

        return fullPath.StartsWith(Path.GetFullPath(windowsDirectory) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSamePath(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private CommandResult RunNetsh(IEnumerable<string> arguments)
    {
        if (_netshPath is null)
            return new CommandResult(-1, string.Empty, "netsh.exe was not found.", false);

        return ProcessRunner.Run(_netshPath, arguments, CommandTimeout);
    }

    private static string FirstLine(CommandResult result)
    {
        if (result.TimedOut) return "netsh timed out";
        var text = result.Output;
        if (string.IsNullOrWhiteSpace(text)) return $"netsh exited with code {result.ExitCode}";
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line?.Trim() ?? $"netsh exited with code {result.ExitCode}";
    }

    private static string? ResolveNetshPath()
    {
        try
        {
            var systemDirectory = Environment.SystemDirectory;
            if (string.IsNullOrWhiteSpace(systemDirectory)) return null;

            var path = Path.Combine(systemDirectory, "netsh.exe");
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SecurityException)
        {
            return null;
        }
    }
}
