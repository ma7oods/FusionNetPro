using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.App.Styles;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.AppShield;

/// <summary>
/// App Shield: every managed application grouped by category, with a four-way policy
/// per app (block / suspend / throttle / ignore), live running state, custom
/// executables and an elevation-aware firewall notice.
/// </summary>
public sealed class AppShieldViewModel : ViewModelBase
{
    private const string AllCategories = "*";

    private readonly DispatcherTimer _statusTimer;
    private readonly List<ShieldAppItem> _rows = [];
    private readonly HashSet<string> _runningNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suspendedNames = new(StringComparer.OrdinalIgnoreCase);

    private bool _subscribed;
    private bool _languageSubscribed;
    private bool _isPolling;
    private bool _refreshingFusion;
    private string _searchText = string.Empty;
    private string _selectedCategory = AllCategories;
    private bool _runningOnly;
    private bool _isElevated;
    private bool _isArmed;
    private bool _hasGroups;
    private bool _hasCustomApps;
    private int _activeCount;
    private int _totalCount;
    private string _summaryText = string.Empty;
    private string _lastUpdated = string.Empty;
    private AppControlAction _newCustomAction = AppControlAction.BlockNetwork;

    public AppShieldViewModel(IAppServices services)
    {
        S = services ?? throw new ArgumentNullException(nameof(services));

        AddCustomAppCommand = new RelayCommand(AddCustomApp);
        RefreshStatusCommand = new RelayCommand(() => _ = RefreshProcessStatesAsync());
        RestartElevatedCommand = new RelayCommand(RestartElevated);

        // The running/suspended badges are polled while the page is visible only.
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _statusTimer.Tick += (_, _) => _ = RefreshProcessStatesAsync();

        RebuildActionOptions();
        RebuildCategories();
        RebuildRows();
        RebuildCustomApps();
        RebuildGroups();
        UpdateSummary();
    }

    private IAppServices S { get; }

    // ---------------------------------------------------------------- commands

    public RelayCommand AddCustomAppCommand { get; }
    public RelayCommand RefreshStatusCommand { get; }
    public RelayCommand RestartElevatedCommand { get; }

    // ---------------------------------------------------------------- collections

    public ObservableCollection<ShieldCategoryChip> Categories { get; } = [];
    public ObservableCollection<ShieldGroupItem> Groups { get; } = [];
    public ObservableCollection<ShieldCustomAppItem> CustomApps { get; } = [];
    public ObservableCollection<ShieldActionOption> ActionOptions { get; } = [];

    // ---------------------------------------------------------------- header summary

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public int ActiveCount => _activeCount;

    public int TotalCount => _totalCount;

    public bool IsElevated
    {
        get => _isElevated;
        private set => SetProperty(ref _isElevated, value);
    }

    public bool IsArmed
    {
        get => _isArmed;
        private set => SetProperty(ref _isArmed, value);
    }

    public bool HasGroups
    {
        get => _hasGroups;
        private set => SetProperty(ref _hasGroups, value);
    }

    public bool HasCustomApps
    {
        get => _hasCustomApps;
        private set => SetProperty(ref _hasCustomApps, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }

    // ---------------------------------------------------------------- filters

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            RebuildGroups();
        }
    }

    public bool RunningOnly
    {
        get => _runningOnly;
        set
        {
            if (!SetProperty(ref _runningOnly, value)) return;
            RebuildGroups();
        }
    }

    public AppControlAction NewCustomAction
    {
        get => _newCustomAction;
        set
        {
            if (value == AppControlAction.Ignore) value = AppControlAction.BlockNetwork;
            SetProperty(ref _newCustomAction, value);
        }
    }

    // ---------------------------------------------------------------- lifecycle

    public Task ActivateAsync()
    {
        if (!_subscribed)
        {
            S.Fusion.StateChanged += OnFusionStateChanged;
            _subscribed = true;
        }

        if (!_languageSubscribed)
        {
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            _languageSubscribed = true;
        }

        IsElevated = S.Elevation.IsElevated;
        IsArmed = S.Fusion.State is FusionModeState.Active or FusionModeState.Arming;

        RebuildActionOptions();
        RebuildCategories();
        RebuildRows();
        RebuildCustomApps();
        RebuildGroups();
        UpdateSummary();

        _statusTimer.Start();
        _ = RefreshProcessStatesAsync();
        return Task.CompletedTask;
    }

    public void Deactivate()
    {
        if (_subscribed)
        {
            S.Fusion.StateChanged -= OnFusionStateChanged;
            _subscribed = false;
        }

        if (_languageSubscribed)
        {
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            _languageSubscribed = false;
        }

        _statusTimer.Stop();
    }

    public void OnSettingsChanged(FusionSettings settings) =>
        UiDispatcher.Post(() =>
        {
            SyncFromSettings();
            UpdateSummary();
        });

    // ---------------------------------------------------------------- service events

    private void OnFusionStateChanged(object? sender, FusionStateChangedEventArgs e) =>
        UiDispatcher.Post(() => IsArmed = e.State is FusionModeState.Active or FusionModeState.Arming);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        UiDispatcher.Post(() =>
        {
            RebuildActionOptions();
            RebuildCategories();
            RebuildRows();
            RebuildCustomApps();
            RebuildGroups();
            UpdateSummary();
        });

    // ---------------------------------------------------------------- building the list

    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var app in ManagedAppCatalog.All)
        {
            _rows.Add(new ShieldAppItem(app, S.Settings.GetAction(app.Id), ApplyBuiltInAction));
        }

        ApplyProcessStates(_rows);
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add(new ShieldCategoryChip(
            AllCategories, Loc.T("Shield.CategoryAll"), SelectCategory, _selectedCategory == AllCategories));

        foreach (var category in ManagedAppCatalog.Categories)
        {
            Categories.Add(new ShieldCategoryChip(
                category,
                CategoryLabel(category),
                SelectCategory,
                string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void RebuildActionOptions()
    {
        // "Ignore" is intentionally absent: a custom application is switched off with its
        // enable toggle, and Core normalises an ignored action back to BlockNetwork.
        ActionOptions.Clear();
        ActionOptions.Add(new ShieldActionOption(AppControlAction.BlockNetwork, Loc.T("Shield.Block")));
        ActionOptions.Add(new ShieldActionOption(AppControlAction.Suspend, Loc.T("Shield.Suspend")));
        ActionOptions.Add(new ShieldActionOption(AppControlAction.Throttle, Loc.T("Shield.Throttle")));
        OnPropertyChanged(nameof(ActionOptions));
    }

    private void SelectCategory(string category)
    {
        _selectedCategory = category;
        foreach (var chip in Categories)
            chip.IsSelected = string.Equals(chip.Value, category, StringComparison.OrdinalIgnoreCase);

        RebuildGroups();
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        var search = _searchText.Trim();

        foreach (var category in ManagedAppCatalog.Categories)
        {
            if (_selectedCategory != AllCategories &&
                !string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IEnumerable<ShieldAppItem> query = _rows.Where(row =>
                string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase));

            if (search.Length > 0)
            {
                query = query.Where(row =>
                    row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.ProcessNames.Any(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            if (_runningOnly) query = query.Where(row => row.IsRunning);

            var apps = query.ToArray();
            if (apps.Length == 0) continue;

            Groups.Add(new ShieldGroupItem(
                CategoryLabel(category),
                CategoryGlyph(category),
                Loc.Format("Shield.GroupCount", apps.Length),
                apps));
        }

        HasGroups = Groups.Count > 0;
    }

    private void UpdateSummary()
    {
        var builtInActive = ManagedAppCatalog.All.Count(app => S.Settings.GetAction(app.Id).IsEnforced());
        var customActive = S.Settings.CustomApps.Count(app => app.Enabled && app.Action.IsEnforced());

        _activeCount = builtInActive + customActive;
        _totalCount = ManagedAppCatalog.All.Count + S.Settings.CustomApps.Count(app => app.Enabled);

        SummaryText = Loc.Format("Shield.Summary", _activeCount, _totalCount);
        RaiseAll(nameof(ActiveCount), nameof(TotalCount), nameof(SummaryText));
    }

    private void SyncFromSettings()
    {
        foreach (var row in _rows) row.ApplyAction(S.Settings.GetAction(row.Id));

        var settingsApps = S.Settings.CustomApps;
        var sameSet = settingsApps.Count == CustomApps.Count &&
                      settingsApps.All(app => CustomApps.Any(item =>
                          string.Equals(item.Id, app.Id, StringComparison.OrdinalIgnoreCase)));

        if (sameSet)
        {
            foreach (var item in CustomApps)
            {
                var source = settingsApps.FirstOrDefault(app =>
                    string.Equals(app.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                if (source is not null) item.Sync(source);
            }
        }
        else
        {
            RebuildCustomApps();
        }
    }

    // ---------------------------------------------------------------- actions

    private async void ApplyBuiltInAction(ShieldAppItem item, AppControlAction action)
    {
        try
        {
            // Update the row before saving: saving raises SettingsChanged, which may re-sync the list.
            item.ApplyAction(action);
            S.Settings.SetAction(item.Id, action);
            S.SaveSettings();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            return;
        }

        if (!S.Fusion.IsArmed)
        {
            ToastService.Instance.Show(Loc.T("Shield.Applied"), ToastKind.Success);
            return;
        }

        await RefreshFusionAsync();
    }

    private async void ApplyCustomAction(ShieldCustomAppItem item, AppControlAction action)
    {
        try
        {
            var index = IndexOfCustomApp(item.Id);
            if (index < 0) return;

            S.Settings.CustomApps[index] = S.Settings.CustomApps[index] with { Action = action };
            S.SaveSettings();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            return;
        }

        if (!S.Fusion.IsArmed)
        {
            ToastService.Instance.Show(Loc.T("Shield.Applied"), ToastKind.Success);
            return;
        }

        await RefreshFusionAsync();
    }

    private async void SetCustomEnabled(ShieldCustomAppItem item, bool enabled)
    {
        try
        {
            var index = IndexOfCustomApp(item.Id);
            if (index < 0) return;

            S.Settings.CustomApps[index] = S.Settings.CustomApps[index] with { Enabled = enabled };
            S.SaveSettings();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            return;
        }

        if (!S.Fusion.IsArmed) return;
        await RefreshFusionAsync();
    }

    private async void RemoveCustomApp(ShieldCustomAppItem item)
    {
        try
        {
            var index = IndexOfCustomApp(item.Id);
            if (index < 0) return;

            S.Settings.CustomApps.RemoveAt(index);
            S.SaveSettings();
            RebuildCustomApps();
            UpdateSummary();

            ToastService.Instance.Show(Loc.Format("Shield.CustomRemoved", item.Name), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            return;
        }

        if (!S.Fusion.IsArmed) return;
        await RefreshFusionAsync();
    }

    private async Task RefreshFusionAsync()
    {
        if (_refreshingFusion) return;
        _refreshingFusion = true;
        try
        {
            await S.Fusion.RefreshAsync();
            ToastService.Instance.Show(Loc.T("Shield.AppliedArmed"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Shield.RefreshFailed", ex.Message), ToastKind.Warning);
        }
        finally
        {
            _refreshingFusion = false;
        }
    }

    private void RestartElevated()
    {
        try
        {
            if (!S.Elevation.TryRestartElevated())
            {
                ToastService.Instance.Show(Loc.T("Shield.ElevationCancelled"), ToastKind.Warning);
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    // ---------------------------------------------------------------- custom apps

    private void RebuildCustomApps()
    {
        CustomApps.Clear();
        foreach (var app in S.Settings.CustomApps)
        {
            CustomApps.Add(new ShieldCustomAppItem(app, ApplyCustomAction, SetCustomEnabled, RemoveCustomApp));
        }

        ApplyProcessStates(CustomApps);
        HasCustomApps = CustomApps.Count > 0;
    }

    private void AddCustomApp()
    {
        try
        {
            var path = S.Files.PickOpenPath(Loc.T("Dialog.ExeFilter"));
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                ToastService.Instance.Show(Loc.T("Shield.CustomInvalid"), ToastKind.Warning);
                return;
            }

            var full = Path.GetFullPath(path);
            if (S.Settings.CustomApps.Any(app =>
                    string.Equals(app.ExecutablePath, full, StringComparison.OrdinalIgnoreCase)))
            {
                ToastService.Instance.Show(Loc.T("Shield.CustomExists"), ToastKind.Warning);
                return;
            }

            var custom = CustomManagedApp.Create(full, _newCustomAction);
            S.Settings.CustomApps.Add(custom);
            S.SaveSettings();
            RebuildCustomApps();
            UpdateSummary();

            ToastService.Instance.Show(Loc.Format("Shield.CustomAdded", custom.Name), ToastKind.Success);

            if (S.Fusion.IsArmed) _ = RefreshFusionAsync();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    private int IndexOfCustomApp(string id) =>
        S.Settings.CustomApps.FindIndex(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- live process state

    private async Task RefreshProcessStatesAsync()
    {
        if (_isPolling) return;
        _isPolling = true;
        try
        {
            var processes = await Task.Run(() => S.Processes.GetProcesses()).ConfigureAwait(true);

            _runningNames.Clear();
            _suspendedNames.Clear();
            foreach (var process in processes)
            {
                if (string.IsNullOrWhiteSpace(process.ProcessName)) continue;
                _runningNames.Add(process.ProcessName);
                if (process.IsSuspended) _suspendedNames.Add(process.ProcessName);
            }

            ApplyProcessStates(_rows);
            ApplyProcessStates(CustomApps);
            LastUpdated = Loc.Format("Shield.Updated", Humanize.Clock(DateTimeOffset.UtcNow));

            // The "running only" filter depends on live state, so re-apply it when active.
            if (_runningOnly) RebuildGroups();
        }
        catch (Exception)
        {
            // Processes can disappear mid-enumeration; keep the last known state.
            // The activity log already records service failures.
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void ApplyProcessStates(IEnumerable<ShieldAppItem> rows)
    {
        foreach (var row in rows)
        {
            row.UpdateState(
                row.ProcessNames.Any(_runningNames.Contains),
                row.ProcessNames.Any(_suspendedNames.Contains));
        }
    }

    private void ApplyProcessStates(IEnumerable<ShieldCustomAppItem> rows)
    {
        foreach (var row in rows)
        {
            row.UpdateState(
                _runningNames.Contains(row.ProcessName),
                _suspendedNames.Contains(row.ProcessName));
        }
    }

    // ---------------------------------------------------------------- display helpers

    /// <summary>Localises the English category names that come from the frozen Core catalog.</summary>
    public static string CategoryLabel(string category) => category switch
    {
        ManagedAppCatalog.Launchers => Loc.T("Shield.Cat.Launchers"),
        ManagedAppCatalog.CloudSync => Loc.T("Shield.Cat.CloudSync"),
        ManagedAppCatalog.Torrents => Loc.T("Shield.Cat.Torrents"),
        ManagedAppCatalog.Downloads => Loc.T("Shield.Cat.Downloads"),
        ManagedAppCatalog.Browsers => Loc.T("Shield.Cat.Browsers"),
        ManagedAppCatalog.UpdateAgents => Loc.T("Shield.Cat.UpdateAgents"),
        ManagedAppCatalog.RemoteTools => Loc.T("Shield.Cat.RemoteTools"),
        _ => category
    };

    private static string CategoryGlyph(string category) => category switch
    {
        ManagedAppCatalog.Launchers => Glyphs.Game,
        ManagedAppCatalog.CloudSync => Glyphs.Globe,
        ManagedAppCatalog.Torrents => Glyphs.Download,
        ManagedAppCatalog.Downloads => Glyphs.Download,
        ManagedAppCatalog.Browsers => Glyphs.Globe,
        ManagedAppCatalog.UpdateAgents => Glyphs.Refresh,
        ManagedAppCatalog.RemoteTools => Glyphs.Settings,
        _ => Glyphs.Shield
    };
}

/// <summary>One managed application row with its four-way policy control.</summary>
public sealed class ShieldAppItem : ViewModelBase
{
    private readonly Action<ShieldAppItem, AppControlAction> _setAction;
    private AppControlAction _action;
    private bool _isRunning;
    private bool _isSuspended;

    public ShieldAppItem(ManagedApp app, AppControlAction action, Action<ShieldAppItem, AppControlAction> setAction)
    {
        Id = app.Id;
        Name = app.Name;
        Category = app.Category;
        Description = app.Description;
        ProcessNames = app.ProcessNames;
        IsRecommended = app.Recommended;
        _action = action;
        _setAction = setAction;

        BlockCommand = new RelayCommand(() => _setAction(this, AppControlAction.BlockNetwork));
        SuspendCommand = new RelayCommand(() => _setAction(this, AppControlAction.Suspend));
        ThrottleCommand = new RelayCommand(() => _setAction(this, AppControlAction.Throttle));
        IgnoreCommand = new RelayCommand(() => _setAction(this, AppControlAction.Ignore));
    }

    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public IReadOnlyList<string> ProcessNames { get; }
    public string ProcessesText => string.Join(", ", ProcessNames);
    public bool IsRecommended { get; }
    public string RecommendedGlyph => Glyphs.Star;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public RelayCommand BlockCommand { get; }
    public RelayCommand SuspendCommand { get; }
    public RelayCommand ThrottleCommand { get; }
    public RelayCommand IgnoreCommand { get; }

    public AppControlAction Action => _action;
    public bool IsBlock => _action == AppControlAction.BlockNetwork;
    public bool IsSuspend => _action == AppControlAction.Suspend;
    public bool IsThrottle => _action == AppControlAction.Throttle;
    public bool IsIgnore => _action == AppControlAction.Ignore;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool IsSuspended
    {
        get => _isSuspended;
        private set => SetProperty(ref _isSuspended, value);
    }

    public void ApplyAction(AppControlAction action)
    {
        _action = action;
        RaiseActionProps();
    }

    public void RaiseActionProps() =>
        RaiseAll(nameof(Action), nameof(IsBlock), nameof(IsSuspend), nameof(IsThrottle), nameof(IsIgnore));

    public void UpdateState(bool running, bool suspended)
    {
        IsRunning = running;
        IsSuspended = suspended;
    }
}

/// <summary>One user-added application with its policy, enable switch and delete action.</summary>
public sealed class ShieldCustomAppItem : ViewModelBase
{
    private readonly Action<ShieldCustomAppItem, AppControlAction> _setAction;
    private readonly Action<ShieldCustomAppItem, bool> _setEnabled;
    private AppControlAction _action;
    private bool _enabled;
    private bool _isRunning;
    private bool _isSuspended;

    public ShieldCustomAppItem(
        CustomManagedApp app,
        Action<ShieldCustomAppItem, AppControlAction> setAction,
        Action<ShieldCustomAppItem, bool> setEnabled,
        Action<ShieldCustomAppItem> remove)
    {
        Id = app.Id;
        Name = app.Name;
        ExecutablePath = app.ExecutablePath;
        ProcessName = app.ProcessName;
        _action = app.Action == AppControlAction.Ignore ? AppControlAction.BlockNetwork : app.Action;
        _enabled = app.Enabled;
        _setAction = setAction;
        _setEnabled = setEnabled;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Id { get; }
    public string Name { get; }
    public string ExecutablePath { get; }
    public string ProcessName { get; }
    public RelayCommand RemoveCommand { get; }

    public AppControlAction SelectedAction
    {
        get => _action;
        set
        {
            if (value == AppControlAction.Ignore) value = AppControlAction.BlockNetwork;
            if (!SetProperty(ref _action, value)) return;
            _setAction(this, value);
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            _setEnabled(this, value);
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool IsSuspended
    {
        get => _isSuspended;
        private set => SetProperty(ref _isSuspended, value);
    }

    public void Sync(CustomManagedApp app)
    {
        _enabled = app.Enabled;
        _action = app.Action == AppControlAction.Ignore ? AppControlAction.BlockNetwork : app.Action;
        RaiseAll(nameof(Enabled), nameof(SelectedAction));
    }

    public void UpdateState(bool running, bool suspended)
    {
        IsRunning = running;
        IsSuspended = suspended;
    }
}

/// <summary>One category filter chip on the shield page.</summary>
public sealed class ShieldCategoryChip : ViewModelBase
{
    private bool _isSelected;

    public ShieldCategoryChip(string value, string label, Action<string> select, bool selected)
    {
        Value = value;
        Label = label;
        _isSelected = selected;
        SelectCommand = new RelayCommand(() => select(Value));
    }

    public string Value { get; }
    public string Label { get; }
    public RelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>A category group of application rows.</summary>
public sealed class ShieldGroupItem(string title, string glyph, string countText, IReadOnlyList<ShieldAppItem> apps)
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string CountText { get; } = countText;
    public IReadOnlyList<ShieldAppItem> Apps { get; } = apps;
}

/// <summary>One selectable policy option for the custom-application combo boxes.</summary>
public sealed class ShieldActionOption(AppControlAction value, string label)
{
    public AppControlAction Value { get; } = value;
    public string Label { get; } = label;
    public override string ToString() => Label;
}
