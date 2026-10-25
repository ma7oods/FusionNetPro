using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.AppShield;

/// <summary>Bilingual strings for the App Shield page (keys are prefixed with <c>Shield.</c>).</summary>
internal static class AppShieldStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- header + filters
        Loc.Add("Shield.Summary", "{0} of {1} protections active", "{0} من {1} حماية مفعّلة");
        Loc.Add("Shield.SummaryCaption", "Active policies", "السياسات النشطة");
        Loc.Add("Shield.SearchPlaceholder", "Search applications…", "ابحث عن تطبيق…");
        Loc.Add("Shield.SearchTip", "Filter by name, description or process", "ابحث بالاسم أو الوصف أو العملية");
        Loc.Add("Shield.RunningOnly", "Running only", "العاملة فقط");
        Loc.Add("Shield.RunningOnlyTip", "Show only applications that are running now", "إظهار التطبيقات العاملة الآن فقط");
        Loc.Add("Shield.CategoryTip", "Filter by category", "تصفية حسب الفئة");
        Loc.Add("Shield.CategoryAll", "All categories", "كل الفئات");
        Loc.Add("Shield.GroupsEmpty", "No applications match these filters", "لا توجد تطبيقات تطابق هذه التصفية");
        Loc.Add("Shield.GroupsEmptyBody", "Clear the search or choose another category.", "امسح البحث أو اختر فئة أخرى.");
        Loc.Add("Shield.GroupCount", "{0} apps", "{0} تطبيق");
        Loc.Add("Shield.RefreshStatus", "Refresh status", "تحديث الحالة");
        Loc.Add("Shield.Updated", "Updated {0}", "آخر تحديث {0}");

        // ---------------------------------------------------------------- actions
        Loc.Add("Shield.Block", "Block network", "حجب الشبكة");
        Loc.Add("Shield.BlockShort", "Block", "حجب");
        Loc.Add("Shield.Suspend", "Suspend", "تجميد");
        Loc.Add("Shield.SuspendShort", "Suspend", "تجميد");
        Loc.Add("Shield.Throttle", "Lower CPU/IO priority", "خفض أولوية CPU/IO");
        Loc.Add("Shield.ThrottleShort", "Low priority", "أولوية منخفضة");
        Loc.Add("Shield.Ignore", "Ignore", "تجاهل");
        Loc.Add("Shield.IgnoreShort", "Ignore", "تجاهل");
        Loc.Add("Shield.BlockTip", "Blocks every network packet with Windows Firewall while your game runs.", "يحجب كل حزم الشبكة بجدار ويندوز أثناء تشغيل لعبتك.");
        Loc.Add("Shield.SuspendTip", "Freezes the application's threads; downloads pause and its state is kept.", "يجمّد خيوط التطبيق؛ تتوقف التنزيلات ويُحفظ وضعه.");
        Loc.Add("Shield.ThrottleTip", "Lowers CPU/IO priority only; it does not limit upload or download bandwidth.", "يخفض أولوية CPU/IO فقط؛ لا يقيّد سرعة رفع أو تنزيل الشبكة.");
        Loc.Add("Shield.IgnoreTip", "FusionNet never touches this application.", "لا يتعامل فيوجن نت مع هذا التطبيق إطلاقاً.");
        Loc.Add("Shield.ActionLabel", "Action", "الإجراء");

        // ---------------------------------------------------------------- badges
        Loc.Add("Shield.Running", "RUNNING", "يعمل");
        Loc.Add("Shield.Suspended", "SUSPENDED", "مجمّد");
        Loc.Add("Shield.RecommendedTip", "Recommended rule for this application", "قاعدة موصى بها لهذا التطبيق");
        Loc.Add("Shield.Processes", "Processes", "العمليات");

        // ---------------------------------------------------------------- elevation / info
        Loc.Add("Shield.InfoTitleElevated", "Firewall blocking is available", "حجب الجدار الناري متاح");
        Loc.Add("Shield.InfoTitle", "Firewall blocking requires administrator rights", "حجب الجدار الناري يحتاج صلاحيات المسؤول");
        Loc.Add("Shield.InfoBody",
            "Managed apps lose network access through Windows Firewall rules while your game runs. Your game and Discord are never blocked.",
            "تفقد التطبيقات المُدارة اتصالها بالشبكة بقواعد جدار ويندوز أثناء تشغيل لعبتك. لا تُحجب لعبتك ولا Discord أبداً.");
        Loc.Add("Shield.InfoExtra",
            "Without elevation, block rules are skipped while suspend and throttle keep working.",
            "دون صلاحيات المسؤول تُتجاوز قواعد الحجب ويبقى التجميد والتخفيض فعّالين.");
        Loc.Add("Shield.Armed", "Fusion active", "Fusion نشط");

        // ---------------------------------------------------------------- custom applications
        Loc.Add("Shield.CustomTitle", "Custom applications", "تطبيقات مخصصة");
        Loc.Add("Shield.CustomHelp", "Add any .exe and choose what FusionNet may do to it.", "أضف أي ملف تنفيذي واختر ما يمكن لفيوجن نت فعله به.");
        Loc.Add("Shield.CustomAdd", "Add application", "إضافة تطبيق");
        Loc.Add("Shield.CustomAddTip", "Choose an executable to manage", "اختر ملفاً تنفيذياً لإدارته");
        Loc.Add("Shield.CustomEmpty", "No custom applications yet.", "لا توجد تطبيقات مخصصة بعد.");
        Loc.Add("Shield.CustomExists", "That application is already in your list.", "هذا التطبيق مضاف مسبقاً.");
        Loc.Add("Shield.CustomInvalid", "Choose a valid .exe file.", "اختر ملفاً تنفيذياً صالحاً.");
        Loc.Add("Shield.CustomAdded", "Added {0}.", "تمت إضافة {0}.");
        Loc.Add("Shield.CustomRemoved", "Removed {0}.", "تمت إزالة {0}.");
        Loc.Add("Shield.CustomRemoveTip", "Remove this application", "إزالة هذا التطبيق");
        Loc.Add("Shield.EnabledTip", "Apply this rule while Fusion Mode is active", "تطبيق هذه القاعدة أثناء تفعيل وضع Fusion");
        Loc.Add("Shield.AddWith", "Add with action", "أضف بالإجراء");
        Loc.Add("Shield.CustomActionTip", "Choose what FusionNet may do to this application", "اختر ما يمكن لفيوجن نت فعله بهذا التطبيق");

        // ---------------------------------------------------------------- feedback
        Loc.Add("Shield.Applied", "App Shield rule saved.", "تم حفظ قاعدة درع التطبيقات.");
        Loc.Add("Shield.AppliedArmed", "Rule applied and Fusion Mode refreshed.", "تم تطبيق القاعدة وتحديث وضع Fusion.");
        Loc.Add("Shield.RefreshFailed", "Rule saved, but Fusion Mode could not refresh: {0}", "تم حفظ القاعدة، لكن تعذّر تحديث وضع Fusion: {0}");
        Loc.Add("Shield.ElevationCancelled", "Restart as administrator was cancelled.", "تم إلغاء إعادة التشغيل بصلاحيات المسؤول.");

        // ---------------------------------------------------------------- categories
        Loc.Add("Shield.Cat.Launchers", "Launchers", "المشغّلات");
        Loc.Add("Shield.Cat.CloudSync", "Cloud sync", "المزامنة السحابية");
        Loc.Add("Shield.Cat.Torrents", "Torrent clients", "عملاء التورنت");
        Loc.Add("Shield.Cat.Downloads", "Download managers", "مديرو التنزيل");
        Loc.Add("Shield.Cat.Browsers", "Web browsers", "متصفحات الويب");
        Loc.Add("Shield.Cat.UpdateAgents", "Update agents", "وكلاء التحديث");
        Loc.Add("Shield.Cat.RemoteTools", "Remote tools", "أدوات التحكم عن بعد");
        Loc.Add("Shield.Cat.Custom", "Custom", "مخصص");
    }
}
