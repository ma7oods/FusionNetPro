using FusionNet.Core.Models;

namespace FusionNet.Core.Abstractions;

public sealed class PingTickEventArgs(
    IReadOnlyList<PingSample> samples,
    IReadOnlyDictionary<string, TargetStatistics> statistics) : EventArgs
{
    public IReadOnlyList<PingSample> Samples { get; } = samples;
    public IReadOnlyDictionary<string, TargetStatistics> Statistics { get; } = statistics;
}

public interface IPingMonitor : IDisposable
{
    event EventHandler<PingTickEventArgs>? Tick;
    IReadOnlyDictionary<string, TargetStatistics> Statistics { get; }
    IReadOnlyList<PingSample> LastSamples { get; }
    bool IsRunning { get; }
    int IntervalMs { get; set; }
    int TimeoutMs { get; set; }
    void SetTargets(IEnumerable<string> targets);
    void Start();
    void Stop();
    Task<PingSample> ProbeAsync(string target, CancellationToken cancellationToken = default);
}

public sealed class ThroughputSampleEventArgs(ThroughputSample sample) : EventArgs
{
    public ThroughputSample Sample { get; } = sample;
}

public interface IThroughputMonitor : IDisposable
{
    event EventHandler<ThroughputSampleEventArgs>? SampleReceived;
    ThroughputSample? Latest { get; }
    bool IsRunning { get; }
    void Start();
    void Stop();
}

public interface IAdapterInfoService
{
    IReadOnlyList<AdapterInfo> GetAdapters();
    AdapterInfo? GetDefaultAdapter();
    AdapterInfo? GetAdapter(string? adapterId);
}

public interface IDnsService
{
    /// <summary>Runs real DNS queries against each provider and ranks them by latency.</summary>
    Task<IReadOnlyList<DnsBenchmarkResult>> BenchmarkAsync(
        IEnumerable<DnsProvider> providers,
        IProgress<DnsBenchmarkResult>? progress = null,
        CancellationToken cancellationToken = default);

    OperationResult ApplyServers(IReadOnlyList<string> servers, string? adapterId, IJournal journal);
    OperationResult ResetToAutomatic(string? adapterId, IJournal journal);
    OperationResult FlushCache();
}

public interface IPublicIpService
{
    Task<PublicIpInfo> GetAsync(CancellationToken cancellationToken = default);
}

public interface IDiagnosticsService
{
    Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default);
    string BuildTextReport(DiagnosticReport report);
}
