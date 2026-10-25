using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Tools;

internal static class ToolUiStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        Loc.Add("Tools.Format.OpenFailed","Could not open the browser. Open this URL manually: {0}","تعذر فتح المتصفح. افتح هذا الرابط يدويًا: {0}");
        Loc.Add("Tools.Format.Querying","Querying {0} for {1}; DNS settings are unchanged.","جارٍ استعلام {0} عن {1}؛ لا تغيير لإعدادات DNS.");
        Loc.Add("Tools.Format.AnswerCount","Answer records: {0}.","سجلات الإجابة: {0}.");
        Loc.Add("Tools.Text.SourceOpened","Asked Windows to open the official source.","طُلب من Windows فتح المصدر الرسمي.");
        Loc.Add("Tools.Text.Partial","Limited result display.","عرض محدود للنتائج.");
        Loc.Add("Tools.Text.NoAnswers","The server returned no answer records of this type; this does not prove the internet is disconnected.","لم يعد الخادم سجلات إجابة لهذا النوع؛ هذا لا يثبت انقطاع الإنترنت.");
        Loc.Add("Tools.Text.Bundled", "Integrated", "مدمج");
        Loc.Add("Tools.Text.DnsPurpose", "DNS record inspection; UDP with TCP fallback and a user-selected server.", "فاحص سجلات DNS؛ UDP مع TCP fallback وخادم يحدده المستخدم.");
        Loc.Add("Tools.Text.EtwPurpose", "Per-process ETW byte measurements in Game Boost, with separate consent.", "قياس bytes للعمليات عبر ETW في Game Boost بموافقة منفصلة.");
        Loc.Add("Tools.Text.External", "External source", "مصدر خارجي");
        Loc.Add("Tools.Text.HardwarePurpose", "Read sensor WMI when you run the external tool; no bundled DLL or driver.", "قراءة WMI للحساسات إذا شغّلت الأداة أنت؛ لا DLL أو driver مضمّن.");
        Loc.Add("Tools.Text.HardwareLicense", "MPL-2.0 for the external tool", "MPL-2.0 للأداة الخارجية");
        Loc.Add("Tools.Text.Optional", "Optional tools", "أدوات اختيارية");
        Loc.Add("Tools.Text.SysinternalsPurpose", "Autoruns and Process Explorer for manual verification. No automatic download, execution or EULA acceptance.", "Autoruns وProcess Explorer للتحقق اليدوي. لا تحميل أو تشغيل أو قبول EULA تلقائي.");
        Loc.Add("Tools.Text.SysinternalsLicense", "Microsoft Sysinternals license", "ترخيص Microsoft Sysinternals");
        Loc.Add("Tools.Text.DnsLimit", " DNS query time is not game latency; no DNSSEC verification or record safety guarantee.", " زمن استعلام DNS وليس بنق اللعبة؛ لا تحقق DNSSEC أو ضمان أمان للسجلات.");
        Loc.Add("Tools.Text.DnsCancelled", "Query cancelled or timed out; no substitute results or DNS changes.", "أُلغي الاستعلام أو انتهت مهلته؛ لا نتائج بديلة ولا تغيير DNS.");
        Loc.Add("Tools.Text.DnsFailed", "Could not query the selected server. Check its address/connectivity; no alternate server or settings changes.", "تعذر استعلام الخادم المحدد. تحقق من عنوانه والاتصال؛ لم يُستخدم خادم بديل ولم تتغير إعداداتك.");
        Loc.Add("Tools.Text.HardwareBusy", "Requesting a WMI sample from LibreHardwareMonitor...", "جارٍ طلب عينة WMI من LibreHardwareMonitor...");
        Loc.Add("Tools.Text.HardwareCancelled", "Refresh cancelled. No substitute values were displayed.", "أُلغي التحديث. لم تُعرض قيم بديلة.");
        Loc.Add("Tools.Text.HardwareFailed", "Could not refresh sensors. Check the external WMI source. No driver was installed.", "تعذر تحديث الحساسات؛ تحقق من مصدر WMI الخارجي. لم يُثبّت driver.");
        Loc.Add("Tools.Status.IdleDns","No query has been sent. Choose the domain and server, then inspect records.","لم يُرسل أي استعلام. اختر النطاق والخادم ثم اضغط فحص السجلات.");
        Loc.Add("Tools.Status.IdleHardware","No sensors have been read. Refresh manually from an existing LibreHardwareMonitor WMI source.","لم تُقرأ الحساسات. التحديث يدوي من LibreHardwareMonitor WMI الموجود فقط.");
        Loc.Add("Tools.Status.IdleSource","Buttons open official sources only; no automatic tool download or execution.","تفتح الأزرار المصادر الرسمية فقط؛ لا تحميل أو تشغيل أدوات تلقائيًا.");
        Loc.Add("Tools.Ui.Title", "Diagnostics and hardware tools", "أدوات التشخيص والعتاد");
        Loc.Add("Tools.Ui.Subtitle", "Known sources, clear limits, and checks that start when you choose.", "أدوات موثوقة، مصادر واضحة، وفحوص تبدأ باختيارك.");
        Loc.Add("Tools.Ui.Cancel", "Cancel operation", "إلغاء العملية");
        Loc.Add("Tools.Ui.ReadOnly", "No automatic scans • No Windows setting changes", "لا فحوص تلقائية • لا تغيير لإعدادات Windows");
        Loc.Add("Tools.Ui.DnsTitle", "DNS record inspector", "فاحص سجلات DNS");
        Loc.Add("Tools.Ui.DnsDetails", "DnsClient.NET sends the domain to the selected server only when you click, on port 53. DNS is not encrypted and adapter settings are unchanged. TCP may be used when the response requires it.", "DnsClient.NET • يُرسل النطاق إلى الخادم المحدد عند الضغط فقط، عبر المنفذ53. لا تشفير DNS ولا تغيير لإعدادات المحول. قد يستخدم TCP إذا احتاج الرد ذلك.");
        Loc.Add("Tools.Ui.Domain", "Domain", "النطاق");
        Loc.Add("Tools.Ui.DomainAutomation", "DNS query domain", "نطاق استعلام DNS");
        Loc.Add("Tools.Ui.Server", "DNS server address", "عنوان خادم DNS");
        Loc.Add("Tools.Ui.ServerAutomation", "Inspector server IP address", "عنوان IP لخادم الفحص");
        Loc.Add("Tools.Ui.RecordType", "Record type", "نوع السجل");
        Loc.Add("Tools.Ui.RecordTypeAutomation", "DNS record type", "نوع سجل DNS");
        Loc.Add("Tools.Ui.Inspect", "Inspect records", "فحص السجلات");
        Loc.Add("Tools.Ui.HardwareTitle", "Hardware sensors", "حساسات الجهاز");
        Loc.Add("Tools.Ui.HardwareDetails", "LibreHardwareMonitor WMI reads the source you run. No tool or driver installation, fan control or voltage changes.", "LibreHardwareMonitor WMI • قراءة المصدر الذي تشغّله أنت. لا تثبيت للأداة أو driver ولا تحكم بالمراوح أو الفولتية.");
        Loc.Add("Tools.Ui.RefreshHardware", "Refresh sensors", "تحديث الحساسات");
        Loc.Add("Tools.Ui.Sources", "Components and official projects", "المكونات والمشاريع الرسمية");
        Loc.Add("Tools.Ui.OfficialProject", "Official project", "المشروع الرسمي");
    }
}
