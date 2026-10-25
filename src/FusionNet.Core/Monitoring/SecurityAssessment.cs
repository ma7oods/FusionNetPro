using FusionNet.Core.Models;
using System.Text.Json;

namespace FusionNet.Core.Monitoring;

public static class SecurityAssessment
{
    // Process names of tools that let someone else see or control this PC (remoting_host = Chrome Remote Desktop).
    private static readonly HashSet<string> RemoteControlTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "AnyDesk", "TeamViewer", "TeamViewer_Service", "remoting_host", "rustdesk", "parsecd", "ScreenConnect.ClientService",
        "AA_v3", "Ammyy", "SRService", "SRManager", "winvnc", "tvnserver", "vncserver", "meshagent", "AteraAgent", "client32", "rserver3", "Supremo", "AeroAdmin"
    };

    public static IReadOnlyList<SecurityFinding> Assess(IReadOnlyList<ProcessObservation> processes,
        IReadOnlyList<ConnectionObservation> connections, IReadOnlyList<InventorySection> sections)
    {
        var findings = new List<SecurityFinding>();
        var reportedRemote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections.Where(section => section.Status != "Complete"))
            findings.Add(new("coverage", "Info", "High", "تغطية الفحص غير مكتملة: " + section.Name,
                [section.Status, section.Detail], "راجع سبب عدم توفر البيانات؛ غياب التنبيهات لا يثبت سلامة الجهاز."));
        foreach (var process in processes)
        {
            if (process.CpuPercent > 5 || process.MemoryPercent > 5)
                findings.Add(new("resource-use", "Info", "High", $"استهلاك موارد: {process.Name} (PID {process.Pid})",
                    [$"CPU: {process.CpuPercent?.ToString("F1") ?? "غير متاح"}%", $"RAM: {process.MemoryPercent?.ToString("F1") ?? "غير متاح"}%"],
                    "هذا قياس أداء قصير وليس دليل إصابة. راجع وظيفة التطبيق قبل إغلاقه."));
            var miningName = process.Name.Contains("xmrig", StringComparison.OrdinalIgnoreCase) ||
                process.Name.Contains("minerd", StringComparison.OrdinalIgnoreCase);
            var miningPort = connections.Any(connection => ProcessSampling.SameIdentity(connection.Identity, process.Identity) && connection.State == "Established" &&
                connection.RemotePort is 3333 or 4444 or 5555 or 7777 or 14444);
            if (miningName && process.CpuPercent > 20 && miningPort)
                findings.Add(new("mining-indicators", "Warning", "Medium", $"مؤشرات تعدين تحتاج مراجعة: {process.Name} (PID {process.Pid})",
                    ["اسم مرتبط ببرامج تعدين قد تكون مشروعة", $"CPU المقاس {process.CpuPercent:F1}%", "اتصال بمنفذ شائع في مجمعات التعدين؛ المنفذ ليس إثباتًا للوجهة"],
                    "تحقق هل ثبتّه المستخدم عمدًا. راجع المسار والتوقيع وسجل Defender؛ هذه ليست إصابة مؤكدة."));
            if (RemoteControlTools.Contains(process.Name) && reportedRemote.Add(process.Name))
            {
                var remote = connections.Where(connection => ProcessSampling.SameIdentity(connection.Identity, process.Identity) && connection.State == "Established" &&
                    connection.RemoteAddress is not ("127.0.0.1" or "::1")).Select(connection => $"{connection.RemoteAddress}:{connection.RemotePort}").Distinct().Take(3).ToArray();
                findings.Add(new("remote-access", "Info", "High", $"أداة تحكم بالجهاز عن بعد تعمل: {process.Name} (PID {process.Pid})",
                    [remote.Length > 0 ? "اتصال مفتوح الآن: " + string.Join(", ", remote) : "لا اتصال مفتوح في هذه العينة", "المسار: " + (process.Identity?.Path ?? "غير متاح")],
                    "إذا نصّبتها إنت وتستعملها فطبيعي. إذا لا، أوقفها وراجع سجل اتصالاتها وغيّر كلمات المرور. وجودها وحده ليس دليل اختراق."));
            }
            if (process.Signature == "HashMismatch")
                findings.Add(new("signature-mismatch", "Warning", "Medium", "فشل تطابق توقيع الملف: " + process.Name,
                    ["WinVerifyTrust offline: HashMismatch"], "تحقق من مصدر الملف وافحصه في Windows Security بموافقتك."));
        }
        foreach (var section in sections.Where(section => section.Name is "Startup" or "Tasks"))
            foreach (var item in section.Items.Where(item => item.Detail.Contains("-enc", StringComparison.OrdinalIgnoreCase) ||
                         item.Detail.Contains("FromBase64String", StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("encoded-persistence", "Warning", "Low", "أمر مشفّر ضمن التشغيل التلقائي: " + item.Name,
                    [section.Name, item.Source, "Encoded command indicator"],
                    "قد يكون إجراء إدارة مشروعًا. راجع الأمر محليًا دون تشغيله؛ لم يُعطّل أي عنصر."));
        foreach (var item in sections.Where(section => section.Name == "Defender").SelectMany(section => section.Items))
        {
            try
            {
                using var json = JsonDocument.Parse(item.Detail);
                if (item.Source == "Get-MpThreat" && json.RootElement.TryGetProperty("IsActive", out var active) && active.ValueKind == JsonValueKind.True)
                    findings.Add(new("defender-active", "Warning", "High", "Defender يبلّغ عن كشف نشط يحتاج مراجعة: " + item.Name,
                        ["Get-MpThreat: IsActive=true", item.Source], "راجع Windows Security للتحقق والإجراء المناسب؛ لم يشغّل FusionNet فحصًا أو معالجة."));
                if (item.Source == "Get-MpComputerStatus" && json.RootElement.TryGetProperty("RealTimeProtectionEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                    findings.Add(new("defender-realtime", "Info", "High", "حماية Defender الفورية غير مفعلة في العينة",
                        ["Get-MpComputerStatus: RealTimeProtectionEnabled=false"], "قد يعمل منتج حماية آخر أو يكون Defender في الوضع السلبي. راجع Windows Security؛ هذه الحالة ليست إثبات إصابة."));
            }
            catch (JsonException) { /* raw status remains visible; do not turn malformed evidence into a verdict */ }
        }
        return findings;
    }
}
