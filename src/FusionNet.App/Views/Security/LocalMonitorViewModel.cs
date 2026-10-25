using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using FusionNet.App.Mvvm;
using FusionNet.App.Localization;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Security;

public sealed class LocalMonitorViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource? _scan;
    private bool _polling;
    private bool _busy;
    private bool _languageSubscribed;
    private string _status = "Monitor.Text.InitialStatus";
    private string _session = "Monitor.Text.NoSession";
    private LocalMonitorReport? _report;
    private string _minutes = "60";
    private string _endpoint = "";
    private string _model = "";
    private string _aiOutput = "Monitor.Text.InitialAi";
    private readonly AiCredentialStore _credentials = new(System.IO.Path.Combine(AppPaths.DataDirectory, "ai-credentials"));
    private DateTimeOffset? _trafficTimestamp;
    private bool _onlyMeasuredActive;
    private bool _onlyHighResource;
    private string _search="";
    private readonly List<ProcessMonitorRow> _rows = [];
    private string _approvedChanges = "Monitor.Text.NoApproval";
    private string _storageOutput = "Monitor.Text.InitialStorage";
    private StorageReport? _storageReport;
    private sealed record AiPreferences(string Endpoint, string Model);
    public LocalMonitorViewModel(IAppServices services, bool security)
    {
        _services = services;
        IsSecurity = security;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !_busy);
        CancelCommand = new RelayCommand(() => _scan?.Cancel(), () => _busy);
        StartCommand = new AsyncRelayCommand(StartAsync, () => !_busy && !_services.GameBoost.Status.IsPending && _report is not null);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _services.GameBoost.Status.IsPending);
        ExportCommand = new RelayCommand(Export, () => !_busy && _report is not null);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !_busy && _report is not null);
        SampleTrafficCommand = new AsyncRelayCommand(SampleTrafficAsync, () => !_busy && _report is not null);
        RecommendCommand = new RelayCommand(Recommend, () => IsGame && !_busy);
        StorageCommand = new AsyncRelayCommand(ScanStorageAsync, () => !_busy);
        MonthlyChecklistCommand = new RelayCommand(ShowMonthlyChecklist);
        var preferences = FusionJson.LoadOrCreate(System.IO.Path.Combine(AppPaths.DataDirectory, "ai-provider.json"), () => new AiPreferences("", ""));
        _endpoint = preferences.Endpoint ?? "";
        _model = preferences.Model ?? "";
        _timer.Tick += async (_, _) => await RefreshSessionAsync();
    }
    public bool IsSecurity { get; }
    public bool IsGame => !IsSecurity;
    public string Title => IsSecurity ? Loc.T("Monitor.Text.SecurityTitle") : Loc.T("Monitor.Text.GameTitle");
    public string Status { get => Loc.T(_status); private set => SetProperty(ref _status, value); }
    public string Session { get => Loc.T(_session); private set => SetProperty(ref _session, value); }
    public string Minutes { get => _minutes; set => SetProperty(ref _minutes, value); }
    public string Summary => _report is null ? Loc.T("Monitor.Text.NoResults") :
        Loc.Format("Monitor.Format.Summary",_report.Processes.Count,_report.Connections.Count,_report.Findings.Count,_report.FinishedUtc.ToLocalTime());
    public string ProcessCount=>_report is null?"—":_rows.Count.ToString();
    public string MeasuredCount=>_trafficTimestamp is null?"—":_rows.Count(row=>row.Process.UploadBytesPerSecond>0||row.Process.DownloadBytesPerSecond>0).ToString();
    public string AlertCount=>_report is null?"—":Findings.Count(finding=>finding.Severity=="Warning").ToString();
    public string SelectedCount=>_rows.Count(row=>row.Selected&&row.CanSelect).ToString();
    public string ContextMetricLabel=>IsGame?Loc.T("Monitor.Text.SelectedLabel"):Loc.T("Monitor.Text.SourcesLabel");
    public string ContextMetricValue=>IsGame?SelectedCount:_report is null?"—":_report.Sections.Count(section=>section.Status=="Complete").ToString();
    public bool IsBusy=>_busy;
    public string EmptyMessage=>_report is null?Loc.T("Monitor.Text.InitialEmpty"):Loc.T("Monitor.Text.FilteredEmpty");
    public bool HasNoProcesses=>Processes.Count==0;
    public string Search{get=>_search;set{if(SetProperty(ref _search,value))RenderProcesses();}}
    public bool OnlyHighResource{get=>_onlyHighResource;set{if(SetProperty(ref _onlyHighResource,value))RenderProcesses();}}
    public ObservableCollection<ProcessMonitorRow> Processes { get; } = [];
    public ObservableCollection<SecurityFinding> Findings { get; } = [];
    public ObservableCollection<InventorySectionRow> Sections { get; } = [];
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public RelayCommand ExportCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand SampleTrafficCommand { get; }
    public bool OnlyMeasuredActive
    {
        get => _onlyMeasuredActive;
        set { if (SetProperty(ref _onlyMeasuredActive, value)) RenderProcesses(); }
    }
    public string Endpoint { get => _endpoint; set => SetProperty(ref _endpoint, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string AiOutput { get => Loc.T(_aiOutput); private set => SetProperty(ref _aiOutput, value); }
    public string ApprovedChanges { get => Loc.T(_approvedChanges); private set => SetProperty(ref _approvedChanges, value); }
    public string StorageOutput { get => Loc.T(_storageOutput); private set => SetProperty(ref _storageOutput, value); }
    public bool ComparePhotoContents { get; set; }
    public RelayCommand RecommendCommand { get; }
    public AsyncRelayCommand StorageCommand { get; }
    public RelayCommand MonthlyChecklistCommand { get; }
    public void Activate()
    {
        if(!_languageSubscribed){Loc.Instance.LanguageChanged+=OnLanguageChanged;_languageSubscribed=true;}
        OnLanguageChanged(null,EventArgs.Empty);
        _timer.Start(); _ = RefreshSessionAsync();
    }
    public void Deactivate()
    {
        _timer.Stop(); _scan?.Cancel();
        if(_languageSubscribed){Loc.Instance.LanguageChanged-=OnLanguageChanged;_languageSubscribed=false;}
    }
    private void OnLanguageChanged(object? sender,EventArgs args)
    {
        OnPropertyChanged(string.Empty);
        foreach(var row in _rows)row.RefreshLanguage();
        foreach(var section in Sections)section.RefreshLanguage();
    }

    private async Task ScanAsync()
    {
        _busy = true;
        _scan = new CancellationTokenSource();
        RefreshCommands();
        Status = "Monitor.Text.Scanning";
        try
        {
            var report = await _services.LocalMonitor.ScanAsync(IsSecurity, _scan.Token);
            _report = report;
            _trafficTimestamp = null;
            _rows.Clear(); Findings.Clear(); Sections.Clear();
            var protectedNames = ProtectedNames();
            foreach (var process in report.Processes)
            {
                var endpoints = report.Connections.Where(connection => ProcessSampling.SameIdentity(connection.Identity, process.Identity)).ToArray();
                var row=new ProcessMonitorRow(process, endpoints, GameBoostPolicy.BlockReason(process, protectedNames,
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.ProcessPath ?? ""), IsGame);
                row.PropertyChanged+=OnRowChanged;
                _rows.Add(row);
            }
            RenderProcesses();
            foreach (var finding in report.Findings) Findings.Add(finding);
            foreach (var section in report.Sections) Sections.Add(new(section));
            Status = "Monitor.Text.ScanComplete";
            OnPropertyChanged(nameof(Summary));
        }
        catch (OperationCanceledException) { Status = "Monitor.Text.ScanCancelled"; }
        catch { Status = "Monitor.Text.ScanFailed"; throw; }
        finally { _busy = false; _scan.Dispose(); _scan = null; RefreshCommands(); }
    }

    private ImmutableArray<string> GameNames() => GameCatalog.BuildWatchList(_services.Settings)
        .SelectMany(game => game.ProcessNames).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    // The preview carries these names, so the elevated worker re-checks the same launcher protection.
    private ImmutableArray<string> ProtectedNames() => GameNames().AddRange(GameBoostPolicy.LauncherGuard(GameCatalog.All.Concat(GameCatalog.BuildWatchList(_services.Settings))
        .Where(game => (_report?.Processes ?? []).Any(process => game.MatchesProcess(process.Name))))).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();

    private async Task StartAsync()
    {
        if (_services.Fusion.IsArmed || _services.Journal.HasPendingRestore) throw new InvalidOperationException(Loc.T("Monitor.Text.LegacyPending"));
        var selected = _rows.Where(row => row.Selected && row.CanSelect).Select(row => row.Process.Identity!)
            .DistinctBy(identity => identity.Path, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        if (selected.Length is < 1 or > 10) throw new InvalidOperationException(Loc.T("Monitor.Text.SelectTargets"));
        if (!int.TryParse(Minutes, out var minutes) || minutes is < 1 or > 360) throw new InvalidOperationException(Loc.T("Monitor.Text.InvalidDuration"));
        using var owner = Process.GetCurrentProcess();
        var games = (_report?.Processes ?? []).Where(process => process.Identity is not null &&
            GameNames().Contains(process.Name, StringComparer.OrdinalIgnoreCase)).Select(process => process.Identity!).ToImmutableArray();
        var preview = new GameBoostPreview(Guid.NewGuid(), new(owner.Id, owner.StartTime.ToUniversalTime(), Environment.ProcessPath!),
            DateTimeOffset.UtcNow.AddMinutes(minutes), selected, ProtectedNames(), games);
        var body = new StringBuilder(Loc.T("Monitor.Text.PreviewHeading"));
        body.AppendLine(Loc.Format("Monitor.Format.Duration",minutes));
        for (var index = 0; index < selected.Length; index++)
        {
            var row = _rows.First(row => ProcessSampling.SameIdentity(row.Process.Identity, selected[index]));
            body.AppendLine(Loc.Format("Monitor.Format.Target",row.Name,selected[index].Path,row.Traffic,preview.RuleName(index)));
        }
        body.AppendLine(Loc.Format("Monitor.Format.RecoveryTask",preview.RecoveryTaskName));
        body.AppendLine(Loc.T("Monitor.Text.RecoveryLimits"));
        if (!ReportReviewWindow.Approve(Loc.T("Monitor.Text.ConsentTitle"), body.ToString(), Loc.T("Monitor.Text.ApproveSession"))) return;
        await _services.GameBoost.StartAsync(preview, GameBoostConsent.Approve(preview), default);
        ApprovedChanges = Loc.T("Monitor.Text.ApprovalRecord") + body;
        _services.Activity.Info("GameBoost", $"Approved session {preview.SessionId:N}; {selected.Length} executable targets; {minutes} minutes.");
        await RefreshSessionAsync();
    }

    private async Task StopAsync()
    {
        await _services.GameBoost.StopAsync(default);
        await RefreshSessionAsync();
    }
    private async Task RefreshSessionAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var status = await _services.GameBoost.RefreshAsync(default);
            var elapsed = status.StartedUtc.HasValue ? DateTimeOffset.UtcNow - status.StartedUtc.Value : TimeSpan.Zero;
            Session = Loc.Format("Monitor.Format.Session",Loc.Instance.IsArabic?status.DisplayName:status.State.ToString(),status.State,
                status.RuleCount<0?Loc.T("Monitor.Unavailable"):status.RuleCount.ToString(),elapsed,status.Message);
            RefreshCommands();
        }
        finally { _polling = false; }
    }
    private void RefreshCommands()
    { ScanCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); StartCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged(); AnalyzeCommand.RaiseCanExecuteChanged(); SampleTrafficCommand.RaiseCanExecuteChanged(); RecommendCommand.RaiseCanExecuteChanged(); StorageCommand.RaiseCanExecuteChanged();
        foreach(var name in new[]{nameof(ProcessCount),nameof(MeasuredCount),nameof(AlertCount),nameof(SelectedCount),nameof(ContextMetricValue),nameof(IsBusy)})OnPropertyChanged(name);
    }
    private void OnRowChanged(object? sender,global::System.ComponentModel.PropertyChangedEventArgs args)
    {if(args.PropertyName==nameof(ProcessMonitorRow.Selected)){OnPropertyChanged(nameof(SelectedCount));OnPropertyChanged(nameof(ContextMetricValue));}}

    private void Recommend()
    {
        var choices = GameBoostPolicy.Recommend(_rows.Where(row => row.CanSelect).Select(row => row.Process));
        foreach (var row in _rows) row.Selected = choices.Any(choice => ProcessSampling.SameIdentity(choice.Identity,row.Process.Identity));
        Status = choices.Count == 0 ? Loc.T("Monitor.Text.NoSuggestion") :
            Loc.Format("Monitor.Format.Suggestion",choices.Count);
    }
    private async Task ScanStorageAsync()
    {
        using var picker = new global::System.Windows.Forms.FolderBrowserDialog { Description=Loc.T("Monitor.Text.ChooseFolder"), UseDescriptionForTitle=true };
        if (picker.ShowDialog()!=global::System.Windows.Forms.DialogResult.OK) return;
        var root = picker.SelectedPath;
        if (MessageBox.Show(Loc.Format("Monitor.Format.StorageConsent",root,Loc.T(ComparePhotoContents?"Monitor.Text.PhotoComparisonYes":"Monitor.Text.PhotoComparisonNo")), Loc.T("Monitor.Text.StorageConsentTitle"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
        _busy=true; _scan=new CancellationTokenSource(); RefreshCommands();
        try
        {
            StorageOutput="Monitor.Text.StorageBusy";
            var result=await _services.LocalStorage.ScanAsync(root,ComparePhotoContents,_scan.Token);
            _storageReport=result;
            StorageOutput=JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true,Encoder=global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        }
        catch(OperationCanceledException) { StorageOutput="Monitor.Text.StorageCancelled"; }
        catch { StorageOutput="Monitor.Text.StorageFailed"; throw; }
        finally { _busy=false; _scan.Dispose(); _scan=null; RefreshCommands(); }
    }
    private static void ShowMonthlyChecklist() => ReportReviewWindow.Approve(Loc.T("Monitor.Text.ChecklistTitle"),
        Loc.T("Monitor.Text.Checklist"),Loc.T("Monitor.Text.Done"));

    private void RenderProcesses()
    {
        Processes.Clear();
        foreach (var row in _rows.Where(row => ProcessPresentation.Matches(row.Process,Search,OnlyMeasuredActive,OnlyHighResource))
                     .OrderByDescending(row => row.Process.UploadBytesPerSecond + row.Process.DownloadBytesPerSecond))
            Processes.Add(row);
        OnPropertyChanged(nameof(HasNoProcesses));OnPropertyChanged(nameof(EmptyMessage));
    }
    private async Task SampleTrafficAsync()
    {
        if (_report is null) return;
        if (MessageBox.Show(Loc.T("Monitor.Text.EtwConsent"), Loc.T("Monitor.Text.EtwConsentTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _busy = true; _scan = new CancellationTokenSource(); RefreshCommands();
        try
        {
            Status = "Monitor.Text.EtwBusy";
            var sample = await _services.ProcessTraffic.SampleAsync(_scan.Token);
            _trafficTimestamp = sample.TimestampUtc;
            var rates = sample.Processes.ToDictionary(row => row.Identity.Pid);
            var protectedNames = ProtectedNames();
            var updated = new List<ProcessObservation>();
            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                var process = row.Process;
                if (rates.TryGetValue(process.Pid, out var rate) && ProcessSampling.SameIdentity(process.Identity, rate.Identity))
                    process = process with { UploadBytesPerSecond = rate.UploadBytesPerSecond, DownloadBytesPerSecond = rate.DownloadBytesPerSecond };
                else process = process with { UploadBytesPerSecond = null, DownloadBytesPerSecond = null };
                updated.Add(process);
                var replacement = new ProcessMonitorRow(process, _report.Connections.Where(connection => ProcessSampling.SameIdentity(connection.Identity, process.Identity)).ToArray(),
                    GameBoostPolicy.BlockReason(process, protectedNames, Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.ProcessPath ?? ""), IsGame, _trafficTimestamp)
                    { Selected = row.Selected };
                row.PropertyChanged-=OnRowChanged;
                replacement.PropertyChanged+=OnRowChanged;
                _rows[index] = replacement;
            }
            _report = _report with { Processes = updated };
            RenderProcesses();
            Status = Loc.Format("Monitor.Format.EtwSample",sample.WindowSeconds,_trafficTimestamp.Value.ToLocalTime()) +
                (sample.EventsLost == 0 ? Loc.T("Monitor.Text.EtwComplete") : Loc.T("Monitor.Text.EtwIncomplete"));
        }
        catch (OperationCanceledException) { Status = "Monitor.Text.EtwCancelled"; }
        catch { Status = "Monitor.Text.EtwFailed"; throw; }
        finally { _busy = false; _scan.Dispose(); _scan = null; RefreshCommands(); }
    }

    public void SaveAiProvider(string key)
    {
        var endpoint = AiAnalysis.ValidateEndpoint(Endpoint.Trim());
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200) throw new ArgumentException(Loc.T("Monitor.Text.ModelRequired"));
        if (!string.IsNullOrWhiteSpace(key)) _credentials.Save(endpoint, key);
        FusionJson.SaveAtomic(System.IO.Path.Combine(AppPaths.DataDirectory, "ai-provider.json"), new AiPreferences(endpoint.AbsoluteUri, Model.Trim()));
        AiOutput = "Monitor.Text.ProviderSaved";
    }
    public void RemoveAiKey()
    {
        _credentials.Remove(AiAnalysis.ValidateEndpoint(Endpoint.Trim()));
        AiOutput = "Monitor.Text.KeyRemoved";
    }
    private async Task AnalyzeAsync()
    {
        if (_report is null) return;
        var preview = new AiAnalysisPreview(AiAnalysis.ValidateEndpoint(Endpoint.Trim()), Model.Trim(), AiAnalysis.BuildPayload(_report));
        if (string.IsNullOrWhiteSpace(preview.Model)) throw new InvalidOperationException(Loc.T("Monitor.Text.SaveProviderFirst"));
        var text = Loc.Format("Monitor.Format.CloudConsent",preview.Endpoint,preview.Model,preview.Payload);
        if (!ReportReviewWindow.Approve(Loc.T("Monitor.Text.CloudConsentTitle"), text, Loc.T("Monitor.Text.CloudApprove"))) return;
        _busy = true;
        _scan = new CancellationTokenSource();
        RefreshCommands();
        try
        {
            AiOutput = "Monitor.Text.CloudBusy";
            AiOutput = Loc.T("Monitor.Text.CloudAdvisory") + await _services.SecurityAi.AnalyzeAsync(preview, AiAnalysisConsent.Approve(preview), _credentials.Load(preview.Endpoint), _scan.Token);
        }
        catch (OperationCanceledException) { AiOutput = "Monitor.Text.CloudCancelled"; }
        catch { AiOutput = "Monitor.Text.CloudFailed"; throw; }
        finally { _busy = false; _scan.Dispose(); _scan = null; RefreshCommands(); }
    }

    private void Export()
    {
        if (_report is null) return;
        var json = CreateExportJson(_report, Session, ApprovedChanges, _trafficTimestamp, _storageReport);
        if (!ReportReviewWindow.Approve(Loc.T("Monitor.Text.ExportTitle"), json, Loc.T("Monitor.Text.ExportAction"))) return;
        var path = _services.Files.PickSavePath("fusionnet-local-report.json", "JSON (*.json)|*.json");
        if (path is not null) { File.WriteAllText(path, json, new UTF8Encoding(false)); Status = "Monitor.Text.ExportSaved"; }
    }

    private static string CreateExportJson(LocalMonitorReport report, string session, string approvedChanges, DateTimeOffset? trafficTimestamp, StorageReport? storage)
    {
        var export = new { Schema="fusionnet-local-report-v1", report.StartedUtc, report.FinishedUtc,
            Processes = report.Processes.Select(process => new { process.Name, process.Pid, Path=process.Identity?.Path, StartedUtc=process.Identity?.StartedUtc,
                process.CpuPercent, process.MemoryBytes, process.MemoryPercent, process.Signature, process.Publisher, process.Availability, process.UploadBytesPerSecond, process.DownloadBytesPerSecond }),
            Sections = report.Sections.Select(section => new { section.Name, section.Status, section.Detail, ItemCount = section.Items.Count }),
            Findings = report.Findings, Session=session, ApprovedChanges=approvedChanges, TrafficTimestampUtc=trafficTimestamp, Storage=storage };
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true, Encoder=global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}

public sealed class ProcessMonitorRow(ProcessObservation process, IReadOnlyList<ConnectionObservation> endpoints, string? blockReason, bool game, DateTimeOffset? trafficTimestamp = null) : ViewModelBase
{
    private bool _selected;
    public ProcessObservation Process { get; } = process;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
    public bool CanSelect => game && blockReason is null && (endpoints.Count > 0 || Process.UploadBytesPerSecond > 0 || Process.DownloadBytesPerSecond > 0);
    public bool ShowSelection => game;
    public string Name => $"{Process.Name} — PID {Process.Pid}";
    public string Path => Process.Identity?.Path ?? Loc.T("Monitor.Text.PathUnavailable");
    public string Resources
    {
        get
        {
            var unavailable=Loc.T("Monitor.Unavailable");
            var cpu=Process.CpuPercent is {} percent?$"{percent:F1}%":unavailable;
            var memory=Process.MemoryBytes is {} bytes?$"{bytes/1048576d:F1} MB":unavailable;
            return $"CPU: {cpu} | RAM: {memory}";
        }
    }
    public string Purpose=>ProcessPresentation.Purpose(Process.Name,!Loc.Instance.IsArabic);
    internal void RefreshLanguage()=>OnPropertyChanged(string.Empty);
    public string Traffic => Process.UploadBytesPerSecond.HasValue && Process.DownloadBytesPerSecond.HasValue
        ? $"ETW ↑ {Process.UploadBytesPerSecond / 1024:F2} KB/s | ↓ {Process.DownloadBytesPerSecond / 1024:F2} KB/s — {trafficTimestamp?.ToLocalTime():HH:mm:ss}"
        : Loc.T("Monitor.Text.TrafficUnavailable");
    public string Detail => Loc.Format("Monitor.Format.Identity",Process.Signature,Process.Availability,
        blockReason??Loc.T(endpoints.Count==0?"Monitor.Text.NoConnections":"Monitor.Text.Eligible")) +
        string.Join("\n", endpoints.Take(12).Select(connection => $"{connection.Protocol} {connection.LocalAddress}:{connection.LocalPort} → {connection.RemoteAddress ?? Loc.T("Monitor.Text.UdpUnavailable")}:{connection.RemotePort} ({connection.State})"));
}
public sealed class InventorySectionRow(InventorySection section):ViewModelBase
{
    public string Header => $"{SectionName(section.Name)} — {Loc.T(section.Status=="Complete"?"Monitor.Text.SourceAvailable":section.Status=="Partial"?"Monitor.Text.Partial":"Monitor.Unavailable")} — {section.Items.Count}";
    internal void RefreshLanguage()=>OnPropertyChanged(nameof(Header));
    public string Detail => section.Detail;
    public IReadOnlyList<InventoryItem> Items => section.Items;
    private static string SectionName(string name) => name switch
    {
        "Processes" => Loc.T("Monitor.Text.SectionProcesses"), "Connections" => Loc.T("Monitor.Text.SectionConnections"), "Startup" => Loc.T("Monitor.Text.SectionStartup"),
        "Services" => Loc.T("Monitor.Text.SectionServices"), "Tasks" => Loc.T("Monitor.Text.SectionTasks"), "Defender" => Loc.T("Monitor.Text.SectionDefender"),
        "Signatures" => Loc.T("Monitor.Text.SectionSignatures"), "Extensions" => Loc.T("Monitor.Text.SectionExtensions"), "Backups" => Loc.T("Monitor.Text.SectionBackups"),
        "InstalledApps" => Loc.T("Monitor.Text.SectionInstalledApps"), "AppUsage" => Loc.T("Monitor.Text.SectionAppUsage"), "Hardware" => Loc.T("Monitor.Text.SectionHardware"), _ => name
    };
}
