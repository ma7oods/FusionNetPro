using FusionNet.Core.Models;

namespace FusionNet.Core.Abstractions;

/// <summary>
/// The single service aggregate every view model receives. Implemented once by
/// <c>FusionNet.Windows.WindowsServices</c> and exposed through <c>AppServices.Current</c>.
/// </summary>
public interface IAppServices
{
    ISettingsStore SettingsStore { get; }
    FusionSettings Settings { get; }
    IActivityLog Activity { get; }
    IElevationService Elevation { get; }
    ISystemInfoService SystemInfo { get; }
    IFusionCoordinator Fusion { get; }
    IGameDetectionService Games { get; }
    IJournal Journal { get; }
    IFirewallService Firewall { get; }
    IWindowsServiceController Services { get; }
    IProcessController Processes { get; }
    IRegistryTweakService Tweaks { get; }
    IPowerPlanService Power { get; }
    IAutoStartService AutoStart { get; }
    IStandbyMemoryService Memory { get; }
    IFirewallLockdownService Lockdown { get; }
    IPingMonitor Ping { get; }
    IThroughputMonitor Throughput { get; }
    IAdapterInfoService Adapters { get; }
    IDnsService Dns { get; }
    IPublicIpService PublicIp { get; }
    IDiagnosticsService Diagnostics { get; }
    IFileSaveService Files { get; }
    ILocalMonitor LocalMonitor { get; }
    IGameBoostSession GameBoost { get; }
    Monitoring.ISecurityAiAnalyzer SecurityAi { get; }
    IProcessTrafficSampler ProcessTraffic { get; }
    Monitoring.ILocalStorageAnalyzer LocalStorage { get; }
    IDnsInspector DnsInspector { get; }
    IHardwareMonitor HardwareMonitor { get; }

    /// <summary>Replaces <see cref="Settings"/> with a re-loaded copy and raises SettingsChanged.</summary>
    void ReloadSettings();

    /// <summary>Persists the current <see cref="Settings"/> and raises SettingsChanged.</summary>
    void SaveSettings();

    event EventHandler<FusionSettings>? SettingsChanged;

    /// <summary>Asks the shell to shut down cleanly (used by tray menu and settings page).</summary>
    void RequestExit();
}
