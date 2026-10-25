using FusionNet.Core.Models;

namespace FusionNet.Tests;

/// <summary>Managed-app catalogue integrity and the default rule set created on first run.</summary>
public class ManagedAppCatalogTests
{
    [Fact]
    public void Catalog_HasUniqueIdsAndCompleteMetadata()
    {
        Assert.True(ManagedAppCatalog.All.Count >= 30, "Expected a rich managed-app catalogue.");

        Assert.Equal(ManagedAppCatalog.All.Count,
            ManagedAppCatalog.All.Select(app => app.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.All(ManagedAppCatalog.All, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Id));
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.False(string.IsNullOrWhiteSpace(app.Category));
            Assert.False(string.IsNullOrWhiteSpace(app.Description));
            Assert.NotEmpty(app.ProcessNames);
            Assert.All(app.ProcessNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        });
    }

    [Fact]
    public void Categories_AreNonEmptyAndKnown()
    {
        Assert.NotEmpty(ManagedAppCatalog.Categories);
        Assert.Equal(ManagedAppCatalog.Categories.Count,
            ManagedAppCatalog.Categories.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Contains(ManagedAppCatalog.Launchers, ManagedAppCatalog.Categories);
        Assert.Contains(ManagedAppCatalog.CloudSync, ManagedAppCatalog.Categories);
        Assert.Contains(ManagedAppCatalog.Torrents, ManagedAppCatalog.Categories);
        Assert.Contains(ManagedAppCatalog.Downloads, ManagedAppCatalog.Categories);
        Assert.Contains(ManagedAppCatalog.Browsers, ManagedAppCatalog.Categories);
        Assert.Contains(ManagedAppCatalog.UpdateAgents, ManagedAppCatalog.Categories);
        Assert.Contains(ManagedAppCatalog.RemoteTools, ManagedAppCatalog.Categories);
    }

    [Fact]
    public void RecommendedApps_HaveEnforcedDefaultActions()
    {
        var recommended = ManagedAppCatalog.All.Where(app => app.Recommended).ToArray();
        Assert.NotEmpty(recommended);

        Assert.All(recommended, app =>
        {
            Assert.True(app.DefaultAction.IsEnforced(),
                $"{app.Id} is recommended but defaults to {app.DefaultAction}.");
        });
    }

    [Fact]
    public void DefaultActions_ContainsExactlyTheRecommendedApps()
    {
        var defaults = ManagedAppCatalog.DefaultActions();
        var recommended = ManagedAppCatalog.All.Where(app => app.Recommended).ToArray();

        Assert.Equal(recommended.Length, defaults.Count);
        foreach (var app in recommended)
        {
            Assert.True(defaults.TryGetValue(app.Id, out var action), $"{app.Id} missing from defaults.");
            Assert.Equal(app.DefaultAction, action);
            Assert.True(action.IsEnforced());
        }

        foreach (var app in ManagedAppCatalog.All.Where(app => !app.Recommended))
        {
            Assert.False(defaults.ContainsKey(app.Id), $"{app.Id} must not be pre-selected.");
        }

        // Spot-check the most safety-relevant defaults.
        Assert.Equal(AppControlAction.Throttle, defaults["discord"]);
        Assert.Equal(AppControlAction.BlockNetwork, defaults["qbittorrent"]);
        Assert.Equal(AppControlAction.Suspend, defaults["steam"]);
        Assert.DoesNotContain("chrome", defaults.Keys);
        Assert.DoesNotContain("teamviewer", defaults.Keys);
    }

    [Fact]
    public void Find_And_FindByProcessName_AreCaseInsensitive()
    {
        Assert.Equal("steam", ManagedAppCatalog.Find("STEAM")!.Id);
        Assert.Equal("qbittorrent", ManagedAppCatalog.Find("qbitTorrent")!.Id);
        Assert.Null(ManagedAppCatalog.Find("not-installed-anywhere"));
        Assert.Null(ManagedAppCatalog.Find(null));

        Assert.Equal("chrome", ManagedAppCatalog.FindByProcessName("CHROME")!.Id);
        Assert.Equal("discord", ManagedAppCatalog.FindByProcessName("discordptb")!.Id);
        Assert.Null(ManagedAppCatalog.FindByProcessName("totally-unknown-process"));
    }

    [Fact]
    public void Discord_IsNeverBlockedByDefault()
    {
        var discord = ManagedAppCatalog.Find("discord");
        Assert.NotNull(discord);
        Assert.Equal(AppControlAction.Throttle, discord!.DefaultAction);
        Assert.True(discord.Recommended);
    }

    [Fact]
    public void CustomManagedApp_Create_BuildsBlockingTarget()
    {
        var app = CustomManagedApp.Create(@"C:\Tools\Syncer.exe");

        Assert.StartsWith("custom-", app.Id);
        Assert.Equal("Syncer", app.Name);
        Assert.Equal("Syncer", app.ProcessName);
        Assert.Equal(AppControlAction.BlockNetwork, app.Action);
        Assert.Equal(Path.GetFullPath(@"C:\Tools\Syncer.exe"), app.ExecutablePath);

        var managed = app.ToManagedApp();
        Assert.Equal(app.Id, managed.Id);
        Assert.Equal("Syncer", managed.ProcessNames[0]);
        Assert.False(managed.Recommended);
        Assert.Equal(app.ExecutablePath, managed.Paths[0]);
    }

    [Fact]
    public void CustomManagedApp_Create_RejectsNonExe()
    {
        Assert.Throws<ArgumentException>(() => CustomManagedApp.Create(@"C:\Tools\notes.txt"));
        Assert.ThrowsAny<ArgumentException>(() => CustomManagedApp.Create(""));
    }

    [Fact]
    public void CustomManagedApp_Normalized_FillsIdentityAndActionFallback()
    {
        var app = new CustomManagedApp("", "  ", @"C:\Tools\Capture.exe", AppControlAction.Ignore)
            .Normalized();

        Assert.Equal("Capture", app.Name);
        Assert.False(string.IsNullOrWhiteSpace(app.Id));
        Assert.Equal(AppControlAction.BlockNetwork, app.Action);
        Assert.Equal(Path.GetFullPath(@"C:\Tools\Capture.exe"), app.ExecutablePath);
    }
}
