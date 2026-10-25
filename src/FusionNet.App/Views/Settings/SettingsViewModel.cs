using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.App.Shell;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Settings;

/// <summary>
/// Settings: appearance, startup behaviour, global hotkey, Fusion Mode defaults,
/// Windows services, data management and the about card.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private bool _isActivated;
    private bool _isSubscribed;
    private bool _suppressCommit;
    private bool _startWithWindows;
    private string _hotkeyDisplay = string.Empty;
    private string _hotkeyError = string.Empty;
    private bool _isRecording;
    private string _journalSessionShort = "—";
    private int _journalEntryCount;
    private string _journalStateLabel = string.Empty;
    private string _journalStateKey = "idle";
    private bool _hasPendingRestore;

    public SettingsViewModel(IAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Accents = [];
        Services = [];

        SelectAccentCommand = new RelayCommand(parameter =>
        {
            if (parameter is AccentOption option) SelectAccent(option);
        });
        RecordHotkeyCommand = new RelayCommand(BeginRecording);
        CancelRecordCommand = new RelayCommand(CancelRecording);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync);
        ResetAllCommand = new AsyncRelayCommand(ResetAllAsync);
        RestartElevatedCommand = new RelayCommand(RestartElevated);
        OpenActivityCommand = new RelayCommand(() => ShellNavigation.Go("activity"));

        _journalStateLabel = Loc.T("Set.JournalIdle");
        _hotkeyDisplay = ParseHotkeyDisplay();
    }

    // ------------------------------------------------------------------ collections / commands
    public ObservableCollection<AccentOption> Accents { get; }

    public ObservableCollection<ServiceOption> Services { get; }

    public RelayCommand SelectAccentCommand { get; }

    public RelayCommand RecordHotkeyCommand { get; }

    public RelayCommand CancelRecordCommand { get; }

    public RelayCommand OpenDataFolderCommand { get; }

    public AsyncRelayCommand ExportSettingsCommand { get; }

    public AsyncRelayCommand ResetAllCommand { get; }

    public RelayCommand RestartElevatedCommand { get; }

    public RelayCommand OpenActivityCommand { get; }

    // ------------------------------------------------------------------ appearance
    public bool IsArabicSelected
    {
        get => Loc.Instance.IsArabic;
        set
        {
            if (value) SetLanguage("ar");
            else OnPropertyChanged();
        }
    }

    public bool IsEnglishSelected
    {
        get => !Loc.Instance.IsArabic;
        set
        {
            if (value) SetLanguage("en");
            else OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ startup & behaviour
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            try
            {
                var result = _services.AutoStart.SetEnabled(value);
                if (!result.Success)
                {
                    ToastService.Instance.Show(Loc.Format("Set.AutoStartFailed", result.Message), ToastKind.Error);
                    OnPropertyChanged();
                    return;
                }

                _startWithWindows = _services.AutoStart.IsEnabled;
                _services.Settings.StartWithWindows = _startWithWindows;
                Commit();
                OnPropertyChanged();
            }
            catch (Exception ex)
            {
                ToastService.Instance.Show(Loc.Format("Set.AutoStartFailed", ex.Message), ToastKind.Error);
                OnPropertyChanged();
                System.Diagnostics.Debug.WriteLine($"[Settings] autostart failed: {ex}");
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _services.Settings.MinimizeToTray;
        set
        {
            if (_services.Settings.MinimizeToTray == value) return;
            _services.Settings.MinimizeToTray = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool ShowNotifications
    {
        get => _services.Settings.ShowNotifications;
        set
        {
            if (_services.Settings.ShowNotifications == value) return;
            _services.Settings.ShowNotifications = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool ConfirmExitWhileActive
    {
        get => _services.Settings.ConfirmExitWhileActive;
        set
        {
            if (_services.Settings.ConfirmExitWhileActive == value) return;
            _services.Settings.ConfirmExitWhileActive = value;
            Commit();
            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ fusion defaults
    public bool EnableFirewallBlocking
    {
        get => _services.Settings.EnableFirewallBlocking;
        set
        {
            if (_services.Settings.EnableFirewallBlocking == value) return;
            _services.Settings.EnableFirewallBlocking = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool PauseWindowsDownloadServices
    {
        get => _services.Settings.PauseWindowsDownloadServices;
        set
        {
            if (_services.Settings.PauseWindowsDownloadServices == value) return;
            _services.Settings.PauseWindowsDownloadServices = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool BoostGameProcess
    {
        get => _services.Settings.BoostGameProcess;
        set
        {
            if (_services.Settings.BoostGameProcess == value) return;
            _services.Settings.BoostGameProcess = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool SwitchPowerPlan
    {
        get => _services.Settings.SwitchPowerPlan;
        set
        {
            if (_services.Settings.SwitchPowerPlan == value) return;
            _services.Settings.SwitchPowerPlan = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool ApplyLatencyTweaks
    {
        get => _services.Settings.ApplyLatencyTweaks;
        set
        {
            if (_services.Settings.ApplyLatencyTweaks == value) return;
            _services.Settings.ApplyLatencyTweaks = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool FlushDnsOnEngage
    {
        get => _services.Settings.FlushDnsOnEngage;
        set
        {
            if (_services.Settings.FlushDnsOnEngage == value) return;
            _services.Settings.FlushDnsOnEngage = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool PurgeStandbyMemoryOnEngage
    {
        get => _services.Settings.PurgeStandbyMemoryOnEngage;
        set
        {
            if (_services.Settings.PurgeStandbyMemoryOnEngage == value) return;
            _services.Settings.PurgeStandbyMemoryOnEngage = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool SuspendAppsOnlyWhileGameRuns
    {
        get => _services.Settings.SuspendAppsOnlyWhileGameRuns;
        set
        {
            if (_services.Settings.SuspendAppsOnlyWhileGameRuns == value) return;
            _services.Settings.SuspendAppsOnlyWhileGameRuns = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool AutoEngageOnGameStart
    {
        get => _services.Settings.AutoEngageOnGameStart;
        set
        {
            if (_services.Settings.AutoEngageOnGameStart == value) return;
            _services.Settings.AutoEngageOnGameStart = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool RestoreOnGameExit
    {
        get => _services.Settings.RestoreOnGameExit;
        set
        {
            if (_services.Settings.RestoreOnGameExit == value) return;
            _services.Settings.RestoreOnGameExit = value;
            Commit();
            OnPropertyChanged();
        }
    }

    public bool AutoRestoreOnCrash
    {
        get => _services.Settings.AutoRestoreOnCrash;
        set
        {
            if (_services.Settings.AutoRestoreOnCrash == value) return;
            _services.Settings.AutoRestoreOnCrash = value;
            Commit();
            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ hotkey
    public string HotkeyDisplay
    {
        get => _hotkeyDisplay;
        private set => SetProperty(ref _hotkeyDisplay, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set => SetProperty(ref _isRecording, value);
    }

    public string HotkeyError
    {
        get => _hotkeyError;
        private set
        {
            if (!SetProperty(ref _hotkeyError, value)) return;
            OnPropertyChanged(nameof(HasHotkeyError));
        }
    }

    public bool HasHotkeyError => !string.IsNullOrWhiteSpace(_hotkeyError);

    // ------------------------------------------------------------------ data & journal
    public string DataDirectory => AppPaths.DataDirectory;

    public string JournalSessionShort
    {
        get => _journalSessionShort;
        private set => SetProperty(ref _journalSessionShort, value);
    }

    public int JournalEntryCount
    {
        get => _journalEntryCount;
        private set => SetProperty(ref _journalEntryCount, value);
    }

    public string JournalStateLabel
    {
        get => _journalStateLabel;
        private set => SetProperty(ref _journalStateLabel, value);
    }

    public string JournalStateKey
    {
        get => _journalStateKey;
        private set => SetProperty(ref _journalStateKey, value);
    }

    public bool HasPendingRestore
    {
        get => _hasPendingRestore;
        private set => SetProperty(ref _hasPendingRestore, value);
    }

    // ------------------------------------------------------------------ about
    public string ProductName => AppPaths.ProductName;

    public string VersionText => Loc.Format("Set.Version", SafeSystemInfo(info => info.AppVersion, "3.3.3"));

    public string WindowsText => SafeSystemInfo(info => info.WindowsVersion, Loc.T("Common.Unknown"));

    public string CpuText => SafeSystemInfo(info => info.CpuName, Loc.T("Common.Unknown"));

    public string RamText => Humanize.Bytes(SafeSystemInfoLong(info => info.TotalPhysicalMemoryBytes, 0));

    public string UptimeText
    {
        get
        {
            try
            {
                return Humanize.Duration(_services.SystemInfo.Uptime);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] uptime failed: {ex}");
                return "—";
            }
        }
    }

    public string MachineName => SafeSystemInfo(info => info.MachineName, Environment.MachineName);

    public bool IsElevated => SafeElevated();

    public string ElevationLabel => Loc.T(IsElevated ? "Elevation.Elevated" : "Elevation.NotElevated");

    public string ElevationKey => IsElevated ? "elevated" : "limited";

    // ------------------------------------------------------------------ lifecycle
    public Task ActivateAsync()
    {
        _isActivated = true;
        if (!_isSubscribed)
        {
            _isSubscribed = true;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
        }

        RefreshFromSettings(rebuildLists: true);
        return Task.CompletedTask;
    }

    public void Deactivate()
    {
        _isActivated = false;
        if (_isSubscribed)
        {
            _isSubscribed = false;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }

    public void OnSettingsChanged(FusionSettings settings)
    {
        if (!_isActivated) return;
        RefreshFromSettings(rebuildLists: false);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UiDispatcher.Post(ApplyLocalization);

    // ------------------------------------------------------------------ refresh
    private void RefreshFromSettings(bool rebuildLists)
    {
        _suppressCommit = true;
        try
        {
            if (rebuildLists)
            {
                BuildAccents();
                BuildServices();
            }

            _startWithWindows = SafeAutoStartEnabled();
            HotkeyDisplay = ParseHotkeyDisplay();
            HotkeyError = string.Empty;
            RefreshJournalStatus();
        }
        finally
        {
            _suppressCommit = false;
        }

        RaiseAll(
            nameof(StartWithWindows),
            nameof(MinimizeToTray),
            nameof(ShowNotifications),
            nameof(ConfirmExitWhileActive),
            nameof(EnableFirewallBlocking),
            nameof(PauseWindowsDownloadServices),
            nameof(BoostGameProcess),
            nameof(SwitchPowerPlan),
            nameof(ApplyLatencyTweaks),
            nameof(FlushDnsOnEngage),
            nameof(PurgeStandbyMemoryOnEngage),
            nameof(SuspendAppsOnlyWhileGameRuns),
            nameof(AutoEngageOnGameStart),
            nameof(RestoreOnGameExit),
            nameof(AutoRestoreOnCrash),
            nameof(IsArabicSelected),
            nameof(IsEnglishSelected),
            nameof(DataDirectory),
            nameof(ProductName),
            nameof(VersionText),
            nameof(WindowsText),
            nameof(CpuText),
            nameof(RamText),
            nameof(UptimeText),
            nameof(MachineName),
            nameof(IsElevated),
            nameof(ElevationLabel),
            nameof(ElevationKey),
            nameof(HasPendingRestore));
    }

    private void ApplyLocalization()
    {
        _suppressCommit = true;
        try
        {
            BuildAccents();
            BuildServices();
            RefreshJournalStatus();
        }
        finally
        {
            _suppressCommit = false;
        }

        RaiseAll(
            nameof(VersionText),
            nameof(WindowsText),
            nameof(CpuText),
            nameof(ElevationLabel),
            nameof(IsArabicSelected),
            nameof(IsEnglishSelected));
    }

    private void BuildAccents()
    {
        Accents.Clear();
        foreach (var id in ThemeService.Accents)
        {
            var labelKey = id switch
            {
                "violet" => "Set.AccentViolet",
                "emerald" => "Set.AccentEmerald",
                "amber" => "Set.AccentAmber",
                "rose" => "Set.AccentRose",
                _ => "Set.AccentCyan"
            };

            var selected = string.Equals(id, _services.Settings.Accent, StringComparison.OrdinalIgnoreCase);
            Accents.Add(new AccentOption(id, Loc.T(labelKey), CreateSwatchBrush(id), selected));
        }
    }

    private void BuildServices()
    {
        Services.Clear();
        var arabic = Loc.Instance.IsArabic;
        var paused = _services.Settings.PausedServiceIds;
        foreach (var definition in ServiceCatalog.All)
        {
            var selected = paused.Contains(definition.Id, StringComparer.OrdinalIgnoreCase);
            Services.Add(new ServiceOption(definition, selected, arabic, OnServiceToggled));
        }
    }

    private void OnServiceToggled(ServiceDefinition definition, bool selected)
    {
        if (_suppressCommit) return;

        var ids = _services.Settings.PausedServiceIds;
        if (selected)
        {
            if (!ids.Contains(definition.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(definition.Id);
        }
        else
        {
            ids.RemoveAll(id => string.Equals(id, definition.Id, StringComparison.OrdinalIgnoreCase));
        }

        _services.SaveSettings();
    }

    // ------------------------------------------------------------------ appearance actions
    private void SelectAccent(AccentOption option)
    {
        try
        {
            ThemeService.Apply(option.Id);
            _services.Settings.Accent = option.Id;
            _services.SaveSettings();
            foreach (var accent in Accents) accent.IsSelected = ReferenceEquals(accent, option);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] accent failed: {ex}");
        }
    }

    private void SetLanguage(string language)
    {
        try
        {
            // persist:true lets the shell store the preference for the next start.
            Loc.Instance.SetLanguage(language);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] language failed: {ex}");
        }
        finally
        {
            OnPropertyChanged(nameof(IsArabicSelected));
            OnPropertyChanged(nameof(IsEnglishSelected));
        }
    }

    // ------------------------------------------------------------------ hotkey actions
    public void BeginRecording()
    {
        HotkeyError = string.Empty;
        IsRecording = true;
    }

    public void CancelRecording()
    {
        IsRecording = false;
        HotkeyError = string.Empty;
        ToastService.Instance.Show(Loc.T("Set.HotkeyCancelled"), ToastKind.Info);
    }

    public void ReportHotkeyError(string message)
    {
        HotkeyError = message;
        IsRecording = true;
    }

    /// <summary>Registers the captured chord; keeps the previous hotkey when registration fails.</summary>
    public void ApplyHotkey(HotkeyGesture gesture, Window? owner)
    {
        if (owner is null)
        {
            ReportHotkeyError(Loc.T("Set.HotkeyNoWindow"));
            return;
        }

        try
        {
            if (HotkeyService.Instance.Register(owner, gesture, out var registerError))
            {
                _services.Settings.Hotkey = gesture.ToString();
                _services.SaveSettings();
                HotkeyDisplay = gesture.DisplayString;
                IsRecording = false;
                HotkeyError = string.Empty;
                ToastService.Instance.Show(Loc.T("Set.HotkeySaved"), ToastKind.Success);
                return;
            }

            // Restore the previous registration so the old hotkey keeps working.
            if (HotkeyGesture.TryParse(_services.Settings.Hotkey, out var previous))
            {
                HotkeyService.Instance.Register(owner, previous, out _);
            }

            ReportHotkeyError(string.IsNullOrWhiteSpace(registerError)
                ? Loc.T("Set.HotkeyFailed")
                : registerError);
        }
        catch (Exception ex)
        {
            ReportHotkeyError(Loc.T("Set.HotkeyFailed"));
            System.Diagnostics.Debug.WriteLine($"[Settings] hotkey registration failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ data actions
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = DataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Set.OpenFolderFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Settings] open folder failed: {ex}");
        }
    }

    private async Task ExportSettingsAsync()
    {
        try
        {
            var path = _services.Files.PickSavePath("fusionnet-settings.json", Loc.T("Dialog.JsonFilter"));
            if (string.IsNullOrWhiteSpace(path)) return;

            var json = FusionJson.Serialize(_services.Settings);
            await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ToastService.Instance.Show(Loc.T("Set.ExportDone"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Set.ExportFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Settings] export failed: {ex}");
        }
    }

    private async Task ResetAllAsync()
    {
        if (!Confirm(Loc.T("Set.ResetTitle"), Loc.T("Set.ResetBody"))) return;

        try
        {
            _services.SettingsStore.Reset();
            _services.ReloadSettings();
            ThemeService.Apply(_services.Settings.Accent);
            // Resetting settings also resets the UI language to the product default (Arabic).
            Loc.Instance.SetLanguage(UiLanguagePreference.DefaultLanguage, persist: true);
            RefreshFromSettings(rebuildLists: true);
            _services.SaveSettings();
            ToastService.Instance.Show(Loc.T("Set.ResetDone"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Set.ResetFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Settings] reset failed: {ex}");
        }

        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ helpers
    private void Commit()
    {
        if (_suppressCommit) return;
        _services.SaveSettings();
    }

    private string ParseHotkeyDisplay() =>
        HotkeyGesture.TryParse(_services.Settings.Hotkey, out var gesture)
            ? gesture.DisplayString
            : _services.Settings.Hotkey;

    private void RefreshJournalStatus()
    {
        try
        {
            var document = _services.Journal.Document;
            var entries = _services.Journal.Entries;

            var sessionId = document?.SessionId ?? string.Empty;
            JournalSessionShort = string.IsNullOrWhiteSpace(sessionId)
                ? "—"
                : sessionId[..Math.Min(8, sessionId.Length)].ToUpperInvariant();
            JournalEntryCount = entries?.Count ?? 0;

            var active = document?.Active == true;
            JournalStateKey = active ? "active" : "idle";
            JournalStateLabel = Loc.T(active ? "Set.JournalRecording" : "Set.JournalIdle");
            HasPendingRestore = _services.Journal.HasPendingRestore;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] journal status failed: {ex}");
        }
    }

    private bool SafeAutoStartEnabled()
    {
        try
        {
            return _services.AutoStart.IsEnabled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] autostart probe failed: {ex}");
            return _services.Settings.StartWithWindows;
        }
    }

    private bool SafeElevated()
    {
        try
        {
            return _services.Elevation.IsElevated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] elevation probe failed: {ex}");
            return false;
        }
    }

    private string SafeSystemInfo(Func<ISystemInfoService, string> read, string fallback)
    {
        try
        {
            return read(_services.SystemInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] system info failed: {ex}");
            return fallback;
        }
    }

    private long SafeSystemInfoLong(Func<ISystemInfoService, long> read, long fallback)
    {
        try
        {
            return read(_services.SystemInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] system info failed: {ex}");
            return fallback;
        }
    }

    private void RestartElevated()
    {
        if (_services.Elevation.TryRestartElevated()) return;
        ToastService.Instance.Show(Loc.T("Set.ElevationCancelled"), ToastKind.Info);
    }

    private static bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var answer = owner is not null
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    private static Brush CreateSwatchBrush(string accentId)
    {
        var resourceKey = accentId switch
        {
            "violet" => "AccentPreset.Violet",
            "emerald" => "AccentPreset.Emerald",
            "amber" => "AccentPreset.Amber",
            "rose" => "AccentPreset.Rose",
            _ => "AccentPreset.Cyan"
        };

        var color = Application.Current?.TryFindResource(resourceKey) as Color? ?? Colors.Cyan;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}

/// <summary>One accent swatch on the appearance card.</summary>
public sealed class AccentOption : ViewModelBase
{
    private bool _isSelected;

    public AccentOption(string id, string label, Brush brush, bool selected)
    {
        Id = id;
        Label = label;
        Brush = brush;
        _isSelected = selected;
    }

    public string Id { get; }

    public string Label { get; }

    public Brush Brush { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>One Windows service row with a persisted pause selection.</summary>
public sealed class ServiceOption : ViewModelBase
{
    private readonly Action<ServiceDefinition, bool> _onToggled;
    private bool _isSelected;

    public ServiceOption(ServiceDefinition definition, bool selected, bool arabic,
        Action<ServiceDefinition, bool> onToggled)
    {
        Definition = definition;
        _isSelected = selected;
        _onToggled = onToggled;
        Name = arabic ? definition.NameAr : definition.NameEn;
        Description = arabic ? definition.DescriptionAr : definition.DescriptionEn;
    }

    public ServiceDefinition Definition { get; }

    public string ServiceName => Definition.ServiceName;

    public string Name { get; }

    public string Description { get; }

    public bool IsRecommended => Definition.Recommended;

    public bool IsAdvanced => Definition.Advanced;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            _onToggled(Definition, value);
        }
    }
}


