using System.Collections.ObjectModel;
using System.IO;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.App.Styles;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;

namespace FusionNet.App.Views.Gaming;

/// <summary>
/// Games &amp; Profiles: detection mode, the full game catalog with live running
/// badges, custom game executables and the automation switches that drive Fusion Mode.
/// </summary>
public sealed class GamingViewModel : ViewModelBase
{
    private const string AllCategories = "*";

    private bool _subscribed;
    private bool _languageSubscribed;
    private string _searchText = string.Empty;
    private string _selectedCategory = AllCategories;
    private bool _isArmed;
    private IReadOnlyList<GameProfile> _running = [];

    public GamingViewModel(IAppServices services)
    {
        S = services ?? throw new ArgumentNullException(nameof(services));

        SetAutoModeCommand = new RelayCommand(SetAutoMode);
        SetPinnedModeCommand = new RelayCommand(SetPinnedMode);
        AddCustomGameCommand = new RelayCommand(AddCustomGame);
        RefreshRunningCommand = new RelayCommand(RefreshRunning);

        RebuildCategories();
        RebuildGames();
        RebuildCustomGames();
        RebuildRunningGames();
    }

    private IAppServices S { get; }

    // ---------------------------------------------------------------- commands

    public RelayCommand SetAutoModeCommand { get; }
    public RelayCommand SetPinnedModeCommand { get; }
    public RelayCommand AddCustomGameCommand { get; }
    public RelayCommand RefreshRunningCommand { get; }

    // ---------------------------------------------------------------- collections

    public ObservableCollection<GameCategoryChip> Categories { get; } = [];
    public ObservableCollection<GameCardItem> Games { get; } = [];
    public ObservableCollection<CustomGameItem> CustomGames { get; } = [];
    public ObservableCollection<RunningGameItem> RunningGames { get; } = [];

    // ---------------------------------------------------------------- mode

    public bool IsAutoMode =>
        string.Equals(S.Settings.SelectedGameId, GameCatalog.AutoProfileId, StringComparison.OrdinalIgnoreCase);

    public bool IsPinnedMode => !IsAutoMode;

    public bool IsArmed
    {
        get => _isArmed;
        private set => SetProperty(ref _isArmed, value);
    }

    private string PinnedGameName()
    {
        if (IsAutoMode) return Loc.T("Game.ModeTargetAuto");

        var builtIn = GameCatalog.Find(S.Settings.SelectedGameId);
        if (builtIn is not null) return builtIn.Name;

        var custom = S.Settings.CustomGames.FirstOrDefault(game =>
            string.Equals(game.Id, S.Settings.SelectedGameId, StringComparison.OrdinalIgnoreCase));
        return custom?.Name ?? Loc.T("Game.ModeTargetAuto");
    }

    public string ModeTarget => PinnedGameName();

    public string ModeCurrent => Loc.Format("Game.ModeCurrent", ModeTarget);

    // ---------------------------------------------------------------- filter state

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            RebuildGames();
            RaiseAll(nameof(HasGames), nameof(GamesSummary));
        }
    }

    public bool HasGames => Games.Count > 0;

    public string GamesSummary => Loc.Format("Game.GamesCount", Games.Count);

    public bool HasRunningGames => RunningGames.Count > 0;

    public string RunningSummary => Loc.Format("Game.RunningCount", RunningGames.Count);

    public bool HasCustomGames => CustomGames.Count > 0;

    // ---------------------------------------------------------------- automation

    public bool AutoEngageOnGameStart
    {
        get => S.Settings.AutoEngageOnGameStart;
        set
        {
            if (S.Settings.AutoEngageOnGameStart == value) return;
            S.Settings.AutoEngageOnGameStart = value;
            S.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool RestoreOnGameExit
    {
        get => S.Settings.RestoreOnGameExit;
        set
        {
            if (S.Settings.RestoreOnGameExit == value) return;
            S.Settings.RestoreOnGameExit = value;
            S.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool SuspendAppsOnlyWhileGameRuns
    {
        get => S.Settings.SuspendAppsOnlyWhileGameRuns;
        set
        {
            if (S.Settings.SuspendAppsOnlyWhileGameRuns == value) return;
            S.Settings.SuspendAppsOnlyWhileGameRuns = value;
            S.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool ConfirmExitWhileActive
    {
        get => S.Settings.ConfirmExitWhileActive;
        set
        {
            if (S.Settings.ConfirmExitWhileActive == value) return;
            S.Settings.ConfirmExitWhileActive = value;
            S.SaveSettings();
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- lifecycle

    public Task ActivateAsync()
    {
        if (!_subscribed)
        {
            S.Games.Changed += OnGamesChanged;
            S.Fusion.StateChanged += OnFusionStateChanged;
            _subscribed = true;
        }

        if (!_languageSubscribed)
        {
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            _languageSubscribed = true;
        }

        _running = S.Games.RunningGames;
        IsArmed = S.Fusion.State is FusionModeState.Active or FusionModeState.Arming;

        RebuildCategories();
        RebuildGames();
        RebuildCustomGames();
        RebuildRunningGames();
        RaiseMode();
        RaiseAutomation();

        S.Games.PollNow();
        return Task.CompletedTask;
    }

    public void Deactivate()
    {
        if (_subscribed)
        {
            S.Games.Changed -= OnGamesChanged;
            S.Fusion.StateChanged -= OnFusionStateChanged;
            _subscribed = false;
        }

        if (_languageSubscribed)
        {
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            _languageSubscribed = false;
        }
    }

    public void OnSettingsChanged(FusionSettings settings)
    {
        RebuildGames();
        RebuildCustomGames();
        RaiseMode();
        RaiseAutomation();
        RaiseAll(nameof(HasGames), nameof(GamesSummary), nameof(HasCustomGames),
            nameof(HasRunningGames), nameof(RunningSummary));
    }

    // ---------------------------------------------------------------- service events

    private void OnGamesChanged(object? sender, GameDetectionEventArgs e) =>
        UiDispatcher.Post(() =>
        {
            _running = e.RunningGames;
            UpdateRunningFlags();
            RebuildRunningGames();
        });

    private void OnFusionStateChanged(object? sender, FusionStateChangedEventArgs e) =>
        UiDispatcher.Post(() => IsArmed = e.State is FusionModeState.Active or FusionModeState.Arming);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        UiDispatcher.Post(() =>
        {
            RebuildCategories();
            RebuildGames();
            RebuildCustomGames();
            RebuildRunningGames();
            RaiseMode();
            RaiseAutomation();
            RaiseAll(nameof(HasGames), nameof(GamesSummary), nameof(HasCustomGames),
                nameof(HasRunningGames), nameof(RunningSummary));
        });

    // ---------------------------------------------------------------- mode actions

    private void SetAutoMode()
    {
        if (IsAutoMode) return;
        S.Settings.SelectedGameId = GameCatalog.AutoProfileId;
        SaveAndSync();
        RefreshFusionIfArmedAsync();
        RebuildGames();
        RaiseMode();
        ToastService.Instance.Show(Loc.T("Game.UnpinnedToast"), ToastKind.Success);
    }

    private void SetPinnedMode()
    {
        if (IsPinnedMode) return;
        ToastService.Instance.Show(Loc.T("Game.PinFirst"), ToastKind.Warning);
    }

    private void PinGame(GameCardItem card)
    {
        var pinned = string.Equals(S.Settings.SelectedGameId, card.Id, StringComparison.OrdinalIgnoreCase);
        if (pinned)
        {
            S.Settings.SelectedGameId = GameCatalog.AutoProfileId;
            SaveAndSync();
            RefreshFusionIfArmedAsync();
            RebuildGames();
            RaiseMode();
            ToastService.Instance.Show(Loc.T("Game.UnpinnedToast"), ToastKind.Success);
            return;
        }

        S.Settings.SelectedGameId = card.Id;
        SaveAndSync();
        RefreshFusionIfArmedAsync();
        RebuildGames();
        RaiseMode();
        ToastService.Instance.Show(Loc.Format("Game.PinnedToast", card.Name), ToastKind.Success);
    }

    private void RaiseMode() =>
        RaiseAll(nameof(IsAutoMode), nameof(IsPinnedMode), nameof(ModeTarget), nameof(ModeCurrent));

    private void RaiseAutomation() =>
        RaiseAll(nameof(AutoEngageOnGameStart), nameof(RestoreOnGameExit),
            nameof(SuspendAppsOnlyWhileGameRuns), nameof(ConfirmExitWhileActive));

    private void SaveAndSync()
    {
        S.SaveSettings();
        ApplyWatchList();
    }

    private void ApplyWatchList()
    {
        try
        {
            S.Games.SetWatchList(GameCatalog.BuildWatchList(S.Settings));
            S.Games.PollNow();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Warning);
        }
    }

    /// <summary>Refreshes the active session when a game rule changes while Fusion Mode is armed.</summary>
    private async void RefreshFusionIfArmedAsync()
    {
        if (!IsArmed) return;
        try
        {
            await S.Fusion.RefreshAsync();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Warning);
        }
    }

    // ---------------------------------------------------------------- categories + games

    private void RebuildCategories()
    {
        var selected = _selectedCategory;
        Categories.Clear();
        Categories.Add(new GameCategoryChip(AllCategories, Loc.T("Game.CategoryAll"), SelectCategory, selected == AllCategories));

        foreach (var category in GameCatalog.Categories)
        {
            Categories.Add(new GameCategoryChip(
                category,
                CategoryLabel(category),
                SelectCategory,
                string.Equals(category, selected, StringComparison.OrdinalIgnoreCase)));
        }

        OnPropertyChanged(nameof(Categories));
    }

    private void SelectCategory(string category)
    {
        _selectedCategory = category;
        foreach (var chip in Categories)
            chip.IsSelected = string.Equals(chip.Value, category, StringComparison.OrdinalIgnoreCase);

        RebuildGames();
        RaiseAll(nameof(HasGames), nameof(GamesSummary));
    }

    private void RebuildGames()
    {
        Games.Clear();

        IEnumerable<GameProfile> source = GameCatalog.All;

        if (_selectedCategory != AllCategories)
        {
            source = source.Where(game =>
                string.Equals(game.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        var search = _searchText.Trim();
        if (search.Length > 0)
        {
            source = source.Where(game =>
                game.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                game.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                game.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (game.SteamAppId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                game.ProcessNames.Any(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var profile in source)
        {
            Games.Add(CreateCard(profile));
        }

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(GamesSummary));
    }

    private GameCardItem CreateCard(GameProfile profile)
    {
        var selected = string.Equals(S.Settings.SelectedGameId, profile.Id, StringComparison.OrdinalIgnoreCase);
        var running = IsProfileRunning(profile);

        return new GameCardItem(profile, PinGame)
        {
            IsSelected = selected,
            IsRunning = running,
            CategoryLabel = CategoryLabel(profile.Category),
            ProcessesText = string.Join(", ", profile.ProcessNames),
            SteamText = profile.SteamAppId is null
                ? Loc.T("Game.SteamNone")
                : Loc.Format("Game.SteamApp", profile.SteamAppId),
            HasSteam = profile.SteamAppId is not null,
            PinLabel = selected ? Loc.T("Game.Pinned") : Loc.T("Game.Pin"),
            PinGlyph = selected ? Glyphs.Check : Glyphs.Add,
            PinTooltip = selected ? Loc.T("Game.UnpinTip") : Loc.T("Game.PinTip"),
            Initial = profile.Name.Length > 0 ? profile.Name[..1].ToUpperInvariant() : "?"
        };
    }

    private bool IsProfileRunning(GameProfile profile) =>
        _running.Any(running =>
            string.Equals(running.Id, profile.Id, StringComparison.OrdinalIgnoreCase) ||
            profile.MatchesAnyProcess(running.ProcessNames));

    private void UpdateRunningFlags()
    {
        foreach (var card in Games)
        {
            card.IsRunning = IsProfileRunning(card.Profile);
        }
    }

    // ---------------------------------------------------------------- custom games

    private void RebuildCustomGames()
    {
        CustomGames.Clear();
        foreach (var game in S.Settings.CustomGames)
        {
            CustomGames.Add(new CustomGameItem(game, SetCustomGameEnabled, RemoveCustomGame));
        }

        OnPropertyChanged(nameof(HasCustomGames));
    }

    private void AddCustomGame()
    {
        try
        {
            var path = S.Files.PickOpenPath(Loc.T("Dialog.ExeFilter"));
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                ToastService.Instance.Show(Loc.T("Game.CustomInvalid"), ToastKind.Warning);
                return;
            }

            var full = Path.GetFullPath(path);
            if (S.Settings.CustomGames.Any(game =>
                    string.Equals(game.ExecutablePath, full, StringComparison.OrdinalIgnoreCase)))
            {
                ToastService.Instance.Show(Loc.T("Game.CustomExists"), ToastKind.Warning);
                return;
            }

            var custom = CustomGame.Create(full);
            S.Settings.CustomGames.Add(custom);
            SaveAndSync();
            RebuildCustomGames();
            ToastService.Instance.Show(Loc.Format("Game.CustomAdded", custom.Name), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
        }
    }

    private void SetCustomGameEnabled(CustomGameItem item, bool enabled)
    {
        var index = S.Settings.CustomGames.FindIndex(game =>
            string.Equals(game.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        S.Settings.CustomGames[index] = S.Settings.CustomGames[index] with { Enabled = enabled };

        // A disabled game can no longer be the pinned target.
        if (!enabled && string.Equals(S.Settings.SelectedGameId, item.Id, StringComparison.OrdinalIgnoreCase))
            S.Settings.SelectedGameId = GameCatalog.AutoProfileId;

        SaveAndSync();
        RebuildCustomGames();
        RebuildGames();
        RaiseMode();
    }

    private void RemoveCustomGame(CustomGameItem item)
    {
        var existing = S.Settings.CustomGames.FirstOrDefault(game =>
            string.Equals(game.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;

        S.Settings.CustomGames.Remove(existing);
        if (string.Equals(S.Settings.SelectedGameId, item.Id, StringComparison.OrdinalIgnoreCase))
            S.Settings.SelectedGameId = GameCatalog.AutoProfileId;

        SaveAndSync();
        RebuildCustomGames();
        RebuildGames();
        RaiseMode();
        ToastService.Instance.Show(Loc.Format("Game.CustomRemoved", item.Name), ToastKind.Success);
    }

    // ---------------------------------------------------------------- running games

    private void RefreshRunning()
    {
        try
        {
            S.Games.PollNow();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Warning);
        }

        _running = S.Games.RunningGames;
        UpdateRunningFlags();
        RebuildRunningGames();
    }

    private void RebuildRunningGames()
    {
        RunningGames.Clear();
        foreach (var game in _running)
        {
            RunningGames.Add(new RunningGameItem
            {
                Name = game.Name,
                Category = CategoryLabel(game.Category),
                Processes = string.Join(", ", game.ProcessNames),
                Initial = game.Name.Length > 0 ? game.Name[..1].ToUpperInvariant() : "?"
            });
        }

        RaiseAll(nameof(HasRunningGames), nameof(RunningSummary));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Localises the English category names that come from the frozen Core catalog.</summary>
    public static string CategoryLabel(string category) => category switch
    {
        "Tactical FPS" => Loc.T("Game.Cat.TacticalFPS"),
        "Battle Royale" => Loc.T("Game.Cat.BattleRoyale"),
        "MOBA" => Loc.T("Game.Cat.Moba"),
        "Hero Shooter" => Loc.T("Game.Cat.HeroShooter"),
        "Survival" => Loc.T("Game.Cat.Survival"),
        "Extraction" => Loc.T("Game.Cat.Extraction"),
        "Open World" => Loc.T("Game.Cat.OpenWorld"),
        "Sports" => Loc.T("Game.Cat.Sports"),
        "Looter Shooter" => Loc.T("Game.Cat.LooterShooter"),
        "Arena FPS" => Loc.T("Game.Cat.ArenaFPS"),
        "Sandbox" => Loc.T("Game.Cat.Sandbox"),
        _ => category
    };
}

/// <summary>One game card in the catalog grid.</summary>
public sealed class GameCardItem : ViewModelBase
{
    private bool _isSelected;
    private bool _isRunning;

    public GameCardItem(GameProfile profile, Action<GameCardItem> pin)
    {
        Profile = profile;
        PinCommand = new RelayCommand(() => pin(this));
    }

    public GameProfile Profile { get; }

    public string Id => Profile.Id;
    public string Name => Profile.Name;
    public IReadOnlyList<string> ProcessNames => Profile.ProcessNames;
    public RelayCommand PinCommand { get; }

    public string CategoryLabel { get; init; } = string.Empty;
    public string ProcessesText { get; init; } = string.Empty;
    public string SteamText { get; init; } = string.Empty;
    public bool HasSteam { get; init; }
    public string Initial { get; init; } = "?";
    public string PinLabel { get; init; } = string.Empty;
    public string PinGlyph { get; init; } = string.Empty;
    public string PinTooltip { get; init; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }
}

/// <summary>One category filter chip.</summary>
public sealed class GameCategoryChip : ViewModelBase
{
    private bool _isSelected;

    public GameCategoryChip(string value, string label, Action<string> select, bool selected)
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

/// <summary>One custom game row with its enable switch and delete action.</summary>
public sealed class CustomGameItem : ViewModelBase
{
    private readonly Action<CustomGameItem, bool> _setEnabled;
    private bool _enabled;

    public CustomGameItem(CustomGame game, Action<CustomGameItem, bool> setEnabled, Action<CustomGameItem> remove)
    {
        Id = game.Id;
        Name = game.Name;
        ExecutablePath = game.ExecutablePath;
        ProcessName = game.ProcessName;
        _enabled = game.Enabled;
        _setEnabled = setEnabled;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Id { get; }
    public string Name { get; }
    public string ExecutablePath { get; }
    public string ProcessName { get; }
    public RelayCommand RemoveCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            _setEnabled(this, value);
        }
    }
}

/// <summary>One row in the "currently running" list.</summary>
public sealed class RunningGameItem
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Processes { get; init; }
    public required string Initial { get; init; }
}
