using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Core.Planning;

/// <summary>One application the active plan will suspend, throttle or block.</summary>
public sealed record PlannedAppRule(
    string Id,
    string Name,
    AppControlAction Action,
    string? ExecutablePath,
    IReadOnlyList<int> ProcessIds,
    IReadOnlyList<string> ProcessNames);

/// <summary>The complete desired state of a Fusion Mode session.</summary>
public sealed class OptimizationPlan
{
    public FusionSettings Settings { get; init; } = null!;
    public GameProfile? Game { get; init; }
    public int? GameProcessId { get; init; }
    public bool GameRunning { get; init; }
    public IReadOnlyList<PlannedAppRule> AppRules { get; init; } = [];
    public IReadOnlyList<ServiceDefinition> ServicesToPause { get; init; } = [];
    public IReadOnlyList<TweakDefinition> Tweaks { get; init; } = [];
    public IReadOnlyList<PlannedFirewallRule> FirewallRules { get; init; } = [];
    public bool SwitchPowerPlan { get; init; }
    public bool FlushDns { get; init; }
    public bool PurgeMemory { get; init; }
    public bool BoostGame { get; init; }

    public bool HasWork => GameRunning &&
        (AppRules.Count > 0 || ServicesToPause.Count > 0 || Tweaks.Count > 0 ||
         FirewallRules.Count > 0 || SwitchPowerPlan || FlushDns || PurgeMemory || BoostGame);
}

/// <summary>
/// Pure planner: given settings + current processes it decides exactly what Fusion Mode
/// should do. No side effects - everything here is unit tested.
/// </summary>
public static class OptimizationPlanner
{
    public static GameProfile? DetectGame(FusionSettings settings, IReadOnlyList<ProcessSnapshot> running)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(running);

        var watchList = GameCatalog.BuildWatchList(settings);
        return GameCatalog.DetectRunning(
            watchList,
            running.Select(p => p.ProcessName),
            settings.SelectedGameId == GameCatalog.AutoProfileId ? null : settings.SelectedGameId);
    }

    public static OptimizationPlan Build(
        FusionSettings settings,
        GameProfile? game,
        IReadOnlyList<ProcessSnapshot> running)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(running);

        game ??= DetectGame(settings, running);
        var runningNames = running.Select(p => p.ProcessName).ToArray();
        var gameRunning = game is not null && game.MatchesAnyProcess(runningNames);
        var enforceApps = gameRunning || !settings.SuspendAppsOnlyWhileGameRuns;

        var targets = new List<AppRuleTarget>();
        foreach (var app in ManagedAppCatalog.All)
        {
            var action = settings.GetAction(app.Id);
            if (!action.IsEnforced()) continue;
            targets.Add(new AppRuleTarget(app.Id, app.Name, action, app.ProcessNames, app.Paths));
        }

        foreach (var custom in settings.CustomApps.Where(c => c is not null && c.Enabled && c.Action.IsEnforced()))
            targets.Add(AppRuleTarget.FromCustom(custom));

        // Only Riot titles run without their launcher, and voice chat must never be frozen.
        // Every running game counts, not just the detected one: a Steam game beside VALORANT still needs Steam.
        var runningGames = GameCatalog.All
            .Concat(GameCatalog.BuildWatchList(settings))
            .Concat(game is null ? [] : [game])
            .Where(g => g.MatchesAnyProcess(runningNames))
            .DistinctBy(g => g.Id, StringComparer.OrdinalIgnoreCase);
        var launcherNames = GameBoostPolicy.LauncherGuard(runningGames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        targets = targets
            .Where(t => !t.ProcessNames.Any(launcherNames.Contains))
            .Select(t => t.Action == AppControlAction.Suspend &&
                         t.ProcessNames.Any(n => n.StartsWith("discord", StringComparison.OrdinalIgnoreCase))
                ? t with { Action = AppControlAction.Throttle }
                : t)
            .ToList();

        var appRules = enforceApps ? BuildAppRules(targets, running, settings.EnableFirewallBlocking, game) : [];

        var services = gameRunning && settings.PauseWindowsDownloadServices
            ? settings.PausedServiceIds
                .Select(ServiceCatalog.Find)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToArray()
            : [];

        var tweaks = settings.ApplyLatencyTweaks
            ? settings.EnabledTweakIds.Select(TweakCatalog.Find).Where(t => t is not null).Select(t => t!).ToArray()
            : [];

        var firewallRules = settings.EnableFirewallBlocking
            ? FirewallPlanner.Build(targets, running, game)
            : [];

        var gameProcess = game is null
            ? null
            : running.FirstOrDefault(p => game.MatchesProcess(p.ProcessName));

        return new OptimizationPlan
        {
            Settings = settings,
            Game = game,
            GameRunning = gameRunning,
            GameProcessId = gameProcess?.ProcessId,
            AppRules = appRules,
            ServicesToPause = services,
            Tweaks = tweaks,
            FirewallRules = firewallRules,
            SwitchPowerPlan = gameRunning && settings.SwitchPowerPlan,
            FlushDns = gameRunning && settings.FlushDnsOnEngage,
            PurgeMemory = gameRunning && settings.PurgeStandbyMemoryOnEngage,
            BoostGame = gameRunning && settings.BoostGameProcess
        };
    }

    private static IReadOnlyList<PlannedAppRule> BuildAppRules(
        IReadOnlyList<AppRuleTarget> targets,
        IReadOnlyList<ProcessSnapshot> running,
        bool firewallEnabled,
        GameProfile? game)
    {
        var result = new List<PlannedAppRule>();

        foreach (var target in targets)
        {
            if (target.ProcessNames.Any(name => GameCatalog.IsRiotSupportProcess(name) || game?.MatchesProcess(name) == true)) continue;
            if (target.CustomPath is not null &&
                (GameCatalog.IsRiotSupportProcess(Path.GetFileNameWithoutExtension(target.CustomPath)) ||
                 game?.MatchesProcess(Path.GetFileNameWithoutExtension(target.CustomPath)) == true)) continue;
            if (target.Action == AppControlAction.BlockNetwork && !firewallEnabled) continue;

            var matches = running
                .Where(p => target.ProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var path = matches.Select(m => m.ExecutablePath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (path is null && target.CustomPath is not null) path = target.CustomPath;

            if (matches.Length == 0 && target.Action != AppControlAction.BlockNetwork) continue;

            result.Add(new PlannedAppRule(
                target.AppId,
                target.DisplayName,
                target.Action,
                path,
                matches.Select(m => m.ProcessId).ToArray(),
                target.ProcessNames));
        }

        return result;
    }
}
