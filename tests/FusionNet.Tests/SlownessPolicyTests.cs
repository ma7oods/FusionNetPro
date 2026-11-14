using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class SlownessPolicyTests
{
    private static SlownessSample Healthy() => new(TimeSpan.FromDays(1), null, 6, [new("chrome", 4)],
        new([new("Samsung SSD", "SSD", "Healthy", true)], 0, 0, 0, 0));
    private static DiagnosticSeverity Worst(SlownessSample sample) => SlownessPolicy.Evaluate(sample).Max(finding => finding.Severity);

    [Fact] public void HealthyPcHasNoWarnings() => Assert.True(Worst(Healthy()) < DiagnosticSeverity.Warning);

    [Fact] public void EachSlownessCauseRaisesAWarning()
    {
        Assert.Equal(DiagnosticSeverity.Warning, Worst(Healthy() with { Uptime = TimeSpan.FromDays(9) }));
        Assert.Equal(DiagnosticSeverity.Warning, Worst(Healthy() with { PowerPlanGuid = SlownessPolicy.PowerSaverGuid.ToUpperInvariant() }));
        Assert.Equal(DiagnosticSeverity.Warning, Worst(Healthy() with { StartupApps = 20 }));
        Assert.Equal(DiagnosticSeverity.Warning, Worst(Healthy() with { TopCpu = [new("miner", 60)] }));
        Assert.Equal(DiagnosticSeverity.Warning, Worst(Healthy() with { Probe = Healthy().Probe! with { Disks = [new("WD Blue", "HDD", "Healthy", true)] } }));
        Assert.Equal(DiagnosticSeverity.Warning, Worst(Healthy() with { Probe = Healthy().Probe! with { PowerLoss = 1 } }));
    }

    [Fact] public void DiskFailureSignsAreCritical()
    {
        Assert.Equal(DiagnosticSeverity.Critical, Worst(Healthy() with { Probe = Healthy().Probe! with { DiskErrors = 3 } }));
        Assert.Equal(DiagnosticSeverity.Critical, Worst(Healthy() with { Probe = Healthy().Probe! with { Disks = [new("Old", "HDD", "Unhealthy", false)] } }));
    }

    [Fact] public void ProbeParsesPowerShellOutputIncludingSingleDiskObject()
    {
        var probe = SlownessPolicy.ParseProbe("""{"Disks":{"Name":"NVMe","Media":"SSD","Health":"Healthy","System":true},"Crashes":2,"PowerLoss":0,"Hardware":1,"DiskErrors":0}""");
        Assert.NotNull(probe);
        Assert.Single(probe.Disks);
        Assert.True(probe.Disks[0].System);
        Assert.Equal(1, probe.Hardware);
        Assert.Null(SlownessPolicy.ParseProbe("not json"));
        Assert.Null(SlownessPolicy.ParseProbe(""));
    }
}
