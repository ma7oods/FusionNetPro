using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Tests;

/// <summary>
/// <see cref="FusionSettings.Normalize"/> is the self-healing entry point for every persisted
/// preference: any corrupted or out-of-range value must be repaired instead of throwing.
/// </summary>
public class FusionSettingsTests
{
    [Fact]
    public void CreateDefault_ReturnsNormalizedDefaults()
    {
        var settings = FusionSettings.CreateDefault();

        Assert.Equal(FusionSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal("en", settings.Language);
        Assert.Equal(AccentCatalog.DefaultId, settings.Accent);
        Assert.Equal(HotkeyGesture.Default.ToString(), settings.Hotkey);
        Assert.Equal("Ctrl+Alt+F", settings.Hotkey);
        Assert.Equal(1000, settings.PingIntervalMs);
        Assert.Equal(1000, settings.PingTimeoutMs);
        Assert.Equal(GameCatalog.AutoProfileId, settings.SelectedGameId);
        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8" }, settings.PingTargets);
        Assert.Equal(ServiceCatalog.DefaultPausedIds, settings.PausedServiceIds);
        Assert.Equal(TweakCatalog.RecommendedIds, settings.EnabledTweakIds);
        Assert.Equal(ManagedAppCatalog.DefaultActions().Count, settings.AppActions.Count);
        Assert.Empty(settings.CustomApps);
        Assert.Empty(settings.CustomGames);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.EnableFirewallBlocking);
        Assert.True(settings.SuspendAppsOnlyWhileGameRuns);
        Assert.Null(settings.SelectedAdapterId);
    }

    [Fact]
    public void CreateDefault_KeepsEveryRecommendedAppRule()
    {
        var recommended = ManagedAppCatalog.DefaultActions();
        Assert.True(recommended.Count >= 10, "Expected at least ten pre-selected recommended apps.");

        var settings = FusionSettings.CreateDefault();
        foreach (var (appId, action) in recommended)
        {
            Assert.True(action.IsEnforced(), $"{appId} default action must be enforced.");
            Assert.Equal(action, settings.GetAction(appId));
        }
    }

    [Fact]
    public void Normalize_RepairsLanguageAndAccent()
    {
        var settings = new FusionSettings { Language = "EN", Accent = "purple" };
        settings.Normalize();
        Assert.Equal("en", settings.Language);
        Assert.Equal(AccentCatalog.DefaultId, settings.Accent);

        settings.Language = "fr";
        settings.Accent = "emerald";
        settings.Normalize();
        Assert.Equal("en", settings.Language);
        Assert.Equal("emerald", settings.Accent);

        settings.Language = null!;
        settings.Accent = null!;
        settings.Normalize();
        Assert.Equal("en", settings.Language);
        Assert.Equal(AccentCatalog.DefaultId, settings.Accent);
    }

    [Fact]
    public void Normalize_RepairsInvalidHotkey()
    {
        var settings = new FusionSettings { Hotkey = "definitely-not-a-hotkey" };
        settings.Normalize();
        Assert.Equal(HotkeyGesture.Default.ToString(), settings.Hotkey);

        settings.Hotkey = "Ctrl+Shift+S";
        settings.Normalize();
        Assert.Equal("Ctrl+Shift+S", settings.Hotkey);
    }

    [Fact]
    public void Normalize_ClampsMonitoringAndSessionValues()
    {
        var low = new FusionSettings
        {
            PingIntervalMs = 1,
            PingTimeoutMs = 1,
            SessionCount = -10,
            SchemaVersion = 99
        };
        low.Normalize();
        Assert.Equal(500, low.PingIntervalMs);
        Assert.Equal(250, low.PingTimeoutMs);
        Assert.Equal(0, low.SessionCount);
        Assert.Equal(FusionSettings.CurrentSchemaVersion, low.SchemaVersion);

        var high = new FusionSettings { PingIntervalMs = 999_999, PingTimeoutMs = 999_999 };
        high.Normalize();
        Assert.Equal(10_000, high.PingIntervalMs);
        Assert.Equal(5_000, high.PingTimeoutMs);
    }

    [Fact]
    public void Normalize_FiltersAppActions()
    {
        var settings = new FusionSettings
        {
            AppActions = new Dictionary<string, AppControlAction>
            {
                ["steam"] = AppControlAction.Suspend,
                ["qbittorrent"] = AppControlAction.BlockNetwork,
                ["chrome"] = AppControlAction.Ignore,           // Ignore is never persisted
                ["not-a-real-app"] = AppControlAction.Suspend,  // unknown catalog id
                ["   "] = AppControlAction.Suspend              // blank id
            }
        };

        settings.Normalize();

        Assert.Equal(2, settings.AppActions.Count);
        Assert.Equal(AppControlAction.Suspend, settings.GetAction("steam"));
        Assert.Equal(AppControlAction.Suspend, settings.GetAction("STEAM"));
        Assert.Equal(AppControlAction.BlockNetwork, settings.GetAction("QBITTORRENT"));
        Assert.Equal(AppControlAction.Ignore, settings.GetAction("chrome"));
        Assert.Equal(AppControlAction.Ignore, settings.GetAction("not-a-real-app"));
    }

    [Fact]
    public void Normalize_RepairsNullAppActions()
    {
        var settings = new FusionSettings { AppActions = null! };
        settings.Normalize();
        Assert.NotNull(settings.AppActions);
        Assert.Empty(settings.AppActions);
        Assert.Equal(AppControlAction.Ignore, settings.GetAction("steam"));
    }

    [Fact]
    public void Normalize_FiltersAndDedupesCustomApps()
    {
        var settings = new FusionSettings
        {
            CustomApps =
            [
                new CustomManagedApp("", "   ", @"C:\Games\Tool.exe", AppControlAction.Ignore),
                new CustomManagedApp("", "", @"c:\games\tool.exe", AppControlAction.Suspend),
                new CustomManagedApp("notes", "Notes", @"C:\Games\notes.txt", AppControlAction.Suspend),
                new CustomManagedApp("noext", "NoExt", @"C:\Games\binary", AppControlAction.Suspend),
                new CustomManagedApp("blank", "Blank", "   ", AppControlAction.Suspend)
            ]
        };
        settings.CustomApps.Add(null!);

        settings.Normalize();

        Assert.Single(settings.CustomApps);
        var app = settings.CustomApps[0];
        Assert.StartsWith("custom-", app.Id);
        Assert.Equal("Tool", app.Name);
        Assert.Equal(AppControlAction.BlockNetwork, app.Action); // Ignore falls back to BlockNetwork
        Assert.Equal(Path.GetFullPath(@"C:\Games\Tool.exe"), app.ExecutablePath);
        Assert.Equal("Tool", app.ProcessName);
    }

    [Fact]
    public void Normalize_FiltersAndDedupesCustomGames()
    {
        var settings = new FusionSettings
        {
            CustomGames =
            [
                new CustomGame("custom-a", "   ", @"C:\Games\Alpha.exe"),
                new CustomGame("custom-b", "Beta", @"C:\Games\Beta.EXE"),
                new CustomGame("custom-c", "Duplicate", @"c:\games\alpha.exe"),
                new CustomGame("custom-d", "Notes", @"C:\Games\readme.txt")
            ]
        };
        settings.CustomGames.Add(null!);

        settings.Normalize();

        Assert.Equal(2, settings.CustomGames.Count);
        Assert.Equal("Alpha", settings.CustomGames[0].Name);
        Assert.Equal(Path.GetFullPath(@"C:\Games\Alpha.exe"), settings.CustomGames[0].ExecutablePath);
        Assert.Equal("Beta", settings.CustomGames[1].Name);
        Assert.DoesNotContain(settings.CustomGames, game => game.Name == "Duplicate");
    }

    [Fact]
    public void Normalize_ResetsUnknownSelectedGame()
    {
        var settings = new FusionSettings { SelectedGameId = "not-a-real-game" };
        settings.Normalize();
        Assert.Equal(GameCatalog.AutoProfileId, settings.SelectedGameId);

        settings.SelectedGameId = null!;
        settings.Normalize();
        Assert.Equal(GameCatalog.AutoProfileId, settings.SelectedGameId);
    }

    [Fact]
    public void Normalize_KeepsKnownBuiltInAndCustomGameSelection()
    {
        var settings = new FusionSettings { SelectedGameId = "valorant" };
        settings.Normalize();
        Assert.Equal("valorant", settings.SelectedGameId);

        settings = new FusionSettings
        {
            SelectedGameId = "custom-game-1",
            CustomGames = [new CustomGame("custom-game-1", "My Game", @"C:\Games\MyGame.exe")]
        };
        settings.Normalize();
        Assert.Equal("custom-game-1", settings.SelectedGameId);
    }

    [Fact]
    public void Normalize_CanonicalisesAndFiltersServiceIds()
    {
        var settings = new FusionSettings
        {
            PausedServiceIds = ["wuauserv", "DoSvc", "not-a-service", "WUAUSERV", "  "]
        };

        settings.Normalize();

        Assert.Equal(new[] { "wuauserv", "dosvc" }, settings.PausedServiceIds);
    }

    [Fact]
    public void Normalize_FiltersTweakIds()
    {
        var settings = new FusionSettings
        {
            EnabledTweakIds = ["tcp-ack-frequency", "TCP-ACK-FREQUENCY", "unknown-tweak", "network-throttling", ""]
        };

        settings.Normalize();

        Assert.Equal(new[] { "tcp-ack-frequency", "network-throttling" }, settings.EnabledTweakIds);
    }

    [Fact]
    public void CreateDefault_UsesGameModeWithoutGlobalNetworkRegistryChanges()
    {
        var settings = FusionSettings.CreateDefault();

        Assert.Equal(new[] { "game-mode", "game-mode-auto" }, settings.EnabledTweakIds);
        Assert.False(settings.FlushDnsOnEngage);
    }

    [Fact]
    public void Normalize_PreservesSavedOptInTweaksAndMasterSwitch()
    {
        var settings = FusionJson.Deserialize<FusionSettings>(
            """{"ApplyLatencyTweaks":false,"FlushDnsOnEngage":true,"EnabledTweakIds":["tcp-ack-frequency","game-gpu-priority","game-sfio-priority"]}""")!;

        settings.Normalize();

        Assert.False(settings.ApplyLatencyTweaks);
        Assert.True(settings.FlushDnsOnEngage);
        Assert.Equal(new[] { "tcp-ack-frequency", "game-gpu-priority", "game-sfio-priority" }, settings.EnabledTweakIds);
    }

    [Fact]
    public void Normalize_CleansPingTargets()
    {
        var settings = new FusionSettings
        {
            PingTargets = ["1.1.1.1", " 1.1.1.1 ", "8.8.8.8", "not a host", "-bad", "trailing-", "", null!]
        };

        settings.Normalize();

        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8" }, settings.PingTargets);
    }

    [Fact]
    public void Normalize_LimitsPingTargetsToFive()
    {
        var settings = new FusionSettings
        {
            PingTargets = ["1.1.1.1", "2.2.2.2", "3.3.3.3", "4.4.4.4", "5.5.5.5", "6.6.6.6", "7.7.7.7"]
        };

        settings.Normalize();

        Assert.Equal(5, settings.PingTargets.Count);
        Assert.Equal("5.5.5.5", settings.PingTargets[^1]);
    }

    [Fact]
    public void Normalize_FallsBackToDefaultPingTargetsWhenNoneValid()
    {
        var settings = new FusionSettings { PingTargets = ["not a host", "-bad", "", "  "] };
        settings.Normalize();
        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8" }, settings.PingTargets);
    }

    [Fact]
    public void Normalize_KeepsHostNamePingTargets()
    {
        var settings = new FusionSettings { PingTargets = ["Game.Example.com", "10.0.0.1"] };
        settings.Normalize();
        Assert.Equal(new[] { "Game.Example.com", "10.0.0.1" }, settings.PingTargets);
    }

    [Fact]
    public void Normalize_FiltersCustomDnsServers()
    {
        var settings = new FusionSettings
        {
            CustomDnsServers = ["1.1.1.1", "1.1.1.1", "8.8.8.8", "::1", "999.1.1.1", "9.9.9.9", "8.8.4.4"]
        };

        settings.Normalize();

        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9", "8.8.4.4" }, settings.CustomDnsServers);
    }

    [Fact]
    public void Normalize_NormalisesAdapterId()
    {
        var settings = new FusionSettings { SelectedAdapterId = "   " };
        settings.Normalize();
        Assert.Null(settings.SelectedAdapterId);

        settings.SelectedAdapterId = "{GUID}";
        settings.Normalize();
        Assert.Equal("{GUID}", settings.SelectedAdapterId);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var settings = FusionSettings.CreateDefault();
        var first = FusionJson.Serialize(settings);
        settings.Normalize();
        Assert.Equal(first, FusionJson.Serialize(settings));
    }

    [Fact]
    public void GetAction_ReturnsIgnoreForUnknownApp()
    {
        var settings = FusionSettings.CreateDefault();
        Assert.Equal(AppControlAction.Ignore, settings.GetAction("definitely-not-managed"));
        Assert.Equal(AppControlAction.Ignore, settings.GetAction(""));
    }

    [Fact]
    public void SetAction_StoresEnforcedActionsAndRemovesIgnore()
    {
        var settings = FusionSettings.CreateDefault();

        settings.SetAction("chrome", AppControlAction.BlockNetwork);
        Assert.Equal(AppControlAction.BlockNetwork, settings.GetAction("chrome"));

        settings.SetAction("chrome", AppControlAction.Ignore);
        Assert.Equal(AppControlAction.Ignore, settings.GetAction("chrome"));
        Assert.DoesNotContain("chrome", settings.AppActions.Keys);

        settings.SetAction("", AppControlAction.Suspend); // must be a no-op, not a throw
        Assert.Equal(AppControlAction.Ignore, settings.GetAction(""));
    }

    [Fact]
    public void IsTweakEnabled_RespectsMasterSwitchAndCase()
    {
        var settings = FusionSettings.CreateDefault();
        settings.EnabledTweakIds.Add("network-throttling");

        Assert.True(settings.IsTweakEnabled("network-throttling"));
        Assert.True(settings.IsTweakEnabled("NETWORK-THROTTLING"));
        Assert.False(settings.IsTweakEnabled("win32-priority")); // available but not a default

        settings.EnabledTweakIds.Add("win32-priority");
        Assert.True(settings.IsTweakEnabled("win32-priority"));

        settings.ApplyLatencyTweaks = false;
        Assert.False(settings.IsTweakEnabled("network-throttling"));
    }

    [Fact]
    public void IsServicePaused_RespectsMasterSwitchAndCase()
    {
        var settings = FusionSettings.CreateDefault();

        Assert.True(settings.IsServicePaused("wuauserv"));
        Assert.True(settings.IsServicePaused("WUAUSERV"));
        Assert.False(settings.IsServicePaused("sysmain")); // available but not a default

        settings.PausedServiceIds.Add("sysmain");
        Assert.True(settings.IsServicePaused("sysmain"));

        settings.PauseWindowsDownloadServices = false;
        Assert.False(settings.IsServicePaused("wuauserv"));
    }

    [Fact]
    public void Clone_IsDeepAndRoundTripsThroughJson()
    {
        var settings = FusionSettings.CreateDefault();
        settings.SetAction("chrome", AppControlAction.Suspend);
        settings.CustomGames.Add(new CustomGame("custom-x", "X", @"C:\Games\X.exe"));
        settings.PingTargets = ["4.4.4.4"];

        var clone = settings.Clone();

        Assert.NotSame(settings, clone);
        Assert.Equal(FusionJson.Serialize(settings), FusionJson.Serialize(clone));
        Assert.Equal("4.4.4.4", clone.PingTargets[0]);

        clone.PingTargets.Add("5.5.5.5");
        Assert.Single(settings.PingTargets);
    }
}

