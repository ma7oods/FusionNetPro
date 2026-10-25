using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Tests;

/// <summary>
/// The ping accumulator owns all live latency maths: min/max/avg, RFC 3550 style jitter,
/// packet loss, a 0-100 quality score and the rolling capacity window.
/// </summary>
public class PingStatisticsAccumulatorTests
{
    private const string Target = "1.1.1.1";

    private static PingSample Good(double rtt, int sequence = 0) =>
        new(Target, sequence, rtt, DateTimeOffset.UtcNow);

    private static PingSample Lost(int sequence = 0) =>
        new(Target, sequence, null, DateTimeOffset.UtcNow, "timeout");

    [Fact]
    public void NewAccumulator_StartsWithEmptyStatistics()
    {
        var accumulator = new PingStatisticsAccumulator(Target);

        var stats = accumulator.Snapshot();

        Assert.Equal(Target, stats.Target);
        Assert.Equal(0, stats.Sent);
        Assert.Equal(0, stats.Received);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(0, stats.LossPercent);
        Assert.Equal(0, stats.QualityScore);
        Assert.Equal(1, accumulator.Add(Good(10, 1)).Received);
    }

    [Fact]
    public void Constructor_NormalisesTargetAndClampsCapacity()
    {
        Assert.Equal("1.1.1.1", new PingStatisticsAccumulator(" 1.1.1.1 ").Target);
        Assert.Equal("?", new PingStatisticsAccumulator("   ").Target);
        Assert.Equal("?", new PingStatisticsAccumulator(null!).Target);

        // Capacity is clamped to at least 5 samples.
        var small = new PingStatisticsAccumulator(Target, capacity: 1);
        for (var i = 1; i <= 6; i++) small.Add(Good(i, i));

        var stats = small.Snapshot();
        Assert.Equal(6, stats.Sent);
        Assert.Equal(6, stats.Received);
        Assert.Equal(2, stats.MinMs);   // samples 2..6 kept
        Assert.Equal(6, stats.MaxMs);
        Assert.Equal(4, stats.AverageMs);
    }

    [Fact]
    public void Add_AllLost_ReportsFullLossAndZeroQuality()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        for (var i = 0; i < 4; i++) accumulator.Add(Lost(i));

        var stats = accumulator.Snapshot();

        Assert.Equal(4, stats.Sent);
        Assert.Equal(0, stats.Received);
        Assert.Equal(4, stats.Lost);
        Assert.Equal(100, stats.LossPercent);
        Assert.Equal(0, stats.QualityScore);
        Assert.Equal(0, stats.MinMs);
        Assert.Equal(0, stats.MaxMs);
        Assert.Equal(0, stats.AverageMs);
        Assert.Equal(0, stats.JitterMs);
    }

    [Fact]
    public void Add_CleanSequence_ComputesMinMaxAverageAndJitter()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        accumulator.Add(Good(10, 1));
        accumulator.Add(Good(20, 2));
        accumulator.Add(Good(30, 3));

        var stats = accumulator.Snapshot();

        Assert.Equal(3, stats.Sent);
        Assert.Equal(3, stats.Received);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(10, stats.MinMs);
        Assert.Equal(30, stats.MaxMs);
        Assert.Equal(20, stats.AverageMs);
        Assert.Equal(10, stats.JitterMs); // mean |delta| = (10 + 10) / 2
        Assert.Equal(30, stats.LastMs);
        Assert.Equal(0, stats.LossPercent);
        Assert.Equal(89, stats.QualityScore);
    }

    [Fact]
    public void Add_AlternatingValues_ComputesMeanAbsoluteJitter()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        foreach (var rtt in new[] { 10d, 20d, 10d, 20d }) accumulator.Add(Good(rtt));

        var stats = accumulator.Snapshot();

        Assert.Equal(10, stats.JitterMs);
        Assert.Equal(15, stats.AverageMs);
        Assert.Equal(10, stats.MinMs);
        Assert.Equal(20, stats.MaxMs);
        Assert.Equal(20, stats.LastMs);
    }

    [Fact]
    public void Add_SingleSample_HasZeroJitter()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        accumulator.Add(Good(20));

        var stats = accumulator.Snapshot();

        Assert.Equal(20, stats.MinMs);
        Assert.Equal(20, stats.MaxMs);
        Assert.Equal(20, stats.AverageMs);
        Assert.Equal(0, stats.JitterMs);
        Assert.Equal(100, stats.QualityScore);
    }

    [Fact]
    public void Add_MixedLoss_ComputesLossPercent()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        accumulator.Add(Good(10, 1));
        accumulator.Add(Lost(2));
        accumulator.Add(Good(20, 3));
        accumulator.Add(Lost(4));

        var stats = accumulator.Snapshot();

        Assert.Equal(4, stats.Sent);
        Assert.Equal(2, stats.Received);
        Assert.Equal(2, stats.Lost);
        Assert.Equal(50, stats.LossPercent);
        Assert.InRange(stats.QualityScore, 0, 30); // heavy loss must dominate the score
    }

    [Fact]
    public void Add_RollingWindow_KeepsOnlyLatestCapacitySamples()
    {
        var accumulator = new PingStatisticsAccumulator(Target, capacity: 5);
        for (var i = 1; i <= 7; i++) accumulator.Add(Good(i, i));

        var stats = accumulator.Snapshot();

        Assert.Equal(7, stats.Sent);
        Assert.Equal(7, stats.Received);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(3, stats.MinMs);   // samples 1 and 2 rolled out
        Assert.Equal(7, stats.MaxMs);
        Assert.Equal(5, stats.AverageMs); // (3+4+5+6+7)/5
        Assert.Equal(7, stats.LastMs);
    }

    [Fact]
    public void Add_NegativeOrNullRttIsCountedAsLost()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        accumulator.Add(Good(10));
        accumulator.Add(new PingSample(Target, 2, -5, DateTimeOffset.UtcNow));

        var stats = accumulator.Snapshot();

        Assert.Equal(2, stats.Sent);
        Assert.Equal(1, stats.Received);
        Assert.Equal(1, stats.Lost);
        Assert.Equal(50, stats.LossPercent);
    }

    [Fact]
    public void Reset_ClearsAllCountersButKeepsTarget()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        accumulator.Add(Good(10));
        accumulator.Add(Good(20));

        accumulator.Reset();
        var stats = accumulator.Snapshot();

        Assert.Equal(Target, stats.Target);
        Assert.Equal(0, stats.Sent);
        Assert.Equal(0, stats.Received);
        Assert.Equal(0, stats.Lost);
        Assert.Equal(1, accumulator.Add(Good(30)).Received);
    }

    [Fact]
    public void Add_NullSample_Throws()
    {
        var accumulator = new PingStatisticsAccumulator(Target);
        Assert.Throws<ArgumentNullException>(() => accumulator.Add(null!));
    }

    [Fact]
    public void CalculateQuality_ReturnsZeroWhenNothingWasReceived()
    {
        Assert.Equal(0, PingStatisticsAccumulator.CalculateQuality(0, 0, 0, 0));
        Assert.Equal(0, PingStatisticsAccumulator.CalculateQuality(0, 5, 10, 1));
        Assert.Equal(0, PingStatisticsAccumulator.CalculateQuality(-1, 5, 10, 1));
    }

    [Fact]
    public void CalculateQuality_PerfectLinkScoresOneHundred()
    {
        Assert.Equal(100, PingStatisticsAccumulator.CalculateQuality(10, 10, 10, 0));
        Assert.Equal(100, PingStatisticsAccumulator.CalculateQuality(10, 10, 30, 0));
    }

    [Fact]
    public void CalculateQuality_UsesLossJitterAndLatencyInOrder()
    {
        var lowLatency = PingStatisticsAccumulator.CalculateQuality(10, 10, 20, 0);
        var highLatency = PingStatisticsAccumulator.CalculateQuality(10, 10, 300, 0);
        Assert.True(lowLatency > highLatency);

        var lowJitter = PingStatisticsAccumulator.CalculateQuality(10, 10, 20, 2);
        var highJitter = PingStatisticsAccumulator.CalculateQuality(10, 10, 20, 30);
        Assert.True(lowJitter > highJitter);

        var noLoss = PingStatisticsAccumulator.CalculateQuality(10, 10, 20, 0);
        var someLoss = PingStatisticsAccumulator.CalculateQuality(9, 10, 20, 0);
        Assert.True(noLoss > someLoss);
    }

    [Fact]
    public void CalculateQuality_IsAlwaysWithinBounds()
    {
        Assert.Equal(80, PingStatisticsAccumulator.CalculateQuality(10, 10, 1000, 0)); // latency penalty capped
        Assert.Equal(30, PingStatisticsAccumulator.CalculateQuality(5, 10, 10, 0));    // loss penalty capped at 70
        Assert.InRange(PingStatisticsAccumulator.CalculateQuality(10, 10, 50, 100), 0, 100);
    }
}
