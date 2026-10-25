#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/FusionNet.App/FusionNet.App.csproj

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FusionNet.App.Localization;
using FusionNet.App.Views.Tools;
using FusionNet.App.Services;
using FusionNet.Core.Models;

// Resource initialization only; no App.Run, application startup, real services or commands.
Exception? failure = null;
foreach(var address in new[]{"file:///C:/bad.exe","http://example.test","https://user:secret@example.test","not a link"})
    if(ExternalLinks.TryOpen(address))throw new Exception("Untrusted link was accepted.");
var thread = new Thread(() =>
{
    try
    {
        var application = new FusionNet.App.App();
        application.InitializeComponent();
        Loc.Instance.SetLanguage("ar", persist: false);
        var trace = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(trace));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        Directory.CreateDirectory("qa/screenshots");

        foreach (var width in new[] { 1250, 790 })
        {
            var tools = new ToolsView(new ToolsFixture());
            Capture(tools, $"tools-{width}", width, application, includeBottom: true);

            var dashboard = LoadLayout("src/FusionNet.App/Views/Dashboard/DashboardView.xaml");
            dashboard.DataContext = new DashboardFixture();
            Capture(dashboard, $"dashboard-{width}", width, application, includeBottom: true);
        }

        foreach (var width in new[] { 1500, 1080 })
        {
            var dashboard = LoadLayout("src/FusionNet.App/Views/Dashboard/DashboardView.xaml");
            dashboard.DataContext = new DashboardFixture();
            var shell = LoadLayout("src/FusionNet.App/MainWindow.xaml");
            shell.DataContext = new ShellFixture(dashboard);
            Capture(shell, $"shell-{width}", width, application, includeBottom: false);
        }

        Loc.Instance.SetLanguage("en",persist:false);
        if(Loc.T("Dash.ReviewSession")!="Review gaming session" || Loc.T("Shell.PrepareGame")!="Prepare gaming session")
            throw new Exception("New shell/dashboard copy did not switch to English.");
        foreach(var width in new[]{1250,790})
        {
            var englishTools=new ToolsView(new ToolsFixture());
            Capture(englishTools,$"tools-{width}-en",width,application,includeBottom:true);
        }
        var englishDashboard=LoadLayout("src/FusionNet.App/Views/Dashboard/DashboardView.xaml");
        englishDashboard.DataContext=new DashboardFixture();
        var englishShell=LoadLayout("src/FusionNet.App/MainWindow.xaml");
        englishShell.DataContext=new ShellFixture(englishDashboard);
        Capture(englishShell,"shell-1080-en",1080,application,includeBottom:false);

        if (trace.ToString().Contains("Error:", StringComparison.Ordinal))
            throw new Exception("Offline fixture binding errors: " + trace);
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null) global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
Console.WriteLine("Offline Tools, dashboard and shell renders passed. Synthetic data only; no scans, network requests, ETW, Firewall, UAC, API or installation.");
return 0;

static UserControl LoadLayout(string path)
{
    XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var document = XDocument.Load(path);
    var root = document.Root ?? throw new Exception("Missing fixture XAML root.");
    var originalRootName = root.Name.LocalName;
    if (originalRootName is not ("Window" or "PageBase")) throw new Exception("Unexpected fixture XAML root.");
    root.Name = wpf + "UserControl";

    foreach (var element in root.DescendantsAndSelf().ToArray())
    {
        if(element.Name.NamespaceName.StartsWith("clr-namespace:",StringComparison.Ordinal) &&
            !element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))
            element.Name=XNamespace.Get(element.Name.NamespaceName+";assembly=FusionNetPro")+element.Name.LocalName;
        if (element.Name.LocalName == "WindowChrome.WindowChrome" || element.Name.LocalName == "ToastHost")
        {
            element.Remove();
            continue;
        }
        if (element.Name.LocalName == originalRootName + ".Resources") element.Name = wpf + "UserControl.Resources";
        foreach (var attribute in element.Attributes().ToArray())
        {
            if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:", StringComparison.Ordinal) &&
                !attribute.Value.Contains(";assembly=", StringComparison.Ordinal))
                attribute.Value += ";assembly=FusionNetPro";
            else if (attribute.Name == x + "Class" || attribute.Name.LocalName is "Click" or "Loaded" or "Unloaded" ||
                     attribute.Name.LocalName.StartsWith("WindowChrome.", StringComparison.Ordinal))
                attribute.Remove();
            else if (attribute.Name.LocalName == "Source" && attribute.Value == "/Assets/logo.png")
                attribute.Value = "pack://application:,,,/FusionNetPro;component/Assets/logo.png";
        }
    }
    foreach (var attribute in root.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration &&
                 attribute.Name.LocalName is "Title" or "Width" or "Height" or "MinWidth" or "MinHeight" or
                     "WindowStartupLocation" or "WindowStyle" or "ResizeMode").ToArray()) attribute.Remove();
    // Never instantiate the real MainWindow or DashboardView constructors and their services.
    return (UserControl)XamlReader.Parse(document.ToString());
}

static void Capture(UserControl panel, string label, int width, Application application, bool includeBottom)
{
    const int height = 900;
    panel.Width = width;
    panel.Height = height;
    panel.Background = (Brush)application.FindResource("Brush.Bg");
    panel.Measure(new Size(width, height));
    panel.Arrange(new Rect(0, 0, width, height));
    panel.UpdateLayout();
    if (panel.FlowDirection != Loc.Instance.FlowDirection) throw new Exception($"Language direction differs: {label}.");
    var scrollBar=Find<global::System.Windows.Controls.Primitives.ScrollBar>(panel);
    if(scrollBar is not null && scrollBar.Template.FindName("PART_Track",scrollBar) is global::System.Windows.Controls.Primitives.Track track)
    {
        if(track.Orientation!=scrollBar.Orientation)throw new Exception("Scroll track orientation differs from its control.");
        if(track.Thumb?.Template.FindName("Root",track.Thumb) is Border thumb && thumb.CornerRadius.TopLeft>4)
            throw new Exception("Oversized scrollbar radius distorts the scroll thumb.");
    }
    foreach(var key in new[]{"Btn.Primary","Btn.Ghost","Btn.Nav","Workspace.Tab","Input.TextBox","Input.ComboBox","Input.CheckBox"})
    {
        var style=(Style)application.FindResource(key);
        while(style is not null && !style.Setters.OfType<Setter>().Any(setter=>setter.Property==Control.FocusVisualStyleProperty && setter.Value is not null))style=style.BasedOn;
        if(style is null)throw new Exception("Keyboard focus style missing: "+key);
    }
    var controlBorder=((SolidColorBrush)application.FindResource("Brush.ControlBorder")).Color;
    foreach(var surface in new[]{"Brush.Bg","Brush.Surface","Brush.SurfaceAlt"})
    {
        var background=((SolidColorBrush)application.FindResource(surface)).Color;
        if(Contrast(controlBorder,background)<3)throw new Exception("Enabled input boundary contrast below 3:1 on "+surface);
        foreach(var text in new[]{"Brush.Text","Brush.TextMuted","Brush.TextDim"})
            if(Contrast(((SolidColorBrush)application.FindResource(text)).Color,background)<4.5)
                throw new Exception("Text contrast below 4.5:1: "+text+" on "+surface);
    }
    Save(panel, label, width, height);
    if (!includeBottom) return;
    var scroll = Find<ScrollViewer>(panel) ?? throw new Exception($"Scroll viewer missing: {label}.");
    scroll.ScrollToEnd();
    panel.UpdateLayout();
    Save(panel, label + "-bottom", width, height);
}

static double Contrast(Color first,Color second)
{
    static double Channel(byte value){var component=value/255d;return component<=0.04045?component/12.92:Math.Pow((component+0.055)/1.055,2.4);}
    static double Luminance(Color color)=>0.2126*Channel(color.R)+0.7152*Channel(color.G)+0.0722*Channel(color.B);
    var a=Luminance(first);var b=Luminance(second);
    return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
}

static void Save(FrameworkElement panel, string label, int width, int height)
{
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(panel);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var file = File.Create($"qa/screenshots/{label}-offline.png");
    encoder.Save(file);
}

static T? Find<T>(DependencyObject parent) where T : DependencyObject
{
    if (parent is T found) return found;
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        if (Find<T>(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
    return null;
}

public sealed class ToolsFixture
{
    public string Host { get; set; } = "example.test";
    public string Server { get; set; } = "192.0.2.53";
    public string RecordType { get; set; } = "TXT";
    public string[] RecordTypes { get; } = ["A", "AAAA", "CNAME", "MX", "TXT", "NS"];
    public string DnsStatus => "بيانات تصميم اصطناعية؛ لم يُرسل أي استعلام DNS. زمن المثال ليس بنق اللعبة.";
    public string HardwareStatus => "عينة تصميم مصطنعة من المصدر الخارجي؛ لا حساسات حقيقية لهذا الجهاز.";
    public string SourceStatus=>"المصادر الرسمية؛ أزرار التجربة غير متصلة بأوامر تشغيل.";
    public DnsRecordObservation[] Records { get; } =
    [
        new("example.test.", "A", 300, "192.0.2.10"),
        new("example.test.", "TXT", 60, "Synthetic UI fixture only. This deliberately long record checks wrapping without contacting a DNS server or changing adapter settings.")
    ];
    public SensorRow[] Sensors { get; } =
    [
        new(new("حرارة المعالج — بيانات مصطنعة", "Temperature", 55.5, "°C", "/synthetic/cpu/temperature/0")),
        new(new("استهلاك البطاقة — بيانات مصطنعة", "Load", 23.25, "%", "/synthetic/gpu/load/0")),
        new(new("سرعة المروحة — بيانات مصطنعة", "Fan", 1200, "RPM", "/synthetic/fan/0"))
    ];
    public IReadOnlyList<ToolSource> Sources => ToolsViewModel.Catalog;
    public object? LookupCommand => null;
    public object? HardwareCommand => null;
    public object? CancelCommand => null;
    public object? OpenSourceCommand => null;
}

public sealed class DashboardFixture
{
    public string GameBoostTitle => "جاهز لمراجعة جلسة اللعب";
    public string GameBoostDetail => "بيانات تصميم اصطناعية فقط. لا توجد قواعد أو جلسة فعلية؛ ابدأ بالقياس ثم المراجعة.";
    public string StateTitle => "في الانتظار";
    public string StateBody => "لم يُغيَّر أي شيء على حاسوبك. يتفعّل وضع Fusion لحاله لما تبدي لعبة مراقبة، أو فعّله الآن.";
    public string EngageLabel => "تفعيل وضع Fusion";
    public string EngageTooltip => "طبّق خطة وضع Fusion الكاملة الآن";
    public bool IsElevated => false;
    public string PingValue => "39";
    public string PingQuality => "عينة مصطنعة";
    public string JitterValue => "2.5";
    public string JitterQuality => "عينة مصطنعة";
    public string LossValue => "0";
    public string LossQuality => "عينة مصطنعة";
    public string DownloadValue => "128";
    public string DownloadUnit => "KB/s";
    public string DownloadQuality => "عينة مصطنعة";
    public string DownloadRate => "128 KB/s";
    public string UploadRate => "24 KB/s";
    public bool HasThroughput => true;
    public bool HasSelectedData => true;
    public bool HasEnforcements => false;
    public bool IsArmed => false;
    public bool HasActivity => true;
    public bool HasDetectedGame => false;
    public bool AutoEngageOnGameStart { get; set; } = true;
    public string DetectedGameGlyph => "\uE7FC";
    public string DetectedGameName => "لا اكتشاف ألعاب أثناء اختبار الواجهة";
    public string DetectedGameMeta => "لا قراءة للعمليات الحقيقية؛ fixture فقط.";
    public string SelectedAvg => "39";
    public string SelectedMin => "37";
    public string SelectedMax => "43";
    public string SelectedJitter => "2.5 ms";
    public string SelectedLoss => "0%";
    public string SelectedTarget => "192.0.2.1";
    public string SelectedQuality => "مصطنع";
    public double[] PingHistory => [37, 40, 38, 39, 42, 41, 39];
    public double[] JitterHistory => [2, 3, 2, 2.5, 3, 2, 2.5];
    public double[] LossHistory => [0, 0, 0, 0, 0, 0, 0];
    public double[] DownloadHistory => [8000, 12000, 20000, 15000, 25000, 18000, 22000];
    public double[] UploadHistory => [1000, 2500, 1200, 1800, 2000, 1300, 2100];
    public double[] SelectedTargetHistory => PingHistory;
    public TargetFixture[] Targets { get; } = [new()];
    public object[] Enforcements => [];
    public ActivityFixture[] Activities { get; } = [new()];
    public object? RefreshCommand => null;
    public object? OpenGameBoostCommand => null;
    public object? OpenSecurityCommand => null;
    public object? OpenToolsCommand => null;
    public object? ToggleFusionCommand => null;
    public object? ViewActivityCommand => null;
}

public sealed class TargetFixture
{
    public string Target => "192.0.2.1";
    public bool IsSelected => true;
    public object? SelectCommand => null;
}

public sealed class ActivityFixture
{
    public string Time => "12:34:56";
    public string Message => "سجل تصميم اصطناعي؛ لم يُطبّق أي تغيير.";
    public LogLevel Level => LogLevel.Info;
}

public sealed class ShellFixture(object page)
{
    public string CurrentPageTitle => "لوحة القيادة — معاينة مصطنعة";
    public string GameBoostStateText => "لا جلسة — قراءة فقط";
    public string ElevationText => "صلاحيات مستخدم عادي";
    public string LanguageTooltip => "تبديل اللغة";
    public string LanguageToggleText => "EN";
    public string FusionButtonText => "مراجعة Fusion المتقدم";
    public string VersionText => "3.3.3 · UI fixture";
    public bool IsLimitedMode => true;
    public bool HasPendingRestore => false;
    public string PendingRestoreText => "لا تغييرات في fixture";
    public object CurrentPage => page;
    public NavGroupFixture[] NavGroups { get; } =
    [
        new("التحكم", [new("لوحة القيادة", "\uE80F", true), new("Game Boost", "\uE7FC", false), new("الألعاب والملفات", "\uE713", false)]),
        new("المراقبة والتشخيص", [new("مراقب الأمان", "", false), new("DNS وزمن الاستجابة", "\uE774", false)]),
        new("النظام", [new("سجل النشاط", "\uE81C", false), new("الإعدادات", "\uE713", false)])
    ];
    public object? RestartElevatedCommand => null;
    public object? ToggleLanguageCommand => null;
    public object? OpenSettingsCommand => null;
    public object? OpenGameBoostCommand => null;
    public object? ToggleFusionCommand => null;
    public object? RestoreSessionCommand => null;
}

public sealed record NavGroupFixture(string Title, NavItemFixture[] Items);
public sealed class NavItemFixture(string title, string glyph, bool selected)
{
    public string Title => title;
    public string Glyph => glyph;
    public bool IsSelected { get; set; } = selected;
}
