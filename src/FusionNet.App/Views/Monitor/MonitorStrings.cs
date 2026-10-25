using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Monitor;

/// <summary>Localized strings for the Live Monitor page.</summary>
internal static class MonitorStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- adapter card
        Loc.Add("Mon.AdapterTitle", "Active adapter", "الكرت النشط");
        Loc.Add("Mon.AdapterBody", "Everything Windows reports about the connection carrying your traffic.",
            "كل ما يقدّمه ويندوز عن الاتصال الذي يحمل حركة بياناتك.");
        Loc.Add("Mon.LinkSpeed", "Link speed", "سرعة الوصلة");
        Loc.Add("Mon.IPv4Label", "IPv4", "IPv4");
        Loc.Add("Mon.Gateway", "Gateway", "البوابة");
        Loc.Add("Mon.Mac", "MAC address", "عنوان MAC");
        Loc.Add("Mon.DnsServers", "DNS servers", "خوادم DNS");
        Loc.Add("Mon.Ssid", "Wi-Fi network", "شبكة Wi-Fi");
        Loc.Add("Mon.Signal", "Signal", "الإشارة");
        Loc.Add("Mon.Received", "Received", "المستلَم");
        Loc.Add("Mon.Sent", "Sent", "المرسَل");
        Loc.Add("Mon.Wireless", "Wi-Fi", "لاسلكي");
        Loc.Add("Mon.Wired", "Ethernet", "سلكي");
        Loc.Add("Mon.NoAdapter", "No active network adapter was found.", "لم يتم العثور على كرت شبكة نشط.");
        Loc.Add("Mon.Refreshed", "Adapter information refreshed.", "تم تحديث معلومات الكرت.");
        Loc.Add("Mon.ReadFailed", "Could not read the adapter details.", "تعذّرت قراءة تفاصيل الكرت.");

        // ---------------------------------------------------------------- live latency
        Loc.Add("Mon.TargetsTitle", "Live latency", "زمن الاستجابة المباشر");
        Loc.Add("Mon.TargetsBody", "Min, average, max, jitter and loss for every target FusionNet probes.",
            "الأدنى والمتوسط والأعلى والتذبذب والفقدان لكل هدف يقيسه فيوجن نت.");
        Loc.Add("Mon.Min", "Min", "الأدنى");
        Loc.Add("Mon.Avg", "Avg", "المتوسط");
        Loc.Add("Mon.Max", "Max", "الأعلى");
        Loc.Add("Mon.Jitter", "Jitter", "التذبذب");
        Loc.Add("Mon.Loss", "Loss", "الفقدان");
        Loc.Add("Mon.Quality", "Quality", "الجودة");
        Loc.Add("Mon.Last", "Last", "الأخير");
        Loc.Add("Mon.NoPingData", "Waiting for the first probe…", "بانتظار أول قياس…");
        Loc.Add("Mon.StartMonitoring", "Start monitoring", "بدء المراقبة");
        Loc.Add("Mon.StopMonitoring", "Stop monitoring", "إيقاف المراقبة");
        Loc.Add("Mon.MonitoringStarted", "Live monitoring started.", "بدأت المراقبة المباشرة.");
        Loc.Add("Mon.MonitoringStopped", "Live monitoring stopped.", "تم إيقاف المراقبة المباشرة.");
        Loc.Add("Mon.QualityExcellent", "Excellent", "ممتاز");
        Loc.Add("Mon.QualityGood", "Good", "جيد");
        Loc.Add("Mon.QualityFair", "Fair", "مقبول");
        Loc.Add("Mon.QualityPoor", "Poor", "ضعيف");
        Loc.Add("Mon.QualityUnknown", "Unknown", "غير معروف");

        // ---------------------------------------------------------------- throughput
        Loc.Add("Mon.ThroughputTitle", "Throughput", "سرعة النقل");
        Loc.Add("Mon.ThroughputBody", "Sampled once per second from the active adapter.", "تُقاس كل ثانية من الكرت النشط.");
        Loc.Add("Mon.Download", "Download", "التنزيل");
        Loc.Add("Mon.Upload", "Upload", "الرفع");
        Loc.Add("Mon.NoSamples", "Waiting for traffic samples…", "بانتظار عينات الحركة…");

        // ---------------------------------------------------------------- targets editor
        Loc.Add("Mon.TargetEditorTitle", "Probe targets", "أهداف القياس");
        Loc.Add("Mon.TargetEditorBody",
            "FusionNet pings these hosts continuously. Up to five targets; use a game server's region host for the most useful number.",
            "يقيس فيوجن نت زمن الاستجابة لهذه المضيفات باستمرار. حتى خمسة أهداف؛ استخدم مضيف منطقة سيرفر اللعبة للحصول على أدق رقم.");
        Loc.Add("Mon.TargetPlaceholder", "Host name or IP address", "اسم مضيف أو عنوان IP");
        Loc.Add("Mon.AddTarget", "Add", "إضافة");
        Loc.Add("Mon.RemoveTarget", "Remove target", "إزالة الهدف");
        Loc.Add("Mon.TargetsSaved", "Probe targets updated.", "تم تحديث أهداف القياس.");
        Loc.Add("Mon.TargetsMax", "You can monitor up to 5 targets.", "يمكنك مراقبة 5 أهداف كحد أقصى.");
        Loc.Add("Mon.InvalidTarget", "Enter a valid host name or IP address.", "أدخل اسم مضيف أو عنوان IP صالحاً.");
        Loc.Add("Mon.DuplicateTarget", "That target is already in the list.", "هذا الهدف موجود في القائمة مسبقاً.");
        Loc.Add("Mon.NeedTarget", "Keep at least one probe target.", "أبقِ هدف قياس واحداً على الأقل.");
        Loc.Add("Mon.Interval", "Probe interval", "فاصل القياس");
        Loc.Add("Mon.IntervalHint", "500 ms is the fastest allowed interval; 1000 ms is the default.",
            "أسرع فاصل مسموح 500 مللي ثانية، والافتراضي 1000 مللي ثانية.");
        Loc.Add("Mon.TargetsLabel", "Targets", "الأهداف");

        // ---------------------------------------------------------------- diagnostics
        Loc.Add("Mon.DiagnosticsTitle", "Network diagnostics", "تشخيص الشبكة");
        Loc.Add("Mon.DiagnosticsBody",
            "Checks adapters, DNS, latency, Windows services, tweaks and elevation, then tells you exactly what to fix.",
            "يفحص كروت الشبكة وDNS وزمن الاستجابة وخدمات ويندوز والتحسينات وصلاحيات المسؤول، ثم يخبرك بما يجب إصلاحه.");
        Loc.Add("Mon.RunDiagnostics", "Run diagnostics", "تشغيل التشخيص");
        Loc.Add("Mon.RunningDiagnostics", "Checking…", "جارٍ الفحص…");
        Loc.Add("Mon.ExportReport", "Export report", "تصدير التقرير");
        Loc.Add("Mon.FindingsTitle", "Findings", "النتائج");
        Loc.Add("Mon.NoFindings", "Run diagnostics to see what FusionNet would improve.",
            "شغّل التشخيص لترى ما يمكن لفيوجن نت تحسينه.");
        Loc.Add("Mon.Critical", "Critical", "حرج");
        Loc.Add("Mon.Warnings", "Warnings", "تحذيرات");
        Loc.Add("Mon.Passed", "Passed", "سليم");
        Loc.Add("Mon.Environment", "Environment", "البيئة");
        Loc.Add("Mon.Suggestion", "Suggestion", "الاقتراح");
        Loc.Add("Mon.ReportSaved", "Diagnostics report saved.", "تم حفظ تقرير التشخيص.");
        Loc.Add("Mon.ReportFailed", "Could not save the report: {0}", "تعذّر حفظ التقرير: {0}");
        Loc.Add("Mon.DiagnosticsFailed", "Diagnostics failed: {0}", "فشل التشخيص: {0}");
        Loc.Add("Mon.RunFirst", "Run diagnostics first.", "شغّل التشخيص أولاً.");
        Loc.Add("Mon.ReportTitle", "FusionNet Pro — diagnostics report", "فيوجن نت برو — تقرير التشخيص");
        Loc.Add("Mon.DiagnosticsDone", "{0} findings: {1} critical, {2} warnings.", "{0} نتيجة: {1} حرجة و{2} تحذيراً.");
        Loc.Add("Mon.NoFindingsFound", "Everything looks healthy.", "كل شيء يبدو سليماً.");
        Loc.Add("Mon.EnvLine", "{0} · {1} · {2}", "{0} · {1} · {2}");
        Loc.Add("Mon.LastRun", "Last check {0}", "آخر فحص {0}");
        Loc.Add("Mon.DiagnosticsCanceled", "Diagnostics cancelled.", "تم إلغاء التشخيص.");
        Loc.Add("Mon.Cancel", "Cancel", "إلغاء");
        Loc.Add("Mon.ElevationCancelled", "Administrator elevation was cancelled.", "تم إلغاء طلب صلاحيات المسؤول.");
        Loc.Add("Mon.LimitedMode",
            "Some checks need administrator rights. Run the app as administrator for a complete report.",
            "بعض الفحوصات تحتاج صلاحيات المسؤول. شغّل التطبيق كمسؤول للحصول على تقرير كامل.");
    }
}
