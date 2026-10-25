namespace FusionNet.Core.Models;

/// <summary>What FusionNet should do with a managed background application.</summary>
public enum AppControlAction
{
    /// <summary>Leave the application alone.</summary>
    Ignore = 0,

    /// <summary>Block the application's network access with Windows Firewall while the game runs.</summary>
    BlockNetwork = 1,

    /// <summary>Freeze the application's threads (downloads stop, state is preserved).</summary>
    Suspend = 2,

    /// <summary>Drop the application to low CPU/IO priority so voice and light traffic keep working.</summary>
    Throttle = 3
}

public static class AppControlActionExtensions
{
    public static bool IsEnforced(this AppControlAction action) =>
        action is AppControlAction.BlockNetwork or AppControlAction.Suspend or AppControlAction.Throttle;

    public static string ToStorageKey(this AppControlAction action) => action switch
    {
        AppControlAction.BlockNetwork => "block",
        AppControlAction.Suspend => "suspend",
        AppControlAction.Throttle => "throttle",
        _ => "ignore"
    };

    public static AppControlAction Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "block" or "blocknetwork" or "block-network" => AppControlAction.BlockNetwork,
        "suspend" or "freeze" => AppControlAction.Suspend,
        "throttle" or "low" => AppControlAction.Throttle,
        _ => AppControlAction.Ignore
    };
}

/// <summary>Lifecycle of the unified Fusion Mode (formerly "gaming mode" / "focus mode").</summary>
public enum FusionModeState
{
    Idle = 0,
    Arming = 1,
    Active = 2,
    Restoring = 3,
    Error = 4
}

public enum LogLevel
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public enum ServicePauseState
{
    Unknown = 0,
    Running = 1,
    Stopped = 2,
    StartPending = 3,
    StopPending = 4,
    Paused = 5,
    NotFound = 6
}

public enum ProcessPriority
{
    Idle = 0,
    BelowNormal = 1,
    Normal = 2,
    AboveNormal = 3,
    High = 4,
    RealTime = 5
}

public enum TweakScope
{
    /// <summary>HKLM - machine wide value.</summary>
    Machine = 0,

    /// <summary>HKCU - current user value.</summary>
    CurrentUser = 1,

    /// <summary>Value must be written into every active TCP/IP interface sub-key.</summary>
    PerInterface = 2
}

public enum TweakValueKind
{
    Dword = 0,
    Qword = 1,
    String = 2,
    ExpandString = 3
}

public enum TweakRisk
{
    Safe = 0,
    Moderate = 1,
    Advanced = 2
}

public enum DiagnosticSeverity
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum JournalKind
{
    Service = 0,
    Registry = 1,
    PowerPlan = 2,
    Process = 3,
    Firewall = 4,
    Dns = 5,
    Memory = 6,
    Other = 7
}

public enum GameProfileKind
{
    Auto = 0,
    BuiltIn = 1,
    Custom = 2
}
