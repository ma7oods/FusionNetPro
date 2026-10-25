using System.Reflection;
using FusionNet.Windows.Network;

namespace FusionNet.Tests;

public sealed class ThroughputMonitorTests
{
    [Fact]
    public void Deltas_OnlyCountTrafficOnAdaptersPresentInBothSamples()
    {
        var previous = Counters(("ethernet", 100, 50), ("removed", 500_000, 500_000));
        var current = Counters(("ethernet", 140, 60), ("new-wifi", 900_000, 800_000));

        Assert.Equal((40d, 10d), Deltas(previous, current));
    }

    [Fact]
    public void Deltas_CounterResetDoesNotHideTrafficOnOtherAdapters()
    {
        var previous = Counters(("reset", 10_000, 100), ("stable", 100, 100));
        var current = Counters(("reset", 10, 120), ("stable", 170, 140));

        Assert.Equal((70d, 60d), Deltas(previous, current));
    }

    [Fact]
    public void Deltas_ReconnectedAdapterStartsWithANewBaseline()
    {
        var initial = Counters(("wifi", 500_000, 200_000));
        var disconnected = Counters();
        var reconnected = Counters(("wifi", 900_000, 300_000));

        Assert.Equal((0d, 0d), Deltas(initial, disconnected));
        Assert.Equal((0d, 0d), Deltas(disconnected, reconnected));
        Assert.Equal((100d, 50d), Deltas(reconnected, Counters(("wifi", 900_100, 300_050))));
    }

    private static Dictionary<string, (long Received, long Sent)> Counters(
        params (string Id, long Received, long Sent)[] adapters) =>
        adapters.ToDictionary(adapter => adapter.Id, adapter => (adapter.Received, adapter.Sent));

    private static (double Received, double Sent) Deltas(
        Dictionary<string, (long Received, long Sent)> previous,
        Dictionary<string, (long Received, long Sent)> current)
    {
        var method = typeof(ThroughputMonitor).GetMethod("CalculateDeltas", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return ((double, double))method.Invoke(null, [previous, current])!;
    }
}
