using FusionNet.Core.Models;
using FusionNet.Core.Planning;
using FusionNet.Tests.Support;

namespace FusionNet.Tests;

/// <summary>
/// The firewall planner is safety-critical: it must only ever target block rules, must never
/// touch protected processes, and must dedupe by executable path.
/// </summary>
public class FirewallPlannerTests
{
    private static AppRuleTarget Target(
        string id = "qbittorrent",
        string name = "qBittorrent",
        AppControlAction action = AppControlAction.BlockNetwork,
        string[]? processNames = null,
        string[]? knownPaths = null,
        string? customPath = null) =>
        new(id, name, action, processNames ?? ["qbittorrent"], knownPaths ?? [], customPath);

    private static ProcessSnapshot Running(string processName, string? path = @"C:\Apps\app.exe", int pid = 100) =>
        new(pid, processName, path, 10_000_000, false, false, 1);

    [Fact]
    public void IsProtected_CoversSystemCriticalProcesses()
    {
        Assert.True(FirewallPlanner.IsProtected(null!, null));
        Assert.True(FirewallPlanner.IsProtected("", null));
        Assert.True(FirewallPlanner.IsProtected("   ", null));

        foreach (var name in new[] { "System", "svchost", "lsass", "csrss", "explorer", "dwm", "Discord", "DiscordPTB" })
            Assert.True(FirewallPlanner.IsProtected(name, null), $"{name} must be protected.");

        Assert.False(FirewallPlanner.IsProtected("chrome", null));
        Assert.False(FirewallPlanner.IsProtected("qbittorrent", null));
    }

    [Fact]
    public void IsProtected_ProtectsTheRunningGameProcess()
    {
        var cs2 = GameCatalog.Find("counter-strike-2");

        Assert.True(FirewallPlanner.IsProtected("cs2", cs2));
        Assert.True(FirewallPlanner.IsProtected("CS2", cs2));
        Assert.False(FirewallPlanner.IsProtected("notepad", cs2));
    }

    [Fact]
    public void Build_OnlyBlockNetworkTargetsProduceRules()
    {
        var targets = new[]
        {
            Target("block", "Blocker", AppControlAction.BlockNetwork, ["blocker"], customPath: @"C:\Apps\blocker.exe"),
            Target("suspend", "Suspender", AppControlAction.Suspend, ["suspender"], customPath: @"C:\Apps\suspender.exe"),
            Target("throttle", "Throttler", AppControlAction.Throttle, ["throttler"], customPath: @"C:\Apps\throttler.exe"),
            Target("ignore", "Ignorer", AppControlAction.Ignore, ["ignorer"], customPath: @"C:\Apps\ignorer.exe")
        };

        var rules = FirewallPlanner.Build(targets, [], null);

        Assert.Single(rules);
        Assert.Equal("FusionNet: Blocker", rules[0].DisplayName);
        Assert.Equal(Path.GetFullPath(@"C:\Apps\blocker.exe"), rules[0].ProgramPath);
        Assert.StartsWith(FirewallPlanner.RulePrefix, rules[0].Name);
    }

    [Fact]
    public void Build_DedupesByExecutablePath()
    {
        var targets = new[]
        {
            Target("a", "App A", customPath: @"C:\Apps\shared.exe"),
            Target("b", "App B", customPath: @"C:\Apps\shared.exe")
        };

        var rules = FirewallPlanner.Build(targets, [], null);

        Assert.Single(rules);
        Assert.Equal("FusionNet: App A", rules[0].DisplayName);
    }

    [Fact]
    public void Build_UsesTheRunningProcessPathWhenAvailable()
    {
        var targets = new[] { Target("steam", "Steam", processNames: ["steam"]) };
        ProcessSnapshot[] running = [Running("steam", @"C:\Program Files (x86)\Steam\steam.exe", pid: 42)];

        var rules = FirewallPlanner.Build(targets, running, null);

        Assert.Single(rules);
        Assert.Equal(Path.GetFullPath(@"C:\Program Files (x86)\Steam\steam.exe"), rules[0].ProgramPath);
    }

    [Fact]
    public void Build_SkipsRunningProcessesWithoutExecutablePath()
    {
        var targets = new[] { Target("qbittorrent", "qBittorrent") };
        ProcessSnapshot[] running = [Running("qbittorrent", path: null)];

        Assert.Empty(FirewallPlanner.Build(targets, running, null));
    }

    [Fact]
    public void Build_SkipsProtectedProcessesEvenWhenTheyMatchATarget()
    {
        var targets = new[] { Target("voice", "Voice Chat", processNames: ["discord"]) };
        ProcessSnapshot[] running = [Running("Discord", @"C:\Users\me\AppData\Local\Discord\app-1.0\Discord.exe")];

        Assert.Empty(FirewallPlanner.Build(targets, running, null));
    }

    [Fact]
    public void Build_SkipsTheRunningGameProcess()
    {
        var cs2 = GameCatalog.Find("counter-strike-2");
        var targets = new[] { Target("not-actually-an-app", "Fake CS2", processNames: ["cs2"]) };
        ProcessSnapshot[] running = [Running("cs2", @"C:\Games\cs2.exe")];

        Assert.Empty(FirewallPlanner.Build(targets, running, cs2));
    }

    [Fact]
    public void Build_CustomAppPathRuleDoesNotRequireAnExistingFile()
    {
        var target = Target("custom-app", "Syncer", customPath: @"C:\Tools\NeverInstalled\Syncer.exe");

        var rules = FirewallPlanner.Build([target], [], null);

        Assert.Single(rules);
        Assert.Equal(Path.GetFullPath(@"C:\Tools\NeverInstalled\Syncer.exe"), rules[0].ProgramPath);
    }

    [Fact]
    public void Build_ResolvesExistingKnownInstallPaths()
    {
        using var temp = new TempDirectory();
        var executable = temp.CreateFile(@"FakeApp\FakeApp.exe");
        var target = Target("fake", "Fake App", processNames: ["FakeApp"], knownPaths: [executable]);

        var rules = FirewallPlanner.Build([target], [], null);

        Assert.Single(rules);
        Assert.Equal(Path.GetFullPath(executable), rules[0].ProgramPath);
    }

    [Fact]
    public void Build_MissingKnownPathsProduceNoRules()
    {
        var missing = Path.Combine(Path.GetTempPath(), "FusionNetPro.Tests", "missing-" + Guid.NewGuid().ToString("N"), "app.exe");
        var target = Target("missing", "Missing App", processNames: ["MissingApp"], knownPaths: [missing]);

        Assert.Empty(FirewallPlanner.Build([target], [], null));
    }

    [Fact]
    public void Build_OrdersRulesByDisplayName()
    {
        var targets = new[]
        {
            Target("zeta", "Zeta", customPath: @"C:\Apps\zeta.exe"),
            Target("alpha", "Alpha", customPath: @"C:\Apps\alpha.exe"),
            Target("mike", "Mike", customPath: @"C:\Apps\mike.exe")
        };

        var rules = FirewallPlanner.Build(targets, [], null);

        Assert.Equal(new[] { "FusionNet: Alpha", "FusionNet: Mike", "FusionNet: Zeta" },
            rules.Select(rule => rule.DisplayName).ToArray());
    }

    [Fact]
    public void Build_NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => FirewallPlanner.Build(null!, [], null));
        Assert.Throws<ArgumentNullException>(() => FirewallPlanner.Build([], null!, null));
    }

    [Fact]
    public void BuildServiceRules_RequiresAnExistingSvchost()
    {
        using var temp = new TempDirectory();
        var svchost = temp.CreateFile(@"System32\svchost.exe");
        var services = ServiceCatalog.All.Take(2).ToArray();

        var rules = FirewallPlanner.BuildServiceRules(services, svchost);

        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.StartsWith("FNP_SVC_", rule.Name);
            Assert.NotNull(rule.ServiceName);
            Assert.Equal(Path.GetFullPath(svchost), rule.ProgramPath);
        });
        Assert.Contains(rules, rule => rule.Name == "FNP_SVC_WUAUSERV");
        Assert.Contains(rules, rule => rule.Name == "FNP_SVC_DOSVC");
    }

    [Fact]
    public void BuildServiceRules_IsEmptyWithoutASvchostPath()
    {
        Assert.Empty(FirewallPlanner.BuildServiceRules(ServiceCatalog.All, null));
        Assert.Empty(FirewallPlanner.BuildServiceRules(ServiceCatalog.All, ""));
        Assert.Empty(FirewallPlanner.BuildServiceRules(ServiceCatalog.All, @"C:\does\not\exist\svchost.exe"));
    }

    [Fact]
    public void CreateRule_IsDeterministicAndPathBased()
    {
        var first = FirewallPlanner.CreateRule("App", @"C:\Apps\a.exe");
        var second = FirewallPlanner.CreateRule("App", @"c:\apps\a.exe");

        Assert.Equal(first.Name, second.Name); // path is hashed case-insensitively
        Assert.StartsWith(FirewallPlanner.RulePrefix + "APP_", first.Name);
        Assert.Equal("FusionNet: App", first.DisplayName);
        Assert.NotEqual(first.Name, FirewallPlanner.CreateRule("App", @"C:\Apps\b.exe").Name);
    }

    [Fact]
    public void KnownPaths_ExpandAndResolve()
    {
        using var temp = new TempDirectory();
        var executable = temp.CreateFile(@"FakeApp\FakeApp.exe");

        Assert.Null(KnownPaths.Expand(null));
        Assert.Null(KnownPaths.Expand("   "));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Equal(Path.Combine(programFiles, "App", "a.exe"), KnownPaths.Expand(@"%ProgramFiles%\App\a.exe"));
        Assert.Null(KnownPaths.Expand(@"%NOT_A_REAL_ENV_VAR%\app.exe"));

        Assert.True(KnownPaths.TryResolve(executable, out var resolved));
        Assert.Equal(Path.GetFullPath(executable), resolved);
        Assert.False(KnownPaths.TryResolve(Path.Combine(temp.Root, "missing.exe"), out _));
    }

    [Fact]
    public void AppRuleTarget_FactoriesMapCatalogData()
    {
        var managed = ManagedAppCatalog.Find("qbittorrent")!;
        var fromManaged = AppRuleTarget.FromManagedApp(managed);

        Assert.Equal(managed.Id, fromManaged.AppId);
        Assert.Equal(managed.Name, fromManaged.DisplayName);
        Assert.Equal(managed.DefaultAction, fromManaged.Action);
        Assert.Equal(managed.ProcessNames, fromManaged.ProcessNames);
        Assert.Null(fromManaged.CustomPath);

        var custom = CustomManagedApp.Create(@"C:\Tools\Capture.exe");
        var fromCustom = AppRuleTarget.FromCustom(custom);

        Assert.Equal(custom.Id, fromCustom.AppId);
        Assert.Equal(custom.Action, fromCustom.Action);
        Assert.Equal(custom.ExecutablePath, fromCustom.CustomPath);
        Assert.Equal("Capture", fromCustom.ProcessNames[0]);
    }
}
