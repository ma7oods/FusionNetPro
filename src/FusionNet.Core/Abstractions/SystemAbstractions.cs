using FusionNet.Core.Models;
using FusionNet.Core.Planning;

namespace FusionNet.Core.Abstractions;

public interface ISettingsStore
{
    string FilePath { get; }
    FusionSettings Load();
    void Save(FusionSettings settings);
    void Reset();
}

public interface IActivityLog
{
    event EventHandler<ActivityEntry>? EntryAdded;
    IReadOnlyList<ActivityEntry> Snapshot();
    void Log(LogLevel level, string category, string message, string? detail = null);
    void Info(string category, string message, string? detail = null);
    void Success(string category, string message, string? detail = null);
    void Warning(string category, string message, string? detail = null);
    void Error(string category, string message, string? detail = null);
    void Clear();
    string ExportText();
}

public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>Relaunches the app elevated; returns false when the user cancels the UAC prompt.</summary>
    bool TryRestartElevated(string? arguments = null);
}

public interface ISystemInfoService
{
    bool IsElevated { get; }
    string MachineName { get; }
    string WindowsVersion { get; }
    string AppVersion { get; }
    string DataDirectory { get; }
    long TotalPhysicalMemoryBytes { get; }
    long AvailablePhysicalMemoryBytes { get; }
    long TotalSystemDriveFreeBytes { get; }
    string CpuName { get; }
    int LogicalProcessorCount { get; }
    bool IsWindows11 { get; }
    DateTimeOffset ProcessStartedAtUtc { get; }
    TimeSpan Uptime { get; }
}

public interface IFirewallService
{
    bool IsSupported { get; }
    OperationResult ApplyRules(IReadOnlyList<PlannedFirewallRule> rules);
    OperationResult RemoveRules(IEnumerable<string> ruleNames);
    int RemoveAllOwnedRules();
    IReadOnlyList<string> GetOwnedRuleNames();
}

public interface IWindowsServiceController
{
    ServiceStateInfo GetState(ServiceDefinition service);
    OperationResult Stop(ServiceDefinition service, TimeSpan timeout);
    OperationResult Start(ServiceDefinition service, TimeSpan timeout);
}

public interface IProcessController
{
    IReadOnlyList<ProcessSnapshot> GetProcesses();
    ProcessSnapshot? FindByProcessName(string processName);
    OperationResult SetSuspended(int processId, bool suspended);
    OperationResult SetPriority(int processId, ProcessPriority priority);
    OperationResult BoostForGaming(int processId);
    OperationResult RestoreDefaultPriority(int processId);
    bool IsSuspended(int processId);
}

public interface IRegistryTweakService
{
    TweakState Read(TweakDefinition tweak);
    IReadOnlyList<TweakState> ReadAll(IEnumerable<TweakDefinition> tweaks);
    OperationResult Apply(TweakDefinition tweak, IJournal journal);
    OperationResult Revert(TweakDefinition tweak, IJournal journal);
    OperationResult ApplyAll(IEnumerable<TweakDefinition> tweaks, IJournal journal);
    OperationResult RevertAll(IEnumerable<TweakDefinition> tweaks, IJournal journal);
}

public interface IPowerPlanService
{
    string? ActivePlanName { get; }
    string? ActivePlanGuid { get; }
    bool IsHighPerformanceActive { get; }
    OperationResult SwitchToHighPerformance(IJournal journal);
    OperationResult Restore(string? planGuid, IJournal journal);
}

public interface IAutoStartService
{
    bool IsEnabled { get; }
    string CommandDisplay { get; }
    OperationResult SetEnabled(bool enabled);
}

public interface IStandbyMemoryService
{
    /// <summary>Purges the standby list; returns freed bytes (0 when unavailable).</summary>
    long Purge();
}

/// <summary>
/// Experimental strict mode inherited from the GameFocus prototype: set every Windows Firewall
/// profile to block outbound traffic and allow only the game, DNS and DHCP.
/// Always reversible through the journal; never enabled by default.
/// </summary>
public interface IFirewallLockdownService
{
    bool IsActive { get; }
    string? ActiveGamePath { get; }
    OperationResult Enable(string? gameExecutablePath, IJournal journal);
    OperationResult Disable(IJournal journal);

    /// <summary>Restores firewall policies if a previous session crashed while lockdown was active.</summary>
    OperationResult RestoreIfActive(IJournal journal);
}

public sealed class GameDetectionEventArgs(GameProfile? game, IReadOnlyList<GameProfile> runningGames) : EventArgs
{
    public GameProfile? Game { get; } = game;
    public IReadOnlyList<GameProfile> RunningGames { get; } = runningGames;
}

public interface IGameDetectionService : IDisposable
{
    event EventHandler<GameDetectionEventArgs>? Changed;
    GameProfile? CurrentGame { get; }
    IReadOnlyList<GameProfile> RunningGames { get; }
    bool IsGameRunning { get; }
    TimeSpan PollInterval { get; set; }
    void SetWatchList(IReadOnlyList<GameProfile> profiles);
    void Start();
    void Stop();
    void PollNow();
}

public sealed record RestoreResult(int Restored, int Failed, IReadOnlyList<string> Errors);

public interface IJournal
{
    JournalDocument Document { get; }
    IReadOnlyList<JournalEntry> Entries { get; }
    bool HasPendingRestore { get; }

    /// <summary>Starts a new session document and persists it immediately.</summary>
    void Begin(string? gameName = null);
    JournalEntry Record(JournalKind kind, string target, string description,
        string? previousValue = null, string? appliedValue = null);
    void MarkRestored(string entryId);
    void Save();
    void Complete();
    void Clear();
}

public interface IFusionCoordinator : IAsyncDisposable
{
    FusionModeState State { get; }
    bool IsArmed { get; }
    FusionSnapshot Snapshot { get; }
    string? LastError { get; }
    bool AutomationEnabled { get; }

    event EventHandler<FusionStateChangedEventArgs>? StateChanged;
    event EventHandler<FusionSnapshot>? SnapshotChanged;

    Task<OperationResult> EngageAsync(bool manual = true);
    Task<OperationResult> DisengageAsync();
    Task RefreshAsync();
    void StartAutomation();
    void StopAutomation();
    Task<OperationResult> RestoreInterruptedSessionAsync();
    Task<OperationResult> ApplyTweaksAsync(IEnumerable<string> tweakIds);
    Task<OperationResult> RevertTweaksAsync(IEnumerable<string> tweakIds);
    Task<OperationResult> ApplySingleTweakAsync(string tweakId, bool apply);
}

public sealed class FusionStateChangedEventArgs(FusionModeState state, string message, GameProfile? game) : EventArgs
{
    public FusionModeState State { get; } = state;
    public string Message { get; } = message;
    public GameProfile? Game { get; } = game;
}

public interface IFileSaveService
{
    string? PickSavePath(string suggestedFileName, string filter);
    string? PickOpenPath(string filter);
}
