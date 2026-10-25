using FusionNet.Core.Models;
using FusionNet.Core.Planning;

namespace FusionNet.Tests;

/// <summary>
/// <see cref="OptimizationPlanner"/> is pure: given settings and a process list it produces the
/// complete desired state of a Fusion Mode session. These tests pin every gating rule.
/// </summary>
public class OptimizationPlannerTests
{
    private static ProcessSnapshot Process(string name, int pid = 100, string? path = null) =>
        new(pid, name, path ?? $@"C:\Apps\{name}.exe", 1_000_000, false, false, 1);

    /// <summary>Settings with no pre-selected app rules, so firewall tests never depend on installed apps.</summary>
    private static FusionSettings Isolated()
    {
        var settings = FusionSettings.CreateDefault();
        settings.AppActions.Clear();
        return settings;
    }

    [Fact]
    public void DetectGame_FindsRunningBuiltInGameFromAutoWatchList()
    {
        var settings = Isolated();

        var game = OptimizationPlanner.DetectGame(settings, [Process("cs2")]);

        Assert.NotNull(game);
        Assert.Equal("counter-strike-2", game!.Id);
    }

    [Fact]
    public void DetectGame_PinnedGameIgnoresOtherRunningGames()
    {
        var settings = Isolated();
        settings.SelectedGameId = "valorant";

        Assert.Null(OptimizationPlanner.DetectGame(settings, [Process("cs2")]));
        Assert.Equal("valorant", OptimizationPlanner.DetectGame(settings, [Process("VALORANT-Win64-Shipping")])!.Id);
    }

    [Fact]
    public void DetectGame_DetectsEnabledCustomGame()
    {
        var settings = Isolated();
        settings.CustomGames.Add(new CustomGame("custom-hero", "Hero", @"C:\Games\Hero.exe"));

        var game = OptimizationPlanner.DetectGame(settings, [Process("Hero")]);

        Assert.NotNull(game);
        Assert.Equal("custom-hero", game!.Id);
    }

    [Fact]
    public void Build_NoGameRunning_SuspendAppsOnlyWhileGameRunsGatesAppRules()
    {
        var settings = Isolated();
        settings.SetAction("onedrive", AppControlAction.Suspend);

        var plan = OptimizationPlanner.Build(settings, null, [Process("OneDrive")]);

        Assert.Null(plan.Game);
        Assert.False(plan.GameRunning);
        Assert.Null(plan.GameProcessId);
        Assert.Empty(plan.AppRules);
        Assert.Empty(plan.ServicesToPause);
        Assert.Empty(plan.FirewallRules);

        // Tweaks are a user preference independent of the current game, but no session work happens.
        Assert.Equal(TweakCatalog.Recommended.Count, plan.Tweaks.Count);
        Assert.False(plan.SwitchPowerPlan);
        Assert.False(plan.FlushDns);
        Assert.False(plan.PurgeMemory);
        Assert.False(plan.BoostGame);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void Build_LaunchersAreOnlyControlledForRiotGames()
    {
        var settings = Isolated();
        settings.SetAction("steam", AppControlAction.Suspend);
        settings.SetAction("epic", AppControlAction.BlockNetwork);

        var steamGame = OptimizationPlanner.Build(settings, null,
            [Process("cs2", 500), Process("steam", 700), Process("EpicGamesLauncher", 800)]);
        Assert.Empty(steamGame.AppRules);
        Assert.Empty(steamGame.FirewallRules);

        var riot = OptimizationPlanner.Build(settings, null,
            [Process("VALORANT-Win64-Shipping", 500), Process("steam", 700), Process("EpicGamesLauncher", 800)]);
        Assert.Equal("steam", Assert.Single(riot.AppRules, r => r.Action == AppControlAction.Suspend).Id);
        Assert.NotEmpty(riot.FirewallRules);

        // VALORANT is the detected game, but a Steam game is also open: Steam must stay untouched.
        settings.SelectedGameId = "valorant";
        var both = OptimizationPlanner.Build(settings, null,
            [Process("VALORANT-Win64-Shipping", 500), Process("cs2", 600), Process("steam", 700)]);
        Assert.Equal("valorant", both.Game!.Id);
        Assert.Empty(both.AppRules);
        Assert.Empty(both.FirewallRules);

        // A custom game beside VALORANT counts too.
        settings.CustomGames.Add(new CustomGame("custom-hero", "Hero", @"C:\Games\Hero.exe"));
        var custom = OptimizationPlanner.Build(settings, null,
            [Process("VALORANT-Win64-Shipping", 500), Process("Hero", 600), Process("steam", 700)]);
        Assert.Empty(custom.AppRules);
    }

    [Fact]
    public void Build_DiscordIsThrottledNeverSuspended()
    {
        var settings = Isolated();
        settings.SetAction("discord", AppControlAction.Suspend);

        var plan = OptimizationPlanner.Build(settings, null, [Process("VALORANT-Win64-Shipping", 500), Process("Discord", 700)]);

        var rule = Assert.Single(plan.AppRules);
        Assert.Equal("discord", rule.Id);
        Assert.Equal(AppControlAction.Throttle, rule.Action);
    }

    [Fact]
    public void Build_GameRunning_EnforcesAppsAndSessionFlags()
    {
        var settings = Isolated();
        settings.FlushDnsOnEngage = true;
        settings.SetAction("onedrive", AppControlAction.Suspend);

        var plan = OptimizationPlanner.Build(settings, null, [Process("cs2", 500), Process("OneDrive", 700)]);

        Assert.NotNull(plan.Game);
        Assert.Equal("counter-strike-2", plan.Game!.Id);
        Assert.True(plan.GameRunning);
        Assert.Equal(500, plan.GameProcessId);

        var rule = Assert.Single(plan.AppRules);
        Assert.Equal("onedrive", rule.Id);
        Assert.Equal(AppControlAction.Suspend, rule.Action);
        Assert.Equal(new[] { 700 }, rule.ProcessIds);
        Assert.Equal(Path.GetFullPath(@"C:\Apps\OneDrive.exe"), rule.ExecutablePath);

        Assert.Equal(3, plan.ServicesToPause.Count); // wuauserv, dosvc, bits
        Assert.True(plan.SwitchPowerPlan);
        Assert.True(plan.FlushDns);
        Assert.False(plan.PurgeMemory);
        Assert.True(plan.BoostGame);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void Build_ManualPinnedGame_IsUsedEvenWhenNotDetected()
    {
        var settings = Isolated();
        settings.SelectedGameId = "valorant";
        var game = GameCatalog.Find("valorant");

        var plan = OptimizationPlanner.Build(settings, game, [Process("VALORANT-Win64-Shipping", 321)]);

        Assert.Same(game, plan.Game);
        Assert.True(plan.GameRunning);
        Assert.Equal(321, plan.GameProcessId);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void Build_FirewallGatingOff_SkipsBlockRulesButKeepsSuspendAndThrottle()
    {
        var settings = Isolated();
        settings.EnableFirewallBlocking = false;
        settings.SetAction("qbittorrent", AppControlAction.BlockNetwork);
        settings.SetAction("onedrive", AppControlAction.Suspend);
        settings.SetAction("discord", AppControlAction.Throttle);

        var plan = OptimizationPlanner.Build(settings, null,
            [Process("cs2"), Process("qbittorrent"), Process("OneDrive"), Process("discord")]);

        Assert.Empty(plan.FirewallRules);
        Assert.DoesNotContain(plan.AppRules, rule => rule.Action == AppControlAction.BlockNetwork);
        Assert.Contains(plan.AppRules, rule => rule.Id == "onedrive" && rule.Action == AppControlAction.Suspend);
        Assert.Contains(plan.AppRules, rule => rule.Id == "discord" && rule.Action == AppControlAction.Throttle);
    }

    [Fact]
    public void Build_FirewallEnabled_PlansBlockRulesForRunningApps()
    {
        // A custom app keeps this deterministic: the managed catalogue may resolve to a real
        // install path on a developer machine, which must not influence the assertion.
        var settings = Isolated();
        settings.CustomApps.Add(new CustomManagedApp("custom-block", "Blocker",
            @"C:\Tools\Blocker.exe", AppControlAction.BlockNetwork));

        var plan = OptimizationPlanner.Build(settings, null,
            [Process("cs2"), Process("Blocker", 900, @"C:\Tools\Blocker.exe")]);

        var rule = Assert.Single(plan.FirewallRules);
        Assert.Equal(Path.GetFullPath(@"C:\Tools\Blocker.exe"), rule.ProgramPath);
        Assert.Contains(plan.AppRules, appRule => appRule.Id == "custom-block" && appRule.Action == AppControlAction.BlockNetwork);
    }

    [Fact]
    public void Build_TweaksDisabled_ProducesNoTweaks()
    {
        var settings = Isolated();
        settings.ApplyLatencyTweaks = false;

        var plan = OptimizationPlanner.Build(settings, null, [Process("cs2")]);

        Assert.Empty(plan.Tweaks);
    }

    [Fact]
    public void Build_ServiceSelectionFollowsSettings()
    {
        var settings = Isolated();
        settings.PausedServiceIds = ["wuauserv"];

        var plan = OptimizationPlanner.Build(settings, null, [Process("cs2")]);
        var service = Assert.Single(plan.ServicesToPause);
        Assert.Equal("wuauserv", service.Id);

        settings.PauseWindowsDownloadServices = false;
        Assert.Empty(OptimizationPlanner.Build(settings, null, [Process("cs2")]).ServicesToPause);
    }

    [Fact]
    public void Build_ServicesAreOnlyPausedWhileTheGameRuns()
    {
        var settings = Isolated();
        var plan = OptimizationPlanner.Build(settings, null, [Process("notepad")]);

        Assert.Empty(plan.ServicesToPause);
    }

    [Fact]
    public void Build_EngageFlagsFollowGameStateAndSettings()
    {
        var settings = Isolated();
        settings.PurgeStandbyMemoryOnEngage = false;
        settings.SwitchPowerPlan = false;
        settings.FlushDnsOnEngage = false;
        settings.BoostGameProcess = false;

        var runningPlan = OptimizationPlanner.Build(settings, null, [Process("cs2")]);
        Assert.False(runningPlan.SwitchPowerPlan);
        Assert.False(runningPlan.FlushDns);
        Assert.False(runningPlan.PurgeMemory);
        Assert.False(runningPlan.BoostGame);

        settings.PurgeStandbyMemoryOnEngage = true;
        settings.SwitchPowerPlan = true;
        settings.FlushDnsOnEngage = true;
        settings.BoostGameProcess = true;

        var idlePlan = OptimizationPlanner.Build(settings, null, [Process("notepad")]);
        Assert.False(idlePlan.SwitchPowerPlan);
        Assert.False(idlePlan.FlushDns);
        Assert.False(idlePlan.PurgeMemory);
        Assert.False(idlePlan.BoostGame);
    }

    [Fact]
    public void Build_CustomAppsAreEnforcedOnlyWhenEnabled()
    {
        var settings = Isolated();
        settings.SuspendAppsOnlyWhileGameRuns = false;
        settings.CustomApps.Add(CustomManagedApp.Create(@"C:\Tools\Sync.exe"));
        settings.CustomApps.Add(new CustomManagedApp("disabled", "Disabled", @"C:\Tools\Disabled.exe",
            AppControlAction.Suspend, Enabled: false));

        var plan = OptimizationPlanner.Build(settings, null, [Process("Sync", 55), Process("Disabled")]);

        var rule = Assert.Single(plan.AppRules);
        Assert.StartsWith("custom-", rule.Id);
        Assert.Equal("Sync", rule.Name);
        Assert.Equal(AppControlAction.BlockNetwork, rule.Action);
        Assert.Equal(new[] { 55 }, rule.ProcessIds);
        Assert.Equal(Path.GetFullPath(@"C:\Apps\Sync.exe"), rule.ExecutablePath); // resolved from the running process
    }

    [Fact]
    public void Build_BlockedCustomAppWithoutRunningProcessStillGetsRules()
    {
        var settings = Isolated();
        settings.SuspendAppsOnlyWhileGameRuns = false;
        settings.CustomApps.Add(CustomManagedApp.Create(@"C:\Tools\Syncer.exe"));

        var plan = OptimizationPlanner.Build(settings, null, []);

        var appRule = Assert.Single(plan.AppRules);
        Assert.Equal(Path.GetFullPath(@"C:\Tools\Syncer.exe"), appRule.ExecutablePath);
        Assert.Empty(appRule.ProcessIds);

        var firewallRule = Assert.Single(plan.FirewallRules);
        Assert.Equal(Path.GetFullPath(@"C:\Tools\Syncer.exe"), firewallRule.ProgramPath);
    }

    [Fact]
    public void Build_UnknownEnabledTweakIdsAreIgnored()
    {
        var settings = Isolated();
        settings.EnabledTweakIds = ["not-a-tweak", "network-throttling"];

        var plan = OptimizationPlanner.Build(settings, null, [Process("cs2")]);

        var tweak = Assert.Single(plan.Tweaks);
        Assert.Equal("network-throttling", tweak.Id);
    }

    [Fact]
    public void Build_HasWorkRequiresARunningGame()
    {
        var settings = Isolated();
        settings.SuspendAppsOnlyWhileGameRuns = false;
        settings.SetAction("onedrive", AppControlAction.Suspend);

        var plan = OptimizationPlanner.Build(settings, null, [Process("OneDrive")]);

        Assert.False(plan.GameRunning);
        Assert.NotEmpty(plan.AppRules); // rules are planned even without a game...
        Assert.False(plan.HasWork);     // ...but nothing is executed
    }

    [Fact]
    public void Build_NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => OptimizationPlanner.Build(null!, null, []));
        Assert.Throws<ArgumentNullException>(() => OptimizationPlanner.Build(FusionSettings.CreateDefault(), null, null!));
        Assert.Throws<ArgumentNullException>(() => OptimizationPlanner.DetectGame(null!, []));
        Assert.Throws<ArgumentNullException>(() => OptimizationPlanner.DetectGame(FusionSettings.CreateDefault(), null!));
    }
}
