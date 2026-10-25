using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Gaming;

/// <summary>Bilingual strings for the Games &amp; Profiles page (keys are prefixed with <c>Game.</c>).</summary>
internal static class GamingStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- mode selector
        Loc.Add("Game.ModeTitle", "Detection mode", "وضع الاكتشاف");
        Loc.Add("Game.ModeHelp", "Choose how FusionNet decides which game to protect.", "اختر كيف يحدد فيوجن نت اللعبة التي يحميها.");
        Loc.Add("Game.ModeAuto", "Automatic (detect any game)", "تلقائي (اكتشاف أي لعبة)");
        Loc.Add("Game.ModeAutoHelp", "Every supported game can trigger Fusion Mode.", "كل لعبة مدعومة يمكنها تشغيل وضع Fusion.");
        Loc.Add("Game.ModePinned", "Pinned game", "لعبة مثبتة");
        Loc.Add("Game.ModePinnedHelp", "Only your pinned game can trigger Fusion Mode.", "لعبتك المثبتة فقط هي التي تشغّل وضع Fusion.");
        Loc.Add("Game.ModeCurrent", "Current target: {0}", "الهدف الحالي: {0}");
        Loc.Add("Game.ModeTargetAuto", "Automatic detection", "الاكتشاف التلقائي");
        Loc.Add("Game.PinFirst", "Pin a game from the list below first.", "ثبّت لعبة من القائمة أدناه أولاً.");
        Loc.Add("Game.PinnedToast", "{0} is now the pinned game.", "{0} أصبحت اللعبة المثبتة.");
        Loc.Add("Game.UnpinnedToast", "Back to automatic detection.", "رجوع إلى الاكتشاف التلقائي.");
        Loc.Add("Game.ModeAutoTip", "Detect and protect any supported game", "اكتشف واحمِ أي لعبة مدعومة");
        Loc.Add("Game.ModePinnedTip", "Protect only the game you pin", "احمِ اللعبة التي تثبتها فقط");

        // ---------------------------------------------------------------- game cards
        Loc.Add("Game.Pin", "Pin", "تثبيت");
        Loc.Add("Game.Pinned", "Pinned", "مثبتة");
        Loc.Add("Game.PinTip", "Pin this game so only it triggers Fusion Mode", "ثبّت هذه اللعبة لتكون الوحيدة التي تشغّل وضع Fusion");
        Loc.Add("Game.UnpinTip", "Pinned — click to return to automatic detection", "مثبتة — انقر للرجوع إلى الاكتشاف التلقائي");
        Loc.Add("Game.RunningBadge", "RUNNING", "تعمل الآن");
        Loc.Add("Game.Processes", "Processes", "العمليات");
        Loc.Add("Game.SteamApp", "Steam {0}", "Steam {0}");
        Loc.Add("Game.SteamNone", "No Steam app id", "لا يوجد معرّف Steam");
        Loc.Add("Game.GamesTitle", "Supported games", "الألعاب المدعومة");
        Loc.Add("Game.GamesCount", "{0} games", "{0} لعبة");
        Loc.Add("Game.SearchPlaceholder", "Search games…", "ابحث عن لعبة…");
        Loc.Add("Game.SearchTip", "Filter by name, category, process or Steam id", "ابحث بالاسم أو الفئة أو العملية أو معرّف Steam");
        Loc.Add("Game.NoResults", "No games match your filters.", "لا توجد ألعاب تطابق التصفية.");
        Loc.Add("Game.NoResultsBody", "Try a different search term or category.", "جرّب كلمة بحث أو فئة أخرى.");
        Loc.Add("Game.CategoryTip", "Filter by category", "تصفية حسب الفئة");
        Loc.Add("Game.CategoryAll", "All games", "كل الألعاب");

        // ---------------------------------------------------------------- categories (translated from the Core catalog)
        Loc.Add("Game.Cat.TacticalFPS", "Tactical FPS", "تصويب تكتيكي");
        Loc.Add("Game.Cat.BattleRoyale", "Battle Royale", "باتل رويال");
        Loc.Add("Game.Cat.Moba", "MOBA", "MOBA");
        Loc.Add("Game.Cat.HeroShooter", "Hero Shooter", "تصويب أبطال");
        Loc.Add("Game.Cat.Survival", "Survival", "بقاء");
        Loc.Add("Game.Cat.Extraction", "Extraction", "استخراج");
        Loc.Add("Game.Cat.OpenWorld", "Open World", "عالم مفتوح");
        Loc.Add("Game.Cat.Sports", "Sports", "رياضة");
        Loc.Add("Game.Cat.LooterShooter", "Looter Shooter", "تصويب الغنائم");
        Loc.Add("Game.Cat.ArenaFPS", "Arena FPS", "تصويب سريع");
        Loc.Add("Game.Cat.Sandbox", "Sandbox", "صندوق رملي");
        Loc.Add("Game.Cat.Custom", "Custom", "مخصص");

        // ---------------------------------------------------------------- custom games
        Loc.Add("Game.CustomTitle", "Custom games", "ألعاب مخصصة");
        Loc.Add("Game.CustomHelp", "Add any game executable FusionNet should detect.", "أضف أي ملف لعبة ليكتشفه فيوجن نت.");
        Loc.Add("Game.CustomAdd", "Add game", "إضافة لعبة");
        Loc.Add("Game.CustomAddTip", "Choose a game executable to watch", "اختر ملف اللعبة الذي تريد مراقبته");
        Loc.Add("Game.CustomEmpty", "No custom games yet.", "لا توجد ألعاب مخصصة بعد.");
        Loc.Add("Game.CustomExists", "That executable is already in your list.", "هذا الملف مضاف إلى قائمتك مسبقاً.");
        Loc.Add("Game.CustomInvalid", "Choose a valid .exe file.", "اختر ملفاً تنفيذياً صالحاً.");
        Loc.Add("Game.CustomAdded", "Added {0}.", "تمت إضافة {0}.");
        Loc.Add("Game.CustomRemoved", "Removed {0}.", "تمت إزالة {0}.");
        Loc.Add("Game.CustomRemoveTip", "Remove this custom game", "إزالة هذه اللعبة المخصصة");
        Loc.Add("Game.EnabledTip", "Detect this game automatically when it runs", "اكتشاف هذه اللعبة تلقائياً عند تشغيلها");

        // ---------------------------------------------------------------- automation
        Loc.Add("Game.AutomationTitle", "Automation", "الأتمتة");
        Loc.Add("Game.AutoEngage", "Engage when a game starts", "التفعيل عند بدء اللعبة");
        Loc.Add("Game.AutoEngageHelp", "Fusion Mode engages automatically when a watched game launches. Needs the app running as administrator.", "يبدأ وضع Fusion تلقائياً عند إطلاق لعبة مراقبة. يحتاج تشغيل البرنامج كمسؤول.");
        Loc.Add("Game.RestoreOnExit", "Restore when the game exits", "الاستعادة عند إغلاق اللعبة");
        Loc.Add("Game.RestoreOnExitHelp", "Every journaled change is undone automatically.", "يُلغى تلقائياً كل تغيير مسجّل.");
        Loc.Add("Game.SuspendOnly", "Apply app rules only during a session", "تطبيق قواعد التطبيقات أثناء الجلسة فقط");
        Loc.Add("Game.SuspendOnlyHelp", "Background rules stay inactive while no game runs.", "تبقى قواعد الخلفية غير نشطة عند عدم تشغيل لعبة.");
        Loc.Add("Game.ConfirmExit", "Ask before exiting while active", "السؤال قبل الإغلاق أثناء التفعيل");
        Loc.Add("Game.ConfirmExitHelp", "Prevents closing with changes still applied.", "يمنع الإغلاق مع بقاء التغييرات مطبقة.");

        // ---------------------------------------------------------------- running now
        Loc.Add("Game.RunningTitle", "Currently running", "قيد التشغيل الآن");
        Loc.Add("Game.RunningRefresh", "Refresh running games", "تحديث الألعاب العاملة");
        Loc.Add("Game.RunningEmpty", "No supported game is running.", "لا توجد لعبة مدعومة قيد التشغيل.");
        Loc.Add("Game.RunningCount", "{0} running", "{0} قيد التشغيل");
        Loc.Add("Game.ArmedNotice", "Fusion Mode is active.", "وضع Fusion نشط.");
        Loc.Add("Game.ArmedNoticeBody", "Changing the watch list refreshes the active session automatically.", "تغيير قائمة المراقبة يحدّث الجلسة النشطة تلقائياً.");

        // ---------------------------------------------------------------- how it works
        Loc.Add("Game.HowTitle", "How it works", "كيف يعمل");
        Loc.Add("Game.HowDetect", "Detect", "الاكتشاف");
        Loc.Add("Game.HowDetectBody", "Running processes are checked every two seconds.", "تُفحص العمليات كل ثانيتين.");
        Loc.Add("Game.HowEnforce", "Enforce", "الفرض");
        Loc.Add("Game.HowEnforceBody", "Firewall, services, tweaks and app rules are applied and journaled.", "تُطبّق قواعد الجدار والخدمات والتحسينات وقواعد التطبيقات وتُسجّل.");
        Loc.Add("Game.HowRestore", "Restore", "الاستعادة");
        Loc.Add("Game.HowRestoreBody", "Stopping restores every change, even after a crash.", "الإيقاف يستعيد كل تغيير حتى بعد انهيار مفاجئ.");
    }
}
