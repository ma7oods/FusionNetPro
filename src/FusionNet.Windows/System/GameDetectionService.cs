using System.Diagnostics;
using System.ComponentModel;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.Windows.System;

/// <summary>
/// Cheap 2-second process-name watcher that tells the app when a watched game starts or exits.
/// The event only fires when the detected set actually changes, and no exception can escape the
/// timer callback.
/// </summary>
public sealed class GameDetectionService : IGameDetectionService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(1);

    private readonly ISettingsStore _settingsStore;
    private readonly object _gate = new();
    private readonly object _pollGate = new();
    private Timer? _timer;
    private bool _started;
    private bool _disposed;
    private int _polling;
    private TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

    private IReadOnlyList<GameProfile> _watchList = [];
    private GameProfile? _currentGame;
    private IReadOnlyList<GameProfile> _runningGames = [];

    public GameDetectionService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public event EventHandler<GameDetectionEventArgs>? Changed;

    public GameProfile? CurrentGame
    {
        get
        {
            lock (_gate) return _currentGame;
        }
    }

    public IReadOnlyList<GameProfile> RunningGames
    {
        get
        {
            lock (_gate) return _runningGames;
        }
    }

    public bool IsGameRunning => CurrentGame is not null;

    public TimeSpan PollInterval
    {
        get
        {
            lock (_gate) return _pollInterval;
        }
        set
        {
            var clamped = value < MinimumInterval ? MinimumInterval : value > MaximumInterval ? MaximumInterval : value;
            Timer? toRestart = null;
            bool restart;
            lock (_gate)
            {
                if (clamped == _pollInterval) return;
                _pollInterval = clamped;
                restart = _started && !_disposed;
                if (restart) toRestart = _timer;
            }

            if (restart) RestartTimer(toRestart);
        }
    }

    public void SetWatchList(IReadOnlyList<GameProfile> profiles)
    {
        var copy = (profiles ?? [])
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
            .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_gate)
        {
            _watchList = copy;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            if (_watchList.Count == 0) _watchList = BuildWatchListFromSettings();
            _started = true;

            var period = _pollInterval;
            _timer = new Timer(_ => SafePoll(), null, TimeSpan.FromMilliseconds(250), period);
        }
    }

    public void Stop()
    {
        Timer? timer;
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    public void PollNow() => SafePoll();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void RestartTimer(Timer? previous)
    {
        previous?.Dispose();

        Timer? timer = null;
        lock (_gate)
        {
            if (_disposed || !_started) return;
            var period = _pollInterval;
            timer = new Timer(_ => SafePoll(), null, TimeSpan.FromMilliseconds(250), period);
            _timer = timer;
        }
    }

    private void SafePoll()
    {
        // Skip (never queue) a tick when the previous scan is still running.
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;

        try
        {
            Poll();
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                              Win32Exception or IOException or NotSupportedException)
        {
            // Timer callbacks must be bulletproof; the next tick simply tries again.
            Debug.WriteLine("Game detection poll failed: " + exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void Poll()
    {
        bool disposed;
        IReadOnlyList<GameProfile> watchList;
        lock (_gate)
        {
            disposed = _disposed;
            watchList = _watchList;
        }

        if (disposed || watchList.Count == 0) return;

        var runningNames = ReadRunningProcessNames();
        GameProfile[] matches = runningNames.Count == 0
            ? []
            : watchList.Where(profile => profile.MatchesAnyProcess(runningNames)).ToArray();
        var current = matches.FirstOrDefault();

        GameProfile? previousGame;
        IReadOnlyList<GameProfile> previousRunning;
        bool changed;
        lock (_gate)
        {
            previousGame = _currentGame;
            previousRunning = _runningGames;

            changed = !string.Equals(previousGame?.Id, current?.Id, StringComparison.OrdinalIgnoreCase) ||
                      !SameIds(previousRunning, matches);
            if (!changed) return;

            _currentGame = current;
            _runningGames = matches;
        }

        RaiseChanged(current, matches);
    }

    private static bool SameIds(IReadOnlyList<GameProfile> left, IReadOnlyList<GameProfile> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Id, right[index].Id, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static HashSet<string> ReadRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return names;
        }

        foreach (var process in processes)
        {
            try
            {
                var name = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Process exited between enumeration and inspection.
            }
            finally
            {
                try { process.Dispose(); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Nothing left to release.
                }
            }
        }

        return names;
    }

    private IReadOnlyList<GameProfile> BuildWatchListFromSettings()
    {
        try
        {
            var settings = _settingsStore.Load();
            return GameCatalog.BuildWatchList(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine("Could not build the game watch list: " + ex.Message);
            return [];
        }
    }

    private void RaiseChanged(GameProfile? game, IReadOnlyList<GameProfile> running)
    {
        var handler = Changed;
        if (handler is null) return;

        try
        {
            handler(this, new GameDetectionEventArgs(game, running));
        }
        catch (Exception ex)
        {
            // A subscriber bug must not kill the detection timer.
            Debug.WriteLine("Game detection subscriber failed: " + ex);
        }
    }
}
