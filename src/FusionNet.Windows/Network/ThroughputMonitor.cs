using System.Diagnostics;
using System.Net.NetworkInformation;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.Network;

/// <summary>
/// One-second throughput meter. Samples cumulative IP statistics of every operational,
/// non-loopback/non-tunnel adapter, computes the delta per second and publishes it.
///
/// The first sample only establishes a baseline; the event fires from the second tick onward.
/// All reads are defensive: a single adapter error (e.g. it disappeared mid-poll) never stops
/// the meter and never throws to the caller.
/// </summary>
public sealed class ThroughputMonitor : IThroughputMonitor
{
    private const int SampleIntervalMs = 1_000;
    private const double MinimumElapsedSeconds = 0.2;

    private readonly object _lifecycleGate = new();
    private readonly Timer _timer;

    private Dictionary<string, (long Received, long Sent)> _previousCounters = new(StringComparer.OrdinalIgnoreCase);
    private long _previousTimestamp;
    private int _running;
    private int _sampleInFlight;
    private volatile ThroughputSample? _latest;
    private volatile bool _disposed;

    public ThroughputMonitor()
    {
        _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<ThroughputSampleEventArgs>? SampleReceived;

    public ThroughputSample? Latest => _latest;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _running) == 1) return;

            CaptureBaseline();
            Volatile.Write(ref _running, 1);
            _timer.Change(SampleIntervalMs, SampleIntervalMs);
        }
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _running) == 0) return;

            Volatile.Write(ref _running, 0);
            try
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // Timer already torn down by Dispose().
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Volatile.Write(ref _running, 0);
        try
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }

        _timer.Dispose();
        SampleReceived = null;
    }

    private void CaptureBaseline()
    {
        _previousCounters = ReadCounters();
        _previousTimestamp = Stopwatch.GetTimestamp();
    }

    private void OnTimer(object? state)
    {
        if (_disposed || Volatile.Read(ref _running) == 0) return;
        if (Interlocked.CompareExchange(ref _sampleInFlight, 1, 0) != 0) return;

        try
        {
            ThroughputSample sample;
            lock (_lifecycleGate)
            {
                if (_disposed || Volatile.Read(ref _running) == 0) return;
                var counters = ReadCounters();
                var timestamp = Stopwatch.GetTimestamp();
                var elapsedSeconds = (timestamp - _previousTimestamp) / (double)Stopwatch.Frequency;
                if (elapsedSeconds < MinimumElapsedSeconds) return;

                var (receivedDelta, sentDelta) = CalculateDeltas(_previousCounters, counters);
                sample = new ThroughputSample(
                    DateTimeOffset.UtcNow,
                    receivedDelta / elapsedSeconds,
                    sentDelta / elapsedSeconds);

                _previousCounters = counters;
                _previousTimestamp = timestamp;
                _latest = sample;
            }
            RaiseSample(sample);
        }
        catch (Exception)
        {
            // Never let a transient WMI/IP helper failure kill the sampling timer. The next tick retries.
        }
        finally
        {
            Interlocked.Exchange(ref _sampleInFlight, 0);
        }
    }

    private void RaiseSample(ThroughputSample sample)
    {
        var handler = SampleReceived;
        if (handler is null) return;

        try
        {
            handler(this, new ThroughputSampleEventArgs(sample));
        }
        catch (Exception)
        {
            // A subscriber bug must not stop the throughput meter.
        }
    }

    private static (double Received, double Sent) CalculateDeltas(
        IReadOnlyDictionary<string, (long Received, long Sent)> previous,
        IReadOnlyDictionary<string, (long Received, long Sent)> current)
    {
        double received = 0;
        double sent = 0;
        foreach (var (id, counters) in current)
        {
            // New/reconnected adapters need a baseline; their lifetime totals are not new traffic.
            if (!previous.TryGetValue(id, out var baseline)) continue;
            received += Math.Max(0, counters.Received - baseline.Received);
            sent += Math.Max(0, counters.Sent - baseline.Sent);
        }
        return (received, sent);
    }

    private static Dictionary<string, (long Received, long Sent)> ReadCounters()
    {
        var counters = new Dictionary<string, (long Received, long Sent)>(StringComparer.OrdinalIgnoreCase);

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception)
        {
            // Network stack is being reconfigured — report zero for this sample.
            return counters;
        }

        foreach (var networkInterface in interfaces)
        {
            try
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (NetworkProbeHelper.LooksPseudo(networkInterface)) continue;

                var statistics = networkInterface.GetIPStatistics();
                counters[networkInterface.Id] = (Math.Max(0, statistics.BytesReceived), Math.Max(0, statistics.BytesSent));
            }
            catch (Exception)
            {
                // Interface vanished between enumeration and read; skip it.
            }
        }

        return counters;
    }
}
