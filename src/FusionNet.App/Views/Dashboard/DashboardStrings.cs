using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Dashboard;

/// <summary>Bilingual strings for the Dashboard page (keys are prefixed with <c>Dash.</c>).</summary>
internal static class DashboardStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        Loc.Add("Dash.OverviewSubtitle", "Connection and gaming session, together.", "حالة الاتصال وجلسة اللعب، في مكان واحد.");
        Loc.Add("Dash.ReviewSession", "Review gaming session", "مراجعة جلسة اللعب");
        Loc.Add("Dash.ReviewSequence", "Scan usage  →  Review apps  →  Start after approval", "افحص الاستهلاك  ←  راجع التطبيقات  ←  ابدأ بعد الموافقة");
        Loc.Add("Dash.ConnectionWorkspace", "Connection measurements", "قياس الاتصال");
        Loc.Add("Dash.TargetScope", "Measurements for the selected target below; this may not be the game server.", "قياس الهدف المحدد أدناه؛ لا يمثل بالضرورة خادم اللعبة.");
        Loc.Add("Dash.AdapterRates", "Adapter down / up", "المحول: تنزيل / رفع");
        Loc.Add("Dash.RangeLabel", "Min / max:", "أدنى / أعلى:");
        Loc.Add("Dash.LegacyState", "Advanced Fusion status", "حالة Fusion المتقدم");
        // ---------------------------------------------------------------- hero
        Loc.Add("Dash.Eyebrow", "FUSION MODE", "وضع FUSION");
        Loc.Add("Dash.ProtectionLevel", "PROTECTION", "الحماية");
        Loc.Add("Dash.StateIdleBody",
            "Nothing on your PC has been changed. Fusion Mode engages by itself when a watched game starts, or activate it now.",
            "لم يُغيَّر أي شيء على حاسوبك. يتفعّل وضع Fusion لحاله لما تبدي لعبة مراقبة، أو فعّله الآن.");
        Loc.Add("Dash.StateActiveBody",
            "Firewall rules, services, tweaks and app rules are enforced. Every change is journaled and restored when you stop.",
            "قواعد الجدار الناري والخدمات والتحسينات وقواعد التطبيقات مطبقة. كل تغيير مسجّل ويُستعاد عند الإيقاف.");
        Loc.Add("Dash.StateArmingBody",
            "Applying changes and recording each one in the journal…",
            "جارٍ تطبيق التغييرات وتسجيل كل تغيير في السجل…");
        Loc.Add("Dash.StateRestoringBody",
            "Undoing every change FusionNet made in this session…",
            "جارٍ التراجع عن كل تغيير أجراه فيوجن نت في هذه الجلسة…");
        Loc.Add("Dash.StateErrorBody",
            "Some changes could not be applied. Open the activity log for details.",
            "تعذّر تطبيق بعض التغييرات. افتح سجل النشاط للتفاصيل.");
        Loc.Add("Dash.GamesButton", "Games", "الألعاب");
        Loc.Add("Dash.GamesTip", "Choose the games FusionNet detects and protects", "اختر الألعاب التي يكتشفها ويحميها فيوجن نت");
        Loc.Add("Dash.EngageTip", "Apply the full Fusion Mode plan now", "طبّق خطة وضع Fusion الكاملة الآن");
        Loc.Add("Dash.DisengageTip", "Stop and restore every journaled change", "أوقف واستعد كل تغيير مسجّل");
        Loc.Add("Dash.ArmedPill", "ARMED", "مفعّل");
        Loc.Add("Dash.Live", "LIVE", "مباشر");
        Loc.Add("Dash.SessionRow", "Session", "الجلسة");

        // ---------------------------------------------------------------- metrics
        Loc.Add("Dash.MetricsPing", "Best target average", "متوسط أفضل هدف");
        Loc.Add("Dash.MetricsJitter", "Average jitter", "متوسط التذبذب");
        Loc.Add("Dash.MetricsLoss", "Average loss", "متوسط الفقدان");
        Loc.Add("Dash.MetricsDownload", "Current download", "التنزيل الحالي");
        Loc.Add("Dash.Q.Idle", "Idle", "خامل");
        Loc.Add("Dash.Q.Low", "Light", "خفيف");
        Loc.Add("Dash.Q.Active", "Active", "نشط");
        Loc.Add("Dash.Q.Heavy", "Heavy", "مكثّف");

        // ---------------------------------------------------------------- latency
        Loc.Add("Dash.LatencyTitle", "Live latency", "زمن الاستجابة المباشر");
        Loc.Add("Dash.LatencySubtitle", "Pick the target used for the main reading.", "اختر الهدف المستخدم للقراءة الرئيسية.");
        Loc.Add("Dash.TargetTip", "Use this target for the detailed reading", "استخدم هذا الهدف للقراءة التفصيلية");
        Loc.Add("Dash.Avg", "Avg", "المتوسط");
        Loc.Add("Dash.Min", "Min", "الأدنى");
        Loc.Add("Dash.Max", "Max", "الأعلى");
        Loc.Add("Dash.Jitter", "Jitter", "التذبذب");
        Loc.Add("Dash.Loss", "Loss", "الفقدان");
        Loc.Add("Dash.NoSamples", "Waiting for samples…", "بانتظار القياسات…");
        Loc.Add("Dash.PingUnit", "ms", "ms");

        // ---------------------------------------------------------------- throughput
        Loc.Add("Dash.ThroughputTitle", "Throughput", "السرعة الحالية");
        Loc.Add("Dash.ThroughputEmpty", "Live monitoring is off, so no throughput samples arrive.", "المراقبة المباشرة متوقفة، لذلك لا تصل قياسات للسرعة.");
        Loc.Add("Dash.PerSecond", "/s", "/ث");

        // ---------------------------------------------------------------- enforcement
        Loc.Add("Dash.EnforcedTitle", "What Fusion Mode is enforcing right now", "ما يفرضه وضع Fusion الآن");
        Loc.Add("Dash.EnforcedEmpty", "Nothing is enforced", "لا شيء مفروض الآن");
        Loc.Add("Dash.EnforcedEmptyBody",
            "Engage Fusion Mode or launch a supported game to see every change listed here.",
            "فعّل وضع Fusion أو ابدأ لعبة مدعومة لعرض كل تغيير هنا.");
        Loc.Add("Dash.Firewall", "Firewall rules", "قواعد الجدار الناري");
        Loc.Add("Dash.Services", "Paused services", "خدمات موقوفة");
        Loc.Add("Dash.Apps", "Controlled apps", "تطبيقات تحت السيطرة");
        Loc.Add("Dash.Tweaks", "Applied tweaks", "تحسينات مطبقة");
        Loc.Add("Dash.Power", "Power plan", "خطة الطاقة");
        Loc.Add("Dash.Dns", "DNS", "DNS");
        Loc.Add("Dash.None", "None", "لا شيء");
        Loc.Add("Dash.FirewallDetail", "Outbound and inbound block rules owned by FusionNet", "قواعد حجب للاتجاهين يملكها فيوجن نت");
        Loc.Add("Dash.PowerDetail", "Restored automatically when Fusion Mode stops", "تُستعاد تلقائياً عند إيقاف وضع Fusion");
        Loc.Add("Dash.PowerHigh", "High performance", "أداء عالٍ");
        Loc.Add("Dash.DnsChanged", "Custom servers", "خوادم مخصصة");
        Loc.Add("Dash.DnsAuto", "Automatic (DHCP)", "تلقائي (DHCP)");
        Loc.Add("Dash.More", "+{0} more", "‏+{0} أخرى");

        // ---------------------------------------------------------------- detected game
        Loc.Add("Dash.DetectedTitle", "Detected game", "اللعبة المكتشفة");
        Loc.Add("Dash.DetectedIdle", "Start a supported game and FusionNet detects it automatically.", "ابدأ لعبة مدعومة وسيكتشفها فيوجن نت تلقائياً.");
        Loc.Add("Dash.AutoEngage", "Engage Fusion Mode when a game starts", "تفعيل وضع Fusion تلقائياً عند بدء اللعبة");
        Loc.Add("Dash.AutoEngageHelp", "Needs the app running as administrator. Windows is restored when the game exits.", "يحتاج تشغيل البرنامج كمسؤول. يرجع الويندوز لحاله لما تطلع من اللعبة.");
        Loc.Add("Dash.AutoEngageOn", "Fusion Mode will engage when a watched game starts.", "راح يتفعّل وضع Fusion لما تبدي لعبة مراقبة.");
        Loc.Add("Dash.AutoEngageOff", "Auto-engage is off.", "تم إيقاف التفعيل التلقائي.");

        // ---------------------------------------------------------------- activity
        Loc.Add("Dash.ActivityTitle", "Recent activity", "النشاط الأخير");
        Loc.Add("Dash.ViewAll", "View all", "عرض الكل");
        Loc.Add("Dash.ViewAllTip", "Open the full activity log", "افتح سجل النشاط الكامل");
        Loc.Add("Dash.ActivityEmptyBody", "Everything FusionNet does will be listed here.", "سيُدرج هنا كل ما يفعله فيوجن نت.");

        // ---------------------------------------------------------------- elevation
        Loc.Add("Dash.ElevationCancelled", "Restart as administrator was cancelled.", "تم إلغاء إعادة التشغيل بصلاحيات المسؤول.");
        Loc.Add("Dash.ElevationCancelledTitle", "Administrator restart cancelled", "إلغاء إعادة التشغيل كمسؤول");
    }
}
