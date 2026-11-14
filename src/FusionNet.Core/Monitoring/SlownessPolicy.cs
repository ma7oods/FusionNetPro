using System.Text.Json;
using FusionNet.Core.Models;

namespace FusionNet.Core.Monitoring;

public sealed record DiskHealthInfo(string Name, string Media, string Health, bool System);
public sealed record SlownessProbe(IReadOnlyList<DiskHealthInfo> Disks, int Crashes, int PowerLoss, int Hardware, int DiskErrors);
public sealed record CpuUser(string Name, double Percent);
public sealed record SlownessSample(TimeSpan Uptime, string? PowerPlanGuid, int? StartupApps, IReadOnlyList<CpuUser> TopCpu, SlownessProbe? Probe);

/// <summary>Read-only "why is my PC slow" checks built on Windows' own counters, event logs and storage API.</summary>
public static class SlownessPolicy
{
    public const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string Category = "Performance";

    public static SlownessProbe? ParseProbe(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // PowerShell 5.1 writes a one-element array as a bare object.
            var disks = root.TryGetProperty("Disks", out var list) ? list.ValueKind switch
            {
                JsonValueKind.Array => list.EnumerateArray().ToArray(),
                JsonValueKind.Object => [list],
                _ => []
            } : [];
            return new SlownessProbe(
                disks.Select(disk => new DiskHealthInfo(Text(disk, "Name"), Text(disk, "Media"), Text(disk, "Health"),
                    disk.TryGetProperty("System", out var system) && system.ValueKind == JsonValueKind.True)).ToArray(),
                Count(root, "Crashes"), Count(root, "PowerLoss"), Count(root, "Hardware"), Count(root, "DiskErrors"));
        }
        catch (JsonException) { return null; }

        static string Text(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        static int Count(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;
    }

    public static IEnumerable<DiagnosticFinding> Evaluate(SlownessSample sample)
    {
        var days = (int)sample.Uptime.TotalDays;
        yield return days >= 7
            ? new(Category, "PC has not restarted for a long time", "لم يُعد تشغيل الجهاز منذ مدة طويلة",
                $"Windows has been running for {days} days. Long uptime keeps leaked memory and stuck updates around.", DiagnosticSeverity.Warning,
                "Use Restart, not Shut down: Fast Startup keeps the old session alive after Shut down.",
                "استخدم إعادة التشغيل بدل إيقاف التشغيل، لأن بدء التشغيل السريع يُبقي الجلسة القديمة بعد الإيقاف.")
            : new(Category, "Restarted recently", "أُعيد التشغيل مؤخرًا", $"Windows has been running for {days} day(s).", DiagnosticSeverity.Ok);

        if (string.Equals(sample.PowerPlanGuid, PowerSaverGuid, StringComparison.OrdinalIgnoreCase))
            yield return new(Category, "Power saver plan is active", "خطة توفير الطاقة مفعّلة",
                "Power saver lowers CPU speed to save energy, which slows games and apps.", DiagnosticSeverity.Warning,
                "Switch to Balanced or High performance in Control Panel > Power Options.",
                "حوّل إلى الخطة المتوازنة أو الأداء العالي من لوحة التحكم > خيارات الطاقة.");

        if (sample.StartupApps is { } startup)
            yield return startup >= 15
                ? new(Category, "Many apps start with Windows", "برامج كثيرة تعمل مع بدء ويندوز",
                    $"{startup} apps start automatically, which slows boot and uses memory in the background.", DiagnosticSeverity.Warning,
                    "Turn off the ones you do not need in Settings > Apps > Startup.",
                    "عطّل ما لا تحتاجه من الإعدادات > التطبيقات > بدء التشغيل.")
                : new(Category, "Startup apps are reasonable", "برامج بدء التشغيل معقولة", $"{startup} apps start automatically.", DiagnosticSeverity.Ok);

        var busy = sample.TopCpu.Where(user => user.Percent >= 25).ToArray();
        yield return busy.Length > 0
            ? new(Category, "An app is using a lot of CPU", "برنامج يستهلك المعالج بشدة",
                string.Join(", ", busy.Select(user => $"{user.Name} {user.Percent:0}%")) + " of total CPU right now.", DiagnosticSeverity.Warning,
                "Close it if you are not using it, or check it in Task Manager.",
                "أغلقه إن لم تكن تستخدمه، أو افحصه من مدير المهام.")
            : new(Category, "No app is hogging the CPU", "لا يوجد برنامج يستهلك المعالج بشدة",
                sample.TopCpu.Count == 0 ? "CPU use could not be sampled." : "Top now: " + string.Join(", ", sample.TopCpu.Select(user => $"{user.Name} {user.Percent:0}%")) + ".",
                DiagnosticSeverity.Ok);

        if (sample.Probe is not { } probe)
        {
            yield return new(Category, "Disk health and error logs could not be read", "تعذرت قراءة صحة القرص وسجل الأخطاء",
                "Windows did not answer the storage and event-log queries in time.", DiagnosticSeverity.Info);
            yield break;
        }

        foreach (var disk in probe.Disks.Where(disk => disk.Health is "Unhealthy" or "Warning"))
            yield return new(Category, "A disk reports a health problem", "قرص يبلّغ عن مشكلة في صحته",
                $"{disk.Name}: {disk.Health}.", disk.Health == "Unhealthy" ? DiagnosticSeverity.Critical : DiagnosticSeverity.Warning,
                "Back up your files now, then check or replace the drive.", "انسخ ملفاتك احتياطيًا الآن، ثم افحص القرص أو استبدله.");
        if (probe.Disks.Count > 0 && probe.Disks.All(disk => disk.Health == "Healthy"))
            yield return new(Category, "Disks report healthy", "الأقراص سليمة", $"{probe.Disks.Count} disk(s) checked.", DiagnosticSeverity.Ok);
        if (probe.Disks.FirstOrDefault(disk => disk.System && disk.Media == "HDD") is { } hdd)
            yield return new(Category, "Windows runs on a hard disk (HDD)", "ويندوز مثبّت على قرص ميكانيكي (HDD)",
                $"{hdd.Name} is a spinning disk; it is the most common cause of a slow PC.", DiagnosticSeverity.Warning,
                "Moving Windows to an SSD is the biggest speed upgrade.", "نقل ويندوز إلى SSD هو أكبر تحسين للسرعة.");

        if (probe.DiskErrors > 0)
            yield return new(Category, "Disk errors in the last 7 days", "أخطاء قرص خلال آخر 7 أيام",
                $"{probe.DiskErrors} disk error event(s) (bad blocks, paging or retried I/O).", DiagnosticSeverity.Critical,
                "Back up now; check the drive cable and the drive's health.", "انسخ ملفاتك احتياطيًا الآن، وافحص كابل القرص وصحته.");
        if (probe.Hardware > 0)
            yield return new(Category, "Hardware errors in the last 7 days", "أخطاء عتاد خلال آخر 7 أيام",
                $"{probe.Hardware} WHEA hardware error event(s).", DiagnosticSeverity.Warning,
                "Check temperatures, remove overclocks and update chipset and GPU drivers.", "افحص الحرارة، وألغِ كسر السرعة، وحدّث تعريفات اللوحة الأم وبطاقة الرسوميات.");
        if (probe.PowerLoss > 0)
            yield return new(Category, "Unexpected shutdowns in the last 7 days", "انطفاء مفاجئ خلال آخر 7 أيام",
                $"{probe.PowerLoss} time(s) Windows stopped without shutting down cleanly.", DiagnosticSeverity.Warning,
                "Check the power supply, overheating and recent driver changes.", "افحص مزوّد الطاقة والحرارة وآخر التعريفات المثبّتة.");
        if (probe.Crashes >= 5)
            yield return new(Category, "Apps crashed often in the last 7 days", "تعطّلت برامج كثيرًا خلال آخر 7 أيام",
                $"{probe.Crashes} app crash event(s).", DiagnosticSeverity.Warning,
                "Update or reinstall the apps that crash; Reliability Monitor shows which ones.", "حدّث البرامج التي تتعطل أو أعد تثبيتها؛ مراقب الموثوقية يبيّن أيها.");
    }
}
