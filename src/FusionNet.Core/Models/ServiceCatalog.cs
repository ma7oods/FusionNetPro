namespace FusionNet.Core.Models;

/// <summary>A Windows service FusionNet can pause during a match and restart afterwards.</summary>
public sealed record ServiceDefinition(
    string Id,
    string ServiceName,
    string NameEn,
    string NameAr,
    string DescriptionEn,
    string DescriptionAr,
    bool Recommended,
    bool Advanced);

public static class ServiceCatalog
{
    private static readonly ServiceDefinition[] Services =
    [
        new("wuauserv", "wuauserv", "Windows Update",
            "خدمة تحديث ويندوز",
            "Stops Windows from downloading updates while you play. Windows restarts it later automatically.",
            "توقف تنزيل تحديثات ويندوز أثناء اللعب. سيعيد ويندوز تشغيلها تلقائياً لاحقاً.",
            true, false),

        new("dosvc", "DoSvc", "Delivery Optimization",
            "تحسين التوصيل",
            "Stops peer-to-peer update sharing, which can upload from your PC and consume your whole upstream.",
            "توقف مشاركة التحديثات مع أجهزة أخرى، والتي قد تستهلك كامل سرعة الرفع لديك.",
            true, false),

        new("bits", "BITS", "Background Intelligent Transfer",
            "النقل الذكي في الخلفية",
            "Pauses Windows, Store, and some game launcher background downloads. Resumes automatically when restarted.",
            "توقف تنزيلات ويندوز والمتجر وبعض المشغلات في الخلفية. تُستأنف تلقائياً عند إعادة التشغيل.",
            true, false),

        new("usosvc", "UsoSvc", "Update Orchestrator",
            "منسّق التحديثات",
            "Stops the scheduler that kicks off Windows update scans mid-match.",
            "توقف المجدول الذي يبدأ فحص تحديثات ويندوز أثناء المباراة.",
            false, true),

        new("waasmedicsvc", "WaaSMedicSvc", "Windows Update Medic",
            "طبيب تحديث ويندوز",
            "Repairs and restarts update components. Stopping it is temporary; Windows may force it back on.",
            "تصلح مكونات التحديث وتعيد تشغيلها. إيقافها مؤقت وقد يعيد ويندوز تفعيلها.",
            false, true),

        new("sysmain", "SysMain", "SysMain (Superfetch)",
            "تسريع النظام (Superfetch)",
            "Stops background prefetching that competes for disk and RAM during a match.",
            "توقف التحميل المسبق في الخلفية الذي ينافس اللعبة على القرص والذاكرة.",
            false, true),

        new("wsearch", "WSearch", "Windows Search",
            "بحث ويندوز",
            "Stops background file indexing while you play. Search results stay available, just not updated.",
            "توقف فهرسة الملفات في الخلفية أثناء اللعب. تبقى نتائج البحث متاحة لكن دون تحديث.",
            false, true),

        new("diagtrack", "DiagTrack", "Connected User Experiences and Telemetry",
            "تجارب المستخدم والقياسات",
            "Stops Windows telemetry uploads, which send small amounts of data continuously.",
            "توقف رفع بيانات القياس إلى مايكروسوفت، والتي ترسل بيانات صغيرة باستمرار.",
            false, true),

        new("mapsbroker", "MapsBroker", "Downloaded Maps Manager",
            "مدير الخرائط",
            "Stops the offline maps download service. Very low impact but harmless to pause.",
            "توقف خدمة تنزيل الخرائط دون اتصال. تأثيرها ضعيف لكن إيقافها غير ضار.",
            false, true)
    ];

    public static IReadOnlyList<ServiceDefinition> All { get; } = Array.AsReadOnly(Services);

    public static IReadOnlyList<ServiceDefinition> Recommended { get; } =
        Array.AsReadOnly(Services.Where(s => s.Recommended).ToArray());

    public static IReadOnlyList<string> DefaultPausedIds { get; } =
        Array.AsReadOnly(Services.Where(s => s.Recommended).Select(s => s.Id).ToArray());

    public static ServiceDefinition? Find(string? id) =>
        Services.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(s.ServiceName, id, StringComparison.OrdinalIgnoreCase));

    public static ServiceDefinition? FindByServiceName(string serviceName) =>
        Services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
}
