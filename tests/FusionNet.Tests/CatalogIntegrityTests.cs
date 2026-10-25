using FusionNet.Core.Models;
using FusionNet.Core.Planning;

namespace FusionNet.Tests;

/// <summary>Integrity of the three static catalogues the whole product is driven from.</summary>
public class CatalogIntegrityTests
{
    // ------------------------------------------------------------------ Tweaks

    [Fact]
    public void TweakCatalog_HasExpectedSizeAndUniqueIds()
    {
        Assert.Equal(19, TweakCatalog.All.Count);
        Assert.Equal(19, TweakCatalog.All.Select(tweak => tweak.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, TweakCatalog.Recommended.Count);
        Assert.Equal(2, TweakCatalog.RecommendedIds.Count);
        Assert.Equal(5, TweakCatalog.Categories.Count);
    }

    [Fact]
    public void TweakCatalog_EveryTweakIsBilingualAndComplete()
    {
        Assert.All(TweakCatalog.All, tweak =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.Id));
            Assert.False(string.IsNullOrWhiteSpace(tweak.Category));
            Assert.False(string.IsNullOrWhiteSpace(tweak.RegistryPath));
            Assert.False(string.IsNullOrWhiteSpace(tweak.ValueName));
            Assert.False(string.IsNullOrWhiteSpace(tweak.TitleEn));
            Assert.False(string.IsNullOrWhiteSpace(tweak.TitleAr));
            Assert.False(string.IsNullOrWhiteSpace(tweak.DescriptionEn));
            Assert.False(string.IsNullOrWhiteSpace(tweak.DescriptionAr));
            Assert.NotEqual(tweak.TitleEn, tweak.TitleAr);
            Assert.NotEqual(tweak.DescriptionEn, tweak.DescriptionAr);
            Assert.NotNull(tweak.Value);
        });
    }

    [Fact]
    public void TweakCatalog_RecommendedIdsAreAllFindable()
    {
        Assert.All(TweakCatalog.RecommendedIds, id =>
        {
            var tweak = TweakCatalog.Find(id);
            Assert.NotNull(tweak);
            Assert.True(tweak!.Recommended);
        });

        Assert.Equal(new[] { "game-mode", "game-mode-auto" }, TweakCatalog.RecommendedIds);
        Assert.DoesNotContain("network-throttling", TweakCatalog.RecommendedIds);
        Assert.DoesNotContain("tcp-ack-frequency", TweakCatalog.RecommendedIds);
        Assert.DoesNotContain("tcp-no-delay", TweakCatalog.RecommendedIds);
        Assert.DoesNotContain("game-gpu-priority", TweakCatalog.RecommendedIds);
        Assert.DoesNotContain("game-sfio-priority", TweakCatalog.RecommendedIds);
        Assert.DoesNotContain("win32-priority", TweakCatalog.RecommendedIds);
    }

    [Fact]
    public void TweakCatalog_PerInterfaceTweaksLiveUnderTheInterfacesKey()
    {
        var perInterface = TweakCatalog.All.Where(tweak => tweak.Scope == TweakScope.PerInterface).ToArray();

        Assert.Equal(3, perInterface.Length);
        Assert.All(perInterface, tweak =>
        {
            Assert.EndsWith(@"\Services\Tcpip\Parameters\Interfaces", tweak.RegistryPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(TweakValueKind.Dword, tweak.Kind);
            Assert.True(Convert.ToInt64(tweak.Value) is >= 0 and <= 1);
        });

        var expectedNames = new[] { "TcpAckFrequency", "TCPNoDelay", "TcpDelAckTicks" };
        Assert.All(expectedNames, name =>
            Assert.Contains(perInterface, tweak => string.Equals(tweak.ValueName, name, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void TweakCatalog_EffectiveValueAndDisplayMatchKind()
    {
        var throttling = TweakCatalog.Find("network-throttling")!;
        Assert.Equal(TweakValueKind.Dword, throttling.Kind);
        Assert.Equal("0xFFFFFFFF", throttling.ExpectedDisplay);
        Assert.Equal(-1, throttling.EffectiveValue); // unlimited is stored as 0xFFFFFFFF dword

        var gpuPriority = TweakCatalog.Find("game-gpu-priority")!;
        Assert.Equal(8, gpuPriority.EffectiveValue);

        var scheduling = TweakCatalog.Find("game-scheduling")!;
        Assert.Equal(TweakValueKind.String, scheduling.Kind);
        Assert.Equal("High", scheduling.EffectiveValue);
        Assert.Equal("High", scheduling.ExpectedDisplay);
    }

    [Fact]
    public void TweakCatalog_RequiresRestartFilterWorks()
    {
        var restartTweaks = TweakCatalog.All.Where(tweak => tweak.RequiresRestart).ToArray();
        Assert.Equal(4, restartTweaks.Length);

        var applied = new[] { "network-throttling", "win32-priority", "hw-scheduling", "not-a-tweak" };
        var restart = TweakCatalog.RequiresRestart(applied);

        Assert.Equal(2, restart.Count);
        Assert.Contains(restart, tweak => tweak.Id == "win32-priority");
        Assert.Contains(restart, tweak => tweak.Id == "hw-scheduling");
    }

    [Fact]
    public void TweakCatalog_ByCategoryReturnsOnlyThatCategory()
    {
        var network = TweakCatalog.ByCategory(TweakCatalog.CatNetwork);
        Assert.NotEmpty(network);
        Assert.All(network, tweak => Assert.Equal(TweakCatalog.CatNetwork, tweak.Category));

        Assert.Equal(TweakCatalog.All.Count,
            TweakCatalog.Categories.Sum(category => TweakCatalog.ByCategory(category).Count));
        Assert.Empty(TweakCatalog.ByCategory("no-such-category"));
    }

    // ------------------------------------------------------------------ Services

    [Fact]
    public void ServiceCatalog_HasExpectedSizeAndUniqueIdentity()
    {
        Assert.Equal(9, ServiceCatalog.All.Count);
        Assert.Equal(3, ServiceCatalog.Recommended.Count);
        Assert.Equal(new[] { "wuauserv", "dosvc", "bits" }, ServiceCatalog.DefaultPausedIds);

        Assert.Equal(ServiceCatalog.All.Count,
            ServiceCatalog.All.Select(service => service.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ServiceCatalog.All.Count,
            ServiceCatalog.All.Select(service => service.ServiceName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ServiceCatalog_EveryServiceIsBilingualAndComplete()
    {
        Assert.All(ServiceCatalog.All, service =>
        {
            Assert.False(string.IsNullOrWhiteSpace(service.Id));
            Assert.False(string.IsNullOrWhiteSpace(service.ServiceName));
            Assert.False(string.IsNullOrWhiteSpace(service.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(service.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(service.DescriptionEn));
            Assert.False(string.IsNullOrWhiteSpace(service.DescriptionAr));
            Assert.NotEqual(service.NameEn, service.NameAr);
        });
    }

    [Fact]
    public void ServiceCatalog_RecommendedServicesAreNotAdvancedAndAreFindable()
    {
        Assert.All(ServiceCatalog.Recommended, service =>
        {
            Assert.False(service.Advanced);
            Assert.Same(service, ServiceCatalog.Find(service.Id));
            Assert.Same(service, ServiceCatalog.Find(service.ServiceName));
        });

        Assert.Equal(6, ServiceCatalog.All.Count(service => service.Advanced));
    }

    [Fact]
    public void ServiceCatalog_Find_MapsIdsAndServiceNamesCaseInsensitively()
    {
        Assert.Equal("wuauserv", ServiceCatalog.Find("WUAUSERV")!.Id);
        Assert.Equal("dosvc", ServiceCatalog.Find("DoSvc")!.Id);
        Assert.Equal("dosvc", ServiceCatalog.FindByServiceName("dosvc")!.Id);
        Assert.Null(ServiceCatalog.Find("not-a-service"));
        Assert.Null(ServiceCatalog.Find(null));
        Assert.Null(ServiceCatalog.FindByServiceName("not-a-service"));
    }

    // ------------------------------------------------------------------ DNS

    [Fact]
    public void DnsCatalog_HasExpectedSizeAndValidServers()
    {
        Assert.Equal(8, DnsCatalog.All.Count);
        Assert.Equal(3, DnsCatalog.Recommended.Count);
        Assert.Equal(DnsCatalog.All.Count,
            DnsCatalog.All.Select(provider => provider.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.All(DnsCatalog.All, provider =>
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Id));
            Assert.False(string.IsNullOrWhiteSpace(provider.Name));
            Assert.False(string.IsNullOrWhiteSpace(provider.DescriptionEn));
            Assert.False(string.IsNullOrWhiteSpace(provider.DescriptionAr));
            Assert.NotEqual(provider.DescriptionEn, provider.DescriptionAr);
            Assert.Equal(2, provider.Servers.Count);
            Assert.Equal(2, provider.Servers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(provider.Servers, server => Assert.True(DnsCatalog.IsValidIPv4(server), $"{provider.Id}: {server}"));
        });

        Assert.Contains(DnsCatalog.Recommended, provider => provider.Id == "cloudflare");
        Assert.Contains(DnsCatalog.Recommended, provider => provider.Id == "google");
        Assert.Contains(DnsCatalog.Recommended, provider => provider.Id == "quad9");
    }

    [Fact]
    public void DnsCatalog_IsValidIPv4_RejectsMalformedInput()
    {
        Assert.True(DnsCatalog.IsValidIPv4("1.1.1.1"));
        Assert.True(DnsCatalog.IsValidIPv4("8.8.8.8"));
        Assert.True(DnsCatalog.IsValidIPv4("192.168.0.1"));

        Assert.False(DnsCatalog.IsValidIPv4("::1"));
        Assert.False(DnsCatalog.IsValidIPv4("999.999.999.999"));
        Assert.False(DnsCatalog.IsValidIPv4("not-an-ip"));
        Assert.False(DnsCatalog.IsValidIPv4(""));
        Assert.False(DnsCatalog.IsValidIPv4(null!));
    }

    // ------------------------------------------------------------------ Planner constants

    [Fact]
    public void FirewallRulePrefix_IsStable()
    {
        Assert.Equal("FNP_", FirewallPlanner.RulePrefix);
        var rule = FirewallPlanner.CreateRule("qBittorrent", @"C:\Apps\qbittorrent.exe");
        Assert.StartsWith(FirewallPlanner.RulePrefix, rule.Name);
    }
}
