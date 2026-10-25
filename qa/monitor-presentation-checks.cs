#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/FusionNet.App/FusionNet.App.csproj

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FusionNet.App.Localization;
using FusionNet.App.Views.Security;
using FusionNet.App.Views.Tools;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

// Resources and synthetic rows only: no App.Run, scan, OS mutation, API or real services.
var failures=new List<string>();var checks=0;
var thread=new Thread(()=>
{
    CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
    var app=new FusionNet.App.App();app.InitializeComponent();
    Loc.Instance.SetLanguage("en",persist:false);
    var unknown=new ProcessMonitorRow(new(42,"synthetic",null,null,null,null,null,null,"Unavailable"),[],"Protected",true);
    Check("unknown resources have availability, not numeric units",()=>
    {
        Require(unknown.Resources.Contains("unavailable",StringComparison.OrdinalIgnoreCase),"English availability missing.");
        Require(!unknown.Resources.Contains('%')&&!unknown.Resources.Contains(" MB"),"Unknown values received numeric units.");
        Require(!unknown.Traffic.Contains("KB/s"),"Unknown transfer received a rate.");
    });
    var measured=new ProcessMonitorRow(new(43,"synthetic-browser",new(43,DateTimeOffset.UnixEpoch,@"C:\Synthetic\browser.exe"),12.5,123456789,3,1024,8192,"Synthetic","ValidCached"),[],null,true,DateTimeOffset.UnixEpoch);
    Check("measured resources keep actual values and units",()=>
        Require(measured.Resources.Contains("12.5%")&&measured.Resources.Contains("117.7 MB")&&measured.Traffic.Contains("1.00 KB/s")&&measured.Traffic.Contains("8.00 KB/s"),"Measured values changed."));
    Check("translation cannot make protected rows selectable",()=>Require(!unknown.CanSelect&&measured.CanSelect,"Eligibility changed."));
    var browser=new ProcessMonitorRow(measured.Process with{Name="chrome"},[],null,true);
    Check("English process descriptions remain advisory",()=>Require(browser.Purpose.Contains("Web browser")&&browser.Purpose.Contains("not identity or safety"),"Process purpose did not switch or lost its qualification."));
    Check("consent templates preserve exact target evidence",()=>
    {
        var target=@"C:\Synthetic\name{2} & games.exe";
        var text=Loc.Format("Monitor.Format.Target","synthetic",target,"rate-unavailable","rule-guid");
        Require(text.Contains(target)&&text.Contains("rule-guid")&&text.Contains("does not mean it is malicious"),"Consent changed target evidence or risk qualification.");
        var recovery=Loc.Format("Monitor.Format.RecoveryTask","synthetic-recovery-guid")+Loc.T("Monitor.Text.RecoveryLimits");
        foreach(var term in new[]{"SYSTEM","ProgramData","first rule","minute","Task Scheduler","helper only","DNS"})
            Require(recovery.Contains(term,StringComparison.OrdinalIgnoreCase),"Recovery disclosure missing: "+term);
    });
    Check("session time template formats instead of showing placeholders",()=>
        Require(Loc.Format("Monitor.Format.Session","Idle","Idle","0",TimeSpan.FromSeconds(65),"sample").Contains("00:01:05"),"Elapsed time format invalid."));
    Check("consent dialog preserves payload and has no default approval",()=>
    {
        var reviewType=typeof(LocalMonitorPanel).Assembly.GetType("FusionNet.App.Views.Security.ReportReviewWindow")!;
        var create=reviewType.GetMethod("Create",BindingFlags.NonPublic|BindingFlags.Static)??throw new Exception("Review layout cannot be checked without showing a dialog.");
        var payload="{\"synthetic\":\"مسار {1} & sample\"}";
        var title=Loc.T("Monitor.Text.ExportTitle");
        var dialog=(Window)create.Invoke(null,[title,payload,"Approve synthetic action"])!;
        var content=(FrameworkElement)dialog.Content;Layout(content);
        var buttons=Buttons(content).OfType<Button>().ToArray();
        Require(buttons.All(button=>!button.IsDefault),"Approval became a default action.");
        Require(buttons.Any(button=>button.IsCancel&&Equals(button.Content,"Cancel")),"Localized cancel action missing.");
        var text=Descendants<TextBox>(content).Single();
        Require(text.Text==payload&&text.IsReadOnly&&text.FlowDirection==FlowDirection.LeftToRight,"Consent payload was altered or made editable.");
        Require(dialog.FlowDirection==Loc.Instance.FlowDirection,"Dialog direction differs from the language.");
        var heading=Descendants<TextBlock>(content).Single(block=>block.Text==title);
        Require(heading.TextWrapping==TextWrapping.Wrap,"Data sensitivity notice only exists in a clipped window title.");
        foreach(var width in new[]{400,780})
        {
            content.Measure(new Size(width,520));content.Arrange(new Rect(0,0,width,520));content.UpdateLayout();
            Require(text.ActualHeight>100&&heading.ActualHeight>0&&heading.ActualWidth<=width,"Consent notice/payload overflow at compact size.");
            var bitmap=new RenderTargetBitmap(width,520,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create($"qa/screenshots/report-review-{width}-en-offline.png");encoder.Save(output);
        }
        dialog.Close();
    });
    Check("Arabic export review keeps notice visible at minimum width",()=>
    {
        app.MainWindow=null;Loc.Instance.SetLanguage("ar",persist:false);
        var create=typeof(LocalMonitorPanel).Assembly.GetType("FusionNet.App.Views.Security.ReportReviewWindow")!.GetMethod("Create",BindingFlags.NonPublic|BindingFlags.Static)!;
        var title=Loc.T("Monitor.Text.ExportTitle");
        var dialog=(Window)create.Invoke(null,[title,"{\"synthetic\":\"أدلة تجريبية فقط\"}",Loc.T("Monitor.Text.ExportAction")])!;
        var content=(FrameworkElement)dialog.Content;
        content.Measure(new Size(400,520));content.Arrange(new Rect(0,0,400,520));content.UpdateLayout();
        Require(dialog.FlowDirection==FlowDirection.RightToLeft&&Descendants<TextBlock>(content).Any(block=>block.Text==title&&block.ActualHeight>20),"Arabic notice/direction missing.");
        Require(Descendants<TextBox>(content).Single().ActualHeight>100&&Buttons(content).Any(button=>Equals(button.Content,"حفظ التقرير")),"Arabic payload/action not usable.");
        var bitmap=new RenderTargetBitmap(400,520,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create("qa/screenshots/report-review-400-ar-offline.png"))encoder.Save(output);
        dialog.Close();app.MainWindow=null;Loc.Instance.SetLanguage("en",persist:false);
    });
    var panel=new LocalMonitorPanel();
    Check("local export preserves evidence and nullable values without raw inventory",()=>
    {
        var finding=new SecurityFinding("synthetic-rule","Warning","Low","تحذير تجريبي",["synthetic evidence {1}"],"Verify locally; no automatic changes.");
        var report=new LocalMonitorReport(DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,[unknown.Process],
            [new(42,"TCP","192.0.2.10",123,"198.51.100.20",4444,"Established")],
            [new("Startup","Partial","synthetic coverage",[new("synthetic","registry","secret-raw-command")])],[finding]);
        var method=typeof(LocalMonitorViewModel).GetMethod("CreateExportJson",BindingFlags.Static|BindingFlags.NonPublic)!;
        var json=(string)method.Invoke(null,[report,"synthetic session","synthetic approval",null,null])!;
        using var document=JsonDocument.Parse(json);var result=document.RootElement;
        var alert=result.GetProperty("Findings")[0];
        Require(alert.GetProperty("Explanation").GetString()==finding.Explanation&&alert.GetProperty("Evidence")[0].GetString()==finding.Evidence[0]&&alert.GetProperty("Recommendation").GetString()==finding.Recommendation,"Finding context lost in export.");
        Require(result.GetProperty("Processes")[0].GetProperty("CpuPercent").ValueKind==JsonValueKind.Null,"Missing CPU became zero.");
        Require(!json.Contains("secret-raw-command")&&!json.Contains("198.51.100.20"),"Raw commands/endpoints entered the local export.");
        Require(AiAnalysis.BuildPayload(report).Contains("fusionnet-minimized-v1")&&!AiAnalysis.BuildPayload(report).Contains(finding.Evidence[0]),"Local evidence entered cloud input.");
    });
    Layout(panel);
    Check("monitor action is English",()=>Require(Buttons(panel).Any(button=>Equals(button.Content,"Scan device")),"English scan action missing."));
    Check("maintenance actions are a fixed allowlist without elevation",()=>
    {
        var info=typeof(FusionNet.App.Services.ExternalLinks).GetMethod("MaintenanceStartInfo",BindingFlags.NonPublic|BindingFlags.Static)!;
        var starts=new[]{"startup","apps","filehistory","windowsupdate"}.Select(action=>(System.Diagnostics.ProcessStartInfo)info.Invoke(null,[action])!).ToArray();
        Require(starts[0].FileName=="ms-settings:startupapps"&&starts[1].FileName=="ms-settings:appsfeatures"&&starts[2].FileName.EndsWith(@"\control.exe",StringComparison.OrdinalIgnoreCase)&&starts[2].Arguments=="/name Microsoft.FileHistory"&&starts[3].FileName=="ms-settings:windowsupdate","Maintenance target changed.");
        Require(starts.All(start=>start.Verb!="runas"&&start.UseShellExecute),"Maintenance start requested elevation.");
        var rejected=false;try{info.Invoke(null,["cmd /c del"]);}catch(TargetInvocationException ex)when(ex.InnerException is ArgumentException){rejected=true;}
        Require(rejected,"Unknown maintenance action was accepted.");
    });
    string[] launchKeys=["Monitor.Text.SectionAppUsage","Monitor.Ui.LaunchPrivacy","Monitor.Ui.UpdatesHint","Monitor.Ui.WindowsUpdateSettings"];
    var launchEnglish=launchKeys.Select(Loc.T).ToArray();
    Loc.Instance.SetLanguage("ar",persist:false);
    panel.UpdateLayout();
    Check("launch evidence and privacy copy are bilingual",()=>Require(launchKeys.Select(Loc.T).Zip(launchEnglish).All(pair=>pair.First!=pair.Second&&pair.First!=pair.Second.ToUpperInvariant()&&!pair.First.Contains("Monitor.")),"Launch evidence copy is missing a translation."));
    Check("same monitor view updates to Arabic",()=>Require(Buttons(panel).Any(button=>Equals(button.Content,"فحص الجهاز")),"Existing view did not switch language."));
    Check("unknown Arabic resources retain availability",()=>Require(unknown.Resources.Contains("غير متاح")&&!unknown.Resources.Contains('%'),"Arabic unknown value gained a unit."));
    var unused=DispatchProxy.Create<IAppServices,UnusedServices>();
    var toolsModel=new ToolsViewModel(unused);
    var tools=new ToolsView(toolsModel);
    Layout(tools);
    Loc.Instance.SetLanguage("en",persist:false);
    tools.UpdateLayout();
    Check("tools action is English",()=>Require(Buttons(tools).Any(button=>Equals(button.Content,"Inspect records")),"English DNS action missing."));
    Check("tools idle messages use current language",()=>Require(toolsModel.DnsStatus.Contains("No query")&&toolsModel.HardwareStatus.Contains("No sensors"),"Idle status remained in the old language."));
    Check("tool catalog URLs survive language changes",()=>
    {
        var english=ToolsViewModel.Catalog.Select(source=>source.Url).ToArray();
        Loc.Instance.SetLanguage("ar",persist:false);
        Require(english.SequenceEqual(ToolsViewModel.Catalog.Select(source=>source.Url)),"Translated catalog changed tool destinations.");
    });
    Check("tools language subscription does not duplicate or survive deactivation",()=>
    {
        toolsModel.Activate();toolsModel.Activate();
        var changed=0;toolsModel.PropertyChanged+=(_,_)=>changed++;
        Loc.Instance.SetLanguage("en",persist:false);
        Require(changed==1,"Duplicate or missing language subscription.");
        toolsModel.Deactivate();Loc.Instance.SetLanguage("ar",persist:false);
        Require(changed==1,"Deactivated model kept its language subscription.");
    });
});
thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
foreach(var failure in failures)Console.Error.WriteLine(failure);
Console.WriteLine($"Monitor/tools presentation: {checks-failures.Count}/{checks} passed; synthetic data only.");
return failures.Count==0?0:1;

void Check(string name,Action action){checks++;try{action();Console.WriteLine("PASS "+name);}catch(Exception ex){failures.Add("FAIL "+name+": "+ex.GetBaseException().Message);}}
static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
static void Layout(FrameworkElement view){view.Measure(new Size(1000,900));view.Arrange(new Rect(0,0,1000,900));view.UpdateLayout();}
static IEnumerable<ButtonBase> Buttons(DependencyObject parent)
{
    if(parent is ButtonBase button)yield return button;
    for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
        foreach(var child in Buttons(VisualTreeHelper.GetChild(parent,index)))yield return child;
}
static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
{
    if(parent is T found)yield return found;
    for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
        foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(parent,index)))yield return child;
}
public class UnusedServices:DispatchProxy
{
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new InvalidOperationException("Real service access is forbidden: "+method?.Name);
}
