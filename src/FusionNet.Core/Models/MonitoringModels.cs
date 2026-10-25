namespace FusionNet.Core.Models;

public sealed record ProcessIdentity(int Pid, DateTimeOffset StartedUtc, string Path);
public sealed record ProcessObservation(
    int Pid, string Name, ProcessIdentity? Identity, double? CpuPercent, long? MemoryBytes,
    double? MemoryPercent, double? UploadBytesPerSecond, double? DownloadBytesPerSecond,
    string Availability, string Signature = "Unknown", string? Publisher = null);
public sealed record ConnectionObservation(int Pid, string Protocol, string LocalAddress, int LocalPort,
    string? RemoteAddress, int? RemotePort, string State, ProcessIdentity? Identity = null);
public sealed record InventoryItem(string Name, string Source, string Detail, int? Pid = null);
public sealed record InventorySection(string Name, string Status, string Detail, IReadOnlyList<InventoryItem> Items);
public sealed record SecurityFinding(string Rule, string Severity, string Confidence, string Explanation,
    IReadOnlyList<string> Evidence, string Recommendation);
public sealed record LocalMonitorReport(DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc,
    IReadOnlyList<ProcessObservation> Processes, IReadOnlyList<ConnectionObservation> Connections,
    IReadOnlyList<InventorySection> Sections, IReadOnlyList<SecurityFinding> Findings);
