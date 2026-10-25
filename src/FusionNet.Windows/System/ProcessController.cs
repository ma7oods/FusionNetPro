using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Windows.Native;

namespace FusionNet.Windows.System;

/// <summary>
/// Fast, access-denied-tolerant process inspection plus the native suspend / priority
/// controls. System-critical processes are never touched, no matter what the caller asks.
/// </summary>
public sealed class ProcessController : IProcessController
{
    private const string Category = "Apps";

    /// <summary>Process names that must never be suspended or re-prioritised.</summary>
    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "idle", "memory compression", "smss", "csrss", "wininit", "services",
        "lsass", "svchost", "winlogon", "explorer", "dwm", "fontdrvhost", "sihost", "taskhostw",
        "ctfmon", "runtimebroker", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "audiodg", "conhost", "dllhost", "msmpeng", "nissrv", "securityhealthservice", "wudfhost",
        "logonui", "userinit", "spoolsv", "lsm", "wininit.exe"
    };

    private readonly IActivityLog _activity;
    private readonly ConcurrentDictionary<int, byte> _suspended = new();

    public ProcessController(IActivityLog activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public IReadOnlyList<ProcessSnapshot> GetProcesses()
    {
        var snapshots = new List<ProcessSnapshot>(256);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return snapshots;
        }

        var alive = new HashSet<int>();
        foreach (var process in processes)
        {
            try
            {
                var id = process.Id;
                if (id <= 0) continue;

                string name;
                try { name = process.ProcessName; }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name)) continue;
                alive.Add(id);

                var path = GetExecutablePath(id, process);
                var workingSet = 0L;
                try { workingSet = process.WorkingSet64; }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* exited */ }

                var hasWindow = false;
                try { hasWindow = process.MainWindowHandle != IntPtr.Zero; }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* exited */ }

                var sessionId = 0;
                try { sessionId = process.SessionId; }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* exited */ }

                snapshots.Add(new ProcessSnapshot(
                    id,
                    name,
                    path,
                    workingSet,
                    hasWindow,
                    _suspended.ContainsKey(id),
                    sessionId));
            }
            finally
            {
                try { process.Dispose(); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* nothing left to release */ }
            }
        }

        // Drop suspension bookkeeping for processes that no longer exist.
        if (_suspended.Count > 0)
        {
            foreach (var id in _suspended.Keys)
            {
                if (!alive.Contains(id)) _suspended.TryRemove(id, out _);
            }
        }

        return snapshots;
    }

    public ProcessSnapshot? FindByProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        var processes = GetProcesses();
        var matches = processes
            .Where(snapshot => string.Equals(snapshot.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0) return null;

        // Prefer the newest interactive window, then the biggest process (usually the game).
        return matches
            .OrderByDescending(snapshot => snapshot.HasMainWindow)
            .ThenByDescending(snapshot => snapshot.WorkingSetBytes)
            .First();
    }

    public OperationResult SetSuspended(int processId, bool suspended)
    {
        if (!TryGetProcessName(processId, out var processName))
        {
            if (!suspended) return OperationResult.Ok($"Process {processId} already exited; nothing to resume.");
            return OperationResult.Fail($"Process {processId} is not running.");
        }

        var guard = GuardProcess(processId, processName);
        if (guard is not null) return guard;

        // Only suspension can be short-circuited from bookkeeping: the dictionary is the proof
        // that *this* instance suspended the process. A resume request must always call the
        // kernel, because after a crash/restart the bookkeeping starts empty even though the
        // previous FusionNet session left the process suspended (journal crash recovery path).
        if (suspended && _suspended.ContainsKey(processId))
        {
            return OperationResult.Ok($"{processName} is already suspended.");
        }

        var success = suspended
            ? Win32Native.SuspendProcess(processId, out var errorCode)
            : Win32Native.ResumeProcess(processId, out errorCode);

        if (success)
        {
            if (suspended) _suspended[processId] = 0;
            else _suspended.TryRemove(processId, out _);

            _activity.Info(Category,
                suspended ? $"Suspended {processName} (PID {processId})." : $"Resumed {processName} (PID {processId}).");
            return OperationResult.Ok(suspended ? $"{processName} suspended." : $"{processName} resumed.");
        }

        if (ProcessIsGone(processId))
        {
            _suspended.TryRemove(processId, out _);
            return OperationResult.Ok($"{processName} already exited.");
        }

        _activity.Warning(Category,
            $"Could not {(suspended ? "suspend" : "resume")} {processName} (PID {processId}): {Win32Native.DescribeError(errorCode)}.");

        return errorCode == Win32Native.ErrorAccessDenied
            ? OperationResult.NotElevated($"Suspending {processName}")
            : OperationResult.Fail($"Could not {(suspended ? "suspend" : "resume")} {processName}: {Win32Native.DescribeError(errorCode)}");
    }

    public OperationResult SetPriority(int processId, ProcessPriority priority)
    {
        if (!TryGetProcessName(processId, out var processName))
            return OperationResult.Fail($"Process {processId} is not running.");

        var guard = GuardProcess(processId, processName);
        if (guard is not null) return guard;

        var priorityClass = ToWin32Priority(priority);
        if (!Win32Native.TrySetPriorityClass(processId, priorityClass, out var errorCode))
        {
            _activity.Warning(Category, $"Could not set {processName} priority to {priority}: {Win32Native.DescribeError(errorCode)}.");
            return errorCode == Win32Native.ErrorAccessDenied
                ? OperationResult.NotElevated($"Changing the priority of {processName}")
                : OperationResult.Fail($"Could not set {processName} priority to {priority}: {Win32Native.DescribeError(errorCode)}");
        }

        _activity.Info(Category, $"{processName} (PID {processId}) priority set to {priority}.");
        return OperationResult.Ok($"{processName} priority set to {priority}.");
    }

    public OperationResult BoostForGaming(int processId)
    {
        if (!TryGetProcessName(processId, out var processName))
            return OperationResult.Fail($"Game process {processId} is not running.");

        var guard = GuardProcess(processId, processName);
        if (guard is not null) return guard;

        if (!Win32Native.TrySetPriorityClass(processId, Win32Native.HighPriorityClass, out var errorCode))
        {
            _activity.Warning(Category, $"Could not boost {processName}: {Win32Native.DescribeError(errorCode)}.");
            return errorCode == Win32Native.ErrorAccessDenied
                ? OperationResult.NotElevated($"Boosting {processName}")
                : OperationResult.Fail($"Could not boost {processName}: {Win32Native.DescribeError(errorCode)}");
        }

        // IO priority is a best-effort refinement: CPU priority alone already helps.
        var ioSet = Win32Native.TrySetIoPriority(processId, Win32Native.IoPriorityHigh, out var ioError);
        _activity.Success(Category, ioSet
            ? $"{processName} boosted to High CPU + High IO priority."
            : $"{processName} boosted to High CPU priority (IO priority unavailable: {Win32Native.DescribeError(ioError)}).");

        return OperationResult.Ok(ioSet
            ? $"{processName} boosted to High CPU + High IO priority."
            : $"{processName} boosted to High CPU priority.");
    }

    public OperationResult RestoreDefaultPriority(int processId)
    {
        if (!TryGetProcessName(processId, out var processName))
            return OperationResult.Ok($"Process {processId} already exited; nothing to restore.");

        if (!Win32Native.TrySetPriorityClass(processId, Win32Native.NormalPriorityClass, out var errorCode))
        {
            if (ProcessIsGone(processId)) return OperationResult.Ok($"{processName} already exited.");

            _activity.Warning(Category, $"Could not restore {processName} priority: {Win32Native.DescribeError(errorCode)}.");
            return errorCode == Win32Native.ErrorAccessDenied
                ? OperationResult.NotElevated($"Restoring the priority of {processName}")
                : OperationResult.Fail($"Could not restore {processName} priority: {Win32Native.DescribeError(errorCode)}");
        }

        Win32Native.TrySetIoPriority(processId, Win32Native.IoPriorityNormal, out _);
        _activity.Info(Category, $"{processName} priority restored to Normal.");
        return OperationResult.Ok($"{processName} priority restored to Normal.");
    }

    public bool IsSuspended(int processId) => _suspended.ContainsKey(processId);

    /// <summary>True for OS-critical process names that FusionNet never suspends or re-prioritises.</summary>
    public static bool IsSystemCriticalProcess(string processName) =>
        !string.IsNullOrWhiteSpace(processName) &&
        (CriticalProcessNames.Contains(processName) || GameCatalog.IsRiotSupportProcess(processName));

    private static string? GetExecutablePath(int processId, Process process)
    {
        if (Win32Native.TryQueryProcessPath(processId, out var path, out _) && !string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            var module = process.MainModule;
            var fileName = module?.FileName;
            return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Access denied / 32-bit boundary / exited: callers tolerate a null path.
            return null;
        }
    }

    private bool TryGetProcessName(int processId, out string processName)
    {
        processName = string.Empty;
        if (processId <= 0) return false;

        try
        {
            using var process = Process.GetProcessById(processId);
            processName = process.ProcessName;
            return !string.IsNullOrWhiteSpace(processName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private OperationResult? GuardProcess(int processId, string processName)
    {
        if (processId <= 4 || processId == Environment.ProcessId || IsSystemCriticalProcess(processName))
        {
            _activity.Warning(Category, $"Refused to touch the system-critical process {processName} (PID {processId}).");
            return OperationResult.Fail($"{processName} is a protected system process and was left untouched.");
        }

        return null;
    }

    private static bool ProcessIsGone(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    private static uint ToWin32Priority(ProcessPriority priority) => priority switch
    {
        ProcessPriority.Idle => Win32Native.IdlePriorityClass,
        ProcessPriority.BelowNormal => Win32Native.BelowNormalPriorityClass,
        ProcessPriority.AboveNormal => Win32Native.AboveNormalPriorityClass,
        ProcessPriority.High => Win32Native.HighPriorityClass,
        ProcessPriority.RealTime => Win32Native.RealTimePriorityClass,
        _ => Win32Native.NormalPriorityClass
    };
}
