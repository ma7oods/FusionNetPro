using System.Collections.ObjectModel;
using System.Windows;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Tuner;

/// <summary>
/// Network Tuner: every reversible registry tweak FusionNet knows about, grouped by
/// category, with target/current values, risk, restart requirement and a live switch.
/// </summary>
public sealed class TunerViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private bool _isBusy;
    private bool _isSubscribed;
    private bool _isActivated;
    private string _statusText = string.Empty;

    public TunerViewModel(IAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Groups = [];
        BuildGroups();

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(showToast: true), () => !IsBusy);
        ApplyRecommendedCommand = new AsyncRelayCommand(ApplyRecommendedAsync, () => !IsBusy);
        RevertAllCommand = new AsyncRelayCommand(RevertAllAsync, () => !IsBusy);
        ToggleTweakCommand = new RelayCommand(parameter =>
        {
            if (parameter is TweakRow row) _ = ToggleAsync(row);
        });
        RestartElevatedCommand = new RelayCommand(RestartElevated);

        UpdateCounts();
    }

    // ------------------------------------------------------------------ state
    public IAppServices Services => _services;

    public ObservableCollection<TweakGroup> Groups { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ApplyRecommendedCommand { get; }

    public AsyncRelayCommand RevertAllCommand { get; }

    public RelayCommand ToggleTweakCommand { get; }

    public RelayCommand RestartElevatedCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanInteract));
            ApplyRecommendedCommand.RaiseCanExecuteChanged();
            RevertAllCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanInteract => !_isBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsElevated => SafeIsElevated();

    public bool ShowElevationBanner => !IsElevated;

    public int AppliedCount => Groups.Sum(group => group.AppliedCount);

    public int TotalCount => Groups.Sum(group => group.TotalCount);

    public string Summary => Loc.Format("Tuner.Summary", AppliedCount, TotalCount);

    public string RecommendedHint => Loc.Format("Tuner.RecommendedHint", TweakCatalog.RecommendedIds.Count);

    // ------------------------------------------------------------------ lifecycle
    public async Task ActivateAsync()
    {
        _isActivated = true;
        if (!_isSubscribed)
        {
            _isSubscribed = true;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
        }

        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationBanner));
        OnPropertyChanged(nameof(RecommendedHint));
        await RefreshAsync(showToast: false);
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

    public void OnSettingsChanged()
    {
        if (!_isActivated) return;
        // The shell may have changed elevation mode or reverts tweaks; keep the display honest.
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationBanner));
        _ = RefreshAsync(showToast: false);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        UiDispatcher.Post(() => ApplyLocalization());

    // ------------------------------------------------------------------ commands
    private async Task RefreshAsync(bool showToast)
    {
        if (TweakCatalog.All.Count == 0)
        {
            StatusText = Loc.T("Tuner.NoTweaks");
            return;
        }

        IsBusy = true;
        try
        {
            var states = await ReadStatesAsync();
            ApplyStates(states);
            StatusText = string.Empty;
            if (showToast) ToastService.Instance.Show(Loc.T("Tuner.Refreshed"), ToastKind.Info);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("Tuner.ReadFailed");
            ToastService.Instance.Show(Loc.T("Tuner.ReadFailed"), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Tuner] refresh failed: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyRecommendedAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var result = await _services.Fusion.ApplyTweaksAsync(TweakCatalog.RecommendedIds);
            if (!result.Success)
            {
                ToastService.Instance.Show(result.Message, ToastKind.Error);
                return;
            }

            foreach (var id in TweakCatalog.RecommendedIds) AddToSettings(id);

            var states = await ReadStatesAsync();
            ApplyStates(states);
            StatusText = string.Empty;
            ToastService.Instance.Show(
                Loc.Format("Tuner.RecommendedDone", TweakCatalog.RecommendedIds.Count),
                ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Tuner] apply recommended failed: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RevertAllAsync()
    {
        if (IsBusy) return;

        var appliedIds = _services.Settings.EnabledTweakIds
            .Where(id => TweakCatalog.Find(id) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (appliedIds.Count == 0)
        {
            ToastService.Instance.Show(Loc.T("Tuner.NothingToRevert"), ToastKind.Info);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _services.Fusion.RevertTweaksAsync(appliedIds);
            if (!result.Success)
            {
                ToastService.Instance.Show(result.Message, ToastKind.Error);
                return;
            }

            _services.Settings.EnabledTweakIds.Clear();
            _services.SaveSettings();

            var states = await ReadStatesAsync();
            ApplyStates(states);
            StatusText = string.Empty;
            ToastService.Instance.Show(Loc.Format("Tuner.RevertDone", appliedIds.Count), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Tuner] revert all failed: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleAsync(TweakRow row)
    {
        if (IsBusy || row is null) return;

        var apply = !row.IsApplied;

        if (apply && row.Definition.Risk == TweakRisk.Advanced)
        {
            var message = Loc.Format("Tuner.ConfirmAdvanced", row.Title, row.Description);
            var owner = Application.Current?.MainWindow;
            var title = Loc.T("Tuner.ConfirmTitle");
            var answer = owner is not null
                ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                row.ResyncToggle();
                return;
            }
        }

        IsBusy = true;
        try
        {
            var result = await _services.Fusion.ApplySingleTweakAsync(row.Definition.Id, apply);
            if (!result.Success)
            {
                row.ResyncToggle();
                ToastService.Instance.Show(result.Message, ToastKind.Error);
                return;
            }

            if (apply) AddToSettings(row.Definition.Id);
            else RemoveFromSettings(row.Definition.Id);

            TweakState? state = null;
            try
            {
                var states = await Task.Run(() => _services.Tweaks.ReadAll([row.Definition]));
                state = states.FirstOrDefault();
            }
            catch (Exception readEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Tuner] re-read failed for {row.Definition.Id}: {readEx}");
            }

            if (state is not null) row.Apply(state, Loc.Instance.IsArabic);
            else row.SetAppliedFallback(apply, Loc.Instance.IsArabic);

            UpdateCounts();
            StatusText = string.Empty;
            ToastService.Instance.Show(
                Loc.Format(apply ? "Tuner.Applied" : "Tuner.Reverted", row.Title),
                ToastKind.Success);

            if (apply && row.Definition.RequiresRestart)
            {
                StatusText = Loc.Format("Tuner.RestartHint",
                    TweakCatalog.RequiresRestart([row.Definition.Id]).Count);
            }
        }
        catch (Exception ex)
        {
            row.ResyncToggle();
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Tuner] toggle failed: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RestartElevated()
    {
        if (_services.Elevation.TryRestartElevated()) return;
        ToastService.Instance.Show(Loc.T("Tuner.ElevationCancelled"), ToastKind.Info);
    }

    // ------------------------------------------------------------------ helpers
    private async Task<IReadOnlyList<TweakState>> ReadStatesAsync()
    {
        var definitions = TweakCatalog.All;
        return await Task.Run(() => _services.Tweaks.ReadAll(definitions));
    }

    private void ApplyStates(IReadOnlyList<TweakState> states)
    {
        var map = new Dictionary<string, TweakState>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in states)
        {
            if (state is null) continue;
            map[state.Id] = state;
        }

        var arabic = Loc.Instance.IsArabic;
        foreach (var group in Groups)
        {
            foreach (var row in group.Rows)
            {
                if (map.TryGetValue(row.Definition.Id, out var state)) row.Apply(state, arabic);
                else row.MarkUnknown(arabic);
            }

            group.RefreshSummary();
        }

        UpdateCounts();
    }

    private void AddToSettings(string id)
    {
        var ids = _services.Settings.EnabledTweakIds;
        if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);
        _services.SaveSettings();
    }

    private void RemoveFromSettings(string id)
    {
        _services.Settings.EnabledTweakIds.RemoveAll(existing =>
            string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        _services.SaveSettings();
    }

    private bool SafeIsElevated()
    {
        try
        {
            return _services.Elevation.IsElevated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tuner] elevation probe failed: {ex}");
            return false;
        }
    }

    private void UpdateCounts() =>
        RaiseAll(nameof(AppliedCount), nameof(TotalCount), nameof(Summary), nameof(RecommendedHint));

    private void BuildGroups()
    {
        Groups.Clear();
        var arabic = Loc.Instance.IsArabic;
        foreach (var category in TweakCatalog.Categories)
        {
            var rows = TweakCatalog.ByCategory(category)
                .Select(definition => new TweakRow(definition, arabic))
                .ToList();
            Groups.Add(new TweakGroup(category, CategoryTitle(category), CategorySubtitle(category), rows));
        }
    }

    private void ApplyLocalization()
    {
        var arabic = Loc.Instance.IsArabic;
        foreach (var group in Groups)
        {
            group.RefreshLocalization(CategoryTitle(group.Category), CategorySubtitle(group.Category));
            foreach (var row in group.Rows) row.RefreshLocalization(arabic);
        }

        UpdateCounts();
        if (!IsBusy) StatusText = string.Empty;
    }

    private static string CategoryTitle(string category) => Loc.T(category switch
    {
        TweakCatalog.CatNetwork => "Tuner.Category.Network",
        TweakCatalog.CatMultimedia => "Tuner.Category.Multimedia",
        TweakCatalog.CatGaming => "Tuner.Category.Gaming",
        TweakCatalog.CatAdvancedTcp => "Tuner.Category.AdvancedTcp",
        TweakCatalog.CatPower => "Tuner.Category.Power",
        _ => "Tuner.Category.Network"
    });

    private static string CategorySubtitle(string category) => Loc.T(category switch
    {
        TweakCatalog.CatNetwork => "Tuner.Category.NetworkSubtitle",
        TweakCatalog.CatMultimedia => "Tuner.Category.MultimediaSubtitle",
        TweakCatalog.CatGaming => "Tuner.Category.GamingSubtitle",
        TweakCatalog.CatAdvancedTcp => "Tuner.Category.AdvancedTcpSubtitle",
        TweakCatalog.CatPower => "Tuner.Category.PowerSubtitle",
        _ => "Tuner.SectionBody"
    });
}

/// <summary>One category section of the tuner.</summary>
public sealed class TweakGroup : ViewModelBase
{
    private string _title;
    private string _subtitle;

    public TweakGroup(string category, string title, string subtitle, IEnumerable<TweakRow> rows)
    {
        Category = category;
        _title = title;
        _subtitle = subtitle;
        Rows = new ObservableCollection<TweakRow>(rows);
    }

    public string Category { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public ObservableCollection<TweakRow> Rows { get; }

    public int TotalCount => Rows.Count;

    public int AppliedCount => Rows.Count(row => row.IsApplied);

    public string Summary => Loc.Format("Tuner.GroupSummary", AppliedCount, TotalCount);

    public void RefreshLocalization(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
        RefreshSummary();
    }

    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));
}

/// <summary>One tweak card: localized text, risk/restart metadata and its live state.</summary>
public sealed class TweakRow : ViewModelBase
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _riskLabel = string.Empty;
    private string _expectedText = string.Empty;
    private string _currentText = string.Empty;
    private string _stateLabel = string.Empty;
    private string _errorText = string.Empty;
    private bool _hasError;
    private bool _isOn;

    public TweakRow(TweakDefinition definition, bool arabic)
    {
        Definition = definition;
        _expectedText = definition.ExpectedDisplay;
        RefreshLocalization(arabic);
    }

    public TweakDefinition Definition { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    /// <summary>The switch state, promoted manually so a failed apply can snap it back.</summary>
    public bool IsOn
    {
        get => _isOn;
        private set => SetProperty(ref _isOn, value);
    }

    public bool IsApplied => _isOn;

    public string RiskLabel
    {
        get => _riskLabel;
        private set => SetProperty(ref _riskLabel, value);
    }

    /// <summary>Simple lowercase token the XAML triggers compare against (safe/moderate/advanced).</summary>
    public string RiskKey => Definition.Risk switch
    {
        TweakRisk.Safe => "safe",
        TweakRisk.Moderate => "moderate",
        _ => "advanced"
    };

    public bool RequiresRestart => Definition.RequiresRestart;

    public string ExpectedText
    {
        get => _expectedText;
        private set => SetProperty(ref _expectedText, value);
    }

    public string CurrentText
    {
        get => _currentText;
        private set => SetProperty(ref _currentText, value);
    }

    public string StateLabel
    {
        get => _stateLabel;
        private set => SetProperty(ref _stateLabel, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public void RefreshLocalization(bool arabic)
    {
        Title = arabic ? Definition.TitleAr : Definition.TitleEn;
        Description = arabic ? Definition.DescriptionAr : Definition.DescriptionEn;
        RiskLabel = Loc.T(Definition.Risk switch
        {
            TweakRisk.Safe => "Common.Safe",
            TweakRisk.Moderate => "Common.Moderate",
            _ => "Common.Advanced"
        });
        StateLabel = Loc.T(IsOn ? "Common.Enabled" : "Common.Inactive");
        ExpectedText = Definition.ExpectedDisplay;
        if (string.IsNullOrWhiteSpace(CurrentText)) CurrentText = Loc.T("Tuner.Unavailable");
    }

    public void Apply(TweakState state, bool arabic)
    {
        IsOn = state.Applied;
        ExpectedText = string.IsNullOrWhiteSpace(state.ExpectedValue) ? Definition.ExpectedDisplay : state.ExpectedValue;
        CurrentText = string.IsNullOrWhiteSpace(state.CurrentValue) ? Loc.T("Tuner.Unavailable") : state.CurrentValue;
        StateLabel = Loc.T(state.Applied ? "Common.Enabled" : "Common.Inactive");
        ErrorText = state.ErrorText ?? string.Empty;
        HasError = !string.IsNullOrWhiteSpace(ErrorText);
        if (HasError) CurrentText = Loc.T("Tuner.ReadError");
        if (string.IsNullOrWhiteSpace(Title)) RefreshLocalization(arabic);
    }

    public void MarkUnknown(bool arabic)
    {
        IsOn = false;
        CurrentText = Loc.T("Tuner.Unavailable");
        StateLabel = Loc.T("Common.Inactive");
        ErrorText = string.Empty;
        HasError = false;
        RefreshLocalization(arabic);
    }

    public void SetAppliedFallback(bool applied, bool arabic)
    {
        IsOn = applied;
        StateLabel = Loc.T(applied ? "Common.Enabled" : "Common.Inactive");
        if (string.IsNullOrWhiteSpace(CurrentText)) CurrentText = Loc.T("Tuner.Unavailable");
        if (!applied) CurrentText = Loc.T("Tuner.Unavailable");
        if (string.IsNullOrWhiteSpace(Title)) RefreshLocalization(arabic);
    }

    /// <summary>Forces the one-way switch binding to re-read the view-model state.</summary>
    public void ResyncToggle() => OnPropertyChanged(nameof(IsOn));
}
