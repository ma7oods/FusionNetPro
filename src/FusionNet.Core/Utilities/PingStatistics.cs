using FusionNet.Core.Models;

namespace FusionNet.Core.Utilities;

/// <summary>
/// Thread-safe rolling ping statistics: latency, jitter (RFC 3550 style mean absolute
/// difference of consecutive RTTs), packet loss and a 0-100 quality score.
/// Pure logic so it is fully unit tested.
/// </summary>
public sealed class PingStatisticsAccumulator
{
    private readonly object _gate = new();
    private readonly Queue<double> _rtts;
    private readonly int _capacity;
    private int _sent;
    private int _received;

    public PingStatisticsAccumulator(string target, int capacity = 120)
    {
        Target = string.IsNullOrWhiteSpace(target) ? "?" : target.Trim();
        _capacity = Math.Clamp(capacity, 5, 2000);
        _rtts = new Queue<double>(_capacity);
    }

    public string Target { get; }

    public void Reset()
    {
        lock (_gate)
        {
            _rtts.Clear();
            _sent = 0;
            _received = 0;
        }
    }

    public TargetStatistics Add(PingSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_gate)
        {
            _sent++;
            // Only a finite, non-negative round-trip is a real measurement: NaN compares false and
            // infinity would poison min/max/average/jitter (rendered later as "NaN ms").
            if (sample.RoundTripMs is { } rtt && double.IsFinite(rtt) && rtt >= 0)
            {
                _received++;
                _rtts.Enqueue(rtt);
                while (_rtts.Count > _capacity) _rtts.Dequeue();
            }

            return SnapshotLocked();
        }
    }

    public TargetStatistics Snapshot()
    {
        lock (_gate) return SnapshotLocked();
    }

    private TargetStatistics SnapshotLocked()
    {
        var lost = _sent - _received;
        var lossPercent = _sent == 0 ? 0 : lost * 100d / _sent;

        if (_rtts.Count == 0)
        {
            return new TargetStatistics(Target, _sent, _received, lost, 0, 0, 0, 0, 0,
                Math.Round(lossPercent, 1), 0, DateTimeOffset.UtcNow);
        }

        var min = double.MaxValue;
        var max = 0d;
        var average = 0d;
        var jitter = 0d;
        var jitterSamples = 0;
        var count = 0;
        double? previous = null;
        var last = 0d;

        foreach (var rtt in _rtts)
        {
            count++;
            min = Math.Min(min, rtt);
            max = Math.Max(max, rtt);
            last = rtt;

            // Incremental means: a running sum of extreme (but finite) values can overflow to infinity.
            average += (rtt - average) / count;
            if (previous.HasValue)
            {
                jitterSamples++;
                jitter += (Math.Abs(rtt - previous.Value) - jitter) / jitterSamples;
            }

            previous = rtt;
        }

        var score = CalculateQuality(_received, _sent, average, jitter);

        return new TargetStatistics(
            Target,
            _sent,
            _received,
            lost,
            Math.Round(min, 1),
            Math.Round(max, 1),
            Math.Round(average, 1),
            Math.Round(jitter, 1),
            Math.Round(last, 1),
            Math.Round(lossPercent, 1),
            score,
            DateTimeOffset.UtcNow);
    }

    /// <summary>0-100 quality: packet loss dominates, then jitter, then base latency.</summary>
    public static double CalculateQuality(int received, int sent, double averageMs, double jitterMs)
    {
        if (sent <= 0 || received <= 0) return 0;

        var lossPercent = (sent - received) * 100d / sent;
        var lossPenalty = Math.Min(70, lossPercent * 2.5);
        var jitterPenalty = Math.Min(25, Math.Max(0, jitterMs) * 1.1);
        var latencyPenalty = Math.Min(20, Math.Max(0, averageMs - 30) * 0.22);

        var score = 100 - lossPenalty - jitterPenalty - latencyPenalty;
        return Math.Round(Math.Clamp(score, 0, 100), 0);
    }
}
