using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class ProcessSamplingTests
{
    private static readonly ProcessIdentity Identity = new(42, DateTimeOffset.UnixEpoch, @"C:\Apps\test.exe");

    [Fact]
    public void CpuIsNormalizedAcrossLogicalProcessors() =>
        Assert.Equal(25d, ProcessSampling.Cpu(Identity, Identity, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), 4));

    [Fact]
    public void PidReuseNeverReusesPreviousCounters() =>
        Assert.Null(ProcessSampling.Cpu(Identity, Identity with { StartedUtc = Identity.StartedUtc.AddSeconds(1) }, TimeSpan.Zero,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 4));

    [Fact]
    public void PathComparisonIsWindowsCaseInsensitive() =>
        Assert.Equal(25d, ProcessSampling.Cpu(Identity, Identity with { Path = Identity.Path.ToUpperInvariant() }, TimeSpan.Zero,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 4));

    [Fact]
    public void UnavailableOrResetCountersStayUnknown()
    {
        Assert.Null(ProcessSampling.Cpu(null, Identity, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1), 4));
        Assert.Null(ProcessSampling.Cpu(Identity, Identity, null, TimeSpan.Zero, TimeSpan.FromSeconds(1), 4));
        Assert.Null(ProcessSampling.Cpu(Identity, Identity, TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.FromSeconds(1), 4));
        Assert.Null(ProcessSampling.Cpu(Identity, Identity, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 4));
    }
}
