using FusionNet.Core.Models;

namespace FusionNet.Core.Monitoring;

public static class ProcessPresentation
{
    public static bool Matches(ProcessObservation process,string query,bool measuredOnly,bool resourceOnly)
    {
        query=query.Trim();
        return (!measuredOnly || process.UploadBytesPerSecond>0 || process.DownloadBytesPerSecond>0) &&
            (!resourceOnly || process.CpuPercent>5 || process.MemoryPercent>5) &&
            (query.Length==0 || process.Name.Contains(query,StringComparison.OrdinalIgnoreCase) ||
             process.Pid.ToString().Contains(query,StringComparison.Ordinal) || process.Identity?.Path.Contains(query,StringComparison.OrdinalIgnoreCase)==true);
    }
    public static string Purpose(string name)=>Purpose(name,false);
    public static string Purpose(string name,bool english)
    {
        var app=ManagedAppCatalog.All.FirstOrDefault(app=>app.MatchesProcess(name));
        var description=app?.Category switch
        {
            ManagedAppCatalog.Browsers=>english?"Web browser; may run pages, downloads and background work.":"متصفح ويب؛ قد يشغّل صفحات وتنزيلات وعمليات خلفية.",
            ManagedAppCatalog.CloudSync=>english?"Cloud file sync; may upload or download in the background.":"مزامنة ملفات سحابية؛ قد ترفع أو تنزّل الملفات في الخلفية.",
            ManagedAppCatalog.Downloads=>english?"Download manager; review active downloads before blocking access.":"مدير تنزيل ملفات؛ راجع التنزيلات النشطة قبل حظر اتصاله.",
            ManagedAppCatalog.Torrents=>english?"File-sharing client; may transfer data in both directions.":"عميل مشاركة ملفات؛ قد ينقل البيانات في الاتجاهين.",
            ManagedAppCatalog.Launchers=>english?"Game launcher and updater; the game or sign-in may require it.":"مشغّل ألعاب وتحديثات؛ قد تحتاجه اللعبة أو خدمة تسجيل الدخول.",
            ManagedAppCatalog.UpdateAgents=>english?"Another application's updater; verify its source before changing it.":"مكوّن تحديثات لتطبيق آخر؛ تحقق من المصدر قبل تغييره.",
            ManagedAppCatalog.RemoteTools=>english?"Remote access or administration tool; it may be intentionally in use.":"أداة اتصال أو إدارة عن بُعد؛ قد تكون مستخدمة عمدًا.",
            _=>english?"Process purpose is unknown to the catalog; review its source and path before judging it.":"وظيفة العملية غير معروفة من الكتالوج؛ راجع مصدرها ومسارها قبل الحكم."
        };
        return app is null?description:description+(english?" Description follows the catalog name; not identity or safety verification.":" الوصف حسب الاسم في الكتالوج، وليس إثبات هوية أو أمان.");
    }
}
