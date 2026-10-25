using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using FusionNet.App.Localization;
using FusionNet.App.Shell;
using FusionNet.App.Styles;
using FusionNet.App.Services;

namespace FusionNet.App.Views.Security;

public partial class LocalMonitorPanel : UserControl
{
    public LocalMonitorPanel() => InitializeComponent();
    private static void Open(string url)
    {if(!ExternalLinks.TryOpen(url))ToastService.Instance.Show(Loc.T("Monitor.Text.BrowserFailed"),ToastKind.Warning);}
    private void OpenAutoruns(object sender, RoutedEventArgs e) => Open("https://learn.microsoft.com/sysinternals/downloads/autoruns");
    private void OpenProcessExplorer(object sender, RoutedEventArgs e) => Open("https://learn.microsoft.com/sysinternals/downloads/process-explorer");
    private void OpenHardware(object sender, RoutedEventArgs e) => Open("https://github.com/LibreHardwareMonitor/LibreHardwareMonitor");
    private static void OpenMaintenance(string action)
    {if(!ExternalLinks.TryOpenMaintenance(action))ToastService.Instance.Show(Loc.T("Monitor.Text.MaintenanceFailed"),ToastKind.Warning);}
    private void OpenStartup(object sender,RoutedEventArgs e)=>OpenMaintenance("startup");
    private void OpenInstalledApps(object sender,RoutedEventArgs e)=>OpenMaintenance("apps");
    private void OpenFileHistory(object sender,RoutedEventArgs e)=>OpenMaintenance("filehistory");
    private void OpenWindowsUpdate(object sender,RoutedEventArgs e)=>OpenMaintenance("windowsupdate");
    private void OpenLicenses(object sender, RoutedEventArgs e)
    {
        using var stream = typeof(LocalMonitorPanel).Assembly.GetManifestResourceStream("FusionNet.App.Assets.THIRD-PARTY-NOTICES.txt")!;
        using var reader = new global::System.IO.StreamReader(stream);
        ReportReviewWindow.Approve(Loc.T("Monitor.Ui.Licenses"), reader.ReadToEnd(), Loc.T("Common.Close"));
    }
    private void SaveAiProvider(object sender, RoutedEventArgs e)
    {
        try { ((LocalMonitorViewModel)DataContext).SaveAiProvider(ApiKeyInput.Password); }
        catch (Exception ex) { ToastService.Instance.Show(ex.Message, ToastKind.Error); }
        finally { ApiKeyInput.Clear(); }
    }
    private void RemoveAiKey(object sender, RoutedEventArgs e)
    {
        try { ((LocalMonitorViewModel)DataContext).RemoveAiKey(); ApiKeyInput.Clear(); }
        catch (Exception ex) { ToastService.Instance.Show(ex.Message, ToastKind.Error); }
    }
}

public abstract class LocalMonitorPage : PageBase
{
    private readonly LocalMonitorViewModel _model;
    protected LocalMonitorPage(bool security)
    {
        _model = new LocalMonitorViewModel(AppServices.Current, security);
        Content = new LocalMonitorPanel { DataContext = _model };
    }
    public override Task OnActivatedAsync() { _model.Activate(); return Task.CompletedTask; }
    public override void OnDeactivated() => _model.Deactivate();
}
[PageInfo("gameboost", "Page.GameBoost", Glyphs.Rocket, order: 25, groupKey: "Nav.Group.Control")]
public sealed class GameBoostView() : LocalMonitorPage(false);
[PageInfo("security", "Page.Security", Glyphs.Shield, order: 65, groupKey: "Nav.Group.Insights")]
public sealed class SecurityMonitorView() : LocalMonitorPage(true);
internal static class MonitorStrings
{
    [ModuleInitializer] internal static void Register()
    {
        Loc.Add("Page.GameBoost", "Game Boost", "Game Boost — وضع اللعب");
        Loc.Add("Page.Security", "Security Monitor", "Security Monitor — الأمان");
    }
}
