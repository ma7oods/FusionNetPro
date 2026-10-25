using System.Collections.ObjectModel;
using System.Windows.Threading;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Dashboard;

/// <summary>
/// Drives the Dashboard: Fusion Mode hero, live metrics, enforcement summary,
/// recent activity and the detected-game card. Service events arrive on background
/// threads, so every handler marshals through <see cref="UiDispatcher"/>.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private const int HistoryLimit = 40;
    private const int ActivityLimit = 6;

    private readonly DispatcherTimer _sessionTimer;
    private readonly List<double> _pingHistory = [];
    private readonly List<double> _jitterHistory = [];
    private readonly List<double> _lossHistory = [];
    private readonly List<double> _downloadHistory = [];
    private readonly List<double> _uploadHistory = [];
    private readonly Dictionary<string, List<double>> _targetHistories = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<double> _pingHistoryView = [];
    private IReadOnlyList<double> _jitterHistoryView = [];
    private IReadOnlyList<double> _lossHistoryView = [];
    private IReadOnlyList<double> _downloadHistoryView = [];
    private IReadOnlyList<double> _uploadHistoryView = [];
    private IReadOnlyList<double> _selectedHistoryView = [];

    private FusionSnapshot _snapshot;
    private IReadOnlyDictionary<string, TargetStatistics> _statistics = new Dictionary<string, TargetStatistics>();
    private GameProfile? _detectedGame;
    private string _selectedTarget = string.Empty;
    private DateTimeOffset? _armedAtUtc;
    private string _sessionTime = "00:00";
    private string _enforcementSignature = string.Empty;
    private string _activitySignature = string.Empty;

    private bool _subscribed;
    private bool _languageSubscribed;
    private bool _isBusy;
    private bool _isElevated;
    private bool _hasEnforcements;
    private bool _hasActivity;
    private bool _hasSelectedData;
    private bool _hasThroughput;

    public DashboardViewModel(IAppServices services)
    {
        S = services ?? throw new ArgumentNullException(nameof(services));
        _snapshot = services.Fusion.Snapshot;
        _isElevated = services.Elevation.IsElevated;

        _sessionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sessionTimer.Tick += (_, _) => UpdateSessionTime();

        ToggleFusionCommand = new AsyncRelayCommand(ToggleFusionAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenGamesCommand = new RelayCommand(() => ShellNavigation.Go("gaming"));
        OpenGameBoostCommand = new RelayCommand(() => ShellNavigation.Go("gameboost"));
        ViewActivityCommand = new RelayCommand(() => ShellNavigation.Go("activity"));
        RestartElevatedCommand = new RelayCommand(RestartElevated);
    }

    private IAppServices S { get; }

    // ---------------------------------------------------------------- commands

    public AsyncRelayCommand ToggleFusionCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenGamesCommand { get; }
    public RelayCommand OpenGameBoostCommand { get; }
    public string GameBoostTitle=>Loc.Instance.IsArabic?S.GameBoost.Status.DisplayName:S.GameBoost.Status.State.ToString();
    public string GameBoostDetail=>S.GameBoost.Status.Message;
    public RelayCommand ViewActivityCommand { get; }
    public RelayCommand RestartElevatedCommand { get; }

    // ---------------------------------------------------------------- hero

    public FusionModeState State => _snapshot.State;

    public bool IsActive => _snapshot.IsActive;

    public bool IsArmed => _snapshot.State is FusionModeState.Active or FusionModeState.Arming;

    public bool IsElevated
    {
        get => _isElevated;
        private set => SetProperty(ref _isElevated, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            ToggleFusionCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public double RingProgress => State switch
    {
        FusionModeState.Active => 1d,
        FusionModeState.Arming => 0.62d,
        FusionModeState.Restoring => 0.32d,
        FusionModeState.Error => 0d,
        _ => 0d
    };

    public string RingPercent => (RingProgress * 100).ToString("0") + "%";

    public string StateTitle => State switch
    {
        FusionModeState.Active => Loc.T("Fusion.ProtectedTitle"),
        FusionModeState.Arming => Loc.T("Fusion.ArmingTitle"),
        FusionModeState.Restoring => Loc.T("Fusion.RestoringTitle"),
        FusionModeState.Error => Loc.T("Fusion.ErrorTitle"),
        _ => Loc.T("Fusion.OffTitle")
    };

    public string StateBody => State switch
    {
        FusionModeState.Active => Loc.T("Dash.StateActiveBody"),
        FusionModeState.Arming => Loc.T("Dash.StateArmingBody"),
        FusionModeState.Restoring => Loc.T("Dash.StateRestoringBody"),
        FusionModeState.Error => _snapshot.LastError ?? Loc.T("Dash.StateErrorBody"),
        _ => Loc.T("Dash.StateIdleBody")
    };

    public string EngageLabel => IsArmed ? Loc.T("Fusion.Disengage") : Loc.T("Fusion.Engage");

    public string EngageTooltip => IsArmed ? Loc.T("Dash.DisengageTip") : Loc.T("Dash.EngageTip");

    public string PrimaryGlyph => IsArmed ? Glyphs.Stop : Glyphs.Power;

    public string HotkeyHint => Loc.Format("Fusion.HotkeyHint", S.Settings.Hotkey);

    public string SessionTime
    {
        get => _sessionTime;
        private set => SetProperty(ref _sessionTime, value);
    }

    // ---------------------------------------------------------------- metric tiles

    public string PingValue { get; private set; } = "—";
    public string JitterValue { get; private set; } = "—";
    public string LossValue { get; private set; } = "—";
    public string DownloadValue { get; private set; } = "—";
    public string DownloadUnit => Loc.T("Dash.PerSecond");
    public string PingQuality { get; private set; } = Loc.T("Metric.Unknown");
    public string JitterQuality { get; private set; } = Loc.T("Metric.Unknown");
    public string LossQuality { get; private set; } = Loc.T("Metric.Unknown");
    public string DownloadQuality { get; private set; } = Loc.T("Metric.Unknown");

    public IReadOnlyList<double> PingHistory => _pingHistoryView;
    public IReadOnlyList<double> JitterHistory => _jitterHistoryView;
    public IReadOnlyList<double> LossHistory => _lossHistoryView;
    public IReadOnlyList<double> DownloadHistory => _downloadHistoryView;
    public IReadOnlyList<double> UploadHistory => _uploadHistoryView;

    // ---------------------------------------------------------------- live latency

    public ObservableCollection<PingTargetItem> Targets { get; } = [];

    public string SelectedTarget => _selectedTarget;

    public bool HasSelectedData
    {
        get => _hasSelectedData;
        private set => SetProperty(ref _hasSelectedData, value);
    }

    public string SelectedAvg { get; private set; } = "—";
    public string SelectedMin { get; private set; } = "—";
    public string SelectedMax { get; private set; } = "—";
    public string SelectedJitter { get; private set; } = "—";
    public string SelectedLoss { get; private set; } = "—";
    public string SelectedQuality { get; private set; } = Loc.T("Metric.Unknown");
    public IReadOnlyList<double> SelectedTargetHistory => _selectedHistoryView;

    // ---------------------------------------------------------------- throughput

    public bool HasThroughput
    {
        get => _hasThroughput;
        private set => SetProperty(ref _hasThroughput, value);
    }

    public string DownloadRate { get; private set; } = "—";
    public string UploadRate { get; private set; } = "—";

    // ---------------------------------------------------------------- enforcement

    public ObservableCollection<EnforcementItem> Enforcements { get; } = [];

    public bool HasEnforcements
    {
        get => _hasEnforcements;
        private set => SetProperty(ref _hasEnforcements, value);
    }

    // ---------------------------------------------------------------- activity

    public ObservableCollection<DashboardActivityRow> Activities { get; } = [];

    public bool HasActivity
    {
        get => _hasActivity;
        private set => SetProperty(ref _hasActivity, value);
    }

    // ---------------------------------------------------------------- detected game

    public string DetectedGameName => _detectedGame?.Name ?? Loc.T("Fusion.NoGame");

    public string DetectedGameMeta =>
        _detectedGame is null
            ? Loc.T("Dash.DetectedIdle")
            : string.Join(" · ", _detectedGame.ProcessNames);

    public bool HasDetectedGame => _detectedGame is not null;

    public string DetectedGameGlyph => Glyphs.Game;

    public bool AutoEngageOnGameStart
    {
        get => S.Settings.AutoEngageOnGameStart;
        set
        {
            if (S.Settings.AutoEngageOnGameStart == value) return;
            S.Settings.AutoEngageOnGameStart = value;
            S.SaveSettings();
            OnPropertyChanged();
            ToastService.Instance.Show(
                Loc.T(value ? "Dash.AutoEngageOn" : "Dash.AutoEngageOff"),
                ToastKind.Success);
        }
    }

    // ---------------------------------------------------------------- lifecycle

    public Task ActivateAsync()
    {
        if (!_subscribed)
        {
            S.Fusion.SnapshotChanged += OnSnapshotChanged;
            S.Fusion.StateChanged += OnStateChanged;
            S.Ping.Tick += OnPingTick;
            S.Throughput.SampleReceived += OnThroughputSample;
            S.Games.Changed += OnGamesChanged;
            S.Activity.EntryAdded += OnActivityEntry;
            _subscribed = true;
        }

        if (!_languageSubscribed)
        {
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            _languageSubscribed = true;
        }

        IsElevated = S.Elevation.IsElevated;
        StartMonitorsOwnedByShell();

        _sessionTimer.Start();
        EnsureTargets();
        ApplySnapshot(S.Fusion.Snapshot);
        ApplyStatistics(S.Ping.Statistics);
        ApplyThroughput(S.Throughput.Latest);
        RebuildEnforcements(force: true);
        RefreshActivity(force: true);
        UpdateSessionTime();

        _detectedGame = S.Fusion.Snapshot.Game ?? S.Games.CurrentGame;
        RaiseAll(
            nameof(DetectedGameName), nameof(DetectedGameMeta), nameof(HasDetectedGame),
            nameof(HotkeyHint), nameof(AutoEngageOnGameStart));

        return Task.CompletedTask;
    }

    public void Deactivate()
    {
        if (_subscribed)
        {
            S.Fusion.SnapshotChanged -= OnSnapshotChanged;
            S.Fusion.StateChanged -= OnStateChanged;
            S.Ping.Tick -= OnPingTick;
            S.Throughput.SampleReceived -= OnThroughputSample;
            S.Games.Changed -= OnGamesChanged;
            S.Activity.EntryAdded -= OnActivityEntry;
            _subscribed = false;
        }

        if (_languageSubscribed)
        {
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            _languageSubscribed = false;
        }

        _sessionTimer.Stop();
        IsBusy = false;
        // Never stop the ping/throughput monitors: the shell owns them.
    }

    public void OnSettingsChanged(FusionSettings settings)
    {
        EnsureTargets();
        RaiseAll(
            nameof(HotkeyHint), nameof(AutoEngageOnGameStart), nameof(EngageLabel),
            nameof(EngageTooltip));
        ApplyStatistics(S.Ping.Statistics);
    }

    /// <summary>Starts the network monitors only when live monitoring is on and the shell has not started them.</summary>
    private void StartMonitorsOwnedByShell()
    {
        if (!S.Settings.LiveMonitoring) return;

        try
        {
            if (!S.Ping.IsRunning) S.Ping.Start();
            if (!S.Throughput.IsRunning) S.Throughput.Start();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Warning);
        }
    }

    // ---------------------------------------------------------------- service events

    private void OnSnapshotChanged(object? sender, FusionSnapshot snapshot) =>
        UiDispatcher.Post(() =>
        {
            ApplySnapshot(snapshot);
            RebuildEnforcements(force: false);
        });

    private void OnStateChanged(object? sender, FusionStateChangedEventArgs e) =>
        UiDispatcher.Post(() =>
        {
            ApplySnapshot(S.Fusion.Snapshot);
            RebuildEnforcements(force: false);
            RefreshActivity(force: false);
        });

    private void OnPingTick(object? sender, PingTickEventArgs e) =>
        UiDispatcher.Post(() => ApplyStatistics(e.Statistics));

    private void OnThroughputSample(object? sender, ThroughputSampleEventArgs e) =>
        UiDispatcher.Post(() => ApplyThroughput(e.Sample));

    private void OnGamesChanged(object? sender, GameDetectionEventArgs e) =>
        UiDispatcher.Post(() =>
        {
            _detectedGame = e.Game ?? _snapshot.Game;
            RaiseAll(nameof(DetectedGameName), nameof(DetectedGameMeta), nameof(HasDetectedGame));
        });

    private void OnActivityEntry(object? sender, ActivityEntry entry) =>
        UiDispatcher.Post(() => RefreshActivity(force: false));

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        UiDispatcher.Post(() =>
        {
            // Re-compute every derived string so the page switches language instantly.
            ApplySnapshot(_snapshot);
            ApplyStatistics(_statistics, append: false);
            RebuildEnforcements(force: true);
            RefreshActivity(force: true);
            RaiseAll(
                nameof(DetectedGameName), nameof(DetectedGameMeta), nameof(HasDetectedGame),
                nameof(HotkeyHint), nameof(AutoEngageOnGameStart), nameof(DownloadUnit),
                nameof(EngageTooltip));
        });

    // ---------------------------------------------------------------- apply state

    private void ApplySnapshot(FusionSnapshot snapshot)
    {
        _snapshot = snapshot;
        _armedAtUtc = snapshot.State is FusionModeState.Active or FusionModeState.Arming
            ? snapshot.ArmedAtUtc
            : null;

        _detectedGame = snapshot.Game ?? _detectedGame ?? S.Games.CurrentGame;

        RaiseAll(
            nameof(State), nameof(IsActive), nameof(IsArmed),
            nameof(RingProgress), nameof(RingPercent),
            nameof(StateTitle), nameof(StateBody),
            nameof(EngageLabel), nameof(EngageTooltip), nameof(PrimaryGlyph),
            nameof(DetectedGameName), nameof(DetectedGameMeta), nameof(HasDetectedGame));

        UpdateSessionTime();
    }

    private void UpdateSessionTime()
    {
        RaiseAll(nameof(GameBoostTitle),nameof(GameBoostDetail));
        if (_armedAtUtc is { } armed && State is FusionModeState.Active or FusionModeState.Arming)
        {
            var span = DateTimeOffset.UtcNow - armed;
            SessionTime = Humanize.Duration(span < TimeSpan.Zero ? TimeSpan.Zero : span);
        }
        else
        {
            SessionTime = "00:00";
        }
    }

    private void ApplyStatistics(IReadOnlyDictionary<string, TargetStatistics> statistics, bool append = true)
    {
        _statistics = statistics;

        var known = statistics.Values.Where(s => s.Sent > 0).ToArray();
        var live = known.Where(s => s.Received > 0).ToArray();

        if (live.Length > 0)
        {
            var best = live.OrderBy(s => s.AverageMs).First();
            PingValue = best.AverageMs.ToString("0");
            PingQuality = QualityLabel(best.QualityScore);
            if (append) Append(_pingHistory, best.AverageMs);

            var jitter = live.Average(s => s.JitterMs);
            JitterValue = jitter.ToString("0.#");
            JitterQuality = JitterQualityLabel(jitter);
            if (append) Append(_jitterHistory, jitter);

            var loss = live.Average(s => s.LossPercent);
            LossValue = loss.ToString("0.#");
            LossQuality = LossQualityLabel(loss);
            if (append) Append(_lossHistory, loss);
        }
        else if (known.Length > 0)
        {
            // Probes are being sent but nothing came back. That is a real answer (the target is
            // unreachable), so surface it instead of leaving the tiles stuck on "Unknown".
            var loss = known.Average(s => s.LossPercent);
            PingValue = "—";
            PingQuality = Loc.T("Metric.NoReply");
            JitterValue = "—";
            JitterQuality = Loc.T("Metric.Unknown");
            LossValue = loss.ToString("0.#");
            LossQuality = LossQualityLabel(loss);
            if (append) Append(_lossHistory, loss);
        }
        else
        {
            PingValue = "—";
            JitterValue = "—";
            LossValue = "—";
            PingQuality = Loc.T("Metric.Unknown");
            JitterQuality = Loc.T("Metric.Unknown");
            LossQuality = Loc.T("Metric.Unknown");
        }

        // Rolling history per ping target (used by the selected-target sparkline).
        if (append)
        {
            foreach (var target in S.Settings.PingTargets)
            {
                if (!statistics.TryGetValue(target, out var stats) || stats.Received <= 0) continue;
                Append(GetTargetHistory(target), stats.AverageMs);
            }
        }

        UpdateSelectedReadout();
        RaiseHistories();
        RaiseAll(nameof(PingValue), nameof(PingQuality), nameof(JitterValue), nameof(JitterQuality),
            nameof(LossValue), nameof(LossQuality));
    }

    private void ApplyThroughput(ThroughputSample? sample)
    {
        HasThroughput = sample is not null;

        if (sample is null)
        {
            DownloadRate = "—";
            UploadRate = "—";
            DownloadValue = "—";
            DownloadQuality = Loc.T("Metric.Unknown");
        }
        else
        {
            DownloadRate = Humanize.Rate(sample.DownloadBytesPerSecond);
            UploadRate = Humanize.Rate(sample.UploadBytesPerSecond);
            DownloadValue = Humanize.Bytes(sample.DownloadBytesPerSecond);
            DownloadQuality = RateQualityLabel(sample.DownloadBytesPerSecond);
            Append(_downloadHistory, sample.DownloadBytesPerSecond);
            Append(_uploadHistory, sample.UploadBytesPerSecond);
        }

        RaiseHistories();
        RaiseAll(nameof(DownloadRate), nameof(UploadRate), nameof(DownloadValue), nameof(DownloadQuality));
    }

    // ---------------------------------------------------------------- live latency targets

    private void EnsureTargets()
    {
        var targets = S.Settings.PingTargets;

        for (var i = Targets.Count - 1; i >= 0; i--)
        {
            if (!targets.Contains(Targets[i].Target, StringComparer.OrdinalIgnoreCase))
                Targets.RemoveAt(i);
        }

        foreach (var target in targets)
        {
            if (Targets.Any(t => string.Equals(t.Target, target, StringComparison.OrdinalIgnoreCase))) continue;
            Targets.Add(new PingTargetItem(target, SelectTarget));
        }

        if (string.IsNullOrWhiteSpace(_selectedTarget) ||
            !targets.Contains(_selectedTarget, StringComparer.OrdinalIgnoreCase))
        {
            _selectedTarget = targets.Count > 0 ? targets[0] : string.Empty;
        }

        UpdateTargetSelection();
        OnPropertyChanged(nameof(SelectedTarget));
        UpdateSelectedReadout();
    }

    private void SelectTarget(string target)
    {
        if (string.Equals(_selectedTarget, target, StringComparison.OrdinalIgnoreCase)) return;
        _selectedTarget = target;
        UpdateTargetSelection();
        OnPropertyChanged(nameof(SelectedTarget));
        UpdateSelectedReadout();
    }

    private void UpdateTargetSelection()
    {
        foreach (var item in Targets)
            item.IsSelected = string.Equals(item.Target, _selectedTarget, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSelectedReadout()
    {
        if (_statistics.TryGetValue(_selectedTarget, out var stats) && stats.Sent > 0)
        {
            // Samples were sent. Received == 0 means "no reply", which is a known failure state,
            // not an unknown value: show the loss honestly and keep the reading visible.
            var hasReplies = stats.Received > 0;
            HasSelectedData = true;
            SelectedAvg = hasReplies ? stats.AverageMs.ToString("0") : "—";
            SelectedMin = hasReplies ? Humanize.Ms(stats.MinMs) : "—";
            SelectedMax = hasReplies ? Humanize.Ms(stats.MaxMs) : "—";
            SelectedJitter = hasReplies ? Humanize.Ms(stats.JitterMs, 1) : "—";
            SelectedLoss = Humanize.Percent(stats.LossPercent, 1);
            SelectedQuality = hasReplies ? QualityLabel(stats.QualityScore) : Loc.T("Metric.NoReply");
        }
        else
        {
            HasSelectedData = false;
            SelectedAvg = "—";
            SelectedMin = "—";
            SelectedMax = "—";
            SelectedJitter = "—";
            SelectedLoss = "—";
            SelectedQuality = Loc.T("Metric.Unknown");
        }

        _selectedHistoryView = _targetHistories.TryGetValue(_selectedTarget, out var history)
            ? history.ToArray()
            : [];

        RaiseAll(nameof(HasSelectedData), nameof(SelectedAvg), nameof(SelectedMin), nameof(SelectedMax),
            nameof(SelectedJitter), nameof(SelectedLoss), nameof(SelectedQuality),
            nameof(SelectedTargetHistory));
    }

    private List<double> GetTargetHistory(string target)
    {
        if (_targetHistories.TryGetValue(target, out var history)) return history;
        history = [];
        _targetHistories[target] = history;
        return history;
    }

    // ---------------------------------------------------------------- enforcement summary

    private void RebuildEnforcements(bool force)
    {
        List<EnforcementItem> items;
        try
        {
            items = BuildEnforcements();
        }
        catch (Exception)
        {
            // A service can be momentarily unavailable (for example a busy WMI power plan).
            // Keep the previous summary instead of breaking the page.
            return;
        }

        var signature = string.Join("|", items.Select(i => $"{i.Label}:{i.Value}:{i.Detail}"));
        if (!force && signature == _enforcementSignature) return;
        _enforcementSignature = signature;

        Enforcements.Clear();
        foreach (var item in items) Enforcements.Add(item);
        HasEnforcements = Enforcements.Count > 0;
    }

    private List<EnforcementItem> BuildEnforcements()
    {
        var items = new List<EnforcementItem>();
        var entries = SafeJournalEntries();
        var active = entries.Where(e => !e.Restored).ToArray();

        var firewall = active.Where(e => e.Kind == JournalKind.Firewall).ToArray();
        var firewallCount = Math.Max(_snapshot.ActiveFirewallRules, firewall.Length);
        if (firewallCount > 0)
        {
            var names = firewall
                .Select(e => CleanRuleName(e.Target, e.Description))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            items.Add(new EnforcementItem(
                Glyphs.Shield,
                Loc.T("Dash.Firewall"),
                firewallCount.ToString(),
                names.Length == 0 ? Loc.T("Dash.FirewallDetail") : JoinNames(names)));
        }

        var services = active.Where(e => e.Kind == JournalKind.Service).ToArray();
        var serviceCount = Math.Max(_snapshot.PausedServices, services.Length);
        if (serviceCount > 0)
        {
            var names = services
                .Select(e => ServiceCatalog.Find(e.Target))
                .Where(s => s is not null)
                .Select(s => Loc.Instance.IsArabic ? s!.NameAr : s!.NameEn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            items.Add(new EnforcementItem(
                Glyphs.Stop,
                Loc.T("Dash.Services"),
                serviceCount.ToString(),
                names.Length == 0 ? Loc.T("Dash.None") : JoinNames(names)));
        }

        var processes = active.Where(e => e.Kind == JournalKind.Process).ToArray();
        var controlledCount = Math.Max(_snapshot.ControlledProcesses, processes.Length);
        if (controlledCount > 0)
        {
            var names = processes
                .Select(e => ProcessNameFromTarget(e.Target))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            items.Add(new EnforcementItem(
                Glyphs.Lock,
                Loc.T("Dash.Apps"),
                controlledCount.ToString(),
                names.Length == 0 ? Loc.T("Dash.None") : JoinNames(names)));
        }

        var tweaks = active.Where(e => e.Kind == JournalKind.Registry).ToArray();
        var appliedTweaks = Math.Max(_snapshot.AppliedTweaks, tweaks.Length);
        if (appliedTweaks > 0)
        {
            var names = tweaks
                .Select(e => TweakFromTarget(e.Target))
                .Where(t => t is not null)
                .Select(t => Loc.Instance.IsArabic ? t!.TitleAr : t!.TitleEn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            items.Add(new EnforcementItem(
                Glyphs.Tuner,
                Loc.T("Dash.Tweaks"),
                appliedTweaks.ToString(),
                names.Length == 0 ? Loc.T("Dash.None") : JoinNames(names)));
        }

        var power = active.Where(e => e.Kind == JournalKind.PowerPlan).ToArray();
        if (power.Length > 0 || (IsArmed && S.Settings.SwitchPowerPlan))
        {
            var plan = S.Power.ActivePlanName;
            items.Add(new EnforcementItem(
                Glyphs.Bolt,
                Loc.T("Dash.Power"),
                string.IsNullOrWhiteSpace(plan) ? Loc.T("Dash.PowerHigh") : plan,
                Loc.T("Dash.PowerDetail")));
        }

        var dns = active.Where(e => e.Kind == JournalKind.Dns).ToArray();
        if (dns.Length > 0)
        {
            var detail = dns
                .Select(e => e.AppliedValue ?? e.Description)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            items.Add(new EnforcementItem(
                Glyphs.Globe,
                Loc.T("Dash.Dns"),
                Loc.T("Dash.DnsChanged"),
                detail.Length == 0 ? string.Empty : JoinNames(detail)));
        }

        return items;
    }

    private IReadOnlyList<JournalEntry> SafeJournalEntries()
    {
        try
        {
            return S.Journal.Entries;
        }
        catch (Exception)
        {
            // The journal is optional for the dashboard; never let it break the page.
            return [];
        }
    }

    private static string JoinNames(IReadOnlyList<string> names)
    {
        const int limit = 3;
        return names.Count <= limit
            ? string.Join(" · ", names)
            : string.Join(" · ", names.Take(limit)) + " · " + Loc.Format("Dash.More", names.Count - limit);
    }

    private static string CleanRuleName(string? target, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            var text = description.Trim();
            foreach (var prefix in new[] { "Firewall:", "Blocked", "Block" })
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[prefix.Length..].TrimStart(' ', ':', '-', '·');
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(text) && !text.Contains("FNP_", StringComparison.Ordinal)) return text;
        }

        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        return target.StartsWith(FusionNet.Core.Planning.FirewallPlanner.RulePrefix, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : target.Trim();
    }

    private static string ProcessNameFromTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        var separator = target.IndexOf('|');
        return separator >= 0 ? target[(separator + 1)..].Trim() : target.Trim();
    }

    private static TweakDefinition? TweakFromTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var id = target.StartsWith("tweak:", StringComparison.OrdinalIgnoreCase)
            ? target["tweak:".Length..]
            : target;
        return TweakCatalog.Find(id);
    }

    // ---------------------------------------------------------------- activity

    private void RefreshActivity(bool force)
    {
        IReadOnlyList<ActivityEntry> snapshot;
        try
        {
            snapshot = S.Activity.Snapshot();
        }
        catch (Exception)
        {
            snapshot = [];
        }

        var rows = snapshot
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(ActivityLimit)
            .ToArray();

        var signature = string.Join("|", rows.Select(r => r.TimestampUtc.UtcDateTime.Ticks + r.Message));
        if (!force && signature == _activitySignature) return;
        _activitySignature = signature;

        Activities.Clear();
        foreach (var entry in rows)
        {
            Activities.Add(new DashboardActivityRow
            {
                Time = Humanize.Clock(entry.TimestampUtc),
                Message = entry.Message,
                Level = entry.Level
            });
        }

        HasActivity = Activities.Count > 0;
    }

    // ---------------------------------------------------------------- commands

    private async Task ToggleFusionAsync()
    {
        IsBusy = true;
        try
        {
            if (S.Fusion.IsArmed || S.Fusion.State is FusionModeState.Active or FusionModeState.Arming)
            {
                var result = await S.Fusion.DisengageAsync();
                if (result.Success)
                    ToastService.Instance.Show(Loc.T("Fusion.Disengaged"), ToastKind.Success);
                else
                    ToastService.Instance.Show(result.Message, ToastKind.Warning);
            }
            else
            {
                var result = await S.Fusion.EngageAsync();
                if (result.Success)
                {
                    ToastService.Instance.Show(Loc.T("Fusion.ManualEngaged"), ToastKind.Success);
                }
                else if (!IsElevated)
                {
                    ToastService.Instance.Show(Loc.T("Fusion.RequiresAdmin"), ToastKind.Warning);
                }
                else
                {
                    ToastService.Instance.Show(result.Message, ToastKind.Warning);
                }
            }

            await S.Fusion.RefreshAsync();
            ApplySnapshot(S.Fusion.Snapshot);
            RebuildEnforcements(force: true);
            RefreshActivity(force: true);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        await S.Fusion.RefreshAsync();
        ApplySnapshot(S.Fusion.Snapshot);
        ApplyStatistics(S.Ping.Statistics);
        ApplyThroughput(S.Throughput.Latest);
        RebuildEnforcements(force: true);
        RefreshActivity(force: true);
    }

    private void RestartElevated()
    {
        try
        {
            if (!S.Elevation.TryRestartElevated())
            {
                ToastService.Instance.ShowLocalized(
                    "Dash.ElevationCancelled", ToastKind.Warning, Loc.T("Dash.ElevationCancelledTitle"));
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    // ---------------------------------------------------------------- helpers

    private void RaiseHistories()
    {
        _pingHistoryView = _pingHistory.ToArray();
        _jitterHistoryView = _jitterHistory.ToArray();
        _lossHistoryView = _lossHistory.ToArray();
        _downloadHistoryView = _downloadHistory.ToArray();
        _uploadHistoryView = _uploadHistory.ToArray();
        _selectedHistoryView = _targetHistories.TryGetValue(_selectedTarget, out var selected)
            ? selected.ToArray()
            : [];

        RaiseAll(nameof(PingHistory), nameof(JitterHistory), nameof(LossHistory),
            nameof(DownloadHistory), nameof(UploadHistory), nameof(SelectedTargetHistory));
    }

    private static void Append(List<double> list, double value)
    {
        if (!double.IsFinite(value)) return;
        list.Add(value);
        while (list.Count > HistoryLimit) list.RemoveAt(0);
    }

    private static string QualityLabel(double score) => score switch
    {
        >= 90 => Loc.T("Metric.Exellent"),
        >= 75 => Loc.T("Metric.Good"),
        >= 50 => Loc.T("Metric.Fair"),
        > 0 => Loc.T("Metric.Poor"),
        _ => Loc.T("Metric.Unknown")
    };

    private static string JitterQualityLabel(double jitter) => jitter switch
    {
        <= 5 => Loc.T("Metric.Exellent"),
        <= 15 => Loc.T("Metric.Good"),
        <= 30 => Loc.T("Metric.Fair"),
        _ => Loc.T("Metric.Poor")
    };

    private static string LossQualityLabel(double lossPercent) => lossPercent switch
    {
        <= 0.5 => Loc.T("Metric.Exellent"),
        <= 2 => Loc.T("Metric.Good"),
        <= 5 => Loc.T("Metric.Fair"),
        _ => Loc.T("Metric.Poor")
    };

    private static string RateQualityLabel(double bytesPerSecond) => bytesPerSecond switch
    {
        < 1024 => Loc.T("Dash.Q.Idle"),
        < 512 * 1024 => Loc.T("Dash.Q.Low"),
        < 5 * 1024 * 1024 => Loc.T("Dash.Q.Active"),
        _ => Loc.T("Dash.Q.Heavy")
    };
}

/// <summary>One chip in the live-latency target selector.</summary>
public sealed class PingTargetItem : ViewModelBase
{
    private bool _isSelected;

    public PingTargetItem(string target, Action<string> select)
    {
        Target = target;
        SelectCommand = new RelayCommand(() => select(Target));
    }

    public string Target { get; }

    public RelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>One row in the "what is enforced right now" card.</summary>
public sealed record EnforcementItem(string Glyph, string Label, string Value, string Detail)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

/// <summary>One recent activity row on the dashboard.</summary>
public sealed class DashboardActivityRow
{
    public required string Time { get; init; }
    public required string Message { get; init; }
    public LogLevel Level { get; init; }
}
