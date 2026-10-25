using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Windows.Network;

/// <summary>
/// Live ICMP monitor. Probes every configured target in parallel on a timer, feeds a
/// <see cref="PingStatisticsAccumulator"/> per target and publishes a snapshot per tick.
///
/// Design notes:
/// * One tick can never overlap the next one; a slow tick is simply skipped.
/// * Every probe is isolated: a failed ping becomes a failed <see cref="PingSample"/> with an
///   error label, never an exception into the timer or the UI.
/// * <see cref="IntervalMs"/> and <see cref="TimeoutMs"/> can be changed while running.
/// </summary>
public sealed class PingMonitor : IPingMonitor
{
    public const int MinIntervalMs = 500;
    public const int MaxIntervalMs = 10_000;
    public const int MinTimeoutMs = 250;
    public const int MaxTimeoutMs = 5_000;

    /// <summary>Grace added to the ICMP timeout so a hung DNS lookup cannot stall the tick forever.</summary>
    private const int ProbeGraceMs = 250;

    private readonly IActivityLog _activity;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentDictionary<string, PingStatisticsAccumulator> _accumulators = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TargetStatistics> _statistics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _sequences = new(StringComparer.OrdinalIgnoreCase);

    private string[] _targets = [];
    private IReadOnlyList<PingSample> _lastSamples = [];
    private Timer? _timer;
    private CancellationTokenSource? _cancellation;
    private int _intervalMs = 1_000;
    private int _timeoutMs = 1_000;
    private int _running;
    private int _tickInFlight;
    private int _consecutiveAllFailures;
    private volatile bool _disposed;

    public PingMonitor(IActivityLog activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public event EventHandler<PingTickEventArgs>? Tick;

    public IReadOnlyDictionary<string, TargetStatistics> Statistics => _statistics;

    public IReadOnlyList<PingSample> LastSamples => Volatile.Read(ref _lastSamples);

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public int IntervalMs
    {
        get => Volatile.Read(ref _intervalMs);
        set
        {
            var clamped = Math.Clamp(value, MinIntervalMs, MaxIntervalMs);
            Volatile.Write(ref _intervalMs, clamped);

            lock (_lifecycleGate)
            {
                if (_timer is null) return;
                try
                {
                    _timer.Change(clamped, clamped);
                }
                catch (ObjectDisposedException)
                {
                    // Stop() disposed the timer at the same moment; the next Start() recreates it.
                }
            }
        }
    }

    public int TimeoutMs
    {
        get => Volatile.Read(ref _timeoutMs);
        set => Volatile.Write(ref _timeoutMs, Math.Clamp(value, MinTimeoutMs, MaxTimeoutMs));
    }

    public void SetTargets(IEnumerable<string> targets)
    {
        var cleaned = new List<string>(4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (targets is not null)
        {
            foreach (var candidate in targets)
            {
                var normalized = NormalizeTarget(candidate);
                if (normalized is null || !seen.Add(normalized)) continue;
                cleaned.Add(normalized);
                if (cleaned.Count >= 8) break;
            }
        }

        lock (_lifecycleGate)
        {
            _targets = [.. cleaned];

            foreach (var target in _targets)
            {
                _accumulators.TryAdd(target, new PingStatisticsAccumulator(target));
                _statistics.TryAdd(target, TargetStatistics.Empty(target));
            }

            foreach (var stale in _accumulators.Keys.Where(t => !seen.Contains(t)).ToArray())
                _accumulators.TryRemove(stale, out _);

            foreach (var stale in _statistics.Keys.Where(t => !seen.Contains(t)).ToArray())
                _statistics.TryRemove(stale, out _);

            foreach (var stale in _sequences.Keys.Where(t => !seen.Contains(t)).ToArray())
                _sequences.TryRemove(stale, out _);
        }

        _activity.Info("Monitor", _targets.Length == 0
            ? "Ping monitor targets cleared."
            : $"Ping monitor targets set: {string.Join(", ", _targets)}.");
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _running) == 1) return;

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            Volatile.Write(ref _running, 1);

            var interval = IntervalMs;
            _timer = new Timer(OnTimer, _cancellation.Token, 0, interval);
        }

        _activity.Info("Monitor", $"Ping monitor started ({_targets.Length} targets every {IntervalMs} ms, timeout {TimeoutMs} ms).");
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _running) == 0 && _timer is null) return;

            Volatile.Write(ref _running, 0);
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
            cancellation = _cancellation;
            _cancellation = null;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down by Dispose().
        }

        // Do not Dispose() here: an in-flight probe may still hold a registration on the token.
        _ = DisposeCancellationLaterAsync(cancellation);

        _activity.Info("Monitor", "Ping monitor stopped.");
    }

    public async Task<PingSample> ProbeAsync(string target, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeTarget(target) ?? target?.Trim() ?? "?";
        var sequence = _sequences.AddOrUpdate(normalized, 1, static (_, value) => value + 1);
        return await ProbeCoreAsync(normalized, sequence, TimeoutMs, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancellationTokenSource? cancellation;
        lock (_lifecycleGate)
        {
            Volatile.Write(ref _running, 0);
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
            cancellation = _cancellation;
            _cancellation = null;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Nothing to do — already cancelled.
        }

        _ = DisposeCancellationLaterAsync(cancellation);
        Tick = null;
    }

    private void OnTimer(object? state)
    {
        if (_disposed || Volatile.Read(ref _running) == 0) return;
        if (state is not CancellationToken token) return;

        // A tick that takes longer than the interval must not overlap the next one.
        if (Interlocked.CompareExchange(ref _tickInFlight, 1, 0) != 0) return;

        _ = RunTickAsync(token);
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var targets = _targets;
            if (targets.Length == 0) return;

            var timeout = TimeoutMs;
            var probes = new Task<PingSample>[targets.Length];
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                var sequence = _sequences.AddOrUpdate(target, 1, static (_, value) => value + 1);
                probes[i] = ProbeCoreAsync(target, sequence, timeout, cancellationToken);
            }

            var samples = await Task.WhenAll(probes).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || _disposed) return;

            var statisticsSnapshot = new Dictionary<string, TargetStatistics>(samples.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var sample in samples)
            {
                if (!_accumulators.TryGetValue(sample.Target, out var accumulator)) continue;
                var statistics = accumulator.Add(sample);
                _statistics[sample.Target] = statistics;
                statisticsSnapshot[sample.Target] = statistics;
            }

            Volatile.Write(ref _lastSamples, samples);
            UpdateFailureStreak(samples);
            RaiseTick(samples, statisticsSnapshot);
        }
        catch (OperationCanceledException)
        {
            // Stop()/Dispose() cancelled the tick — normal shutdown path.
        }
        catch (Exception ex)
        {
            _activity.Warning("Monitor", "Ping tick failed.", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _tickInFlight, 0);
        }
    }

    private async Task<PingSample> ProbeCoreAsync(string target, int sequence, int timeoutMs, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        if (cancellationToken.IsCancellationRequested)
            return new PingSample(target, sequence, null, timestamp, "cancelled");

        Task<PingReply>? pingTask = null;
        Ping? ping = null;
        try
        {
            ping = new Ping();
            pingTask = ping.SendPingAsync(target, timeoutMs);
            var reply = await pingTask
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs + ProbeGraceMs), cancellationToken)
                .ConfigureAwait(false);

            return reply.Status == IPStatus.Success
                ? new PingSample(target, sequence, reply.RoundtripTime, DateTimeOffset.UtcNow)
                : new PingSample(target, sequence, null, DateTimeOffset.UtcNow, DescribeStatus(reply.Status));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveFault(pingTask);
            return new PingSample(target, sequence, null, timestamp, "cancelled");
        }
        catch (TimeoutException)
        {
            ObserveFault(pingTask);
            return new PingSample(target, sequence, null, timestamp, "timeout");
        }
        catch (Exception ex)
        {
            ObserveFault(pingTask);
            return new PingSample(target, sequence, null, timestamp, DescribeException(ex));
        }
        finally
        {
            ping?.Dispose();
        }
    }

    private void RaiseTick(IReadOnlyList<PingSample> samples, IReadOnlyDictionary<string, TargetStatistics> statistics)
    {
        var handler = Tick;
        if (handler is null) return;

        try
        {
            handler(this, new PingTickEventArgs(samples, statistics));
        }
        catch (Exception ex)
        {
            // A misbehaving subscriber must never take the monitor down.
            _activity.Warning("Monitor", "A ping tick subscriber threw an exception.", ex.Message);
        }
    }

    private void UpdateFailureStreak(IReadOnlyList<PingSample> samples)
    {
        if (samples.Count > 0 && samples.All(sample => !sample.Success))
        {
            _consecutiveAllFailures++;
            if (_consecutiveAllFailures == 1 || _consecutiveAllFailures % 60 == 0)
            {
                _activity.Warning("Monitor",
                    $"All ping targets are unreachable ({_consecutiveAllFailures} consecutive ticks).",
                    string.Join(", ", samples.Select(sample => $"{sample.Target}: {sample.ErrorText}")));
            }
        }
        else
        {
            _consecutiveAllFailures = 0;
        }
    }

    private async Task DisposeCancellationLaterAsync(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;

        if (Volatile.Read(ref _tickInFlight) == 0)
        {
            cancellation.Dispose();
            return;
        }

        // Let the in-flight probes drain their token registrations before the source is disposed.
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        cancellation.Dispose();
    }

    private static void ObserveFault(Task? task)
    {
        if (task is null) return;
        _ = task.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string DescribeStatus(IPStatus status) => status switch
    {
        IPStatus.TimedOut => "timeout",
        IPStatus.DestinationHostUnreachable => "host unreachable",
        IPStatus.DestinationNetworkUnreachable => "network unreachable",
        IPStatus.DestinationPortUnreachable => "port unreachable",
        IPStatus.DestinationUnreachable => "destination unreachable",
        IPStatus.PacketTooBig => "packet too big",
        IPStatus.TtlExpired => "ttl expired",
        IPStatus.BadDestination => "bad destination",
        _ => status.ToString()
    };

    private static string DescribeException(Exception exception) => exception switch
    {
        PingException => exception.InnerException?.Message ?? exception.Message,
        InvalidOperationException => exception.Message,
        ArgumentException => "invalid host",
        _ => exception.GetType().Name
    };

    /// <summary>Accepts IPv4 literals and DNS host names; rejects URLs, paths and embedded whitespace.</summary>
    private static string? NormalizeTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var trimmed = target.Trim();
        if (trimmed.Length is 0 or > 253) return null;

        if (IPAddress.TryParse(trimmed, out var address))
        {
            // IPv4 and IPv6 literals are both valid ICMP targets.
            return address.ToString();
        }

        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character)) return null;
            if (character is '/' or '\\' or ':' or '?' or '#' or '@' or '=' or '"' or '\'') return null;
        }

        return trimmed;
    }
}
