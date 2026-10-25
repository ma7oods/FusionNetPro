using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Activity;

/// <summary>
/// Activity Log: the live, filterable history of everything FusionNet did, plus the
/// session journal card that can recover an interrupted session.
/// </summary>
public sealed class ActivityViewModel : ViewModelBase
{
    private const int MaxEntries = 500;

    private readonly IAppServices _services;
    private readonly List<ActivityRow> _all = [];

    private bool _isActivated;
    private bool _isSubscribed;
    private bool _isRestoring;
    private bool _autoScroll = true;
    private string _searchText = string.Empty;
    private LogLevel? _levelFilter;
    private ActivityRow? _selectedEntry;
    private string _sessionIdShort = "—";
    private int _journalEntryCount;
    private bool _journalActive;
    private bool _hasPendingRestore;
    private string _sessionStartedText = string.Empty;
    private int _totalCount;
    private int _successCount;
    private int _infoCount;
    private int _warningCount;
    private int _errorCount;

    public ActivityViewModel(IAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Entries = [];
        Filters = [];
        BuildFilters();

        SelectFilterCommand = new RelayCommand(parameter =>
        {
            if (parameter is LevelFilterChip chip) SelectFilter(chip);
        });
        ClearCommand = new AsyncRelayCommand(ClearAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        RestoreNowCommand = new AsyncRelayCommand(RestoreNowAsync, () => !IsRestoring);
        CopyEntryCommand = new RelayCommand(CopyEntry);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    // ------------------------------------------------------------------ state
    public ObservableCollection<ActivityRow> Entries { get; }

    public ObservableCollection<LevelFilterChip> Filters { get; }

    public RelayCommand SelectFilterCommand { get; }

    public AsyncRelayCommand ClearCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public AsyncRelayCommand RestoreNowCommand { get; }

    public RelayCommand CopyEntryCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Raised when a new entry arrives and auto-scroll is on.</summary>
    public event EventHandler? ScrollToTopRequested;

    public ActivityRow? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value)) return;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selectedEntry is not null;

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            RebuildFiltered();
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public int SuccessCount
    {
        get => _successCount;
        private set => SetProperty(ref _successCount, value);
    }

    public int InfoCount
    {
        get => _infoCount;
        private set => SetProperty(ref _infoCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetProperty(ref _errorCount, value);
    }

    public string EntryCountText => Loc.Format("Log.EntryCount", TotalCount);

    public string FilteredCountText => Loc.Format("Log.FilteredCount", Entries.Count);

    public string EmptyTitle => TotalCount == 0 ? Loc.T("Log.EmptyTitle") : Loc.T("Log.NoMatches");

    public string EmptyBody => TotalCount == 0 ? Loc.T("Log.EmptyBody") : Loc.T("Log.NoMatches");

    public bool ShowEmptyState => Entries.Count == 0;

    public string SessionIdShort
    {
        get => _sessionIdShort;
        private set => SetProperty(ref _sessionIdShort, value);
    }

    public int JournalEntryCount
    {
        get => _journalEntryCount;
        private set => SetProperty(ref _journalEntryCount, value);
    }

    public bool JournalActive
    {
        get => _journalActive;
        private set
        {
            if (!SetProperty(ref _journalActive, value)) return;
            OnPropertyChanged(nameof(SessionStatusLabel));
            OnPropertyChanged(nameof(SessionStatusKey));
        }
    }

    public string SessionStatusLabel =>
        Loc.T(JournalActive ? "Log.SessionRecording" : "Log.SessionIdle");

    public string SessionStatusKey => JournalActive ? "active" : "idle";

    public string SessionStartedText
    {
        get => _sessionStartedText;
        private set => SetProperty(ref _sessionStartedText, value);
    }

    public bool HasPendingRestore
    {
        get => _hasPendingRestore;
        private set => SetProperty(ref _hasPendingRestore, value);
    }

    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (!SetProperty(ref _isRestoring, value)) return;
            OnPropertyChanged(nameof(RestoreButtonText));
            RestoreNowCommand.RaiseCanExecuteChanged();
        }
    }

    public string RestoreButtonText => Loc.T(IsRestoring ? "Log.Restoring" : "Log.RestoreNow");

    public bool ShowJournalEmpty => JournalEntryCount == 0;

    // ------------------------------------------------------------------ lifecycle
    public Task ActivateAsync()
    {
        _isActivated = true;
        if (!_isSubscribed)
        {
            _isSubscribed = true;
            _services.Activity.EntryAdded += OnEntryAdded;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
        }

        ReloadFromSnapshot();
        RefreshJournal();
        return Task.CompletedTask;
    }

    public void Deactivate()
    {
        _isActivated = false;
        if (_isSubscribed)
        {
            _isSubscribed = false;
            _services.Activity.EntryAdded -= OnEntryAdded;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }

    public void OnSettingsChanged()
    {
        if (_isActivated) RefreshJournal();
    }

    private void OnEntryAdded(object? sender, ActivityEntry entry) =>
        UiDispatcher.Post(() => AddEntry(entry));

    private void OnLanguageChanged(object? sender, EventArgs e) => UiDispatcher.Post(ApplyLocalization);

    // ------------------------------------------------------------------ loading
    private async Task RefreshAsync()
    {
        ReloadFromSnapshot();
        RefreshJournal();
        await Task.CompletedTask;
    }

    private void ReloadFromSnapshot()
    {
        var arabic = Loc.Instance.IsArabic;
        var snapshot = _services.Activity.Snapshot();
        var previousId = SelectedEntry?.Raw.TimestampUtc;

        _all.Clear();
        foreach (var entry in snapshot.OrderByDescending(entry => entry.TimestampUtc).Take(MaxEntries))
        {
            _all.Add(new ActivityRow(entry, arabic));
        }

        RebuildFiltered();

        if (_selectedEntry is not null && !Entries.Contains(_selectedEntry))
        {
            SelectedEntry = null;
        }

        _ = previousId;
        UpdateCounters();
    }

    private void AddEntry(ActivityEntry entry)
    {
        if (!_isActivated) return;

        var row = new ActivityRow(entry, Loc.Instance.IsArabic);
        _all.Insert(0, row);
        while (_all.Count > MaxEntries) _all.RemoveAt(_all.Count - 1);

        RebuildFiltered();
        UpdateCounters();
        RefreshJournal();

        if (_autoScroll) ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildFiltered()
    {
        var selected = _selectedEntry;
        var query = (_searchText ?? string.Empty).Trim();

        Entries.Clear();
        foreach (var row in _all)
        {
            if (_levelFilter is { } level && row.Raw.Level != level) continue;
            if (query.Length > 0 && !row.Matches(query)) continue;
            Entries.Add(row);
        }

        if (selected is not null && !Entries.Contains(selected)) SelectedEntry = null;
        else if (selected is not null) SelectedEntry = selected;

        RaiseAll(nameof(FilteredCountText), nameof(ShowEmptyState), nameof(EmptyTitle), nameof(EmptyBody));
    }

    private void UpdateCounters()
    {
        TotalCount = _all.Count;
        SuccessCount = _all.Count(row => row.Raw.Level == LogLevel.Success);
        InfoCount = _all.Count(row => row.Raw.Level == LogLevel.Info);
        WarningCount = _all.Count(row => row.Raw.Level == LogLevel.Warning);
        ErrorCount = _all.Count(row => row.Raw.Level == LogLevel.Error);

        foreach (var chip in Filters)
        {
            chip.Count = chip.Level switch
            {
                null => TotalCount,
                LogLevel.Success => SuccessCount,
                LogLevel.Info => InfoCount,
                LogLevel.Warning => WarningCount,
                LogLevel.Error => ErrorCount,
                _ => 0
            };
        }

        RaiseAll(nameof(EntryCountText), nameof(FilteredCountText), nameof(ShowEmptyState),
            nameof(EmptyTitle), nameof(EmptyBody));
    }

    // ------------------------------------------------------------------ filters
    private void BuildFilters()
    {
        Filters.Clear();
        Filters.Add(new LevelFilterChip("Log.FilterAll", null, selected: true));
        Filters.Add(new LevelFilterChip("Log.FilterSuccess", LogLevel.Success, selected: false));
        Filters.Add(new LevelFilterChip("Log.FilterInfo", LogLevel.Info, selected: false));
        Filters.Add(new LevelFilterChip("Log.FilterWarning", LogLevel.Warning, selected: false));
        Filters.Add(new LevelFilterChip("Log.FilterError", LogLevel.Error, selected: false));
    }

    private void SelectFilter(LevelFilterChip chip)
    {
        _levelFilter = chip.Level;
        foreach (var item in Filters) item.IsSelected = ReferenceEquals(item, chip);
        RebuildFiltered();
    }

    // ------------------------------------------------------------------ actions
    private async Task ClearAsync()
    {
        if (!Confirm(Loc.T("Log.ClearTitle"), Loc.T("Log.ClearBody"))) return;

        try
        {
            _services.Activity.Clear();
            _all.Clear();
            Entries.Clear();
            SelectedEntry = null;
            UpdateCounters();
            ToastService.Instance.Show(Loc.T("Log.Cleared"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Activity] clear failed: {ex}");
        }

        await Task.CompletedTask;
    }

    private async Task ExportAsync()
    {
        try
        {
            var text = _services.Activity.ExportText();
            if (string.IsNullOrWhiteSpace(text))
            {
                ToastService.Instance.Show(Loc.T("Log.ExportEmpty"), ToastKind.Info);
                return;
            }

            var path = _services.Files.PickSavePath("fusionnet-activity.txt", Loc.T("Dialog.TextFilter"));
            if (string.IsNullOrWhiteSpace(path)) return;

            await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ToastService.Instance.Show(Loc.T("Log.ExportDone"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Log.ExportFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Activity] export failed: {ex}");
        }
    }

    private async Task RestoreNowAsync()
    {
        if (IsRestoring) return;

        IsRestoring = true;
        try
        {
            var result = await _services.Fusion.RestoreInterruptedSessionAsync();
            if (result.Success)
            {
                var message = string.IsNullOrWhiteSpace(result.Message) ? Loc.T("Common.Done") : result.Message;
                ToastService.Instance.Show(Loc.Format("Log.RestoreDone", message), ToastKind.Success);
            }
            else
            {
                ToastService.Instance.Show(Loc.Format("Log.RestoreFailed", result.Message), ToastKind.Error);
            }

            RefreshJournal();
            ReloadFromSnapshot();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Log.RestoreFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Activity] restore failed: {ex}");
        }
        finally
        {
            IsRestoring = false;
        }
    }

    private void CopyEntry()
    {
        if (_selectedEntry is null) return;

        try
        {
            Clipboard.SetText(_selectedEntry.FullText);
            ToastService.Instance.Show(Loc.T("Log.Copied"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Activity] clipboard failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ journal
    private void RefreshJournal()
    {
        try
        {
            var document = _services.Journal.Document;
            var entries = _services.Journal.Entries;

            var sessionId = document?.SessionId ?? string.Empty;
            SessionIdShort = string.IsNullOrWhiteSpace(sessionId)
                ? "—"
                : sessionId[..Math.Min(8, sessionId.Length)].ToUpperInvariant();

            JournalEntryCount = entries?.Count ?? 0;
            JournalActive = document?.Active == true;
            HasPendingRestore = _services.Journal.HasPendingRestore;

            SessionStartedText = document is null
                ? string.Empty
                : Loc.Format("Log.SessionStarted", Humanize.Relative(document.StartedAtUtc));

            OnPropertyChanged(nameof(ShowJournalEmpty));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Activity] journal read failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ helpers
    private void ApplyLocalization()
    {
        var arabic = Loc.Instance.IsArabic;
        foreach (var row in _all) row.RefreshLocalization(arabic);
        foreach (var chip in Filters) chip.RefreshLocalization();

        RefreshJournal();
        RebuildFiltered();
        RaiseAll(nameof(EntryCountText), nameof(EmptyTitle), nameof(EmptyBody));
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
}

/// <summary>One activity-log row with level colouring and localized labels.</summary>
public sealed class ActivityRow : ViewModelBase
{
    private string _levelLabel;

    public ActivityRow(ActivityEntry entry, bool arabic)
    {
        Raw = entry;
        _levelLabel = LevelLabelFor(entry.Level);
        _ = arabic;
    }

    public ActivityEntry Raw { get; }

    public DateTimeOffset TimestampUtc => Raw.TimestampUtc;

    public string TimeText => Humanize.Clock(Raw.TimestampUtc);

    public string Message => Raw.Message;

    public string Category => Raw.Category;

    public string Detail => Raw.Detail ?? string.Empty;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string LevelKey => Raw.Level switch
    {
        LogLevel.Success => "success",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        _ => "info"
    };

    public string LevelLabel
    {
        get => _levelLabel;
        private set => SetProperty(ref _levelLabel, value);
    }

    public string Glyph => Raw.Level switch
    {
        LogLevel.Success => "\uE73E",
        LogLevel.Warning => "\uE7BA",
        LogLevel.Error => "\uE783",
        _ => "\uE946"
    };

    public string FullText =>
        $"[{Raw.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}] {Raw.Level} · {Raw.Category}: {Raw.Message}" +
        (HasDetail ? Environment.NewLine + Detail : string.Empty);

    public bool Matches(string query) =>
        Raw.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Raw.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (Raw.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    public void RefreshLocalization(bool arabic)
    {
        LevelLabel = LevelLabelFor(Raw.Level);
        _ = arabic;
    }

    private static string LevelLabelFor(LogLevel level) => Loc.T(level switch
    {
        LogLevel.Success => "Log.FilterSuccess",
        LogLevel.Warning => "Log.FilterWarning",
        LogLevel.Error => "Log.FilterError",
        _ => "Log.FilterInfo"
    });
}

/// <summary>A single-select level chip with a live counter.</summary>
public sealed class LevelFilterChip : ViewModelBase
{
    private bool _isSelected;
    private int _count;
    private string _label;

    public LevelFilterChip(string labelKey, LogLevel? level, bool selected)
    {
        LabelKey = labelKey;
        Level = level;
        _isSelected = selected;
        _label = Loc.T(labelKey);
    }

    public string LabelKey { get; }

    public LogLevel? Level { get; }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public int Count
    {
        get => _count;
        set
        {
            if (!SetProperty(ref _count, value)) return;
            OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void RefreshLocalization() => Label = Loc.T(LabelKey);
}
