using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.App.Shell;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IAppServices _services;
    private readonly Dictionary<string, PageBase> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NavItemViewModel> _navItems = [];
    private readonly DispatcherTimer _sessionTimer;
    private NavItemViewModel? _selectedNav;
    private object? _currentPage;
    private string _currentPageTitle = string.Empty;
    private FusionSnapshot _snapshot;
    private bool _isBusy;
    private bool _isElevated;
    private bool _hasPendingRestore;
    private string _sessionTimeText = "00:00";
    private string _gameNameText = string.Empty;
    private bool _disposed;
    private bool _boostRefreshing;

    public MainViewModel(IAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _snapshot = services.Fusion.Snapshot;
        _isElevated = services.Elevation.IsElevated;
        _hasPendingRestore = services.Journal.HasPendingRestore;

        SelectPageCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavItemViewModel item) SelectPage(item);
        });

        ToggleFusionCommand = new AsyncRelayCommand(ToggleFusionAsync, () => !_isBusy);
        RestartElevatedCommand = new AsyncRelayCommand(RestartElevatedAsync, () => !_isElevated && !_isBusy);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        OpenSettingsCommand = new RelayCommand(() => Navigate("settings"));
        OpenGameBoostCommand = new RelayCommand(() => Navigate("gameboost"));
        RestoreSessionCommand = new AsyncRelayCommand(RestoreSessionAsync, () => _hasPendingRestore && !_isBusy);

        BuildNavigation();
        RefreshShellTexts();

        _services.Fusion.SnapshotChanged += OnFusionSnapshotChanged;
        _services.Fusion.StateChanged += OnFusionStateChanged;
        _services.SettingsChanged += OnSettingsChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        ShellNavigation.NavigateRequested += OnNavigateRequested;

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += async (_, _) => {UpdateSessionTime();await RefreshGameBoostAsync();};
        _sessionTimer.Start();
    }

    // ------------------------------------------------------------------ navigation
    public ObservableCollection<NavGroupViewModel> NavGroups { get; } = [];

    public ICommand SelectPageCommand { get; }
    public AsyncRelayCommand ToggleFusionCommand { get; }
    public AsyncRelayCommand RestartElevatedCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenGameBoostCommand { get; }
    public string GameBoostStateText=>Loc.Instance.IsArabic?_services.GameBoost.Status.DisplayName:_services.GameBoost.Status.State.ToString();
    private async Task RefreshGameBoostAsync()
    {
        if(_boostRefreshing||_disposed)return;
        _boostRefreshing=true;
        try{await _services.GameBoost.RefreshAsync(default);if(!_disposed)OnPropertyChanged(nameof(GameBoostStateText));}
        catch{ /* keep the last state; the session page reports recovery errors */ }
        finally{_boostRefreshing=false;}
    }
    public AsyncRelayCommand RestoreSessionCommand { get; }

    public event EventHandler? PageChanged;

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        private set => SetProperty(ref _currentPageTitle, value);
    }

    public string CurrentPageTitleUpper => CurrentPageTitle.ToUpperInvariant();

    // ------------------------------------------------------------------ fusion state
    public FusionModeState FusionState => _snapshot.State;

    public bool IsFusionActive => _snapshot.State is FusionModeState.Active or FusionModeState.Arming;

    public bool IsFusionBusy => _snapshot.State is FusionModeState.Arming or FusionModeState.Restoring;

    public double RingProgress => IsFusionActive ? 1d : 0d;

    public string FusionStateText
    {
        get
        {
            var game = _snapshot.Game?.Name;
            return _snapshot.State switch
            {
                FusionModeState.Active when !string.IsNullOrWhiteSpace(game) => Loc.Format("Fusion.GameRunning", game),
                FusionModeState.Active => Loc.T("Fusion.Active"),
                FusionModeState.Arming => Loc.T("Fusion.Arming"),
                FusionModeState.Restoring => Loc.T("Fusion.Restoring"),
                FusionModeState.Error => Loc.T("Fusion.ErrorState"),
                _ => Loc.T("Fusion.Inactive")
            };
        }
    }

    public string FusionButtonText => IsFusionActive ? Loc.T("Fusion.Disengage") : Loc.T("Fusion.Engage");

    public string FusionPillText => _snapshot.State switch
    {
        FusionModeState.Active => Loc.T("Fusion.ProtectedTitle"),
        FusionModeState.Arming => Loc.T("Fusion.ArmingTitle"),
        FusionModeState.Restoring => Loc.T("Fusion.RestoringTitle"),
        FusionModeState.Error => Loc.T("Fusion.ErrorTitle"),
        _ => Loc.T("Fusion.OffTitle")
    };

    public string FusionPillTooltip => FusionStateText;

    public string GameNameText
    {
        get => _gameNameText;
        private set => SetProperty(ref _gameNameText, value);
    }

    public string SessionTimeText
    {
        get => _sessionTimeText;
        private set => SetProperty(ref _sessionTimeText, value);
    }

    public string HotkeyHint => Loc.Format("Fusion.HotkeyHint", _services.Settings.Hotkey);

    public string FusionStatsText => Loc.Format("Fusion.SessionCount", _snapshot.ActiveFirewallRules, _snapshot.PausedServices, _snapshot.AppliedTweaks);

    // ------------------------------------------------------------------ elevation / shell
    public bool IsElevated => _isElevated;

    public bool IsLimitedMode => !_isElevated;

    public string ElevationText => _isElevated ? Loc.T("Elevation.Elevated") : Loc.T("Elevation.NotElevated");

    public string VersionText => Loc.Format("App.Version", _services.SystemInfo.AppVersion);

    public string LanguageToggleText => Loc.Instance.IsArabic ? "EN" : "ع";

    public string LanguageTooltip => Loc.T("TitleBar.SwitchLanguage");

    public bool HasPendingRestore
    {
        get => _hasPendingRestore;
        private set
        {
            if (SetProperty(ref _hasPendingRestore, value)) RestoreSessionCommand.RaiseCanExecuteChanged();
        }
    }

    public string PendingRestoreText => Loc.Format("Shell.PendingRestore", _services.Journal.Entries.Count);

    // ------------------------------------------------------------------ actions
    public void Navigate(string pageKey)
    {
        var item = _navItems.FirstOrDefault(nav => string.Equals(nav.Key, pageKey, StringComparison.OrdinalIgnoreCase));
        if (item is not null) SelectPage(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionTimer.Stop();
        _services.Fusion.SnapshotChanged -= OnFusionSnapshotChanged;
        _services.Fusion.StateChanged -= OnFusionStateChanged;
        _services.SettingsChanged -= OnSettingsChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        ShellNavigation.NavigateRequested -= OnNavigateRequested;

        foreach (var page in _pages.Values)
        {
            try
            {
                page.OnDeactivated();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Deactivate failed: {ex.Message}");
            }
        }
    }

    private async Task ToggleFusionAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        ToggleFusionCommand.RaiseCanExecuteChanged();

        try
        {
            OperationResult result;
            if (IsFusionActive)
            {
                result = await _services.Fusion.DisengageAsync().ConfigureAwait(true);
                if (result.Success) ToastService.Instance.ShowLocalized("Fusion.Disengaged", ToastKind.Success);
            }
            else
            {
                result = await _services.Fusion.EngageAsync(manual: true).ConfigureAwait(true);
                if (result.Success) ToastService.Instance.ShowLocalized("Fusion.ManualEngaged", ToastKind.Success);
            }

            if (!result.Success)
            {
                ToastService.Instance.Show(result.Message, ToastKind.Warning);
                ApplySnapshot(_services.Fusion.Snapshot);
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Shell] Fusion toggle failed: {ex}");
        }
        finally
        {
            _isBusy = false;
            ToggleFusionCommand.RaiseCanExecuteChanged();
            RestartElevatedCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RestartElevatedAsync()
    {
        if (_isElevated) return;

        try
        {
            if (IsFusionActive) await _services.Fusion.DisengageAsync().ConfigureAwait(true);
            _services.SaveSettings();
            // A successful restart requests the normal exit pipeline itself (ElevationService -> ExitRequested).
            if (!_services.Elevation.TryRestartElevated())
                ToastService.Instance.ShowLocalized("Elevation.Cancelled", ToastKind.Warning);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    private async Task RestoreSessionAsync()
    {
        try
        {
            var result = await _services.Fusion.RestoreInterruptedSessionAsync().ConfigureAwait(true);
            ToastService.Instance.Show(result.Message, result.Success ? ToastKind.Success : ToastKind.Warning);
            HasPendingRestore = _services.Journal.HasPendingRestore;
            ApplySnapshot(_services.Fusion.Snapshot);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    private void ToggleLanguage()
    {
        // Fires Loc.Persist; the shell stores the choice for the next start.
        Loc.Instance.Toggle();
        OnPropertyChanged(nameof(LanguageToggleText));
    }

    // ------------------------------------------------------------------ navigation internals
    private void BuildNavigation()
    {
        var pages = typeof(MainViewModel).Assembly.GetTypes()
            .Where(type => !type.IsAbstract &&
                           typeof(PageBase).IsAssignableFrom(type) &&
                           type.GetCustomAttributes(typeof(PageInfoAttribute), inherit: false).Length > 0)
            .Select(type => (Type: type, Info: (PageInfoAttribute)type.GetCustomAttributes(typeof(PageInfoAttribute), inherit: false)[0]))
            .OrderBy(pair => pair.Info.Order)
            .ToArray();

        foreach (var group in pages.GroupBy(pair => pair.Info.GroupKey))
        {
            var groupModel = new NavGroupViewModel(group.Key);
            foreach (var (type, info) in group)
            {
                var item = new NavItemViewModel(this, info.Key, info.TitleKey, info.Glyph, type);
                groupModel.Items.Add(item);
                _navItems.Add(item);
            }

            NavGroups.Add(groupModel);
        }

        if (_navItems.Count > 0) SelectPage(_navItems[0], force: true);
    }

    private void SelectPage(NavItemViewModel item, bool force = false)
    {
        if (!force && ReferenceEquals(_selectedNav, item)) return;

        if (_selectedNav is not null)
        {
            _selectedNav.SetSelectedSilently(false);
            if (_pages.TryGetValue(_selectedNav.Key, out var previous)) SafeDeactivate(previous);
        }

        _selectedNav = item;
        item.SetSelectedSilently(true);

        if (!_pages.TryGetValue(item.Key, out var page))
        {
            try
            {
                page = (PageBase)Activator.CreateInstance(item.PageType)!;
                _pages[item.Key] = page;
            }
            catch (Exception ex)
            {
                ToastService.Instance.Show($"{Loc.T("Common.Error")}: {ex.Message}", ToastKind.Error);
                return;
            }
        }

        CurrentPage = page;
        CurrentPageTitle = Loc.T(item.TitleKey);
        OnPropertyChanged(nameof(CurrentPageTitleUpper));
        PageChanged?.Invoke(this, EventArgs.Empty);
        _ = SafeActivateAsync(page);
    }

    private async Task SafeActivateAsync(PageBase page)
    {
        try
        {
            await page.OnActivatedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"{Loc.T("Common.Error")}: {ex.Message}", ToastKind.Error);
        }
    }

    private void SafeDeactivate(PageBase page)
    {
        try
        {
            page.OnDeactivated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shell] Page deactivate failed: {ex.Message}");
        }
    }

    private void OnNavigateRequested(string pageKey) => Navigate(pageKey);

    // ------------------------------------------------------------------ event handlers
    private void OnFusionSnapshotChanged(object? sender, FusionSnapshot snapshot) =>
        UiDispatcher.Post(() => ApplySnapshot(snapshot));

    private void OnFusionStateChanged(object? sender, FusionStateChangedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Message))
            UiDispatcher.Post(() => ToastService.Instance.Show(args.Message, ToastKind.Info));
    }

    private void ApplySnapshot(FusionSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(FusionState));
        OnPropertyChanged(nameof(IsFusionActive));
        OnPropertyChanged(nameof(IsFusionBusy));
        OnPropertyChanged(nameof(RingProgress));
        OnPropertyChanged(nameof(FusionStateText));
        OnPropertyChanged(nameof(FusionButtonText));
        OnPropertyChanged(nameof(FusionPillText));
        OnPropertyChanged(nameof(FusionPillTooltip));
        OnPropertyChanged(nameof(FusionStatsText));
        GameNameText = snapshot.Game?.Name ?? Loc.T("Fusion.NoGame");
        UpdateSessionTime();
    }

    private void UpdateSessionTime()
    {
        SessionTimeText = _snapshot.ArmedAtUtc is { } armedAt
            ? Humanize.Duration(DateTimeOffset.UtcNow - armedAt)
            : "00:00";
    }

    private void OnSettingsChanged(object? sender, FusionSettings settings)
    {
        UiDispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(HotkeyHint));
            _isElevated = _services.Elevation.IsElevated;
            OnPropertyChanged(nameof(IsElevated));
            OnPropertyChanged(nameof(IsLimitedMode));
            OnPropertyChanged(nameof(ElevationText));
            HasPendingRestore = _services.Journal.HasPendingRestore;

            foreach (var page in _pages.Values)
            {
                try
                {
                    page.OnSettingsChanged(settings);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Shell] Page settings handler failed: {ex.Message}");
                }
            }
        });
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiDispatcher.Post(() =>
        {
            foreach (var group in NavGroups) group.RefreshTitle();
            foreach (var item in _navItems) item.RefreshTitle();
            CurrentPageTitle = _selectedNav is null ? string.Empty : Loc.T(_selectedNav.TitleKey);
            OnPropertyChanged(nameof(CurrentPageTitleUpper));
            RefreshShellTexts();

            foreach (var page in _pages.Values) page.OnLanguageChanged();
        });
    }

    private void RefreshShellTexts()
    {
        OnPropertyChanged(nameof(FusionStateText));
        OnPropertyChanged(nameof(FusionButtonText));
        OnPropertyChanged(nameof(FusionPillText));
        OnPropertyChanged(nameof(FusionPillTooltip));
        OnPropertyChanged(nameof(FusionStatsText));
        OnPropertyChanged(nameof(HotkeyHint));
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(LanguageToggleText));
        OnPropertyChanged(nameof(LanguageTooltip));
        OnPropertyChanged(nameof(PendingRestoreText));
        GameNameText = _snapshot.Game?.Name ?? Loc.T("Fusion.NoGame");
    }
}

public sealed class NavGroupViewModel(string groupKey) : ViewModelBase
{
    public string GroupKey { get; } = groupKey;

    public string Title => Loc.T(GroupKey);

    public ObservableCollection<NavItemViewModel> Items { get; } = [];

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public sealed class NavItemViewModel : ViewModelBase
{
    private readonly MainViewModel _owner;
    private bool _isSelected;

    public NavItemViewModel(MainViewModel owner, string key, string titleKey, string glyph, Type pageType)
    {
        _owner = owner;
        Key = key;
        TitleKey = titleKey;
        Glyph = glyph;
        PageType = pageType;
    }

    public string Key { get; }
    public string TitleKey { get; }
    public string Glyph { get; }
    public Type PageType { get; }

    public string Title => Loc.T(TitleKey);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            // The sidebar item is a ToggleButton; a second click must not visually deselect the
            // page that is still on screen.
            if (!value)
            {
                if (_isSelected) OnPropertyChanged(nameof(IsSelected));
                return;
            }

            if (!SetProperty(ref _isSelected, true)) return;
            _owner.SelectPageCommand.Execute(this);
        }
    }

    internal void SetSelectedSilently(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}
