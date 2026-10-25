#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/FusionNet.App/FusionNet.App.csproj

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using FusionNet.App.Localization;
using FusionNet.App.Views.Security;
using FusionNet.Core.Models;

Exception? error = null;
var thread = new Thread(() =>
{
    try
    {
        // Initialize XAML resources only. Never Run the application or create WindowsServices.
        var application = new FusionNet.App.App();
        application.InitializeComponent();
        Loc.Instance.SetLanguage("ar",persist:false);
        var output = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(output));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        Directory.CreateDirectory("qa/screenshots");
        foreach(var language in new[]{"ar","en"})
        foreach (var security in new[] { false,true })
        foreach (var width in new[]{1250,790})
        {
            Loc.Instance.SetLanguage(language,persist:false);
            var languageSuffix=language=="ar"?"":"-en";
            var panel = new LocalMonitorPanel { DataContext = new Fixture(security), Width=width, Height=900,Background=(Brush)application.FindResource("Brush.Bg") };
            panel.Measure(new Size(width,900)); panel.Arrange(new Rect(0,0,width,900)); panel.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width,900,96,96,PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create("qa/screenshots/"+(security?"security":"gameboost")+(width==790?"-narrow":"")+languageSuffix+"-offline.png"); encoder.Save(file);
            ((global::System.Windows.Controls.ScrollViewer)panel.Content).ScrollToEnd();
            panel.UpdateLayout();
            var bottom = new RenderTargetBitmap(width,900,96,96,PixelFormats.Pbgra32); bottom.Render(panel);
            var bottomEncoder = new PngBitmapEncoder(); bottomEncoder.Frames.Add(BitmapFrame.Create(bottom));
            using var bottomFile = File.Create("qa/screenshots/"+(security?"security":"gameboost")+(width==790?"-narrow":"")+languageSuffix+"-bottom-offline.png"); bottomEncoder.Save(bottomFile);
            var tabs=Find<TabControl>(panel)!;
            for(var index=0;index<tabs.Items.Count;index++)
            {
                if(((TabItem)tabs.Items[index]).Visibility!=Visibility.Visible)continue;
                tabs.SelectedIndex=index;panel.UpdateLayout();
                ((ScrollViewer)panel.Content).ScrollToTop();tabs.BringIntoView();panel.UpdateLayout();
                var frame=new RenderTargetBitmap(width,900,96,96,PixelFormats.Pbgra32);frame.Render(panel);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));
                using var shot=File.Create($"qa/screenshots/{(security?"security":"gameboost")}-{width}-tab{index}{languageSuffix}-offline.png");png.Save(shot);
            }
            if(panel.FlowDirection!=Loc.Instance.FlowDirection) throw new Exception("RTL binding missing.");
        }
        Loc.Instance.SetLanguage("ar",persist:false);
        foreach(var state in new[]{"empty","busy","error"})
        {
            var fixture=new Fixture(false,state);
            var panel=new LocalMonitorPanel{DataContext=fixture,Width=790,Height=900,Background=(Brush)application.FindResource("Brush.Bg")};
            panel.Measure(new Size(790,900));panel.Arrange(new Rect(0,0,790,900));panel.UpdateLayout();
            var busy=Find<ProgressBar>(panel)??throw new Exception("Missing busy state.");
            if(busy.Visibility!=(fixture.IsBusy?Visibility.Visible:Visibility.Collapsed))throw new Exception("Busy visibility differs from model.");
            var rows=Find<ListBox>(panel)??throw new Exception("Missing process list.");
            if(rows.Items.Count!=fixture.Processes.Length)throw new Exception("Process rows differ from model.");
            if(!fixture.Processes.Any() && !fixture.HasNoProcesses)throw new Exception("Empty state suppressed.");
            var bitmap=new RenderTargetBitmap(790,900,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create($"qa/screenshots/gameboost-{state}-offline.png");encoder.Save(file);
        }
        if(output.ToString().Contains("Error:")) throw new Exception("Binding errors: "+output);
    }
    catch(Exception ex) { error=ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if(error is not null) throw error;
Console.WriteLine("Offline WPF renders passed; synthetic data only. No application runtime, inventory, ETW, Firewall, UAC or API invoked.");
return 0;

static T? Find<T>(DependencyObject parent) where T:DependencyObject
{if(parent is T found)return found;for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)if(Find<T>(VisualTreeHelper.GetChild(parent,index)) is {} child)return child;return null;}

public sealed class Fixture(bool security,string state="populated")
{
    public string Title => Loc.T(security?"Monitor.Text.SecurityTitle":"Monitor.Text.GameTitle");
    public bool IsSecurity => security;
    public bool IsGame => !security;
    public string Status => state switch
    {
        "busy"=>"فحص اصطناعي قيد التنفيذ — لا فحص حقيقي لهذا الجهاز.",
        "error"=>"تعذّر الوصول إلى مصدر بيانات اصطناعي. أُبقيت القراءة غير متاحة؛ أعد المحاولة أو راجع مصادر الفحص. لا يوجد تغيير على الجهاز.",
        _=>"بيانات اختبار مصطنعة للتصميم فقط — ليست قياسات لهذا الجهاز."
    };
    public string Summary => "العمليات: 2 | الاتصالات: 3 | عينة توضيحية";
    public string ProcessCount=>state=="empty"?"—":"2";
    public string MeasuredCount=>"1";
    public string AlertCount=>"1";
    public string SelectedCount=>"0";
    public string ContextMetricLabel=>security?"مصادر متاحة":"تطبيقات اخترتها";
    public string ContextMetricValue=>"0";
    public bool IsBusy=>state=="busy";
    public bool HasNoProcesses=>state=="empty";
    public string EmptyMessage=>"بيانات تصميم اصطناعية";
    public string Search{get;set;}="";
    public bool OnlyHighResource{get;set;}
    public string Session => "الحالة: قراءة فقط — لا قواعد أو تغييرات فعلية.";
    public string ApprovedChanges => "لا توجد معاينة موافق عليها.";
    public int Minutes {get;set;}=60;
    public bool OnlyMeasuredActive {get;set;}
    public object? ScanCommand=>null;
    public object? CancelCommand=>null;
    public object? StartCommand=>null;
    public object? StopCommand=>null;
    public object? ExportCommand=>null;
    public object? AnalyzeCommand=>null;
    public object? SampleTrafficCommand=>null;
    public object? RecommendCommand=>null;
    public object? StorageCommand=>null;
    public object? MonthlyChecklistCommand=>null;
    public bool ComparePhotoContents {get;set;}
    public string StorageOutput=>"مجلد اختبار فقط؛ لا حذف.";
    public string Endpoint {get;set;}="";
    public string Model {get;set;}="";
    public string AiOutput=>"الفحص المحلي يعمل بلا API.";
    public ProcessMonitorRow[] Processes {get;} = state=="empty"?[]:[new(new(42,"browser-synthetic",new(42,DateTimeOffset.UnixEpoch,@"C:\Test\browser.exe"),12.5,123456789,3,1024,8192,"مصطنعة", "ValidCached"),
        [new(42,"TCP","192.0.2.1",1234,"198.51.100.1",443,"Established")],null,!security,DateTimeOffset.UnixEpoch),
        new(new(43,"protected-synthetic",null,null,null,null,null,null,"غير متاح"),[],"عملية محمية أو مجهولة",!security)];
    public SecurityFinding[] Findings {get;} = [new("resource-use","Info","High","ملاحظة أداء مصطنعة؛ ليست إصابة",["CPU بعينتين — بيانات اختبار"],"راجع وظيفة التطبيق قبل اتخاذ إجراء.")];
    public InventorySectionRow[] Sections {get;} = [new(new("Defender","Unavailable","مصدر غير مستدعى أثناء اختبار الواجهة",[])),
        new(new("AppUsage","Partial","سجل تشغيل مصطنع؛ ليس دليل عدم استخدام",[new(@"C:\Test\old-tool.exe","HKCU UserAssist executable records; Explorer launch evidence only","{\"LastRecordedLaunchUtc\":\"2026-01-01T00:00:00+00:00\",\"ReviewOlderThanSixMonths\":true}")])),
        new(new("Backups","Partial","إعداد File History مصطنع؛ لا يثبت نسخة ناجحة",[new("ConfigurationStatus","File History","Configured (synthetic)")]))];
}
