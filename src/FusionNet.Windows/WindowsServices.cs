using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;
using FusionNet.Windows.Network;
using FusionNet.Windows.Orchestration;
using FusionNet.Windows.System;
using FusionNet.Windows.Monitoring;
using FusionNet.Core.Monitoring;

namespace FusionNet.Windows;

/// <summary>
/// Composition root: owns every service instance and exposes them through <see cref="IAppServices"/>.
/// The UI creates exactly one of these at startup and disposes it on exit.
/// </summary>
public sealed class WindowsServices : IAppServices, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly JournalService _journal;
    private readonly GameDetectionService _games;
    private readonly FusionCoordinator _fusion;
    private readonly ActivityLog _activity;
    private FusionSettings _settings;
    private bool _disposed;

    public WindowsServices(IFileSaveService files)
    {
        Files = files ?? throw new ArgumentNullException(nameof(files));

        _activity = new ActivityLog();
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _journal = new JournalService();
        _games = new GameDetectionService(_settingsStore);

        var elevation = new ElevationService(restarted: RequestExit);
        var systemInfo = new SystemInfoService();
        var firewall = new FirewallService(_activity);
        var windowsServices = new WindowsServiceController();
        var processes = new ProcessController(_activity);
        var tweaks = new RegistryTweakService(_activity);
        var power = new PowerPlanService(_activity);
        var memory = new StandbyMemoryService();
        var sessionStartGate = new SemaphoreSlim(1, 1);

        Ping = new PingMonitor(_activity);
        Throughput = new ThroughputMonitor();
        Adapters = new AdapterInfoService();
        Dns = new DnsService(_activity);
        PublicIp = new PublicIpService();
        Diagnostics = new DiagnosticsService(systemInfo, Adapters, tweaks, windowsServices, Ping, elevation, _activity);

        _fusion = new FusionCoordinator(
            () => _settings,
            _settingsStore,
            _journal,
            _activity,
            _games,
            firewall,
            windowsServices,
            processes,
            tweaks,
            power,
            memory,
            systemInfo,
            elevation,
            () => GameBoost is not null && GameBoost.Status.IsPending,
            sessionStartGate);

        SettingsStore = _settingsStore;
        Settings = _settings;
        Activity = _activity;
        Elevation = elevation;
        SystemInfo = systemInfo;
        Journal = _journal;
        Games = _games;
        Fusion = _fusion;
        Firewall = firewall;
        Services = windowsServices;
        Processes = processes;
        Tweaks = tweaks;
        Power = power;
        AutoStart = new AutoStartService();
        Memory = memory;
        Lockdown = new StrictLockdownService();

        _games.SetWatchList(GameCatalog.BuildWatchList(_settings));
        LocalMonitor = new WindowsLocalMonitor();
        SecurityAi = new ResponsesAiAnalyzer();
        ProcessTraffic = new WindowsTrafficSampler();
        LocalStorage = new LocalStorageAnalyzer();
        DnsInspector = new DnsRecordInspector();
        HardwareMonitor = new HardwareMonitor();
        GameBoost = new GameBoostSession(new WindowsGameBoostBackend(), () => _fusion.IsTransitioning || _fusion.IsArmed || _journal.HasPendingRestore, sessionStartGate);
    }

    /// <summary>Creates the fully wired service graph. Throws only on programming errors.</summary>
    public static WindowsServices Create(IFileSaveService files) => new(files);

    public ISettingsStore SettingsStore { get; }
    public FusionSettings Settings { get; private set; }
    public IActivityLog Activity { get; }
    public IElevationService Elevation { get; }
    public ISystemInfoService SystemInfo { get; }
    public IFusionCoordinator Fusion { get; }
    public IGameDetectionService Games { get; }
    public IJournal Journal { get; }
    public IFirewallService Firewall { get; }
    public IWindowsServiceController Services { get; }
    public IProcessController Processes { get; }
    public IRegistryTweakService Tweaks { get; }
    public IPowerPlanService Power { get; }
    public IAutoStartService AutoStart { get; }
    public IStandbyMemoryService Memory { get; }
    public IFirewallLockdownService Lockdown { get; }
    public IPingMonitor Ping { get; }
    public IThroughputMonitor Throughput { get; }
    public IAdapterInfoService Adapters { get; }
    public IDnsService Dns { get; }
    public IPublicIpService PublicIp { get; }
    public IDiagnosticsService Diagnostics { get; }
    public IFileSaveService Files { get; }
    public ILocalMonitor LocalMonitor { get; }
    public IGameBoostSession GameBoost { get; }
    public ISecurityAiAnalyzer SecurityAi { get; }
    public IProcessTrafficSampler ProcessTraffic { get; }
    public ILocalStorageAnalyzer LocalStorage { get; }
    public IDnsInspector DnsInspector { get; }
    public IHardwareMonitor HardwareMonitor { get; }

    public event EventHandler<FusionSettings>? SettingsChanged;

    /// <summary>Raised when a page or the tray asks the app to exit; the shell performs a clean shutdown.</summary>
    public event EventHandler? ExitRequested;

    public void ReloadSettings()
    {
        _settings = _settingsStore.Load();
        Settings = _settings;
        _games.SetWatchList(GameCatalog.BuildWatchList(_settings));
        SettingsChanged?.Invoke(this, _settings);
    }

    public void SaveSettings()
    {
        _settingsStore.Save(_settings);
        _games.SetWatchList(GameCatalog.BuildWatchList(_settings));
        SettingsChanged?.Invoke(this, _settings);
    }

    public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SafeStop(_fusion.StopAutomation);
        SafeStop(_games.Dispose);
        SafeStop(Ping.Dispose);
        SafeStop(Throughput.Dispose);
        SettingsChanged = null;
        ExitRequested = null;
    }

    private void SafeStop(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try { _activity.Warning("Shutdown", "A service did not shut down cleanly.", ex.Message); } catch { /* exiting */ }
        }
    }
}
