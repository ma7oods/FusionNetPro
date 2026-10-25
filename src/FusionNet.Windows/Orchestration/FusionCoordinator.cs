using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Planning;
using FusionNet.Core.Utilities;
using FusionNet.Windows.System;

namespace FusionNet.Windows.Orchestration;

/// <summary>
/// The heart of FusionNet Pro: runs the whole Fusion Mode session - firewall blocks, service
/// pauses, app suspension/throttling, game priority boost, power plan, latency tweaks, DNS flush
/// and standby purge - then keeps that desired state reconciled every two seconds and fully
/// reverses it from the crash-safe journal on disengage or after an interrupted session.
/// </summary>
public sealed class FusionCoordinator : IFusionCoordinator
{
    public const string Category = "Fusion";

    private static readonly TimeSpan ReconcilerInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] PriorityPrefix = ["priority:"];
    private const string SuspendedValue = "suspended";
    private const string RunningValue = "running";
    private const string PowerPlanTarget = "powerplan";

    private readonly Func<FusionSettings> _settingsAccessor;
    private readonly ISettingsStore _settingsStore;
    private readonly IJournal _journal;
    private readonly IActivityLog _activity;
    private readonly IGameDetectionService _games;
    private readonly IFirewallService _firewall;
    private readonly IWindowsServiceController _windowsServices;
    private readonly IProcessController _processes;
    private readonly IRegistryTweakService _tweaks;
    private readonly IPowerPlanService _power;
    private readonly IStandbyMemoryService _memory;
    private readonly ISystemInfoService _systemInfo;
    private readonly IElevationService _elevation;
    private readonly Func<bool>? _otherSessionPending;
    private readonly SemaphoreSlim? _sessionStartGate;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();

    private volatile FusionModeState _state = FusionModeState.Idle;
    private volatile string? _lastError;
    private volatile GameProfile? _game;
    private volatile bool _manualSession;
    private volatile bool _automationEnabled;
    private volatile bool _disposed;
    private DateTimeOffset? _armedAtUtc;

    // Session bookkeeping, mutated only while the coordinator gate is held (plus the short
    // snapshots taken under _stateGate).
    private readonly HashSet<string> _activeRuleNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ruleEntryIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pausedServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ControlledProcess> _controlled = new();
    private readonly HashSet<string> _sessionAppliedTweaks = new(StringComparer.OrdinalIgnoreCase);
    private int? _boostedPid;
    private string? _boostedEntryId;
    private string? _boostedName;
    private bool _dnsFlushed;
    private long _memoryPurgedBytes;
    private string? _watchListSignature;
    private string? _activeSessionId;
    private FusionSnapshot? _lastPublishedSnapshot;

    private CancellationTokenSource? _reconcilerCts;
    private Task? _reconcilerTask;

    public FusionCoordinator(
        Func<FusionSettings> settingsAccessor,
        ISettingsStore settingsStore,
        IJournal journal,
        IActivityLog activity,
        IGameDetectionService games,
        IFirewallService firewall,
        IWindowsServiceController windowsServices,
        IProcessController processes,
        IRegistryTweakService tweaks,
        IPowerPlanService power,
        IStandbyMemoryService memory,
        ISystemInfoService systemInfo,
        IElevationService elevation,
        Func<bool>? otherSessionPending = null,
        SemaphoreSlim? sessionStartGate = null)
    {
        _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _windowsServices = windowsServices ?? throw new ArgumentNullException(nameof(windowsServices));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _tweaks = tweaks ?? throw new ArgumentNullException(nameof(tweaks));
        _power = power ?? throw new ArgumentNullException(nameof(power));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
        _otherSessionPending = otherSessionPending;
        _sessionStartGate = sessionStartGate;
    }

    public event EventHandler<FusionStateChangedEventArgs>? StateChanged;

    public event EventHandler<FusionSnapshot>? SnapshotChanged;

    // ================================================================= public surface

    public FusionModeState State => _state;
    public bool IsTransitioning => _gate.CurrentCount == 0;

    // A live session only: armed automation must not block Game Boost or flip the Engage button.
    public bool IsArmed => _state is FusionModeState.Arming or FusionModeState.Active;

    public bool AutomationEnabled => _automationEnabled;

    public string? LastError => _lastError;

    public FusionSnapshot Snapshot
    {
        get
        {
            lock (_stateGate) return BuildSnapshotLocked();
        }
    }

    public async Task<OperationResult> EngageAsync(bool manual = true)
    {
        if (_sessionStartGate is not null) await _sessionStartGate.WaitAsync().ConfigureAwait(false);
        try { return await EngageEntryAsync(manual).ConfigureAwait(false); }
        finally { _sessionStartGate?.Release(); }
    }

    private async Task<OperationResult> EngageEntryAsync(bool manual)
    {
        if (_disposed) return OperationResult.Fail("FusionNet Pro is shutting down.");

        if (!SafeIsElevated())
        {
            _activity.Warning(Category, "Starting Fusion Mode needs administrator rights.");
            return OperationResult.NotElevated("Starting Fusion Mode");
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_otherSessionPending?.Invoke() == true)
                return OperationResult.Fail("Stop and restore Game Boost before starting legacy Fusion Mode.");
            if (_state is FusionModeState.Active or FusionModeState.Arming)
            {
                return OperationResult.Ok("Fusion Mode is already active.");
            }

            return await Task.Run(() => EngageCoreAsync(manual)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _activity.Error(Category, "Fusion Mode could not start: " + ex.Message);
            return OperationResult.Fail("Fusion Mode could not start: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> DisengageAsync()
    {
        if (_disposed) return OperationResult.Fail("FusionNet Pro is shutting down.");

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == FusionModeState.Idle && !_journal.HasPendingRestore)
            {
                return OperationResult.Ok("Fusion Mode is not active.");
            }

            return await Task.Run(DisengageCore).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _activity.Error(Category, "Fusion Mode could not be restored: " + ex.Message);
            return OperationResult.Fail("Fusion Mode could not be restored: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == FusionModeState.Active)
            {
                await Task.Run(ReconcileActiveSession).ConfigureAwait(false);
            }
            else
            {
                RefreshWatchList(CurrentSettings());
                PublishSnapshot();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine("Fusion refresh failed: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Fusion refresh failed: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void StartAutomation()
    {
        if (_disposed) return;

        lock (_stateGate)
        {
            if (_automationEnabled) return;
            _automationEnabled = true;
        }

        try
        {
            // A game detected before we subscribed raises no Changed event; a new detection does.
            var alreadyDetected = _games.IsGameRunning;
            _games.Changed += OnGameChanged;
            _games.Start();
            RefreshWatchList(CurrentSettings());
            _games.PollNow();

            _activity.Info(Category, "Fusion Mode automation armed. Waiting for a game to start.");
            if (alreadyDetected && _games.IsGameRunning && _state is FusionModeState.Idle or FusionModeState.Error)
            {
                TriggerAutoEngage();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException)
        {
            _activity.Error(Category, "Game automation could not be armed: " + ex.Message);
        }

        PublishSnapshot();
    }

    public void StopAutomation()
    {
        lock (_stateGate)
        {
            if (!_automationEnabled) return;
            _automationEnabled = false;
        }

        try
        {
            _games.Changed -= OnGameChanged;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine("Game automation unsubscribe failed: " + ex.Message);
        }

        // An already active session is left to run: the user explicitly asked to stop watching
        // for games, not to end the current match. DisengageAsync restores the machine.
        _activity.Info(Category, "Fusion Mode automation stopped.");
        PublishSnapshot();
    }

    public async Task<OperationResult> RestoreInterruptedSessionAsync()
    {
        if (_disposed) return OperationResult.Fail("FusionNet Pro is shutting down.");

        if (_state is FusionModeState.Active or FusionModeState.Arming)
        {
            // A pending document while this session is live is the *current* session (Begin sets
            // Active=true). The startup auto-restore call must not tear down the new session.
            return string.Equals(_journal.Document.SessionId, _activeSessionId, StringComparison.Ordinal)
                ? OperationResult.Ok("Fusion Mode is active; the interrupted session was already restored.")
                : OperationResult.Fail("Fusion Mode is active. Stop it before restoring an earlier session.");
        }

        if (!_journal.HasPendingRestore) return OperationResult.Ok("No interrupted Fusion Mode session was found.");

        if (!SafeIsElevated())
        {
            _activity.Warning(Category, "An interrupted Fusion Mode session can only be restored as administrator.");
            return OperationResult.NotElevated("Restoring the previous Fusion Mode session");
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(RestoreInterruptedCore).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _activity.Error(Category, "The interrupted session could not be restored: " + ex.Message);
            return OperationResult.Fail("The interrupted session could not be restored: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<OperationResult> ApplyTweaksAsync(IEnumerable<string> tweakIds) => ChangeTweaksAsync(tweakIds, apply: true);

    public Task<OperationResult> RevertTweaksAsync(IEnumerable<string> tweakIds) => ChangeTweaksAsync(tweakIds, apply: false);

    public Task<OperationResult> ApplySingleTweakAsync(string tweakId, bool apply) =>
        ChangeTweaksAsync([tweakId], apply);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _games.Changed -= OnGameChanged;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine("Game automation unsubscribe failed during dispose: " + ex.Message);
        }

        StopReconciler();
        var running = _reconcilerTask;
        if (running is not null)
        {
            try
            {
                await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                Debug.WriteLine("Reconciler did not stop cleanly: " + ex.Message);
            }
        }

        lock (_stateGate)
        {
            _reconcilerCts?.Dispose();
            _reconcilerCts = null;
            _reconcilerTask = null;
        }

        _gate.Dispose();
    }

    // ================================================================= engage

    private async Task<OperationResult> EngageCoreAsync(bool manual)
    {
        _manualSession = manual;
        var journalBegan = false;

        try
        {
            SetState(FusionModeState.Arming, "Preparing Fusion Mode...", null);

            if (_journal.HasPendingRestore)
            {
                _activity.Warning(Category, "An interrupted Fusion Mode session was found; restoring it before starting again.");
                var restore = RestoreInterruptedCore();
                if (!restore.Success) return restore;
            }

            var settings = CurrentSettings();
            var running = SafeGetProcesses();
            var game = DetectGame(settings, running);
            var plan = OptimizationPlanner.Build(settings, game, running);

            _game = plan.Game;

            // A manual session without a running game still applies the environment-level part
            // (tweaks / power plan / DNS / RAM). Automatic sessions always have a running game.
            var noGame = !plan.GameRunning;
            var switchPowerPlan = plan.SwitchPowerPlan || (manual && noGame && settings.SwitchPowerPlan);
            var flushDns = plan.FlushDns || (manual && noGame && settings.FlushDnsOnEngage);
            var purgeMemory = plan.PurgeMemory || (manual && noGame && settings.PurgeStandbyMemoryOnEngage);

            _journal.Begin(plan.Game?.Name);
            journalBegan = true;
            ResetSessionState();
            _activeSessionId = _journal.Document.SessionId;
            _lastError = null;

            var warnings = new List<string>();

            // 1) Temporary firewall blocks (journaled before they are created).
            if (plan.FirewallRules.Count > 0)
            {
                var firewallResult = ApplyFirewallRules(plan.FirewallRules);
                if (!firewallResult.Success)
                {
                    return await AbortEngageAsync("Firewall rules could not be created: " + firewallResult.Message,
                        firewallResult.Details, journalBegan).ConfigureAwait(false);
                }
            }

            // 2) Pause Windows download/update services.
            warnings.AddRange(PauseServices(plan.ServicesToPause));

            // 3) Suspend or throttle the configured background applications.
            warnings.AddRange(ControlPlannedApps(plan.AppRules));

            // 4) Boost the running game process.
            if (plan.BoostGame && plan.GameProcessId is int gameProcessId)
            {
                EnsureGameBoosted(boost: true, gameProcessId, plan.Game?.Name);
            }

            // 5) High performance power plan.
            if (switchPowerPlan) EnsurePowerPlan(switchOn: true, warnings);

            // 6) Latency registry tweaks (only those that are not already applied).
            warnings.AddRange(ApplySessionTweaks(plan.Tweaks));

            // 7) Flush the DNS cache so the game resolves through the fresh, latency-tuned stack.
            if (flushDns) FlushDnsCache();

            // 8) Purge standby memory (opt-in).
            if (purgeMemory) PurgeStandbyMemory();

            _armedAtUtc = DateTimeOffset.UtcNow;
            var activeMessage = plan.Game is not null
                ? $"Fusion Mode is active for {plan.Game.Name}."
                : "Fusion Mode is active.";

            SetState(FusionModeState.Active, activeMessage, plan.Game);
            StartReconciler();
            PublishSnapshot();

            _activity.Success(Category, activeMessage);
            if (warnings.Count > 0)
            {
                _activity.Warning(Category, "Fusion Mode started with notes: " + string.Join("; ", warnings));
            }

            return OperationResult.Ok(activeMessage, warnings.Count > 0 ? warnings : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastError = ex.Message;
            _activity.Error(Category, "Unexpected error while starting Fusion Mode: " + ex);
            await AbortEngageAsync("Unexpected error: " + ex.Message, null, journalBegan).ConfigureAwait(false);
            return OperationResult.Fail("Unexpected error while starting Fusion Mode: " + ex.Message);
        }
    }

    /// <summary>Rolls back everything the aborted session applied and parks in the Error state.</summary>
    private async Task<OperationResult> AbortEngageAsync(string message, IReadOnlyList<string>? details, bool journalBegan)
    {
        try
        {
            var report = journalBegan ? UndoJournal() : new UndoReport([], []);

            // Keep the journal pending when a restore step failed so the next launch (or a retry)
            // can still undo it; otherwise the session is fully reversed and can be closed.
            if (journalBegan && report.CriticalFailures.Count == 0) _journal.Complete();
            ResetSessionState();
            _armedAtUtc = null;

            var errors = details?.ToList() ?? [];
            errors.AddRange(report.CriticalFailures);
            errors.AddRange(report.Warnings);
            if (report.CriticalFailures.Count > 0)
            {
                errors.Add("Rollback was incomplete; the session journal stays pending for crash recovery.");
            }

            _lastError = message;
            SetState(FusionModeState.Error, message, _game);
            _activity.Error(Category, message);
        }
        catch (Exception ex)
        {
            _lastError = message + " Cleanup also failed: " + ex.Message;
            SetState(FusionModeState.Error, _lastError, _game);
            _activity.Error(Category, _lastError);
        }

        StopReconciler();
        PublishSnapshot();
        return await Task.FromResult(OperationResult.Fail(message, details));
    }

    // ================================================================= disengage & recovery

    private OperationResult DisengageCore()
    {
        try
        {
            if (_state == FusionModeState.Idle && !_journal.HasPendingRestore)
            {
                return OperationResult.Ok("Fusion Mode is not active.");
            }

            SetState(FusionModeState.Restoring, "Restoring your normal Windows state...", _game);
            var report = UndoJournal();

            if (report.CriticalFailures.Count == 0)
            {
                _journal.Complete();
                ResetSessionState();
                _manualSession = false;
                _game = null;
                _lastError = null;
                _armedAtUtc = null;
                StopReconciler();
                SetState(FusionModeState.Idle, "Fusion Mode is off. Everything restored.", null);

                if (report.Warnings.Count > 0)
                {
                    _activity.Warning(Category, "Fusion Mode stopped with notes: " + string.Join("; ", report.Warnings));
                }

                _activity.Success(Category, "Fusion Mode is off. Every change was reversed.");
                return OperationResult.Ok("Fusion Mode is off. Everything restored.",
                    report.Warnings.Count > 0 ? report.Warnings : null);
            }

            _lastError = "Restore incomplete: " + string.Join("; ", report.CriticalFailures);
            SetState(FusionModeState.Error, _lastError, _game);
            _activity.Error(Category, _lastError);
            return OperationResult.Fail("Some Windows changes could not be restored. Run Fusion Mode again as administrator to retry.",
                report.CriticalFailures.Concat(report.Warnings).ToArray());
        }
        catch (Exception ex)
        {
            _lastError = "Restore failed: " + ex.Message;
            _activity.Error(Category, _lastError, ex.ToString());
            SetState(FusionModeState.Error, _lastError, _game);
            return OperationResult.Fail(_lastError);
        }
    }

    private OperationResult RestoreInterruptedCore()
    {
        try
        {
            var document = _journal.Document;
            var started = document.StartedAtUtc.ToLocalTime();
            SetState(FusionModeState.Restoring, "Restoring the previous Fusion Mode session...", null);
            _activity.Warning(Category,
                $"A Fusion Mode session started at {started:g} did not restore itself. Undoing its changes now.");

            var report = UndoJournal();

            if (report.CriticalFailures.Count > 0)
            {
                // Leave the journal Active so the next launch or a retry can finish the job.
                _lastError = "Interrupted session restore finished with problems: " + string.Join("; ", report.CriticalFailures);
                SetState(FusionModeState.Error, _lastError, null);
                _activity.Error(Category, _lastError);
                return OperationResult.Fail("The interrupted session was only partially restored.", report.CriticalFailures);
            }

            _journal.Complete();
            ResetSessionState();
            _manualSession = false;
            _game = null;
            _armedAtUtc = null;
            _lastError = null;
            StopReconciler();

            SetState(FusionModeState.Idle, "The interrupted Fusion Mode session was restored.", null);
            _activity.Success(Category, "Previous Fusion Mode session restored. Windows is back to its normal state.");
            return OperationResult.Ok("Interrupted Fusion Mode session restored.",
                report.Warnings.Count > 0 ? report.Warnings : null);
        }
        catch (Exception ex)
        {
            _lastError = "Interrupted session restore failed: " + ex.Message;
            _activity.Error(Category, _lastError, ex.ToString());
            SetState(FusionModeState.Error, _lastError, null);
            return OperationResult.Fail(_lastError);
        }
    }

    // ================================================================= reconciler

    private void StartReconciler()
    {
        lock (_stateGate)
        {
            if (_disposed) return;

            var stillRunning = _reconcilerTask is { IsCompleted: false };
            var stillLive = _reconcilerCts is { IsCancellationRequested: false };
            if (stillRunning && stillLive) return;

            // A cancelled loop may still be finishing its last gate wait; the new loop simply
            // coexists for one tick because the old token immediately ends it.
            if (!stillRunning) _reconcilerCts?.Dispose();

            _reconcilerCts = new CancellationTokenSource();
            var token = _reconcilerCts.Token;
            _reconcilerTask = Task.Run(() => ReconcilerLoopAsync(token));
        }
    }

    private void StopReconciler()
    {
        CancellationTokenSource? cts;
        lock (_stateGate)
        {
            cts = _reconcilerCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            Debug.WriteLine("Reconciler cancellation failed: " + ex.Message);
        }
    }

    private async Task ReconcilerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ReconcilerInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_state == FusionModeState.Active)
                    {
                        await Task.Run(ReconcileActiveSession, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The reconciler must never take the process down; log and try again next tick.
                _activity.Error(Category, "Fusion Mode refresh failed: " + ex.Message);
            }
        }
    }

    private void ReconcileActiveSession()
    {
        if (_state != FusionModeState.Active || _disposed) return;

        var settings = CurrentSettings();
        var running = SafeGetProcesses();
        var game = DetectGame(settings, running);

        if (!_manualSession && game is null && settings.RestoreOnGameExit)
        {
            _activity.Info(Category, "The game exited and Fusion Mode is set to restore on exit.");
            DisengageCore();
            return;
        }

        var plan = OptimizationPlanner.Build(settings, game, running);
        if (plan.Game is not null) _game = plan.Game;

        RefreshWatchList(settings);
        SynchronizeServices(plan.ServicesToPause);
        SynchronizeAppRules(plan.AppRules);
        SynchronizeFirewall(settings.EnableFirewallBlocking, plan.FirewallRules);

        if (plan.BoostGame && plan.GameProcessId is int gameProcessId)
        {
            EnsureGameBoosted(boost: true, gameProcessId, plan.Game?.Name);
        }
        else
        {
            EnsureGameBoosted(boost: false, null, null);
        }

        if (plan.SwitchPowerPlan || (_manualSession && !plan.GameRunning && settings.SwitchPowerPlan))
        {
            EnsurePowerPlan(switchOn: true, warnings: null);
        }

        PublishSnapshot();
    }

    // ================================================================= firewall

    private OperationResult ApplyFirewallRules(IReadOnlyList<PlannedFirewallRule> rules)
    {
        var planned = rules
            .Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.Name))
            .DistinctBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (planned.Length == 0) return OperationResult.Ok("No firewall rules were needed.");

        var entries = new List<(PlannedFirewallRule Rule, JournalEntry? Entry)>(planned.Length);
        foreach (var rule in planned)
        {
            var entry = Record(JournalKind.Firewall, rule.Name,
                $"Block network for {rule.DisplayName}", "absent", rule.DisplayName);
            entries.Add((rule, entry));
        }

        var result = _firewall.ApplyRules(planned);
        if (!result.Success)
        {
            // A partial creation is possible; keep the journal entries un-restored so the rollback
            // (or the next disengage) sweeps every FNP_ rule away with RemoveAllOwnedRules.
            _activity.Warning(Category, "Temporary firewall rules could not be created: " + result.Message);
            return result;
        }

        lock (_stateGate)
        {
            foreach (var (rule, entry) in entries)
            {
                _activeRuleNames.Add(rule.Name);
                if (entry is not null) _ruleEntryIds[rule.Name] = entry.Id;
            }
        }

        _activity.Info(Category, $"Temporary firewall rules active for {planned.Length} program(s).");
        return result;
    }

    private void SynchronizeFirewall(bool enabled, IReadOnlyList<PlannedFirewallRule> rules)
    {
        try
        {
            var desired = enabled
                ? rules.Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.Name))
                    .DistinctBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
            var desiredNames = new HashSet<string>(desired.Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);

            string[] obsolete;
            lock (_stateGate)
            {
                obsolete = _activeRuleNames.Where(name => !desiredNames.Contains(name)).ToArray();
            }

            if (obsolete.Length > 0)
            {
                var removeResult = _firewall.RemoveRules(obsolete);
                if (removeResult.Success)
                {
                    lock (_stateGate)
                    {
                        foreach (var name in obsolete)
                        {
                            _activeRuleNames.Remove(name);
                            if (_ruleEntryIds.TryGetValue(name, out var entryId))
                            {
                                _ruleEntryIds.Remove(name);
                                SafeMarkRestored(entryId);
                            }
                        }
                    }

                    _activity.Info(Category, $"Removed {obsolete.Length} firewall rule(s) that are no longer needed.");
                }
                else
                {
                    _activity.Warning(Category, "Could not remove obsolete firewall rules: " + removeResult.Message);
                }
            }

            PlannedFirewallRule[] missing;
            lock (_stateGate)
            {
                missing = desired.Where(rule => !_activeRuleNames.Contains(rule.Name)).ToArray();
            }

            if (missing.Length == 0) return;

            var entries = new List<(PlannedFirewallRule Rule, JournalEntry? Entry)>(missing.Length);
            foreach (var rule in missing)
            {
                entries.Add((rule, Record(JournalKind.Firewall, rule.Name,
                    $"Block network for {rule.DisplayName}", "absent", rule.DisplayName)));
            }

            var applyResult = _firewall.ApplyRules(missing);
            if (!applyResult.Success)
            {
                // ApplyRules can create a subset. Sweeping every owned rule (not only the requested
                // ones) means the session bookkeeping has to be rebuilt from scratch: otherwise the
                // reconciler would believe rules exist that this failed apply just deleted.
                try { _firewall.RemoveAllOwnedRules(); }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    _activity.Warning(Category, "Firewall cleanup after a failed refresh also failed: " + ex.Message);
                }

                List<string> staleEntryIds;
                lock (_stateGate)
                {
                    staleEntryIds = [.. _ruleEntryIds.Values];
                    _ruleEntryIds.Clear();
                    _activeRuleNames.Clear();
                }

                // The sweep removed every FusionNet rule, so the journal entries are truly restored;
                // the next reconciler tick records fresh entries and re-creates the desired rules.
                foreach (var entryId in staleEntryIds) SafeMarkRestored(entryId);
                foreach (var (_, entry) in entries) SafeMarkRestored(entry);
                _activity.Warning(Category, "Could not refresh firewall rules: " + applyResult.Message);
                return;
            }

            lock (_stateGate)
            {
                foreach (var (rule, entry) in entries)
                {
                    _activeRuleNames.Add(rule.Name);
                    if (entry is not null) _ruleEntryIds[rule.Name] = entry.Id;
                }
            }

            _activity.Info(Category, $"Added {missing.Length} firewall rule(s) for newly detected applications.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Warning(Category, "Firewall refresh failed: " + ex.Message);
        }
    }

    // ================================================================= services

    private List<string> PauseServices(IReadOnlyList<ServiceDefinition> services)
    {
        var warnings = new List<string>();
        if (services.Count == 0) return warnings;

        var stopped = 0;
        foreach (var service in services)
        {
            try
            {
                var state = _windowsServices.GetState(service);
                if (state.State is ServicePauseState.Stopped or ServicePauseState.NotFound) continue;
                if (state.State is ServicePauseState.Unknown) continue;

                var entry = Record(JournalKind.Service, service.ServiceName,
                    $"Pause {service.NameEn}", RunningValue, "stopped");
                _ = entry; // Journal entry stays pending unless the stop succeeds (see failure branch).
                var result = _windowsServices.Stop(service, ServiceStopTimeout);
                if (result.Success)
                {
                    stopped++;
                    lock (_stateGate) _pausedServices.Add(service.ServiceName);
                }
                else
                {
                    // Keep the journal entry pending: a timed-out stop may still complete later and
                    // the rollback must restart the service (starting an already running one is a no-op).
                    warnings.Add(result.Message);
                    _activity.Warning(Category, "Could not pause " + result.Message);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{service.NameEn}: {ex.Message}");
                _activity.Warning(Category, $"Could not pause {service.NameEn}: {ex.Message}");
            }
        }

        if (stopped > 0)
        {
            _activity.Info(Category, $"Paused {stopped} Windows download service(s) for the session.");
        }

        return warnings;
    }

    private void SynchronizeServices(IReadOnlyList<ServiceDefinition> services)
    {
        foreach (var service in services)
        {
            try
            {
                var state = _windowsServices.GetState(service);
                if (state.State is ServicePauseState.Stopped or ServicePauseState.NotFound or ServicePauseState.Unknown) continue;

                var hasEntry = HasOpenEntry(JournalKind.Service, service.ServiceName);
                if (!hasEntry)
                {
                    _ = Record(JournalKind.Service, service.ServiceName, $"Pause {service.NameEn}", RunningValue, "stopped");
                }

                var result = _windowsServices.Stop(service, ServiceStopTimeout);
                if (result.Success)
                {
                    lock (_stateGate) _pausedServices.Add(service.ServiceName);
                    _activity.Info(Category, $"{service.NameEn} was restarted by Windows and has been paused again.");
                }
                else
                {
                    // The pending entry (new or existing) is what makes the rollback start the service again.
                    _activity.Warning(Category, $"Could not re-pause {service.NameEn}: {result.Message}");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _activity.Warning(Category, $"Could not re-pause {service.NameEn}: {ex.Message}");
            }
        }
    }

    // ================================================================= apps (suspend / throttle)

    private List<string> ControlPlannedApps(IReadOnlyList<PlannedAppRule> rules)
    {
        var warnings = new List<string>();

        foreach (var rule in rules)
        {
            if (rule.Action is not (AppControlAction.Suspend or AppControlAction.Throttle)) continue;

            foreach (var processId in rule.ProcessIds.Distinct())
            {
                var processName = ResolveProcessName(processId, rule.ProcessNames);
                ControlProcess(processId, processName, rule.Name, rule.Action, warnings);
            }
        }

        return warnings;
    }

    private bool ControlProcess(int processId, string processName, string displayName,
        AppControlAction action, List<string> warnings)
    {
        lock (_stateGate)
        {
            if (_controlled.ContainsKey(processId)) return true;
        }

        if (IsProtectedProcess(processId, processName))
        {
            _activity.Info(Category, $"Left the protected process {processName} untouched.");
            return true;
        }

        try
        {
            if (action == AppControlAction.Suspend)
            {
                var entry = Record(JournalKind.Process, $"{processId}|{processName}",
                    $"Suspend {displayName}", RunningValue, SuspendedValue);
                var result = _processes.SetSuspended(processId, true);
                if (!result.Success)
                {
                    SafeMarkRestored(entry);
                    warnings.Add(result.Message);
                    _activity.Warning(Category, $"Could not suspend {processName}: {result.Message}");
                    return false;
                }

                lock (_stateGate)
                {
                    _controlled[processId] = new ControlledProcess(processId, processName, action, entry?.Id);
                }

                return true;
            }

            if (action == AppControlAction.Throttle)
            {
                var previousPriority = ReadPriorityLabel(processId);
                var entry = Record(JournalKind.Process, $"{processId}|{processName}",
                    $"Throttle {displayName}", "priority:" + previousPriority, "priority:BelowNormal");
                var result = _processes.SetPriority(processId, ProcessPriority.BelowNormal);
                if (!result.Success)
                {
                    SafeMarkRestored(entry);
                    warnings.Add(result.Message);
                    _activity.Warning(Category, $"Could not throttle {processName}: {result.Message}");
                    return false;
                }

                lock (_stateGate)
                {
                    _controlled[processId] = new ControlledProcess(processId, processName, action, entry?.Id);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{processName}: {ex.Message}");
            _activity.Warning(Category, $"Could not control {processName}: {ex.Message}");
            return false;
        }
    }

    private void SynchronizeAppRules(IReadOnlyList<PlannedAppRule> rules)
    {
        var desired = new Dictionary<int, (AppControlAction Action, string Name)>();
        foreach (var rule in rules)
        {
            if (rule.Action is not (AppControlAction.Suspend or AppControlAction.Throttle)) continue;
            foreach (var processId in rule.ProcessIds.Distinct())
            {
                desired[processId] = (rule.Action, rule.Name);
            }
        }

        List<KeyValuePair<int, ControlledProcess>> tracked;
        lock (_stateGate)
        {
            tracked = _controlled.ToArray().ToList();
        }

        var warnings = new List<string>();
        foreach (var (processId, controlled) in tracked)
        {
            var stillWanted = desired.TryGetValue(processId, out var wanted) && wanted.Action == controlled.Action;
            if (stillWanted) continue;

            if (!ReleaseControlledProcess(processId, controlled, warnings))
            {
                continue;
            }

            if (!desired.ContainsKey(processId))
            {
                _activity.Info(Category, $"{controlled.Name} left the plan; it was restored.");
            }
        }

        foreach (var (processId, (action, name)) in desired)
        {
            bool trackedWithSameAction;
            lock (_stateGate)
            {
                trackedWithSameAction = _controlled.TryGetValue(processId, out var existing) && existing.Action == action;
            }

            if (trackedWithSameAction) continue;

            var processName = ResolveProcessName(processId, [name]);
            ControlProcess(processId, processName, name, action, warnings);
        }

        foreach (var warning in warnings)
        {
            _activity.Warning(Category, warning);
        }
    }

    private bool ReleaseControlledProcess(int processId, ControlledProcess controlled, List<string> warnings)
    {
        JournalEntry? entry = null;
        if (controlled.EntryId is not null)
        {
            entry = _journal.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, controlled.EntryId, StringComparison.Ordinal));
        }

        if (entry is not null && !entry.Restored)
        {
            if (!UndoProcessEntry(entry, warnings)) return false;
            SafeMarkRestored(entry.Id);
            return true;
        }

        // No usable journal entry (older session / partial write): restore a sane default.
        OperationResult fallback;
        try
        {
            fallback = controlled.Action == AppControlAction.Suspend
                ? _processes.SetSuspended(processId, false)
                : _processes.SetPriority(processId, ProcessPriority.Normal);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{controlled.Name}: {ex.Message}");
            return false;
        }

        if (!fallback.Success)
        {
            warnings.Add(fallback.Message);
            return false;
        }

        lock (_stateGate) _controlled.Remove(processId);
        return true;
    }

    // ================================================================= game boost

    private void EnsureGameBoosted(bool boost, int? processId, string? gameLabel)
    {
        int? currentPid;
        string? currentName;
        lock (_stateGate)
        {
            currentPid = _boostedPid;
            currentName = _boostedName;
        }

        if (!boost || processId is null)
        {
            if (currentPid is not null) ReleaseGameBoost(currentPid.Value, currentName);
            return;
        }

        var targetPid = processId.Value;
        if (currentPid == targetPid)
        {
            // Keep the boost pinned even if the game or a launcher reset the priority class.
            if (!IsHighPriority(targetPid))
            {
                var refresh = _processes.BoostForGaming(targetPid);
                if (!refresh.Success) _activity.Warning(Category, "Could not keep the game boosted: " + refresh.Message);
            }

            return;
        }

        if (currentPid is not null) ReleaseGameBoost(currentPid.Value, currentName);

        // The game itself is what we want to boost, so only true system processes are barred here.
        var processName = ResolveProcessName(targetPid, string.IsNullOrWhiteSpace(gameLabel) ? [] : [gameLabel]);
        if (IsSystemProcess(targetPid, processName))
        {
            _activity.Info(Category, "Skipped boosting a system process.");
            return;
        }

        try
        {
            var previousPriority = ReadPriorityLabel(targetPid);
            var entry = Record(JournalKind.Process, $"{targetPid}|{processName}",
                $"Boost {processName}", "priority:" + previousPriority, "priority:High");
            var result = _processes.BoostForGaming(targetPid);
            if (!result.Success)
            {
                SafeMarkRestored(entry);
                _activity.Warning(Category, "Could not boost the game process: " + result.Message);
                return;
            }

            lock (_stateGate)
            {
                _boostedPid = targetPid;
                _boostedEntryId = entry?.Id;
                _boostedName = processName;
            }

            _activity.Success(Category, $"Game process {processName} boosted to High priority.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Warning(Category, "Could not boost the game process: " + ex.Message);
        }
    }

    private void ReleaseGameBoost(int processId, string? processName)
    {
        string? entryId;
        lock (_stateGate)
        {
            entryId = _boostedEntryId;
            _boostedPid = null;
            _boostedEntryId = null;
            _boostedName = null;
        }

        if (entryId is not null)
        {
            var entry = _journal.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, entryId, StringComparison.Ordinal));
            if (entry is not null && !entry.Restored)
            {
                var failures = new List<string>();
                if (UndoProcessEntry(entry, failures)) SafeMarkRestored(entry.Id);
                return;
            }
        }

        try
        {
            var result = _processes.RestoreDefaultPriority(processId);
            if (!result.Success) _activity.Warning(Category, "Could not restore the game priority: " + result.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Warning(Category, "Could not restore the game priority: " + ex.Message);
        }
    }

    // ================================================================= power, tweaks, dns, memory

    private void EnsurePowerPlan(bool switchOn, List<string>? warnings)
    {
        if (!switchOn) return;

        try
        {
            if (_power.IsHighPerformanceActive) return;

            var result = _power.SwitchToHighPerformance(_journal);
            if (!result.Success)
            {
                warnings?.Add(result.Message);
                _activity.Warning(Category, "Power plan could not be switched: " + result.Message);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            warnings?.Add(ex.Message);
            _activity.Warning(Category, "Power plan could not be switched: " + ex.Message);
        }
    }

    private List<string> ApplySessionTweaks(IReadOnlyList<TweakDefinition> tweaks)
    {
        var warnings = new List<string>();
        if (tweaks.Count == 0) return warnings;

        foreach (var tweak in tweaks)
        {
            try
            {
                var state = _tweaks.Read(tweak);
                if (state.Applied)
                {
                    _activity.Info(Category, $"{tweak.Id} is already applied and stays unchanged.");
                    continue;
                }

                var result = _tweaks.Apply(tweak, _journal);
                if (result.Success)
                {
                    lock (_stateGate) _sessionAppliedTweaks.Add(tweak.Id);
                }
                else
                {
                    warnings.Add(result.Message);
                    _activity.Warning(Category, $"Tweak {tweak.Id} could not be applied: {result.Message}");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{tweak.Id}: {ex.Message}");
                _activity.Warning(Category, $"Tweak {tweak.Id} could not be applied: {ex.Message}");
            }
        }

        if (_sessionAppliedTweaks.Count > 0)
        {
            _activity.Info(Category, $"Applied {_sessionAppliedTweaks.Count} latency tweak(s) for this session.");
        }

        return warnings;
    }

    private void FlushDnsCache()
    {
        try
        {
            var result = Native.Win32Native.FlushDnsCache();
            _dnsFlushed = result != 0;
            _activity.Info(Category, result != 0
                ? "DNS resolver cache flushed."
                : "DNS resolver cache flush returned no confirmation; continuing.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _activity.Warning(Category, "DNS cache flush failed: " + ex.Message);
        }
    }

    private void PurgeStandbyMemory()
    {
        try
        {
            var freed = _memory.Purge();
            _memoryPurgedBytes = freed;
            if (freed > 0)
            {
                Record(JournalKind.Memory, "standby", "Purged the Windows standby memory list", "-", freed.ToString());
                _activity.Info(Category, $"Standby memory purged (~{Humanize.Bytes(freed)} freed).");
            }
            else
            {
                _activity.Info(Category, "Standby memory purge requested (no measurable gain).");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _activity.Warning(Category, "Standby memory purge failed: " + ex.Message);
        }
    }

    // ================================================================= tweak API (Tuner page)

    private async Task<OperationResult> ChangeTweaksAsync(IEnumerable<string>? tweakIds, bool apply)
    {
        if (_disposed) return OperationResult.Fail("FusionNet Pro is shutting down.");

        var tweaks = (tweakIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => TweakCatalog.Find(id))
            .Where(tweak => tweak is not null)
            .Select(tweak => tweak!)
            .DistinctBy(tweak => tweak.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tweaks.Length == 0) return OperationResult.Fail("No valid latency tweak was selected.");

        // HasPendingRestore also reads true while a session is active (Begin sets Active=true).
        // Only an inactive/failed coordinator may treat that as a crashed session to recover.
        if (_state is FusionModeState.Idle or FusionModeState.Error && _journal.HasPendingRestore)
        {
            var restored = await RestoreInterruptedSessionAsync().ConfigureAwait(false);
            if (!restored.Success) return restored;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ChangeTweaksCore(tweaks, apply)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return OperationResult.Fail("The tweak could not be changed: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private OperationResult ChangeTweaksCore(IReadOnlyList<TweakDefinition> tweaks, bool apply)
    {
        var sessionActive = _state is FusionModeState.Active or FusionModeState.Arming;
        var journal = sessionActive ? _journal : NullJournal.Instance;
        var changed = 0;
        var failures = new List<string>();

        foreach (var tweak in tweaks)
        {
            try
            {
                if (apply)
                {
                    var result = _tweaks.Apply(tweak, journal);
                    if (result.Success)
                    {
                        changed++;
                        if (sessionActive) lock (_stateGate) _sessionAppliedTweaks.Add(tweak.Id);
                    }
                    else
                    {
                        failures.Add($"{tweak.Id}: {result.Message}");
                    }
                }
                else
                {
                    var reverted = sessionActive && RevertSessionTweak(tweak, failures);
                    if (!reverted)
                    {
                        var result = _tweaks.Revert(tweak, journal);
                        if (result.Success) changed++;
                        else failures.Add($"{tweak.Id}: {result.Message}");
                    }
                    else
                    {
                        changed++;
                    }

                    lock (_stateGate) _sessionAppliedTweaks.Remove(tweak.Id);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                failures.Add($"{tweak.Id}: {ex.Message}");
            }
        }

        var message = apply
            ? $"Applied {changed} of {tweaks.Count} latency tweak(s)."
            : $"Reverted {changed} of {tweaks.Count} latency tweak(s).";

        if (failures.Count > 0)
        {
            message = apply
                ? $"Applied {changed} of {tweaks.Count} tweaks; {failures.Count} failed."
                : $"Reverted {changed} of {tweaks.Count} tweaks; {failures.Count} failed.";
            _activity.Warning(Category, message + " " + string.Join("; ", failures));
        }
        else
        {
            _activity.Success(Category, message);
        }

        RaiseStateChanged(_state, message, _game);
        PublishSnapshot();
        return failures.Count == 0
            ? OperationResult.Ok(message)
            : OperationResult.Fail(message, failures);
    }

    /// <summary>Restores a session-applied tweak to its journaled previous value instead of deleting it.</summary>
    private bool RevertSessionTweak(TweakDefinition tweak, List<string> failures)
    {
        var entries = _journal.Entries
            .Where(entry => entry.Kind == JournalKind.Registry && !entry.Restored &&
                            TweakTargetMatches(entry.Target, tweak.Id))
            .ToArray();
        if (entries.Length == 0) return false;

        var allRestored = true;
        foreach (var entry in entries)
        {
            if (!UndoRegistryEntry(entry, failures))
            {
                allRestored = false;
                continue;
            }

            SafeMarkRestored(entry.Id);
        }

        return allRestored;
    }

    // ================================================================= journal undo engine

    private UndoReport UndoJournal()
    {
        var failures = new List<string>();
        var warnings = new List<string>();
        var entries = _journal.Entries;

        // 1) Resume suspended processes and restore CPU priorities (reverse order, boost included).
        foreach (var entry in entries.Where(entry => entry.Kind == JournalKind.Process && !entry.Restored).Reverse().ToArray())
        {
            if (UndoProcessEntry(entry, failures)) SafeMarkRestored(entry.Id);
        }

        lock (_stateGate)
        {
            _controlled.Clear();
            _boostedPid = null;
            _boostedEntryId = null;
            _boostedName = null;
        }

        // 2) Restart the services Windows Update / delivery optimisation should keep running.
        foreach (var entry in entries.Where(entry => entry.Kind == JournalKind.Service && !entry.Restored).Reverse().ToArray())
        {
            if (UndoServiceEntry(entry, failures)) SafeMarkRestored(entry.Id);
        }

        lock (_stateGate) _pausedServices.Clear();

        // 3) Remove every FusionNet-owned firewall rule (also sweeps crash leftovers).
        var openFirewallEntries = entries
            .Where(entry => entry.Kind == JournalKind.Firewall && !entry.Restored)
            .ToArray();
        if (openFirewallEntries.Length > 0)
        {
            var cleanupComplete = false;
            if (!SafeIsElevated())
            {
                // Never mark the rules restored when the cleanup could not even run: that would
                // complete the journal while temporary block rules are still on the machine.
                failures.Add("Firewall cleanup needs administrator rights; temporary block rules may still be active.");
            }
            else
            {
                try
                {
                    _firewall.RemoveAllOwnedRules();

                    // Verify against the live rule list: a failed netsh delete must keep its
                    // journal entry pending so the next launch/retry can finish the cleanup.
                    var remaining = new HashSet<string>(_firewall.GetOwnedRuleNames(), StringComparer.OrdinalIgnoreCase);
                    var stillOwned = 0;
                    foreach (var entry in openFirewallEntries)
                    {
                        if (remaining.Contains(entry.Target.Trim()))
                        {
                            stillOwned++;
                            failures.Add($"Firewall rule {entry.Target} is still active.");
                        }
                        else
                        {
                            SafeMarkRestored(entry.Id);
                        }
                    }

                    if (stillOwned == 0)
                    {
                        cleanupComplete = true;
                        _activity.Info(Category, $"Removed {openFirewallEntries.Length} temporary firewall rule(s).");
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    failures.Add("Firewall cleanup failed: " + ex.Message);
                }
            }

            if (cleanupComplete)
            {
                lock (_stateGate)
                {
                    _activeRuleNames.Clear();
                    _ruleEntryIds.Clear();
                }
            }
        }

        // 4) Restore the original power plan (the earliest journaled previous value wins).
        var powerEntries = entries.Where(entry => entry.Kind == JournalKind.PowerPlan && !entry.Restored).ToArray();
        if (powerEntries.Length > 0)
        {
            var previousPlan = powerEntries[0].PreviousValue;
            try
            {
                var result = string.IsNullOrWhiteSpace(previousPlan)
                    ? OperationResult.Ok()
                    : _power.Restore(previousPlan, _journal);

                if (result.Success)
                {
                    foreach (var entry in powerEntries) SafeMarkRestored(entry.Id);
                }
                else
                {
                    failures.Add("Power plan: " + result.Message);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                failures.Add("Power plan: " + ex.Message);
            }
        }

        // 5) Put every journaled registry value back exactly as it was.
        foreach (var entry in entries.Where(entry => entry.Kind == JournalKind.Registry && !entry.Restored).Reverse().ToArray())
        {
            if (UndoRegistryEntry(entry, failures)) SafeMarkRestored(entry.Id);
        }

        lock (_stateGate) _sessionAppliedTweaks.Clear();

        // 6) DNS changes recorded by another page that happened inside this session.
        foreach (var entry in entries.Where(entry => entry.Kind == JournalKind.Dns && !entry.Restored).Reverse().ToArray())
        {
            if (RestoreDnsEntry(entry, failures)) SafeMarkRestored(entry.Id);
        }

        // 7) Memory purge has no system state to restore; mark it handled.
        foreach (var entry in entries.Where(entry => entry.Kind == JournalKind.Memory && !entry.Restored))
        {
            SafeMarkRestored(entry.Id);
        }

        return new UndoReport(failures, warnings);
    }

    private bool UndoProcessEntry(JournalEntry entry, List<string> failures)
    {
        if (!TryParseProcessTarget(entry.Target, out var processId, out var processName))
        {
            return true;
        }

        var liveProcessId = ResolveLiveProcess(processId, processName);
        if (liveProcessId is null)
        {
            lock (_stateGate) _controlled.Remove(processId);
            return true;
        }

        OperationResult result;
        try
        {
            if (string.Equals(entry.AppliedValue, SuspendedValue, StringComparison.OrdinalIgnoreCase))
            {
                result = _processes.SetSuspended(liveProcessId.Value, false);
            }
            else if (entry.AppliedValue is not null &&
                     entry.AppliedValue.StartsWith(PriorityPrefix[0], StringComparison.OrdinalIgnoreCase))
            {
                result = _processes.SetPriority(liveProcessId.Value, ParsePriority(entry.PreviousValue));
            }
            else
            {
                lock (_stateGate) _controlled.Remove(processId);
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            failures.Add($"{processName}: {ex.Message}");
            return false;
        }

        if (!result.Success)
        {
            failures.Add($"{processName}: {result.Message}");
            return false;
        }

        lock (_stateGate)
        {
            _controlled.Remove(processId);
            _controlled.Remove(liveProcessId.Value);
            if (_boostedPid == processId || _boostedPid == liveProcessId) _boostedPid = null;
        }

        return true;
    }

    private bool UndoServiceEntry(JournalEntry entry, List<string> failures)
    {
        if (!string.Equals(entry.AppliedValue, "stopped", StringComparison.OrdinalIgnoreCase)) return true;

        var service = ServiceCatalog.FindByServiceName(entry.Target) ?? ServiceCatalog.Find(entry.Target);
        if (service is null) return true;

        try
        {
            var result = _windowsServices.Start(service, ServiceStartTimeout);
            if (!result.Success)
            {
                failures.Add($"{service.NameEn}: {result.Message}");
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            failures.Add($"{service.NameEn}: {ex.Message}");
            return false;
        }

        lock (_stateGate) _pausedServices.Remove(entry.Target);
        return true;
    }

    private bool UndoRegistryEntry(JournalEntry entry, List<string> failures)
    {
        if (!TryParseTweakTarget(entry.Target, out var tweakId, out var interfaceGuid)) return true;

        var tweak = TweakCatalog.Find(tweakId);
        if (tweak is null) return true;

        try
        {
            OperationResult result;
            if (_tweaks is ITweakJournalRestorer restorer)
            {
                result = restorer.RestorePrevious(tweak, entry.PreviousValue, interfaceGuid);
            }
            else if (string.IsNullOrWhiteSpace(entry.PreviousValue) ||
                     string.Equals(entry.PreviousValue, "absent", StringComparison.OrdinalIgnoreCase))
            {
                // Without the restorer the best we can do is remove the value again.
                result = _tweaks.Revert(tweak, _journal);
            }
            else
            {
                failures.Add($"{tweak.Id}: the tweak service cannot restore a previous value.");
                return false;
            }

            if (!result.Success)
            {
                failures.Add($"{tweak.Id}: {result.Message}");
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            failures.Add($"{tweak.Id}: {ex.Message}");
            return false;
        }

        lock (_stateGate) _sessionAppliedTweaks.Remove(tweak.Id);
        return true;
    }

    private bool RestoreDnsEntry(JournalEntry entry, List<string> failures)
    {
        try
        {
            var result = ApplyDns(entry.Target, entry.PreviousValue);
            if (!result.Success)
            {
                // Keep the entry pending: a DNS change that was not undone must not be buried in
                // a "everything restored" journal completion.
                failures.Add("DNS: " + result.Message);
                return false;
            }

            _activity.Info(Category, $"DNS settings restored for {entry.Target}.");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            failures.Add("DNS: " + ex.Message);
            return false;
        }
    }

    private static OperationResult ApplyDns(string? adapterId, string? previousValue)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
            return OperationResult.Fail("no adapter identifier was recorded");

        // The journal stores the adapter id (a GUID such as NetworkInterface.Id), while netsh
        // only accepts the friendly interface name for "name=". Resolve it first.
        var interfaceName = ResolveInterfaceName(adapterId);
        if (interfaceName is null)
        {
            // Unplugged adapters retain their configuration; retry when the adapter returns.
            return OperationResult.Fail("The recorded network adapter is unavailable; DNS recovery remains pending.");
        }

        var netsh = ResolveSystemTool("netsh.exe");
        if (netsh is null) return OperationResult.Fail("netsh.exe was not found");

        return RestoreDnsServers(interfaceName, previousValue,
            arguments => ProcessRunner.Run(netsh, arguments, ToolTimeout).Succeeded);
    }

    private static OperationResult RestoreDnsServers(
        string interfaceName, string? previousValue, Func<string[], bool> run)
    {
        var isDhcp = string.IsNullOrWhiteSpace(previousValue) ||
                     string.Equals(previousValue.Trim(), "dhcp", StringComparison.OrdinalIgnoreCase);

        if (isDhcp)
        {
            var succeeded = run(
            [
                "interface", "ipv4", "set", "dnsservers",
                "name=" + interfaceName,
                "source=dhcp"
            ]);

            return succeeded
                ? OperationResult.Ok("DNS returned to automatic.")
                : OperationResult.Fail("netsh could not reset the DNS servers on the recorded adapter.");
        }

        var servers = previousValue!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (servers.Length == 0 || servers.Any(server => !DnsCatalog.IsValidIPv4(server)))
            return OperationResult.Fail("The journaled DNS server list is invalid; recovery remains pending.");

        var first = run(
        [
            "interface", "ipv4", "set", "dnsservers",
            "name=" + interfaceName,
            "source=static",
            "address=" + servers[0],
            "register=primary",
            "validate=no"
        ]);
        if (!first) return OperationResult.Fail("netsh could not restore the primary DNS server.");

        for (var index = 1; index < servers.Length; index++)
        {
            var restored = run(
            [
                "interface", "ipv4", "add", "dnsservers",
                "name=" + interfaceName,
                "address=" + servers[index],
                "index=" + (index + 1)
            ]);
            if (!restored) return OperationResult.Fail($"netsh could not restore DNS server {index + 1}.");
        }

        return OperationResult.Ok("DNS servers restored.");
    }

    /// <summary>
    /// Maps a journaled adapter identifier (GUID id, friendly name or description) to the
    /// interface name that netsh accepts. Returns null when the adapter cannot be found and the
    /// identifier itself is a GUID (i.e. the adapter has been removed).
    /// </summary>
    private static string? ResolveInterfaceName(string adapterId)
    {
        var trimmed = adapterId.Trim();
        var normalized = trimmed.Trim('{', '}');

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (string.Equals(networkInterface.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(networkInterface.Description, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(networkInterface.Id.Trim('{', '}'), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return networkInterface.Name;
                }
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException or NotSupportedException)
        {
            Debug.WriteLine("Could not resolve the journaled network adapter: " + ex.Message);
        }

        // No match: only a value that cannot be a GUID (friendly name / index) may be used verbatim.
        return Guid.TryParse(normalized, out _) ? null : trimmed;
    }

    private static string? ResolveSystemTool(string fileName)
    {
        try
        {
            var systemDirectory = Environment.SystemDirectory;
            if (string.IsNullOrWhiteSpace(systemDirectory)) return null;
            var path = Path.Combine(systemDirectory, fileName);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SecurityException)
        {
            return null;
        }
    }

    // ================================================================= automation events

    private void OnGameChanged(object? sender, GameDetectionEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var settings = CurrentSettings();

            if (e.Game is not null)
            {
                if (_automationEnabled && settings.AutoEngageOnGameStart &&
                    _state is FusionModeState.Idle or FusionModeState.Error)
                {
                    if (!SafeIsElevated())
                    {
                        _activity.Info(Category,
                            $"{e.Game.Name} detected. Fusion Mode automation is armed but needs administrator rights to start.");
                        return;
                    }

                    _activity.Info(Category, $"{e.Game.Name} detected. Starting Fusion Mode automatically.");
                    TriggerAutoEngage();
                }

                return;
            }

            if (_automationEnabled && !_manualSession && settings.RestoreOnGameExit &&
                _state is FusionModeState.Active or FusionModeState.Arming)
            {
                _activity.Info(Category, "Game exited. Restoring Windows automatically.");
                TriggerAutoDisengage();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Error(Category, "Automatic Fusion Mode handling failed: " + ex.Message);
        }
    }

    private void TriggerAutoEngage()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await EngageAsync(manual: false).ConfigureAwait(false);
                if (!result.Success)
                {
                    _activity.Warning(Category, "Automatic Fusion Mode could not start: " + result.Message);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _activity.Error(Category, "Automatic Fusion Mode crashed: " + ex.Message);
            }
        });
    }

    private void TriggerAutoDisengage()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await DisengageAsync().ConfigureAwait(false);
                if (!result.Success)
                {
                    _activity.Warning(Category, "Automatic restore failed: " + result.Message);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _activity.Error(Category, "Automatic restore crashed: " + ex.Message);
            }
        });
    }

    // ================================================================= helpers

    private FusionSettings CurrentSettings()
    {
        try
        {
            var live = _settingsAccessor();
            if (live is not null) return live.Normalize();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Warning(Category, "Could not read the live settings; loading them from disk. " + ex.Message);
        }

        try
        {
            return _settingsStore.Load().Normalize();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Warning(Category, "Could not load the settings; using defaults. " + ex.Message);
            return FusionSettings.CreateDefault();
        }
    }

    private IReadOnlyList<ProcessSnapshot> SafeGetProcesses()
    {
        try
        {
            return _processes.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _activity.Warning(Category, "Could not read the process list: " + ex.Message);
            return [];
        }
    }

    private GameProfile? DetectGame(FusionSettings settings, IReadOnlyList<ProcessSnapshot> running)
    {
        try
        {
            if (_games.IsGameRunning && _games.CurrentGame is { } detected) return detected;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine("Game detection service unavailable: " + ex.Message);
        }

        try
        {
            return OptimizationPlanner.DetectGame(settings, running);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine("Game planning failed: " + ex.Message);
            return null;
        }
    }

    private void RefreshWatchList(FusionSettings? settings = null)
    {
        try
        {
            settings ??= CurrentSettings();
            var watchList = GameCatalog.BuildWatchList(settings);
            var signature = string.Join("|",
                watchList.Select(profile => profile.Id + ":" + string.Join(",", profile.ProcessNames)));

            bool changed;
            lock (_stateGate)
            {
                changed = !string.Equals(signature, _watchListSignature, StringComparison.Ordinal);
                if (changed) _watchListSignature = signature;
            }

            if (changed) _games.SetWatchList(watchList);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or ArgumentException)
        {
            Debug.WriteLine("Could not refresh the game watch list: " + ex.Message);
        }
    }

    private void ResetSessionState()
    {
        lock (_stateGate)
        {
            _activeRuleNames.Clear();
            _ruleEntryIds.Clear();
            _pausedServices.Clear();
            _controlled.Clear();
            _sessionAppliedTweaks.Clear();
            _boostedPid = null;
            _boostedEntryId = null;
            _boostedName = null;
            _dnsFlushed = false;
            _memoryPurgedBytes = 0;
            _activeSessionId = null;
        }
    }

    private bool IsProtectedProcess(int processId, string processName) =>
        IsSystemProcess(processId, processName) ||
        (_game is not null && _game.MatchesProcess(processName));

    private static bool IsSystemProcess(int processId, string processName) =>
        processId <= 4 ||
        processId == Environment.ProcessId ||
        ProcessController.IsSystemCriticalProcess(processName);

    private string ResolveProcessName(int processId, IEnumerable<string> candidates)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var name = process.ProcessName;
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Process exited; the plan name is a good enough label for the journal.
        }

        return candidates.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "process";
    }

    private static string ReadPriorityLabel(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.PriorityClass.ToString();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return "Normal";
        }
    }

    private static bool IsHighPriority(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.PriorityClass == ProcessPriorityClass.High;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static ProcessPriority ParsePriority(string? journalValue)
    {
        if (!string.IsNullOrWhiteSpace(journalValue) &&
            journalValue.StartsWith(PriorityPrefix[0], StringComparison.OrdinalIgnoreCase))
        {
            var name = journalValue[PriorityPrefix[0].Length..];
            if (Enum.TryParse<ProcessPriority>(name, ignoreCase: true, out var priority)) return priority;
        }

        return ProcessPriority.Normal;
    }

    private static bool TryParseProcessTarget(string target, out int processId, out string processName)
    {
        processId = 0;
        processName = string.Empty;
        if (string.IsNullOrWhiteSpace(target)) return false;

        var separator = target.IndexOf('|');
        if (separator <= 0) return false;
        if (!int.TryParse(target[..separator], out processId) || processId <= 0) return false;

        processName = target[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(processName);
    }

    private static bool TryParseTweakTarget(string target, out string tweakId, out string? interfaceGuid)
    {
        tweakId = string.Empty;
        interfaceGuid = null;
        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith("tweak:", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = target["tweak:".Length..];
        var separator = body.IndexOf("|if:", StringComparison.OrdinalIgnoreCase);
        if (separator >= 0)
        {
            tweakId = body[..separator];
            interfaceGuid = body[(separator + "|if:".Length)..];
        }
        else
        {
            tweakId = body;
        }

        return !string.IsNullOrWhiteSpace(tweakId);
    }

    private static bool TweakTargetMatches(string target, string tweakId) =>
        TryParseTweakTarget(target, out var found, out _) &&
        string.Equals(found, tweakId, StringComparison.OrdinalIgnoreCase);

    private int? ResolveLiveProcess(int processId, string processName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) return processId;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The original pid is gone; fall back to the name recorded in the journal.
        }

        try
        {
            return _processes.FindByProcessName(processName)?.ProcessId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private bool HasOpenEntry(JournalKind kind, string target)
    {
        try
        {
            return _journal.Entries.Any(entry => entry.Kind == kind && !entry.Restored &&
                                                 string.Equals(entry.Target, target, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private JournalEntry? Record(JournalKind kind, string target, string description, string? previousValue, string? appliedValue)
    {
        try
        {
            return _journal.Record(kind, target, description, previousValue, appliedValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _activity.Warning(Category, $"Journal entry could not be written ({target}): {ex.Message}");
            return null;
        }
    }

    private void SafeMarkRestored(JournalEntry? entry) => SafeMarkRestored(entry?.Id);

    private void SafeMarkRestored(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return;
        try
        {
            _journal.MarkRestored(entryId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine("Journal mark-restored failed: " + ex.Message);
        }
    }

    private bool SafeIsElevated()
    {
        try
        {
            if (_elevation.IsElevated) return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine("Elevation check failed: " + ex.Message);
        }

        try
        {
            return _systemInfo.IsElevated;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    // ================================================================= state & snapshot

    private void SetState(FusionModeState state, string message, GameProfile? game)
    {
        _state = state;
        RaiseStateChanged(state, message, game);

        if (state == FusionModeState.Error)
        {
            _lastError = message;
        }

        PublishSnapshot();
    }

    private void RaiseStateChanged(FusionModeState state, string message, GameProfile? game)
    {
        var handler = StateChanged;
        if (handler is null) return;

        try
        {
            handler(this, new FusionStateChangedEventArgs(state, message, game));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Fusion StateChanged subscriber failed: " + ex);
        }
    }

    private void PublishSnapshot()
    {
        FusionSnapshot snapshot;
        lock (_stateGate)
        {
            snapshot = BuildSnapshotLocked();
            if (Equals(snapshot, _lastPublishedSnapshot)) return;
            _lastPublishedSnapshot = snapshot;
        }

        var handler = SnapshotChanged;
        if (handler is null) return;

        try
        {
            handler(this, snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Fusion SnapshotChanged subscriber failed: " + ex);
        }
    }

    private FusionSnapshot BuildSnapshotLocked() => new(
        _state,
        _game,
        _activeRuleNames.Count,
        _pausedServices.Count,
        _controlled.Count + (_boostedPid.HasValue ? 1 : 0),
        _sessionAppliedTweaks.Count,
        _armedAtUtc,
        _lastError,
        SafeIsElevated());

    // ================================================================= inner types

    private sealed record ControlledProcess(int ProcessId, string Name, AppControlAction Action, string? EntryId);

    private readonly record struct UndoReport(IReadOnlyList<string> CriticalFailures, IReadOnlyList<string> Warnings);
}
