using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Monitor;

/// <summary>
/// Live Monitor: adapter details, multi-target latency with history, throughput,
/// probe target management and a full network diagnostics run with export.
/// </summary>
public sealed class MonitorViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private readonly Dictionary<string, PingTargetRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<double> _downloadHistory = [];
    private readonly List<double> _uploadHistory = [];

    private CancellationTokenSource? _diagnosticsCts;
    private CancellationTokenSource? _saveDebounceCts;

    private bool _isActivated;
    private bool _isSubscribed;
    private bool _suppressAdapterSave;
    private bool _isLive;
    private bool _isRunningDiagnostics;
    private AdapterInfo? _selectedAdapter;
    private string _downloadText = "—";
    private string _uploadText = "—";
    private double[] _downloadValues = [];
    private double[] _uploadValues = [];
    private string _targetInput = string.Empty;
    private double _intervalMs = 1000;
    private DiagnosticReport? _lastReport;
    private string _environmentLine = string.Empty;
    private string _lastRunText = string.Empty;
    private string _qualityLabel = string.Empty;
    private string _qualityKey = "unknown";

    public MonitorViewModel(IAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Adapters = [];
        PingTargets = [];
        Findings = [];
        ProbeTargets = [];

        _qualityLabel = Loc.T("Mon.QualityUnknown");

        RefreshCommand = new AsyncRelayCommand(() => RefreshAdaptersAsync(showToast: true));
        StartStopCommand = new RelayCommand(ToggleMonitoring);
        AddTargetCommand = new RelayCommand(AddTarget);
        RemoveTargetCommand = new RelayCommand(parameter =>
        {
            if (parameter is string target) RemoveTarget(target);
        });
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, () => !IsRunningDiagnostics);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => _lastReport is not null && !IsRunningDiagnostics);
        RestartElevatedCommand = new RelayCommand(RestartElevated);
    }

    // ------------------------------------------------------------------ state
    public ObservableCollection<AdapterInfo> Adapters { get; }

    public ObservableCollection<PingTargetRow> PingTargets { get; }

    public ObservableCollection<DiagnosticRow> Findings { get; }

    public ObservableCollection<string> ProbeTargets { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand StartStopCommand { get; }

    public RelayCommand AddTargetCommand { get; }

    public RelayCommand RemoveTargetCommand { get; }

    public AsyncRelayCommand RunDiagnosticsCommand { get; }

    public AsyncRelayCommand ExportReportCommand { get; }

    public RelayCommand RestartElevatedCommand { get; }

    public AdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (!SetProperty(ref _selectedAdapter, value)) return;
            RaiseAdapterProperties();
            if (!_suppressAdapterSave && _isActivated && value is not null)
            {
                _services.Settings.SelectedAdapterId = value.Id;
                _services.SaveSettings();
            }
        }
    }

    public bool HasAdapter => SelectedAdapter is not null;

    public string AdapterName => SelectedAdapter?.Name ?? Loc.T("Mon.NoAdapter");

    public string AdapterDescription => SelectedAdapter?.Description ?? string.Empty;

    public string AdapterTypeLabel => SelectedAdapter?.IsWireless == true
        ? Loc.T("Mon.Wireless")
        : Loc.T("Mon.Wired");

    public string AdapterStatus => SelectedAdapter?.Status ?? "—";

    public string LinkSpeedText => SelectedAdapter is null ? "—" : Humanize.Speed(SelectedAdapter.LinkSpeedBps);

    public string AdapterIpv4Text => JoinOrDash(SelectedAdapter?.IPv4Addresses);

    public string AdapterGatewayText => string.IsNullOrWhiteSpace(SelectedAdapter?.Gateway)
        ? "—"
        : SelectedAdapter!.Gateway!;

    public string AdapterMacText => string.IsNullOrWhiteSpace(SelectedAdapter?.MacAddress)
        ? "—"
        : SelectedAdapter!.MacAddress!;

    public string AdapterDnsText => JoinOrDash(SelectedAdapter?.DnsServers);

    public bool ShowWifiInfo => SelectedAdapter?.IsWireless == true;

    public string AdapterSsidText
    {
        get
        {
            if (SelectedAdapter?.IsWireless != true) return "—";
            var ssid = string.IsNullOrWhiteSpace(SelectedAdapter.Ssid) ? "—" : SelectedAdapter.Ssid!;
            return SelectedAdapter.SignalPercent is { } signal ? $"{ssid}  ·  {signal}%" : ssid;
        }
    }

    public string AdapterReceivedText => SelectedAdapter is null ? "—" : Humanize.Bytes(SelectedAdapter.ReceivedBytes);

    public string AdapterSentText => SelectedAdapter is null ? "—" : Humanize.Bytes(SelectedAdapter.SentBytes);

    public bool IsLive
    {
        get => _isLive;
        private set
        {
            if (!SetProperty(ref _isLive, value)) return;
            OnPropertyChanged(nameof(MonitoringButtonText));
            OnPropertyChanged(nameof(IsNotLive));
        }
    }

    public bool IsNotLive => !_isLive;

    public string MonitoringButtonText => Loc.T(IsLive ? "Mon.StopMonitoring" : "Mon.StartMonitoring");

    public string DownloadText
    {
        get => _downloadText;
        private set => SetProperty(ref _downloadText, value);
    }

    public string UploadText
    {
        get => _uploadText;
        private set => SetProperty(ref _uploadText, value);
    }

    public IReadOnlyList<double> DownloadValues
    {
        get => _downloadValues;
        private set
        {
            _downloadValues = value as double[] ?? value.ToArray();
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<double> UploadValues
    {
        get => _uploadValues;
        private set
        {
            _uploadValues = value as double[] ?? value.ToArray();
            OnPropertyChanged();
        }
    }

    public bool HasThroughputSamples => _downloadHistory.Count > 0;

    public string TargetInput
    {
        get => _targetInput;
        set => SetProperty(ref _targetInput, value);
    }

    public double IntervalMs
    {
        get => _intervalMs;
        set
        {
            var clamped = Math.Clamp(Math.Round(value / 500) * 500, 500, 10_000);
            if (!SetProperty(ref _intervalMs, clamped)) return;
            OnPropertyChanged(nameof(IntervalText));
            _services.Ping.IntervalMs = (int)clamped;
            _services.Settings.PingIntervalMs = (int)clamped;
            ScheduleSettingsSave();
        }
    }

    public string IntervalText => $"{IntervalMs:0} ms";

    public bool IsRunningDiagnostics
    {
        get => _isRunningDiagnostics;
        private set
        {
            if (!SetProperty(ref _isRunningDiagnostics, value)) return;
            OnPropertyChanged(nameof(DiagnosticsButtonText));
            RunDiagnosticsCommand.RaiseCanExecuteChanged();
            ExportReportCommand.RaiseCanExecuteChanged();
        }
    }

    public string DiagnosticsButtonText =>
        Loc.T(IsRunningDiagnostics ? "Mon.RunningDiagnostics" : "Mon.RunDiagnostics");

    public bool IsElevated => SafeIsElevated();

    public bool ShowElevationBanner => !IsElevated;

    public bool HasReport => _lastReport is not null;

    public int CriticalCount => Findings.Count(finding => finding.SeverityKey == "critical");

    public int WarningCount => Findings.Count(finding => finding.SeverityKey == "warning");

    public int PassedCount => Findings.Count(finding => finding.SeverityKey is "ok" or "info");

    public string EnvironmentLine
    {
        get => _environmentLine;
        private set => SetProperty(ref _environmentLine, value);
    }

    public string LastRunText
    {
        get => _lastRunText;
        private set => SetProperty(ref _lastRunText, value);
    }

    public string QualityLabel
    {
        get => _qualityLabel;
        private set => SetProperty(ref _qualityLabel, value);
    }

    public string QualityKey
    {
        get => _qualityKey;
        private set => SetProperty(ref _qualityKey, value);
    }

    // ------------------------------------------------------------------ lifecycle
    public async Task ActivateAsync()
    {
        _isActivated = true;
        if (!_isSubscribed)
        {
            _isSubscribed = true;
            _services.Ping.Tick += OnPingTick;
            _services.Throughput.SampleReceived += OnThroughputSample;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
        }

        LoadTargetsFromSettings();
        await RefreshAdaptersAsync(showToast: false);
        SeedLatencyRows();
        SeedThroughput();
        ApplyLocalization();
        EnvironmentLine = BuildEnvironmentLine();

        if (_services.Settings.LiveMonitoring && !_services.Ping.IsRunning)
        {
            StartMonitoring(showToast: false);
        }
        else
        {
            IsLive = _services.Ping.IsRunning || _services.Throughput.IsRunning;
        }
    }

    public void Deactivate()
    {
        _isActivated = false;
        if (_isSubscribed)
        {
            _isSubscribed = false;
            _services.Ping.Tick -= OnPingTick;
            _services.Throughput.SampleReceived -= OnThroughputSample;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        }

        CancelDiagnostics();

        // Persist the current interval, but never stop the shared monitors: the shell owns them.
        _saveDebounceCts?.Cancel();
        _services.Settings.PingIntervalMs = (int)IntervalMs;
        _services.SaveSettings();
    }

    public void OnSettingsChanged()
    {
        if (!_isActivated) return;
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationBanner));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UiDispatcher.Post(ApplyLocalization);

    // ------------------------------------------------------------------ adapters
    private async Task RefreshAdaptersAsync(bool showToast)
    {
        try
        {
            var adapters = await Task.Run(() => _services.Adapters.GetAdapters());
            var previousId = SelectedAdapter?.Id;
            var preferred = previousId ?? _services.Settings.SelectedAdapterId;

            _suppressAdapterSave = true;
            try
            {
                Adapters.Clear();
                foreach (var adapter in adapters) Adapters.Add(adapter);

                SelectedAdapter = adapters.FirstOrDefault(adapter =>
                                   string.Equals(adapter.Id, preferred, StringComparison.OrdinalIgnoreCase))
                               ?? adapters.FirstOrDefault(adapter => adapter.IsDefaultRoute)
                               ?? adapters.FirstOrDefault();
            }
            finally
            {
                _suppressAdapterSave = false;
            }

            OnPropertyChanged(nameof(HasAdapter));
            if (showToast) ToastService.Instance.Show(Loc.T("Mon.Refreshed"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.T("Mon.ReadFailed"), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Monitor] adapter refresh failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ monitoring
    private void ToggleMonitoring()
    {
        if (IsLive) StopMonitoring();
        else StartMonitoring(showToast: true);
    }

    private void StartMonitoring(bool showToast)
    {
        try
        {
            _services.Ping.SetTargets(ProbeTargets.ToList());
            _services.Ping.IntervalMs = (int)IntervalMs;
            _services.Ping.Start();
            _services.Throughput.Start();
            IsLive = true;

            _services.Settings.LiveMonitoring = true;
            _services.SaveSettings();
            if (showToast) ToastService.Instance.Show(Loc.T("Mon.MonitoringStarted"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            IsLive = false;
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Monitor] start failed: {ex}");
        }
    }

    private void StopMonitoring()
    {
        try
        {
            _services.Ping.Stop();
            _services.Throughput.Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Monitor] stop failed: {ex}");
        }
        finally
        {
            IsLive = false;
            _services.Settings.LiveMonitoring = false;
            _services.SaveSettings();
            ToastService.Instance.Show(Loc.T("Mon.MonitoringStopped"), ToastKind.Info);
        }
    }

    private void OnPingTick(object? sender, PingTickEventArgs args) =>
        UiDispatcher.Post(() => ApplyPingTick(args));

    private void ApplyPingTick(PingTickEventArgs args)
    {
        if (!_isActivated) return;

        List<KeyValuePair<string, TargetStatistics>> snapshot;
        try
        {
            snapshot = args.Statistics.ToList();
        }
        catch (Exception ex)
        {
            // The monitor may still be writing the live dictionary; the next tick has fresh data.
            System.Diagnostics.Debug.WriteLine($"[Monitor] ping tick snapshot failed: {ex}");
            return;
        }

        foreach (var (target, statistics) in snapshot)
        {
            var row = GetOrCreateRow(target);
            row.Apply(statistics);
        }

        RecomputeQuality();
    }

    private void OnThroughputSample(object? sender, ThroughputSampleEventArgs args) =>
        UiDispatcher.Post(() => ApplyThroughput(args.Sample));

    private void ApplyThroughput(ThroughputSample sample)
    {
        if (!_isActivated) return;

        DownloadText = Humanize.Rate(sample.DownloadBytesPerSecond);
        UploadText = Humanize.Rate(sample.UploadBytesPerSecond);

        Push(_downloadHistory, sample.DownloadBytesPerSecond);
        Push(_uploadHistory, sample.UploadBytesPerSecond);

        DownloadValues = _downloadHistory.ToArray();
        UploadValues = _uploadHistory.ToArray();
        OnPropertyChanged(nameof(HasThroughputSamples));
    }

    private void SeedThroughput()
    {
        var latest = _services.Throughput.Latest;
        if (latest is not null) ApplyThroughput(latest);
    }

    private void SeedLatencyRows()
    {
        foreach (var target in ProbeTargets)
        {
            var row = GetOrCreateRow(target);
            try
            {
                if (_services.Ping.Statistics.TryGetValue(target, out var statistics)) row.Apply(statistics);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Monitor] latency seed failed: {ex}");
            }
        }

        RecomputeQuality();
    }

    private PingTargetRow GetOrCreateRow(string target)
    {
        if (_rows.TryGetValue(target, out var existing)) return existing;

        var row = new PingTargetRow(target);
        _rows[target] = row;

        if (!PingTargets.Any(item => string.Equals(item.Target, target, StringComparison.OrdinalIgnoreCase)))
        {
            PingTargets.Add(row);
        }

        return row;
    }

    private void RecomputeQuality()
    {
        var scored = PingTargets.Where(row => row.Sent > 0).Select(row => row.QualityScore).ToList();
        if (scored.Count == 0)
        {
            QualityKey = "unknown";
            QualityLabel = Loc.T("Mon.QualityUnknown");
            return;
        }

        var average = scored.Average();
        QualityKey = QualityKeyFor(average);
        QualityLabel = QualityLabelFor(average);
    }

    // ------------------------------------------------------------------ targets
    private void LoadTargetsFromSettings()
    {
        ProbeTargets.Clear();
        foreach (var target in _services.Settings.PingTargets) ProbeTargets.Add(target);
        if (ProbeTargets.Count == 0)
        {
            ProbeTargets.Add("1.1.1.1");
            ProbeTargets.Add("8.8.8.8");
        }

        _intervalMs = Math.Clamp(_services.Settings.PingIntervalMs, 500, 10_000);
        OnPropertyChanged(nameof(IntervalMs));
        OnPropertyChanged(nameof(IntervalText));
    }

    private void AddTarget()
    {
        var value = (TargetInput ?? string.Empty).Trim();
        if (!IsValidHost(value))
        {
            ToastService.Instance.Show(Loc.T("Mon.InvalidTarget"), ToastKind.Warning);
            return;
        }

        if (ProbeTargets.Count >= 5)
        {
            ToastService.Instance.Show(Loc.T("Mon.TargetsMax"), ToastKind.Warning);
            return;
        }

        if (ProbeTargets.Any(target => string.Equals(target, value, StringComparison.OrdinalIgnoreCase)))
        {
            ToastService.Instance.Show(Loc.T("Mon.DuplicateTarget"), ToastKind.Info);
            return;
        }

        ProbeTargets.Add(value);
        TargetInput = string.Empty;
        PersistTargets(forceRestart: true);
    }

    private void RemoveTarget(string target)
    {
        if (ProbeTargets.Count <= 1)
        {
            ToastService.Instance.Show(Loc.T("Mon.NeedTarget"), ToastKind.Warning);
            return;
        }

        var removed = ProbeTargets.FirstOrDefault(existing =>
            string.Equals(existing, target, StringComparison.OrdinalIgnoreCase));
        if (removed is null) return;

        ProbeTargets.Remove(removed);

        if (_rows.Remove(removed, out var row))
        {
            PingTargets.Remove(row);
        }

        PersistTargets(forceRestart: true);
    }

    private void PersistTargets(bool forceRestart)
    {
        try
        {
            _services.Settings.PingTargets = ProbeTargets.ToList();
            _services.SaveSettings();

            _services.Ping.SetTargets(ProbeTargets.ToList());
            _services.Ping.IntervalMs = (int)IntervalMs;
            if (forceRestart && IsLive) _services.Ping.Start();

            SeedLatencyRows();
            ToastService.Instance.Show(Loc.T("Mon.TargetsSaved"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Monitor] target save failed: {ex}");
        }
    }

    private void ScheduleSettingsSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var cts = _saveDebounceCts;
        _ = SaveSettingsDebouncedAsync(cts);
    }

    private async Task SaveSettingsDebouncedAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(450, cts.Token);
            if (cts.IsCancellationRequested) return;
            _services.Settings.PingIntervalMs = (int)IntervalMs;
            _services.SaveSettings();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer slider movement
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Monitor] interval save failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ diagnostics
    private async Task RunDiagnosticsAsync()
    {
        if (IsRunningDiagnostics) return;

        IsRunningDiagnostics = true;
        Findings.Clear();
        _lastReport = null;
        RaiseDiagnosticsSummary();
        ExportReportCommand.RaiseCanExecuteChanged();

        _diagnosticsCts = new CancellationTokenSource();
        var token = _diagnosticsCts.Token;

        try
        {
            var report = await Task.Run(() => _services.Diagnostics.RunAsync(token), token);
            if (!_isActivated || report is null) return;

            _lastReport = report;
            Findings.Clear();
            foreach (var finding in report.Findings
                         .OrderByDescending(finding => SeverityRank(finding.Severity)))
            {
                Findings.Add(new DiagnosticRow(finding, Loc.Instance.IsArabic));
            }

            EnvironmentLine = BuildEnvironmentLine();
            LastRunText = Loc.Format("Mon.LastRun", Humanize.Clock(report.GeneratedAtUtc));
            RaiseDiagnosticsSummary();

            ToastService.Instance.Show(
                Findings.Count == 0
                    ? Loc.T("Mon.NoFindingsFound")
                    : Loc.Format("Mon.DiagnosticsDone", Findings.Count, CriticalCount, WarningCount),
                CriticalCount > 0 ? ToastKind.Warning : ToastKind.Success);
        }
        catch (OperationCanceledException)
        {
            ToastService.Instance.Show(Loc.T("Mon.DiagnosticsCanceled"), ToastKind.Info);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Mon.DiagnosticsFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Monitor] diagnostics failed: {ex}");
        }
        finally
        {
            IsRunningDiagnostics = false;
            _diagnosticsCts?.Dispose();
            _diagnosticsCts = null;
            ExportReportCommand.RaiseCanExecuteChanged();
        }
    }

    private void CancelDiagnostics()
    {
        try
        {
            _diagnosticsCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already finished
        }
    }

    private async Task ExportReportAsync()
    {
        if (_lastReport is null)
        {
            ToastService.Instance.Show(Loc.T("Mon.RunFirst"), ToastKind.Warning);
            return;
        }

        try
        {
            var path = _services.Files.PickSavePath("fusionnet-diagnostics.txt", Loc.T("Dialog.TextFilter"));
            if (string.IsNullOrWhiteSpace(path)) return;

            var text = _services.Diagnostics.BuildTextReport(_lastReport);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ToastService.Instance.Show(Loc.T("Mon.ReportSaved"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Mon.ReportFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Monitor] export failed: {ex}");
        }
    }

    private void RaiseDiagnosticsSummary() => RaiseAll(
        nameof(HasReport),
        nameof(CriticalCount),
        nameof(WarningCount),
        nameof(PassedCount));

    private string BuildEnvironmentLine()
    {
        try
        {
            var info = _services.SystemInfo;
            var ram = Humanize.Bytes(info.TotalPhysicalMemoryBytes);
            return Loc.Format("Mon.EnvLine", info.WindowsVersion, info.CpuName, ram);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Monitor] environment line failed: {ex}");
            return Loc.T("Common.Unknown");
        }
    }

    private static int SeverityRank(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical => 3,
        DiagnosticSeverity.Warning => 2,
        DiagnosticSeverity.Info => 1,
        _ => 0
    };

    // ------------------------------------------------------------------ helpers
    private void RestartElevated()
    {
        if (_services.Elevation.TryRestartElevated()) return;
        ToastService.Instance.Show(Loc.T("Mon.ElevationCancelled"), ToastKind.Info);
    }

    private bool SafeIsElevated()
    {
        try
        {
            return _services.Elevation.IsElevated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Monitor] elevation probe failed: {ex}");
            return false;
        }
    }

    private void ApplyLocalization()
    {
        foreach (var row in PingTargets) row.RefreshLocalization();
        foreach (var finding in Findings) finding.RefreshLocalization(Loc.Instance.IsArabic);
        OnPropertyChanged(nameof(AdapterTypeLabel));
        OnPropertyChanged(nameof(AdapterName));
        OnPropertyChanged(nameof(MonitoringButtonText));
        OnPropertyChanged(nameof(DiagnosticsButtonText));
        OnPropertyChanged(nameof(IntervalText));
        OnPropertyChanged(nameof(EnvironmentLine));
        RecomputeQuality();
    }

    private static void Push(List<double> values, double sample)
    {
        values.Add(double.IsFinite(sample) && sample > 0 ? sample : 0);
        while (values.Count > 60) values.RemoveAt(0);
    }

    private static string JoinOrDash(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 ? "—" : string.Join(", ", values);

    private static bool IsValidHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length > 253) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            trimmed, @"^[A-Za-z0-9](?:[A-Za-z0-9\-\._:]{0,251}[A-Za-z0-9])?$");
    }

    private static string QualityKeyFor(double score) => score switch
    {
        >= 90 => "excellent",
        >= 75 => "good",
        >= 50 => "fair",
        > 0 => "poor",
        _ => "unknown"
    };

    private static string QualityLabelFor(double score) => Loc.T(score switch
    {
        >= 90 => "Mon.QualityExcellent",
        >= 75 => "Mon.QualityGood",
        >= 50 => "Mon.QualityFair",
        > 0 => "Mon.QualityPoor",
        _ => "Mon.QualityUnknown"
    });

    private void RaiseAdapterProperties() => RaiseAll(
        nameof(HasAdapter),
        nameof(AdapterName),
        nameof(AdapterDescription),
        nameof(AdapterTypeLabel),
        nameof(AdapterStatus),
        nameof(LinkSpeedText),
        nameof(AdapterIpv4Text),
        nameof(AdapterGatewayText),
        nameof(AdapterMacText),
        nameof(AdapterDnsText),
        nameof(ShowWifiInfo),
        nameof(AdapterSsidText),
        nameof(AdapterReceivedText),
        nameof(AdapterSentText));
}

/// <summary>One rolling latency row (target + stats + sparkline history).</summary>
public sealed class PingTargetRow : ViewModelBase
{
    private const int HistoryLength = 40;
    private readonly List<double> _history = [];

    private string _minText = "—";
    private string _avgText = "—";
    private string _maxText = "—";
    private string _jitterText = "—";
    private string _lossText = "—";
    private string _lastText = "—";
    private string _qualityLabel;
    private string _qualityKey = "unknown";
    private string _samplesText = "—";
    private double[] _historyValues = [];
    private int _sent;
    private double _qualityScore;

    public PingTargetRow(string target)
    {
        Target = target;
        _qualityLabel = Loc.T("Mon.QualityUnknown");
    }

    public string Target { get; }

    public int Sent
    {
        get => _sent;
        private set => SetProperty(ref _sent, value);
    }

    public double QualityScore
    {
        get => _qualityScore;
        private set => SetProperty(ref _qualityScore, value);
    }

    public string MinText
    {
        get => _minText;
        private set => SetProperty(ref _minText, value);
    }

    public string AvgText
    {
        get => _avgText;
        private set => SetProperty(ref _avgText, value);
    }

    public string MaxText
    {
        get => _maxText;
        private set => SetProperty(ref _maxText, value);
    }

    public string JitterText
    {
        get => _jitterText;
        private set => SetProperty(ref _jitterText, value);
    }

    public string LossText
    {
        get => _lossText;
        private set => SetProperty(ref _lossText, value);
    }

    public string LastText
    {
        get => _lastText;
        private set => SetProperty(ref _lastText, value);
    }

    public string QualityLabel
    {
        get => _qualityLabel;
        private set => SetProperty(ref _qualityLabel, value);
    }

    public string QualityKey
    {
        get => _qualityKey;
        private set => SetProperty(ref _qualityKey, value);
    }

    public string SamplesText
    {
        get => _samplesText;
        private set => SetProperty(ref _samplesText, value);
    }

    public IReadOnlyList<double> HistoryValues
    {
        get => _historyValues;
        private set
        {
            _historyValues = value as double[] ?? value.ToArray();
            OnPropertyChanged();
        }
    }

    public bool HasHistory => _historyValues.Length > 0;

    public void Apply(TargetStatistics statistics)
    {
        Sent = statistics.Sent;
        QualityScore = statistics.QualityScore;

        MinText = statistics.Received > 0 ? Humanize.Ms(statistics.MinMs, 0) : "—";
        AvgText = statistics.Received > 0 ? Humanize.Ms(statistics.AverageMs, 0) : "—";
        MaxText = statistics.Received > 0 ? Humanize.Ms(statistics.MaxMs, 0) : "—";
        JitterText = statistics.Received > 0 ? Humanize.Ms(statistics.JitterMs, 1) : "—";
        LossText = Humanize.Percent(statistics.LossPercent, statistics.LossPercent < 10 ? 1 : 0);
        LastText = statistics.LastMs > 0
            ? Humanize.Ms(statistics.LastMs, 0)
            : "—";
        SamplesText = $"{statistics.Received}/{statistics.Sent}";

        QualityKey = statistics.QualityScore switch
        {
            >= 90 => "excellent",
            >= 75 => "good",
            >= 50 => "fair",
            > 0 => "poor",
            _ => "unknown"
        };
        QualityLabel = Loc.T(QualityKey switch
        {
            "excellent" => "Mon.QualityExcellent",
            "good" => "Mon.QualityGood",
            "fair" => "Mon.QualityFair",
            "poor" => "Mon.QualityPoor",
            _ => "Mon.QualityUnknown"
        });

        if (statistics.LastMs > 0 && statistics.LossPercent < 100)
        {
            _history.Add(statistics.LastMs);
            while (_history.Count > HistoryLength) _history.RemoveAt(0);
            HistoryValues = _history.ToArray();
            OnPropertyChanged(nameof(HasHistory));
        }
    }

    public void RefreshLocalization() => QualityLabel = Loc.T(QualityKey switch
    {
        "excellent" => "Mon.QualityExcellent",
        "good" => "Mon.QualityGood",
        "fair" => "Mon.QualityFair",
        "poor" => "Mon.QualityPoor",
        _ => "Mon.QualityUnknown"
    });
}

/// <summary>One diagnostics finding, coloured by severity.</summary>
public sealed class DiagnosticRow : ViewModelBase
{
    private readonly DiagnosticFinding _finding;
    private string _title;
    private string _suggestion;

    public DiagnosticRow(DiagnosticFinding finding, bool arabic)
    {
        _finding = finding;
        _title = arabic ? finding.TitleAr : finding.TitleEn;
        _suggestion = arabic ? finding.SuggestionAr ?? string.Empty : finding.SuggestionEn ?? string.Empty;
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Detail => _finding.Detail;

    public string Category => _finding.Category;

    public string Suggestion
    {
        get => _suggestion;
        private set
        {
            if (!SetProperty(ref _suggestion, value)) return;
            OnPropertyChanged(nameof(HasSuggestion));
        }
    }

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Suggestion);

    public string SeverityKey => _finding.Severity switch
    {
        DiagnosticSeverity.Critical => "critical",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "info",
        _ => "ok"
    };

    public string SeverityLabel => Loc.T(_finding.Severity switch
    {
        DiagnosticSeverity.Critical => "Mon.Critical",
        DiagnosticSeverity.Warning => "Mon.Warnings",
        _ => "Mon.Passed"
    });

    public string Glyph => _finding.Severity switch
    {
        DiagnosticSeverity.Critical => "\uE783",
        DiagnosticSeverity.Warning => "\uE7BA",
        DiagnosticSeverity.Info => "\uE946",
        _ => "\uE73E"
    };

    public void RefreshLocalization(bool arabic)
    {
        Title = arabic ? _finding.TitleAr : _finding.TitleEn;
        Suggestion = arabic ? _finding.SuggestionAr ?? string.Empty : _finding.SuggestionEn ?? string.Empty;
    }
}
