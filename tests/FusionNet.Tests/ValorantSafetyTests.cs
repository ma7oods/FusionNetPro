using FusionNet.Core.Models;
using FusionNet.Core.Planning;

namespace FusionNet.Tests;

public class ValorantSafetyTests
{
    [Theory]
    [InlineData("RiotClientServices")]
    [InlineData("RiotClientUx")]
    [InlineData("RiotClientUxRender")]
    [InlineData("vgc")]
    [InlineData("vgtray")]
    public void RiotSupportCannotBeBlockedEvenWithoutADetectedGame(string processName)
    {
        var path = $@"C:\Riot\{processName}.exe";
        var target = new AppRuleTarget("custom", "Custom", AppControlAction.BlockNetwork, [processName], [], path);
        Assert.True(FirewallPlanner.IsProtected(processName.ToUpperInvariant(), null));
        Assert.Empty(FirewallPlanner.Build([target], [], null));
    }

    [Theory]
    [InlineData(AppControlAction.Suspend)]
    [InlineData(AppControlAction.Throttle)]
    [InlineData(AppControlAction.BlockNetwork)]
    public void ValorantPlanExcludesGameAndRiotFromBackgroundActions(AppControlAction action)
    {
        var settings = FusionSettings.CreateDefault();
        settings.AppActions.Clear();
        settings.SelectedGameId = "valorant";
        string[] names = ["VALORANT-Win64-Shipping", "RiotClientServices", "vgc", "vgtray"];
        settings.CustomApps = names.Select(name => CustomManagedApp.Create($@"C:\Riot\{name}.exe", action)).ToList();
        var running = names.Select((name, index) => new ProcessSnapshot(index + 100, name,
            $@"C:\Riot\{name}.exe", 1024, false, false, 1)).ToArray();

        var plan = OptimizationPlanner.Build(settings, null, running);

        Assert.True(plan.GameRunning);
        Assert.Equal("valorant", plan.Game!.Id);
        Assert.Empty(plan.AppRules);
        Assert.Empty(plan.FirewallRules);
        Assert.DoesNotContain(plan.ServicesToPause, service => service.ServiceName.Equals("vgc", StringComparison.OrdinalIgnoreCase));
    }
}
