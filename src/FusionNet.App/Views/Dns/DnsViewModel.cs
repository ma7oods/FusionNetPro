using System.Collections.ObjectModel;
using System.Windows;
using FusionNet.App.Localization;
using FusionNet.App.Mvvm;
using FusionNet.App.Services;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Views.Dns;

/// <summary>
/// DNS &amp; Latency: inspect and change the resolver servers of the active adapter,
/// run a real benchmark against the provider catalogue, and read the public IP.
/// </summary>
public sealed class DnsViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private readonly List<DnsBenchmarkResult> _rawResults = [];

    private CancellationTokenSource? _benchmarkCts;
    private CancellationTokenSource? _publicIpCts;

    private bool _isActivated;
    private bool _isSubscribed;
    private bool _suppressAdapterSave;
    private bool _isBenchmarking;
    private bool _isPublicIpBusy;
    private double _benchmarkProgress;
    private string _benchmarkText = string.Empty;
    private AdapterInfo? _selectedAdapter;
    private DnsResultRow? _bestResult;
    private string _publicIpAddress = string.Empty;
    private string _publicIsp = string.Empty;
    private string _publicCountry = string.Empty;
    private string _publicIpError = string.Empty;
    private string _customDns1 = string.Empty;
    private string _customDns2 = string.Empty;
    private string _customDnsError = string.Empty;

    public DnsViewModel(IAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Adapters = [];
        Providers = [];
        Results = [];
        BuildProviders();

        _benchmarkText = Loc.T("Dns.BenchmarkIdle");

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(showToast: true));
        FlushDnsCommand = new AsyncRelayCommand(FlushDnsAsync);
        ResetAutomaticCommand = new AsyncRelayCommand(ResetAutomaticAsync);
        BenchmarkCommand = new AsyncRelayCommand(BenchmarkAsync, () => !IsBenchmarking);
        CancelBenchmarkCommand = new RelayCommand(CancelBenchmark, () => IsBenchmarking);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync);
        ApplyFastestCommand = new AsyncRelayCommand(ApplyFastestAsync, () => _bestResult is not null && !IsBenchmarking);
        ApplyCustomCommand = new AsyncRelayCommand(ApplyCustomAsync);
        SaveCustomDnsCommand = new RelayCommand(SaveCustomDns);
        RefreshPublicIpCommand = new AsyncRelayCommand(() => RefreshPublicIpAsync(showToast: true));
        RestartElevatedCommand = new RelayCommand(RestartElevated);
    }

    // ------------------------------------------------------------------ state
    public ObservableCollection<AdapterInfo> Adapters { get; }

    public ObservableCollection<DnsProviderRow> Providers { get; }

    public ObservableCollection<DnsResultRow> Results { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand FlushDnsCommand { get; }

    public AsyncRelayCommand ResetAutomaticCommand { get; }

    public AsyncRelayCommand BenchmarkCommand { get; }

    public RelayCommand CancelBenchmarkCommand { get; }

    public AsyncRelayCommand ApplySelectedCommand { get; }

    public AsyncRelayCommand ApplyFastestCommand { get; }

    public AsyncRelayCommand ApplyCustomCommand { get; }

    public RelayCommand SaveCustomDnsCommand { get; }

    public AsyncRelayCommand RefreshPublicIpCommand { get; }

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

    public string AdapterName => SelectedAdapter?.Name ?? Loc.T("Common.NotAvailable");

    public string AdapterDetail
    {
        get
        {
            if (SelectedAdapter is null) return Loc.T("Dns.NoAdapter");
            var badges = new List<string>
            {
                SelectedAdapter.IsWireless ? Loc.T("Dns.AdapterWireless") : Loc.T("Dns.AdapterWired")
            };
            if (!string.IsNullOrWhiteSpace(SelectedAdapter.Status)) badges.Add(SelectedAdapter.Status);
            if (SelectedAdapter.IsDefaultRoute) badges.Add(Loc.T("Dns.AdapterDefault"));
            return string.Join("  ·  ", badges);
        }
    }

    public string AdapterIpv4Text
    {
        get
        {
            var addresses = SelectedAdapter?.IPv4Addresses ?? [];
            return addresses.Count == 0 ? "—" : string.Join(", ", addresses);
        }
    }

    public string CurrentDnsText
    {
        get
        {
            if (SelectedAdapter is null) return Loc.T("Dns.NoAdapter");
            return SelectedAdapter.DnsServers.Count == 0
                ? Loc.T("Dns.NoDnsServers")
                : string.Join("  ·  ", SelectedAdapter.DnsServers);
        }
    }

    public string DnsModeText =>
        SelectedAdapter?.DnsServers.Count > 0 ? Loc.T("Dns.ModeStatic") : Loc.T("Dns.ModeAutomatic");

    public bool IsBenchmarking
    {
        get => _isBenchmarking;
        private set
        {
            if (!SetProperty(ref _isBenchmarking, value)) return;
            OnPropertyChanged(nameof(BenchmarkButtonText));
            BenchmarkCommand.RaiseCanExecuteChanged();
            CancelBenchmarkCommand.RaiseCanExecuteChanged();
            ApplyFastestCommand.RaiseCanExecuteChanged();
        }
    }

    public string BenchmarkButtonText => Loc.T(IsBenchmarking ? "Dns.Benchmarking" : "Dns.Benchmark");

    public double BenchmarkProgress
    {
        get => _benchmarkProgress;
        private set => SetProperty(ref _benchmarkProgress, value);
    }

    public string BenchmarkText
    {
        get => _benchmarkText;
        private set => SetProperty(ref _benchmarkText, value);
    }

    public DnsResultRow? BestResult
    {
        get => _bestResult;
        private set
        {
            if (!SetProperty(ref _bestResult, value)) return;
            OnPropertyChanged(nameof(BestSummary));
            OnPropertyChanged(nameof(HasBestResult));
            ApplyFastestCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasBestResult => _bestResult is not null;

    public string BestSummary => _bestResult is null
        ? Loc.T("Dns.NoBestYet")
        : Loc.Format("Dns.BestSummary", _bestResult.ProviderName, _bestResult.LatencyDisplay);

    public bool IsElevated => SafeIsElevated();

    public bool ShowElevationBanner => !IsElevated;

    public bool IsPublicIpBusy
    {
        get => _isPublicIpBusy;
        private set => SetProperty(ref _isPublicIpBusy, value);
    }

    public string PublicIpAddress
    {
        get => _publicIpAddress;
        private set
        {
            if (!SetProperty(ref _publicIpAddress, value)) return;
            OnPropertyChanged(nameof(HasPublicIp));
        }
    }

    public bool HasPublicIp => !string.IsNullOrWhiteSpace(_publicIpAddress);

    public string PublicIsp
    {
        get => _publicIsp;
        private set => SetProperty(ref _publicIsp, value);
    }

    public string PublicCountry
    {
        get => _publicCountry;
        private set => SetProperty(ref _publicCountry, value);
    }

    public string PublicIpError
    {
        get => _publicIpError;
        private set
        {
            if (!SetProperty(ref _publicIpError, value)) return;
            OnPropertyChanged(nameof(HasPublicIpError));
        }
    }

    public bool HasPublicIpError => !string.IsNullOrWhiteSpace(_publicIpError);

    public string CustomDns1
    {
        get => _customDns1;
        set => SetProperty(ref _customDns1, value);
    }

    public string CustomDns2
    {
        get => _customDns2;
        set => SetProperty(ref _customDns2, value);
    }

    public string CustomDnsError
    {
        get => _customDnsError;
        private set
        {
            if (!SetProperty(ref _customDnsError, value)) return;
            OnPropertyChanged(nameof(HasCustomDnsError));
        }
    }

    public bool HasCustomDnsError => !string.IsNullOrWhiteSpace(_customDnsError);

    // ------------------------------------------------------------------ lifecycle
    public async Task ActivateAsync()
    {
        _isActivated = true;
        if (!_isSubscribed)
        {
            _isSubscribed = true;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
        }

        LoadCustomDnsFromSettings();
        await RefreshAsync(showToast: false);
        await RefreshPublicIpAsync(showToast: false);
    }

    public void Deactivate()
    {
        _isActivated = false;
        if (_isSubscribed)
        {
            _isSubscribed = false;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        }

        CancelBenchmark();
        _publicIpCts?.Cancel();
    }

    public void OnSettingsChanged()
    {
        if (!_isActivated) return;
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(ShowElevationBanner));
        LoadCustomDnsFromSettings();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UiDispatcher.Post(ApplyLocalization);

    // ------------------------------------------------------------------ adapters
    private async Task RefreshAsync(bool showToast)
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

                var selected = adapters.FirstOrDefault(adapter =>
                                   string.Equals(adapter.Id, preferred, StringComparison.OrdinalIgnoreCase))
                               ?? adapters.FirstOrDefault(adapter => adapter.IsDefaultRoute)
                               ?? adapters.FirstOrDefault();

                SelectedAdapter = selected;
            }
            finally
            {
                _suppressAdapterSave = false;
            }

            OnPropertyChanged(nameof(HasAdapter));
            if (showToast) ToastService.Instance.Show(Loc.T("Dns.Refreshed"), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.T("Dns.RefreshFailed"), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Dns] adapter refresh failed: {ex}");
        }
    }

    private async Task FlushDnsAsync()
    {
        try
        {
            var result = await Task.Run(() => _services.Dns.FlushCache());
            ToastService.Instance.Show(
                result.Success ? Loc.T("Dns.FlushDone") : result.Message,
                result.Success ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Dns] flush failed: {ex}");
        }
    }

    private async Task ResetAutomaticAsync()
    {
        if (SelectedAdapter is null)
        {
            ToastService.Instance.Show(Loc.T("Dns.ApplyNeedsAdapter"), ToastKind.Warning);
            return;
        }

        var adapterName = AdapterName;
        if (!Confirm(Loc.T("Dns.ConfirmResetTitle"), Loc.Format("Dns.ConfirmReset", adapterName))) return;

        try
        {
            var adapterId = SelectedAdapter.Id;
            var result = await Task.Run(() => _services.Dns.ResetToAutomatic(adapterId, _services.Journal));
            if (!result.Success)
            {
                ToastService.Instance.Show(result.Message, ToastKind.Error);
                return;
            }

            ToastService.Instance.Show(Loc.T("Dns.ResetDone"), ToastKind.Success);
            await RefreshAsync(showToast: false);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Dns] reset failed: {ex}");
        }
    }

    private async Task ApplySelectedAsync()
    {
        var provider = Providers.FirstOrDefault(provider => provider.IsSelected)?.Provider;
        if (provider is null)
        {
            ToastService.Instance.Show(Loc.T("Dns.ApplyNeedsSelection"), ToastKind.Warning);
            return;
        }

        await ConfirmAndApplyAsync(provider.Name, provider.Servers);
    }

    private async Task ApplyFastestAsync()
    {
        if (BestResult is not { IsSuccess: true })
        {
            ToastService.Instance.Show(Loc.T("Dns.NoBestYet"), ToastKind.Warning);
            return;
        }

        await ConfirmAndApplyAsync(BestResult.ProviderName, GetFastestServers());
    }

    private string[] GetFastestServers() => _rawResults
        .Where(result => result.Success && result.ProviderId == BestResult?.ProviderId)
        .OrderBy(result => result.LatencyMs)
        .Select(result => result.Server)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private async Task ApplyCustomAsync()
    {
        if (!TryReadCustomServers(out var servers)) return;
        await ConfirmAndApplyAsync(Loc.T("Dns.CustomTitle"), servers);
    }

    private async Task ConfirmAndApplyAsync(string label, IReadOnlyList<string> servers)
    {
        if (SelectedAdapter is null)
        {
            ToastService.Instance.Show(Loc.T("Dns.ApplyNeedsAdapter"), ToastKind.Warning);
            return;
        }

        var message = Loc.Format("Dns.ConfirmApply", label, AdapterName, string.Join("  ·  ", servers));
        if (!Confirm(Loc.T("Dns.ConfirmApplyTitle"), message)) return;

        try
        {
            var adapterId = SelectedAdapter.Id;
            var result = await Task.Run(() => _services.Dns.ApplyServers(servers, adapterId, _services.Journal));
            if (!result.Success)
            {
                ToastService.Instance.Show(result.Message, ToastKind.Error);
                return;
            }

            try
            {
                await Task.Run(() => _services.Dns.FlushCache());
            }
            catch (Exception flushEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Dns] post-apply flush failed: {flushEx}");
            }

            ToastService.Instance.Show(Loc.T("Dns.ApplyDone"), ToastKind.Success);
            await RefreshAsync(showToast: false);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.T("Dns.ApplyFailed"), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Dns] apply failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ benchmark
    private async Task BenchmarkAsync()
    {
        if (IsBenchmarking) return;

        var selected = Providers.Where(provider => provider.IsSelected)
            .Select(provider => provider.Provider)
            .ToList();

        if (selected.Count == 0)
        {
            ToastService.Instance.Show(Loc.T("Dns.ApplyNeedsSelection"), ToastKind.Warning);
            return;
        }

        _rawResults.Clear();
        Results.Clear();
        BestResult = null;
        BenchmarkProgress = 0;
        IsBenchmarking = true;

        var total = selected.Sum(provider => provider.Servers.Count);
        BenchmarkText = Loc.Format("Dns.BenchmarkProgress", 0, total);
        _benchmarkCts = new CancellationTokenSource();
        var token = _benchmarkCts.Token;
        var completed = 0;

        var progress = new Progress<DnsBenchmarkResult>(result =>
        {
            if (!_isActivated) return;
            if (_rawResults.Any(existing =>
                    string.Equals(existing.ProviderId, result.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Server, result.Server, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            completed++;
            _rawResults.Add(result);
            BenchmarkProgress = total > 0 ? Math.Clamp(completed * 100.0 / total, 0, 100) : 0;
            BenchmarkText = Loc.Format("Dns.BenchmarkProgress", completed, total);
            PublishResults();
        });

        try
        {
            var finalResults = await Task.Run(
                () => _services.Dns.BenchmarkAsync(selected, progress, token),
                token);

            // The service's return value is authoritative; progress reports are advisory.
            _rawResults.Clear();
            if (finalResults is not null) _rawResults.AddRange(finalResults);
            PublishResults();
            var successCount = _rawResults.Count(result => result.Success);
            ToastService.Instance.Show(
                successCount > 0
                    ? Loc.Format("Dns.BenchmarkDone", successCount)
                    : Loc.T("Dns.BenchmarkNone"),
                successCount > 0 ? ToastKind.Success : ToastKind.Warning,
                durationMs: successCount > 0 ? 4200 : 6000);
        }
        catch (OperationCanceledException)
        {
            ToastService.Instance.Show(Loc.T("Dns.BenchmarkCanceled"), ToastKind.Info);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show(Loc.Format("Dns.BenchmarkFailed", ex.Message), ToastKind.Error);
            System.Diagnostics.Debug.WriteLine($"[Dns] benchmark failed: {ex}");
        }
        finally
        {
            IsBenchmarking = false;
            _benchmarkCts?.Dispose();
            _benchmarkCts = null;
        }
    }

    private void CancelBenchmark()
    {
        try
        {
            _benchmarkCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already finished
        }
    }

    private void PublishResults()
    {
        var sorted = _rawResults
            .OrderByDescending(result => result.Success)
            .ThenBy(result => result.Success ? result.LatencyMs : double.MaxValue)
            .ThenBy(result => result.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Results.Clear();
        DnsResultRow? best = null;
        for (var index = 0; index < sorted.Count; index++)
        {
            var row = new DnsResultRow(sorted[index], index + 1,
                DnsCatalog.Find(sorted[index].ProviderId), Loc.Instance.IsArabic);
            Results.Add(row);
            if (row.IsSuccess) best ??= row;
        }

        if (best is not null)
        {
            best.IsBest = true;
            BestResult = best;
        }
        else
        {
            BestResult = null;
        }
    }

    // ------------------------------------------------------------------ public IP
    private async Task RefreshPublicIpAsync(bool showToast)
    {
        if (IsPublicIpBusy) return;

        IsPublicIpBusy = true;
        PublicIpError = string.Empty;
        _publicIpCts?.Dispose();
        _publicIpCts = new CancellationTokenSource();
        var token = _publicIpCts.Token;

        try
        {
            var info = await _services.PublicIp.GetAsync(token);
            if (!_isActivated) return;

            if (info.Success)
            {
                PublicIpAddress = info.IpAddress!.Trim();
                PublicIsp = string.IsNullOrWhiteSpace(info.Isp) ? Loc.T("Common.Unknown") : info.Isp!.Trim();
                PublicCountry = string.IsNullOrWhiteSpace(info.Country) ? Loc.T("Common.Unknown") : info.Country!.Trim();
                PublicIpError = string.Empty;
                if (showToast) ToastService.Instance.Show(Loc.T("Dns.PublicIpRefreshed"), ToastKind.Success);
            }
            else
            {
                PublicIpAddress = string.Empty;
                PublicIsp = "—";
                PublicCountry = "—";
                PublicIpError = string.IsNullOrWhiteSpace(info.ErrorText)
                    ? Loc.T("Dns.PublicIpError")
                    : info.ErrorText!;
            }
        }
        catch (OperationCanceledException)
        {
            // page closed or superseded request
        }
        catch (Exception ex)
        {
            PublicIpError = Loc.T("Dns.PublicIpError");
            System.Diagnostics.Debug.WriteLine($"[Dns] public IP failed: {ex}");
        }
        finally
        {
            IsPublicIpBusy = false;
        }
    }

    // ------------------------------------------------------------------ custom DNS
    private void LoadCustomDnsFromSettings()
    {
        var saved = _services.Settings.CustomDnsServers;
        CustomDns1 = saved.Count > 0 ? saved[0] : string.Empty;
        CustomDns2 = saved.Count > 1 ? saved[1] : string.Empty;
    }

    private void SaveCustomDns()
    {
        if (!TryReadCustomServers(out var servers)) return;

        _services.Settings.CustomDnsServers = servers.ToList();
        _services.SaveSettings();
        CustomDnsError = string.Empty;
        ToastService.Instance.Show(Loc.T("Dns.CustomSaved"), ToastKind.Success);
    }

    private bool TryReadCustomServers(out List<string> servers)
    {
        servers = [];
        var first = (CustomDns1 ?? string.Empty).Trim();
        var second = (CustomDns2 ?? string.Empty).Trim();

        if (first.Length == 0 && second.Length == 0)
        {
            CustomDnsError = Loc.T("Dns.CustomEmpty");
            return false;
        }

        if (first.Length > 0 && !DnsCatalog.IsValidIPv4(first))
        {
            CustomDnsError = Loc.T("Dns.InvalidIp");
            return false;
        }

        if (second.Length > 0 && !DnsCatalog.IsValidIPv4(second))
        {
            CustomDnsError = Loc.T("Dns.InvalidIp");
            return false;
        }

        if (first.Length > 0) servers.Add(first);
        if (second.Length > 0 && !string.Equals(second, first, StringComparison.OrdinalIgnoreCase))
            servers.Add(second);

        if (servers.Count == 0)
        {
            CustomDnsError = Loc.T("Dns.CustomEmpty");
            return false;
        }

        CustomDnsError = string.Empty;
        return true;
    }

    // ------------------------------------------------------------------ helpers
    private bool SafeIsElevated()
    {
        try
        {
            return _services.Elevation.IsElevated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dns] elevation probe failed: {ex}");
            return false;
        }
    }

    private void RestartElevated()
    {
        if (_services.Elevation.TryRestartElevated()) return;
        ToastService.Instance.Show(Loc.T("Dns.ElevationCancelled"), ToastKind.Info);
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

    private void RaiseAdapterProperties() => RaiseAll(
        nameof(HasAdapter),
        nameof(AdapterName),
        nameof(AdapterDetail),
        nameof(AdapterIpv4Text),
        nameof(CurrentDnsText),
        nameof(DnsModeText));

    private void BuildProviders()
    {
        Providers.Clear();
        var arabic = Loc.Instance.IsArabic;
        foreach (var provider in DnsCatalog.All)
        {
            Providers.Add(new DnsProviderRow(provider, arabic, provider.Recommended));
        }
    }

    private void ApplyLocalization()
    {
        var arabic = Loc.Instance.IsArabic;
        foreach (var provider in Providers) provider.RefreshLocalization(arabic);
        RaiseAdapterProperties();
        OnPropertyChanged(nameof(BestSummary));
        OnPropertyChanged(nameof(BenchmarkButtonText));
        if (!IsBenchmarking && _rawResults.Count == 0) BenchmarkText = Loc.T("Dns.BenchmarkIdle");
        PublishResults();
    }
}

/// <summary>One selectable resolver row in the benchmark card.</summary>
public sealed class DnsProviderRow : ViewModelBase
{
    private bool _isSelected;
    private string _description;

    public DnsProviderRow(DnsProvider provider, bool arabic, bool selected)
    {
        Provider = provider;
        _description = arabic ? provider.DescriptionAr : provider.DescriptionEn;
        _isSelected = selected;
    }

    public DnsProvider Provider { get; }

    public string Name => Provider.Name;

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public bool IsRecommended => Provider.Recommended;

    public string ServersText => string.Join("  ·  ", Provider.Servers);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void RefreshLocalization(bool arabic) =>
        Description = arabic ? Provider.DescriptionAr : Provider.DescriptionEn;
}

/// <summary>One ranked benchmark result row.</summary>
public sealed class DnsResultRow : ViewModelBase
{
    private bool _isBest;

    public DnsResultRow(DnsBenchmarkResult result, int rank, DnsProvider? provider, bool arabic)
    {
        Result = result;
        Rank = rank;
        ProviderId = result.ProviderId;
        ProviderName = string.IsNullOrWhiteSpace(result.ProviderName)
            ? provider?.Name ?? result.ProviderId
            : result.ProviderName;
        Server = result.Server;
        IsSuccess = result.Success;

        LatencyDisplay = result.Success
            ? Humanize.Ms(result.LatencyMs, result.LatencyMs < 10 ? 2 : 1)
            : Loc.T("Dns.StatusTimeout");
        StatusLabel = result.Success ? Loc.T("Dns.StatusOk") : Loc.T("Dns.StatusTimeout");

        var score = result.Score > 0
            ? result.Score
            : result.Success
                ? Math.Clamp((int)Math.Round(100 - result.LatencyMs * 0.9, MidpointRounding.AwayFromZero), 1, 100)
                : 0;
        Score = score;
        ScoreText = result.Success ? score.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
        _ = arabic; // localized labels are resolved through Loc above
    }

    public DnsBenchmarkResult Result { get; }

    public string ProviderId { get; }

    public string ProviderName { get; }

    public string Server { get; }

    public int Rank { get; }

    public string RankText => $"#{Rank}";

    public bool IsSuccess { get; }

    public string LatencyDisplay { get; }

    public string StatusLabel { get; }

    public int Score { get; }

    public string ScoreText { get; }

    public bool IsBest
    {
        get => _isBest;
        set => SetProperty(ref _isBest, value);
    }
}
