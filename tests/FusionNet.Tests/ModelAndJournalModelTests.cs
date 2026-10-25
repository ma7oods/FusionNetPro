using FusionNet.Core.Models;

namespace FusionNet.Tests;

/// <summary>Small model helpers that every subsystem depends on: enums, result types, snapshots.</summary>
public class ModelAndJournalModelTests
{
    [Fact]
    public void AppControlAction_EnumValuesAndStorageHelpersAreStable()
    {
        Assert.Equal(0, (int)AppControlAction.Ignore);
        Assert.Equal(1, (int)AppControlAction.BlockNetwork);
        Assert.Equal(2, (int)AppControlAction.Suspend);
        Assert.Equal(3, (int)AppControlAction.Throttle);

        Assert.False(AppControlAction.Ignore.IsEnforced());
        Assert.True(AppControlAction.BlockNetwork.IsEnforced());
        Assert.True(AppControlAction.Suspend.IsEnforced());
        Assert.True(AppControlAction.Throttle.IsEnforced());

        Assert.Equal("ignore", AppControlAction.Ignore.ToStorageKey());
        Assert.Equal("block", AppControlAction.BlockNetwork.ToStorageKey());
        Assert.Equal("suspend", AppControlAction.Suspend.ToStorageKey());
        Assert.Equal("throttle", AppControlAction.Throttle.ToStorageKey());
    }

    [Theory]
    [InlineData("block", AppControlAction.BlockNetwork)]
    [InlineData("BlockNetwork", AppControlAction.BlockNetwork)]
    [InlineData("block-network", AppControlAction.BlockNetwork)]
    [InlineData("suspend", AppControlAction.Suspend)]
    [InlineData("freeze", AppControlAction.Suspend)]
    [InlineData("throttle", AppControlAction.Throttle)]
    [InlineData("low", AppControlAction.Throttle)]
    [InlineData("ignore", AppControlAction.Ignore)]
    [InlineData("something-else", AppControlAction.Ignore)]
    [InlineData(null, AppControlAction.Ignore)]
    public void AppControlAction_ParseAcceptsStorageAliases(string? value, AppControlAction expected)
    {
        Assert.Equal(expected, AppControlActionExtensions.Parse(value));
    }

    [Fact]
    public void OperationResult_FactoriesAreConsistent()
    {
        var ok = OperationResult.Ok("done", ["one"]);
        Assert.True(ok.Success);
        Assert.Equal("done", ok.Message);
        Assert.Equal(new[] { "one" }, ok.Details);

        var fail = OperationResult.Fail("failed");
        Assert.False(fail.Success);
        Assert.Null(fail.Details);

        var notElevated = OperationResult.NotElevated("Changing DNS");
        Assert.False(notElevated.Success);
        Assert.Contains("administrator", notElevated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(notElevated.Details);
        Assert.NotEmpty(notElevated.Details!);
    }

    [Fact]
    public void FusionSnapshot_IdleAndActiveStateHelpersWork()
    {
        var idle = FusionSnapshot.Idle(elevated: false);
        Assert.False(idle.IsActive);
        Assert.False(idle.Elevated);
        Assert.Equal(FusionModeState.Idle, idle.State);

        Assert.True(new FusionSnapshot(FusionModeState.Arming, null, 0, 0, 0, 0, null, null, true).IsActive);
        Assert.True(new FusionSnapshot(FusionModeState.Active, null, 0, 0, 0, 0, null, null, true).IsActive);
        Assert.False(new FusionSnapshot(FusionModeState.Restoring, null, 0, 0, 0, 0, null, null, true).IsActive);
        Assert.False(new FusionSnapshot(FusionModeState.Error, null, 0, 0, 0, 0, null, "boom", true).IsActive);
    }

    [Fact]
    public void TargetStatistics_EmptyIsAllZero()
    {
        var stats = TargetStatistics.Empty("8.8.8.8");

        Assert.Equal("8.8.8.8", stats.Target);
        Assert.Equal(0, stats.Sent);
        Assert.Equal(0, stats.Received);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(0, stats.LossPercent);
        Assert.Equal(0, stats.QualityScore);
        Assert.Equal(0, stats.MinMs);
        Assert.Equal(0, stats.MaxMs);
        Assert.Equal(0, stats.AverageMs);
        Assert.Equal(0, stats.JitterMs);
        Assert.Equal(0, stats.LastMs);
    }

    [Fact]
    public void PingSample_And_ServiceStateHelpers()
    {
        var success = new PingSample("1.1.1.1", 1, 12.5, DateTimeOffset.UtcNow);
        var failure = new PingSample("1.1.1.1", 2, null, DateTimeOffset.UtcNow, "timeout");
        Assert.True(success.Success);
        Assert.False(failure.Success);

        Assert.True(new ServiceStateInfo("wuauserv", "Windows Update", ServicePauseState.Stopped, true).IsStopped);
        Assert.True(new ServiceStateInfo("wuauserv", "Windows Update", ServicePauseState.Running, true).IsRunning);
        Assert.False(new ServiceStateInfo("wuauserv", "Windows Update", ServicePauseState.NotFound, false).IsRunning);
    }

    [Fact]
    public void DiagnosticReport_AggregatesSeverity()
    {
        DiagnosticFinding Finding(DiagnosticSeverity severity) =>
            new("Network", "Title", "عنوان", "Detail", severity);

        var ok = new DiagnosticReport(DateTimeOffset.UtcNow, "PC", "Windows 11", "3.0.0", false,
            [], [], new Dictionary<string, TargetStatistics>(), [], []);
        Assert.Equal(DiagnosticSeverity.Ok, ok.OverallSeverity);
        Assert.Equal(0, ok.WarningCount);
        Assert.Equal(0, ok.CriticalCount);

        var warned = new DiagnosticReport(DateTimeOffset.UtcNow, "PC", "Windows 11", "3.0.0", false,
            [Finding(DiagnosticSeverity.Warning), Finding(DiagnosticSeverity.Info)],
            [], new Dictionary<string, TargetStatistics>(), [], []);
        Assert.Equal(DiagnosticSeverity.Warning, warned.OverallSeverity);
        Assert.Equal(1, warned.WarningCount);
        Assert.Equal(0, warned.CriticalCount);

        var critical = new DiagnosticReport(DateTimeOffset.UtcNow, "PC", "Windows 11", "3.0.0", true,
            [Finding(DiagnosticSeverity.Warning), Finding(DiagnosticSeverity.Critical)],
            [], new Dictionary<string, TargetStatistics>(), [], []);
        Assert.Equal(DiagnosticSeverity.Critical, critical.OverallSeverity);
        Assert.Equal(1, critical.WarningCount);
        Assert.Equal(1, critical.CriticalCount);
    }

    [Fact]
    public void TweakState_CompletionRatioHandlesZeroTargets()
    {
        Assert.Equal(0, new TweakState("t", false, 0, 0, "-", "-").CompletionRatio);
        Assert.Equal(0, new TweakState("t", false, 4, 0, "-", "-").CompletionRatio);
        Assert.Equal(0.5, new TweakState("t", true, 4, 2, "-", "-").CompletionRatio);
        Assert.Equal(1, new TweakState("t", true, 4, 4, "-", "-").CompletionRatio);
    }

    [Fact]
    public void AdapterInfoAndDnsBenchmarkProvideDisplayHelpers()
    {
        var wireless = new AdapterInfo("id", "Wi-Fi", "Intel AX200", "AA:BB", "Up", 1_000_000_000,
            ["192.168.1.2"], ["1.1.1.1"], "192.168.1.1", IsWireless: true, "HomeNet", 80, IsDefaultRoute: true);
        Assert.Equal("Wi-Fi · Wi-Fi", wireless.Summary);

        var wired = wireless with { IsWireless = false };
        Assert.Equal("Wi-Fi", wired.Summary);

        var success = new DnsBenchmarkResult("cloudflare", "Cloudflare", "1.1.1.1", 12.34, true);
        Assert.Equal("12.3 ms", success.LatencyDisplay);

        var failure = new DnsBenchmarkResult("cloudflare", "Cloudflare", "1.1.1.1", 0, false, "timeout");
        Assert.Equal("timeout", failure.LatencyDisplay);
    }

    [Fact]
    public void PublicIpInfo_SuccessDependsOnAddress()
    {
        Assert.True(new PublicIpInfo("1.2.3.4", "ISP", "US").Success);
        Assert.False(new PublicIpInfo(null, null, null, "offline").Success);
        Assert.False(new PublicIpInfo("   ", null, null).Success);
    }

    [Fact]
    public void JournalKind_AndOtherEnumsKeepStableNumericContract()
    {
        Assert.Equal(0, (int)JournalKind.Service);
        Assert.Equal(1, (int)JournalKind.Registry);
        Assert.Equal(2, (int)JournalKind.PowerPlan);
        Assert.Equal(3, (int)JournalKind.Process);
        Assert.Equal(4, (int)JournalKind.Firewall);
        Assert.Equal(5, (int)JournalKind.Dns);
        Assert.Equal(6, (int)JournalKind.Memory);
        Assert.Equal(7, (int)JournalKind.Other);

        Assert.Equal(0, (int)FusionModeState.Idle);
        Assert.Equal(2, (int)FusionModeState.Active);
        Assert.Equal(0, (int)DiagnosticSeverity.Ok);
        Assert.Equal(3, (int)DiagnosticSeverity.Critical);
        Assert.Equal(0, (int)GameProfileKind.Auto);
        Assert.Equal(2, (int)GameProfileKind.Custom);
    }

    [Fact]
    public void ActivityEntry_ConvertsToLocalTime()
    {
        var entry = new ActivityEntry(DateTimeOffset.UtcNow, LogLevel.Warning, "Firewall", "rule added");

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Firewall", entry.Category);
        Assert.True((DateTimeOffset.Now - entry.LocalTime).Duration() < TimeSpan.FromMinutes(1));
    }
}
