using System.Globalization;

namespace FusionNet.Core.Models;

/// <summary>One ICMP probe result. <see cref="RoundTripMs"/> is null when the probe failed.</summary>
public sealed record PingSample(
    string Target,
    int Sequence,
    double? RoundTripMs,
    DateTimeOffset TimestampUtc,
    string? ErrorText = null)
{
    /// <summary>A probe only succeeded for a finite, non-negative round-trip time.</summary>
    public bool Success => RoundTripMs is { } milliseconds && double.IsFinite(milliseconds) && milliseconds >= 0;
}

/// <summary>Aggregated live statistics for one ping target.</summary>
public sealed record TargetStatistics(
    string Target,
    int Sent,
    int Received,
    int Lost,
    double MinMs,
    double MaxMs,
    double AverageMs,
    double JitterMs,
    double LastMs,
    double LossPercent,
    double QualityScore,
    DateTimeOffset UpdatedAtUtc)
{
    public static TargetStatistics Empty(string target) =>
        new(target, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow);
}

public sealed record ThroughputSample(
    DateTimeOffset TimestampUtc,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond);

/// <summary>Network adapter details shown on the Monitor page.</summary>
public sealed record AdapterInfo(
    string Id,
    string Name,
    string Description,
    string MacAddress,
    string Status,
    long LinkSpeedBps,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> DnsServers,
    string? Gateway,
    bool IsWireless,
    string? Ssid,
    int? SignalPercent,
    bool IsDefaultRoute,
    ulong ReceivedBytes = 0,
    ulong SentBytes = 0)
{
    public string Summary => IsWireless ? $"{Name} · Wi-Fi" : Name;
}

public sealed record DnsBenchmarkResult(
    string ProviderId,
    string ProviderName,
    string Server,
    double LatencyMs,
    bool Success,
    string? ErrorText = null)
{
    public int Score { get; init; }

    public string LatencyDisplay =>
        Success ? LatencyMs.ToString("0.#", CultureInfo.InvariantCulture) + " ms" : "timeout";
}

public sealed record PublicIpInfo(string? IpAddress, string? Isp, string? Country, string? ErrorText = null)
{
    public bool Success => !string.IsNullOrWhiteSpace(IpAddress);
}

public sealed record DiagnosticFinding(
    string Category,
    string TitleEn,
    string TitleAr,
    string Detail,
    DiagnosticSeverity Severity,
    string? SuggestionEn = null,
    string? SuggestionAr = null);

/// <summary>The result of a full system health check, exportable as a text report.</summary>
public sealed record DiagnosticReport(
    DateTimeOffset GeneratedAtUtc,
    string MachineName,
    string WindowsVersion,
    string AppVersion,
    bool Elevated,
    IReadOnlyList<DiagnosticFinding> Findings,
    IReadOnlyList<AdapterInfo> Adapters,
    IReadOnlyDictionary<string, TargetStatistics> PingStatistics,
    IReadOnlyList<ServiceStateInfo> Services,
    IReadOnlyList<TweakState> Tweaks)
{
    public int CriticalCount => Findings.Count(f => f.Severity == DiagnosticSeverity.Critical);
    public int WarningCount => Findings.Count(f => f.Severity == DiagnosticSeverity.Warning);

    public DiagnosticSeverity OverallSeverity =>
        Findings.Any(f => f.Severity == DiagnosticSeverity.Critical) ? DiagnosticSeverity.Critical
        : Findings.Any(f => f.Severity == DiagnosticSeverity.Warning) ? DiagnosticSeverity.Warning
        : DiagnosticSeverity.Ok;
}
