using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Tuner;

/// <summary>Localized strings for the Network Tuner page.</summary>
internal static class TunerStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- page chrome
        Loc.Add("Tuner.SectionTitle", "Windows tuning options", "خيارات ضبط ويندوز");
        Loc.Add("Tuner.SectionBody",
            "Changes are backed up for restoration. Game Mode is recommended; other options need a measured reason and do not guarantee lower ping.",
            "تُحفظ التغييرات لاستعادتها. يُوصى بوضع الألعاب؛ تحتاج الخيارات الأخرى سبباً مبنياً على القياس ولا تضمن خفض البنق.");
        Loc.Add("Tuner.ApplyRecommended", "Apply recommended", "تطبيق الموصى به");
        Loc.Add("Tuner.RevertAll", "Revert all", "التراجع عن الكل");
        Loc.Add("Tuner.Summary", "{0} of {1} tweaks applied", "تم تطبيق {0} من {1} تحسيناً");
        Loc.Add("Tuner.RecommendedHint", "Recommended set: {0} Game Mode settings", "المجموعة الموصى بها: {0} من إعدادات وضع الألعاب");
        Loc.Add("Tuner.Busy", "Reading the registry…", "جارٍ قراءة السجل…");
        Loc.Add("Tuner.ReadFailed", "Could not read the current registry values.", "تعذّرت قراءة قيم السجل الحالية.");
        Loc.Add("Tuner.NoTweaks", "No tweaks are available on this version of Windows.", "لا توجد تحسينات متاحة على هذا الإصدار من ويندوز.");

        // ---------------------------------------------------------------- elevation
        Loc.Add("Tuner.ElevationTitle", "System tweaks need administrator rights",
            "تحسينات النظام تحتاج صلاحيات المسؤول");
        Loc.Add("Tuner.ElevationBody",
            "Machine-wide registry values and the power plan can only be changed with elevation. You can still review every value and description in limited mode.",
            "لا يمكن تغيير قيم السجل على مستوى الجهاز وملف الطاقة إلا بصلاحيات المسؤول. يمكنك مراجعة كل قيمة وشرحها في الوضع المحدود.");
        Loc.Add("Tuner.ElevationCancelled", "Administrator elevation was cancelled.", "تم إلغاء طلب صلاحيات المسؤول.");

        // ---------------------------------------------------------------- categories
        Loc.Add("Tuner.Category.Multimedia", "Multimedia scheduling", "جدولة الوسائط");
        Loc.Add("Tuner.Category.MultimediaSubtitle",
            "Optional MMCSS scheduling values and legacy values retained for restoration.",
            "قيم اختيارية لجدولة MMCSS وقيم قديمة محفوظة لاستعادة التغييرات.");
        Loc.Add("Tuner.Category.Network", "Network stack", "حزمة الشبكة");
        Loc.Add("Tuner.Category.NetworkSubtitle",
            "Optional TCP and QoS settings. TCP options do not affect UDP traffic.",
            "إعدادات TCP وQoS الاختيارية. لا تؤثر خيارات TCP على حركة UDP.");
        Loc.Add("Tuner.Category.Gaming", "Gaming features", "ميزات الألعاب");
        Loc.Add("Tuner.Category.GamingSubtitle",
            "Game Mode, capture pipelines and GPU scheduling.",
            "وضع الألعاب ونظام التسجيل وجدولة كرت الشاشة.");
        Loc.Add("Tuner.Category.AdvancedTcp", "Advanced TCP", "TCP المتقدمة");
        Loc.Add("Tuner.Category.AdvancedTcpSubtitle",
            "Legacy connection and scheduling options for diagnosed problems, not general gaming gains.",
            "خيارات قديمة للاتصالات والجدولة لمعالجة مشاكل محددة، وليست تحسينات عامة للألعاب.");
        Loc.Add("Tuner.Category.Power", "Power behaviour", "سلوك الطاقة");
        Loc.Add("Tuner.Category.PowerSubtitle",
            "Legacy registry value retained for restoring earlier changes. Use Windows power settings.",
            "قيمة قديمة في السجل محفوظة لاستعادة التغييرات السابقة. استخدم إعدادات الطاقة في ويندوز.");

        // ---------------------------------------------------------------- rows
        Loc.Add("Tuner.Expected", "Target", "الهدف");
        Loc.Add("Tuner.Current", "Current", "الحالي");
        Loc.Add("Tuner.Unavailable", "not set", "غير محددة");
        Loc.Add("Tuner.ReadError", "unreadable", "غير قابلة للقراءة");
        Loc.Add("Tuner.GroupSummary", "{0}/{1} applied", "مطبّق {0}/{1}");

        // ---------------------------------------------------------------- feedback
        Loc.Add("Tuner.ConfirmTitle", "Apply an advanced tweak?", "تطبيق تحسين متقدّم؟");
        Loc.Add("Tuner.ConfirmAdvanced",
            "“{0}” changes system-wide behaviour. Check its description and restart requirement.\n\n{1}\n\nApply it now?",
            "«{0}» يغيّر سلوك النظام بالكامل. راجع وصفه ومتطلبات إعادة التشغيل.\n\n{1}\n\nهل تريد تطبيقه الآن؟");
        Loc.Add("Tuner.Applied", "“{0}” is now applied.", "تم تطبيق «{0}» الآن.");
        Loc.Add("Tuner.Reverted", "“{0}” was reverted to its original value.", "تمت إعادة «{0}» إلى قيمته الأصلية.");
        Loc.Add("Tuner.ToggleFailed", "Could not change “{0}”.", "تعذّر تغيير «{0}».");
        Loc.Add("Tuner.RecommendedDone", "{0} recommended tweaks applied.", "تم تطبيق {0} تحسيناً موصى به.");
        Loc.Add("Tuner.RevertDone", "{0} tweaks reverted.", "تم التراجع عن {0} تحسيناً.");
        Loc.Add("Tuner.NothingToRevert", "No tweaks are currently marked as applied.", "لا توجد تحسينات معلّمة كمطبّقة حالياً.");
        Loc.Add("Tuner.Refreshed", "Tweak states refreshed.", "تم تحديث حالة التحسينات.");
        Loc.Add("Tuner.RestartHint", "{0} of them need a Windows restart to take effect.", "{0} منها تحتاج إعادة تشغيل ويندوز لتصبح فعّالة.");
        Loc.Add("Tuner.PartialFailure", "Some tweaks could not be applied: {0}", "تعذّر تطبيق بعض التحسينات: {0}");
    }
}
