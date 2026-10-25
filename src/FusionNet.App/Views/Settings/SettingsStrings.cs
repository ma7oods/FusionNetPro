using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Settings;

/// <summary>Localized strings for the Settings page.</summary>
internal static class SettingsStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- appearance
        Loc.Add("Set.AppearanceTitle", "Appearance", "المظهر");
        Loc.Add("Set.AppearanceBody", "Language and accent apply to the whole interface immediately.",
            "تُطبَّق اللغة واللون المميز على الواجهة كاملةً فوراً.");
        Loc.Add("Set.Accent", "Accent colour", "اللون المميز");
        Loc.Add("Set.AccentCyan", "Cyan", "سماوي");
        Loc.Add("Set.AccentViolet", "Violet", "بنفسجي");
        Loc.Add("Set.AccentEmerald", "Emerald", "زمردي");
        Loc.Add("Set.AccentAmber", "Amber", "عنبري");
        Loc.Add("Set.AccentRose", "Rose", "وردي");
        Loc.Add("Set.Language", "Language", "اللغة");
        Loc.Add("Set.LanguageHint", "The whole interface switches live, including right-to-left layout.",
            "تتبدّل الواجهة كاملةً فوراً بما في ذلك اتجاه الكتابة من اليمين إلى اليسار.");

        // ---------------------------------------------------------------- startup & behaviour
        Loc.Add("Set.StartupTitle", "Startup and behaviour", "البدء والسلوك");
        Loc.Add("Set.StartupBody", "How FusionNet Pro behaves when it starts and when you close it.",
            "كيف يتصرف فيوجن نت برو عند البدء وعند الإغلاق.");
        Loc.Add("Set.StartWithWindows", "Start with Windows", "البدء مع ويندوز");
        Loc.Add("Set.StartWithWindowsHint", "Launch FusionNet Pro automatically when you sign in.",
            "تشغيل فيوجن نت برو تلقائياً عند تسجيل الدخول.");
        Loc.Add("Set.MinimizeToTray", "Close to the tray", "الإغلاق إلى شريط النظام");
        Loc.Add("Set.MinimizeToTrayHint", "Keep monitoring in the background instead of exiting the app.",
            "استمرار المراقبة في الخلفية بدلاً من إغلاق التطبيق.");
        Loc.Add("Set.ShowNotifications", "Notifications", "الإشعارات");
        Loc.Add("Set.ShowNotificationsHint", "Show a toast when Fusion Mode engages, restores or fails.",
            "إظهار تنبيه عند تفعيل وضع Fusion أو استعادته أو عند حدوث خطأ.");
        Loc.Add("Set.ConfirmExitWhileActive", "Confirm exit during Fusion Mode", "تأكيد الخروج أثناء وضع Fusion");
        Loc.Add("Set.ConfirmExitWhileActiveHint", "Ask before closing while the shield is active.",
            "اسأل قبل الإغلاق أثناء نشاط الدرع.");
        Loc.Add("Set.AutoStartFailed", "Windows did not accept the startup change: {0}",
            "لم يقبل ويندوز تغيير البدء التلقائي: {0}");

        // ---------------------------------------------------------------- hotkey
        Loc.Add("Set.HotkeyTitle", "Global hotkey", "الاختصار العام");
        Loc.Add("Set.HotkeyBody", "Toggle Fusion Mode from anywhere without opening the window.",
            "بدّل وضع Fusion من أي مكان دون فتح النافذة.");
        Loc.Add("Set.HotkeyCurrent", "Current hotkey", "الاختصار الحالي");
        Loc.Add("Set.HotkeyRecord", "Record", "تسجيل");
        Loc.Add("Set.HotkeyRecording", "Press the key combination now…", "اضغط تركيبة المفاتيح الآن…");
        Loc.Add("Set.HotkeyNeedsModifier", "Add Ctrl, Alt, Shift or Win to the combination.",
            "أضف Ctrl أو Alt أو Shift أو Win إلى التركيبة.");
        Loc.Add("Set.HotkeyUnsupported", "That key cannot be used in a global hotkey.",
            "لا يمكن استخدام هذا المفتاح في اختصار عام.");
        Loc.Add("Set.HotkeyFailed", "The hotkey could not be registered; the previous one was kept.",
            "تعذّر تسجيل الاختصار؛ تم الإبقاء على الاختصار السابق.");
        Loc.Add("Set.HotkeySaved", "Hotkey updated and registered.", "تم تحديث الاختصار وتسجيله.");
        Loc.Add("Set.HotkeyCancelled", "Hotkey recording cancelled.", "تم إلغاء تسجيل الاختصار.");
        Loc.Add("Set.HotkeyNoWindow", "Open the main window before recording a hotkey.",
            "افتح النافذة الرئيسية قبل تسجيل اختصار.");

        // ---------------------------------------------------------------- fusion defaults
        Loc.Add("Set.FusionTitle", "Fusion Mode defaults", "إعدادات Fusion الافتراضية");
        Loc.Add("Set.FusionBody", "What the shield does when it engages. Every action is journaled and reversible.",
            "ما يفعله الدرع عند التفعيل. كل إجراء مسجّل في السجل ويمكن التراجع عنه.");
        Loc.Add("Set.Firewall", "Block selected apps with Windows Firewall", "حجب التطبيقات المحددة بجدار ويندوز");
        Loc.Add("Set.FirewallHint", "Follows the App Shield rules and never blocks the game or Discord.",
            "يطبّق قواعد درع التطبيقات ولا يحجب اللعبة أو ديسكورد أبداً.");
        Loc.Add("Set.PauseServices", "Pause Windows download services", "إيقاف خدمات تنزيل ويندوز");
        Loc.Add("Set.PauseServicesHint", "Stops update delivery while you play; Windows restarts them later.",
            "يوقف تنزيل التحديثات أثناء اللعب ويعيد ويندوز تشغيلها لاحقاً.");
        Loc.Add("Set.Boost", "Boost the game process", "تسريع عملية اللعبة");
        Loc.Add("Set.BoostHint", "Raises the game's CPU priority for the duration of the session.",
            "يرفع أولوية معالج اللعبة طوال مدة الجلسة.");
        Loc.Add("Set.PowerPlan", "Use the high-performance power plan", "استخدام خطة الطاقة عالية الأداء");
        Loc.Add("Set.PowerPlanHint", "Prevents Windows from down-clocking the CPU mid-match.",
            "يمنع ويندوز من خفض سرعة المعالج أثناء المباراة.");
        Loc.Add("Set.ApplyTweaks", "Apply the selected latency tweaks", "تطبيق تحسينات زمن الاستجابة المحددة");
        Loc.Add("Set.ApplyTweaksHint", "Uses the selection from the Network Tuner page.",
            "يستخدم التحديد من صفحة محسّن الشبكة.");
        Loc.Add("Set.FlushDns", "Flush the DNS cache on engage", "تفريغ ذاكرة DNS عند التفعيل");
        Loc.Add("Set.FlushDnsHint", "Clears stale name lookups before the match starts.",
            "يمسح نتائج الأسماء القديمة قبل بدء المباراة.");
        Loc.Add("Set.PurgeMemory", "Purge standby memory on engage", "تحرير الذاكرة الاحتياطية عند التفعيل");
        Loc.Add("Set.PurgeMemoryHint", "Frees cached RAM for the game; harmless and reversible.",
            "يفرّغ الذاكرة المخبأة للعبة؛ إجراء غير ضار.");
        Loc.Add("Set.SuspendScope", "Keep frozen apps frozen only while the game runs", "إبقاء التطبيقات مجمّدة طوال تشغيل اللعبة فقط");
        Loc.Add("Set.SuspendScopeHint", "When off, suspended apps stay frozen until you restore them manually.",
            "عند الإيقاف تبقى التطبيقات المجمّدة حتى تستعيدها يدوياً.");
        Loc.Add("Set.AutoEngage", "Engage Fusion Mode when a game starts", "تفعيل وضع Fusion تلقائياً عند بدء اللعبة");
        Loc.Add("Set.AutoEngageHint", "Needs the app running as administrator. Windows is restored when the game exits.",
            "يحتاج تشغيل البرنامج كمسؤول. يرجع الويندوز لحاله لما تطلع من اللعبة.");
        Loc.Add("Set.RestoreOnExit", "Restore everything when the game exits", "استعادة كل شيء عند خروج اللعبة");
        Loc.Add("Set.RestoreOnExitHint", "Undoes every change as soon as the match ends.",
            "يتراجع عن كل تغيير فور انتهاء المباراة.");
        Loc.Add("Set.AutoRestoreCrash", "Recover automatically after a crash", "الاستعادة التلقائية بعد انهيار");
        Loc.Add("Set.AutoRestoreCrashHint", "If the app died mid-session, restore that session on the next start.",
            "إذا توقف التطبيق أثناء جلسة، تُستعاد تلك الجلسة عند التشغيل التالي.");

        // ---------------------------------------------------------------- services
        Loc.Add("Set.ServicesTitle", "Windows services to pause", "خدمات ويندوز المراد إيقافها");
        Loc.Add("Set.ServicesBody", "Services FusionNet stops during a match and restarts afterwards.",
            "خدمات يوقفها فيوجن نت أثناء المباراة ثم يعيد تشغيلها بعدها.");
        Loc.Add("Set.ServicesHint", "Recommended services are paused by default. Advanced services can affect Windows features.",
            "تُوقف الخدمات الموصى بها افتراضياً. الخدمات المتقدمة قد تؤثر على ميزات ويندوز.");
        Loc.Add("Set.ServicesEmpty", "No Windows services are available on this system.",
            "لا توجد خدمات ويندوز متاحة على هذا النظام.");

        // ---------------------------------------------------------------- data & safety
        Loc.Add("Set.DataTitle", "Data and safety", "البيانات والأمان");
        Loc.Add("Set.DataBody", "Where FusionNet keeps its settings and journal, and how to move or reset them.",
            "مكان حفظ إعدادات فيوجن نت وسجل الجلسة، وكيفية نقلها أو إعادة تعيينها.");
        Loc.Add("Set.DataFolder", "Data folder", "مجلد البيانات");
        Loc.Add("Set.OpenFolder", "Open folder", "فتح المجلد");
        Loc.Add("Set.OpenFolderFailed", "Could not open the folder: {0}", "تعذّر فتح المجلد: {0}");
        Loc.Add("Set.ExportSettings", "Export settings", "تصدير الإعدادات");
        Loc.Add("Set.ExportDone", "Settings exported as JSON.", "تم تصدير الإعدادات بصيغة JSON.");
        Loc.Add("Set.ExportFailed", "Could not export the settings: {0}", "تعذّر تصدير الإعدادات: {0}");
        Loc.Add("Set.ResetAll", "Reset all settings", "إعادة تعيين كل الإعدادات");
        Loc.Add("Set.ResetTitle", "Reset all settings?", "إعادة تعيين كل الإعدادات؟");
        Loc.Add("Set.ResetBody",
            "Language, accent, hotkey, startup behaviour, Fusion defaults and app rules return to their defaults. Changes already applied to Windows are not reverted here; use the session journal for that.",
            "تعود اللغة واللون والاختصار وسلوك البدء وإعدادات Fusion وقواعد التطبيقات إلى افتراضياتها. لا تُتراجع هنا التغييرات المطبّقة على ويندوز؛ استخدم سجل الجلسة لذلك.");
        Loc.Add("Set.ResetDone", "All settings were reset to their defaults.", "تمت إعادة تعيين كل الإعدادات إلى افتراضياتها.");
        Loc.Add("Set.ResetFailed", "Could not reset the settings: {0}", "تعذّرت إعادة تعيين الإعدادات: {0}");
        Loc.Add("Set.JournalTitle", "Session journal", "سجل الجلسة");
        Loc.Add("Set.JournalSession", "Session", "الجلسة");
        Loc.Add("Set.JournalChanges", "Recorded changes", "التغييرات المسجلة");
        Loc.Add("Set.JournalRecording", "Recording", "جارٍ التسجيل");
        Loc.Add("Set.JournalIdle", "Idle", "خامل");
        Loc.Add("Set.JournalPending",
            "An interrupted session is waiting: open the Activity Log to restore every change.",
            "هناك جلسة غير مكتملة بانتظار الاستعادة: افتح سجل النشاط لاستعادة كل التغييرات.");
        Loc.Add("Set.OpenActivity", "Open activity log", "فتح سجل النشاط");

        // ---------------------------------------------------------------- about
        Loc.Add("Set.AboutTitle", "About", "حول التطبيق");
        Loc.Add("Set.Version", "Version {0}", "الإصدار {0}");
        Loc.Add("Set.Machine", "Computer", "الجهاز");
        Loc.Add("Set.Windows", "Windows", "ويندوز");
        Loc.Add("Set.Cpu", "Processor", "المعالج");
        Loc.Add("Set.Ram", "Memory", "الذاكرة");
        Loc.Add("Set.Uptime", "Uptime", "مدة التشغيل");
        Loc.Add("Set.Permission", "Permissions", "الصلاحيات");
        Loc.Add("Set.Reversible",
            "Every change FusionNet Pro makes is recorded in a journal and can be undone. Nothing is permanent.",
            "كل تغيير يجريه فيوجن نت برو مسجّل في السجل ويمكن التراجع عنه. لا شيء دائم.");
        Loc.Add("Set.BrandSubtitle", "Unified network optimizer and gaming shield", "منظّم الشبكة المتكامل ودرع الألعاب");
        Loc.Add("Set.ElevationCancelled", "Administrator elevation was cancelled.", "تم إلغاء طلب صلاحيات المسؤول.");
    }
}
