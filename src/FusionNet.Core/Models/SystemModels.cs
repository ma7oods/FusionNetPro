namespace FusionNet.Core.Models;

/// <summary>Snapshot of a running process used by planners and the app shield page.</summary>
public sealed record ProcessSnapshot(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    long WorkingSetBytes,
    bool HasMainWindow,
    bool IsSuspended,
    int SessionId)
{
    public override string ToString() => $"{ProcessName} ({ProcessId})";
}

public sealed record ServiceStateInfo(
    string Id,
    string DisplayName,
    ServicePauseState State,
    bool CanStop,
    string? ErrorText = null)
{
    public bool IsStopped => State is ServicePauseState.Stopped;
    public bool IsRunning => State is ServicePauseState.Running;
}

public sealed record TweakState(
    string Id,
    bool Applied,
    int TargetCount,
    int AppliedTargetCount,
    string CurrentValue,
    string ExpectedValue,
    string? ErrorText = null)
{
    public double CompletionRatio => TargetCount <= 0 ? 0 : (double)AppliedTargetCount / TargetCount;
}

/// <summary>Result of a mutating operation. Never throws across the service boundary.</summary>
public sealed record OperationResult(bool Success, string Message, IReadOnlyList<string>? Details = null)
{
    public static OperationResult Ok(string message = "", IReadOnlyList<string>? details = null) =>
        new(true, message, details);

    public static OperationResult Fail(string message, IReadOnlyList<string>? details = null) =>
        new(false, message, details);

    public static OperationResult NotElevated(string action) =>
        new(false, $"{action} needs administrator rights.", ["Restart FusionNet Pro as administrator and try again."]);
}

/// <summary>Live snapshot of the unified Fusion Mode.</summary>
public sealed record FusionSnapshot(
    FusionModeState State,
    GameProfile? Game,
    int ActiveFirewallRules,
    int PausedServices,
    int ControlledProcesses,
    int AppliedTweaks,
    DateTimeOffset? ArmedAtUtc,
    string? LastError,
    bool Elevated)
{
    public static FusionSnapshot Idle(bool elevated) =>
        new(FusionModeState.Idle, null, 0, 0, 0, 0, null, null, elevated);

    public bool IsActive => State is FusionModeState.Active or FusionModeState.Arming;
}

public sealed record ActivityEntry(
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string Category,
    string Message,
    string? Detail = null)
{
    public DateTime LocalTime => TimestampUtc.ToLocalTime().DateTime;
}
