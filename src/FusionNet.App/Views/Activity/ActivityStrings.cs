using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Activity;

/// <summary>Localized strings for the Activity Log page.</summary>
internal static class ActivityStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- toolbar
        Loc.Add("Log.FilterAll", "All", "الكل");
        Loc.Add("Log.FilterSuccess", "Success", "نجاح");
        Loc.Add("Log.FilterInfo", "Info", "معلومة");
        Loc.Add("Log.FilterWarning", "Warnings", "تحذيرات");
        Loc.Add("Log.FilterError", "Errors", "أخطاء");
        Loc.Add("Log.SearchPlaceholder", "Search messages, categories or details…", "ابحث في الرسائل أو الفئات أو التفاصيل…");
        Loc.Add("Log.AutoScroll", "Auto-scroll", "تمرير تلقائي");
        Loc.Add("Log.Clear", "Clear log", "مسح السجل");
        Loc.Add("Log.Export", "Export log", "تصدير السجل");
        Loc.Add("Log.EntryCount", "{0} entries", "{0} سجلاً");
        Loc.Add("Log.FilteredCount", "{0} shown", "{0} ظاهر");
        Loc.Add("Log.Live", "Live", "مباشر");

        // ---------------------------------------------------------------- list
        Loc.Add("Log.EmptyTitle", "No activity yet", "لا يوجد نشاط بعد");
        Loc.Add("Log.EmptyBody", "FusionNet records every change it makes, with a full undo path.",
            "يسجّل فيوجن نت كل تغيير يجريه مع إمكانية التراجع الكامل.");
        Loc.Add("Log.NoMatches", "No entries match the current filter.", "لا توجد سجلات تطابق التصفية الحالية.");
        Loc.Add("Log.DetailTitle", "Entry detail", "تفاصيل السجل");
        Loc.Add("Log.DetailEmpty", "Select an entry to read its full details.", "اختر سجلاً لعرض تفاصيله الكاملة.");
        Loc.Add("Log.Category", "Category", "الفئة");
        Loc.Add("Log.Time", "Time", "الوقت");
        Loc.Add("Log.Level", "Level", "المستوى");
        Loc.Add("Log.NoExtraDetail", "No extra detail was recorded.", "لم تُسجَّل تفاصيل إضافية.");
        Loc.Add("Log.Copy", "Copy entry", "نسخ السجل");
        Loc.Add("Log.Copied", "Entry copied to the clipboard.", "تم نسخ السجل إلى الحافظة.");

        // ---------------------------------------------------------------- clear / export
        Loc.Add("Log.ClearTitle", "Clear the activity log?", "مسح سجل النشاط؟");
        Loc.Add("Log.ClearBody",
            "This clears the log shown here; it does not undo any change FusionNet made. Restore actions stay available in the session journal.",
            "هذا يمسح السجل المعروض فقط ولا يتراجع عن أي تغيير أجراه فيوجن نت. تبقى إجراءات الاستعادة متاحة في سجل الجلسة.");
        Loc.Add("Log.Cleared", "Activity log cleared.", "تم مسح سجل النشاط.");
        Loc.Add("Log.ExportDone", "Activity log exported.", "تم تصدير سجل النشاط.");
        Loc.Add("Log.ExportFailed", "Could not export the log: {0}", "تعذّر تصدير السجل: {0}");
        Loc.Add("Log.ExportEmpty", "There is nothing to export yet.", "لا يوجد ما يمكن تصديره بعد.");

        // ---------------------------------------------------------------- session journal
        Loc.Add("Log.SessionTitle", "Session journal", "سجل الجلسة");
        Loc.Add("Log.SessionBody", "Every change FusionNet made in this session, ready to undo.",
            "كل تغيير أجراه فيوجن نت في هذه الجلسة، جاهز للتراجع.");
        Loc.Add("Log.SessionId", "Session", "الجلسة");
        Loc.Add("Log.RecordedChanges", "Recorded changes", "التغييرات المسجلة");
        Loc.Add("Log.SessionStarted", "Started {0}", "بدأت {0}");
        Loc.Add("Log.SessionRecording", "Recording", "جارٍ التسجيل");
        Loc.Add("Log.SessionIdle", "No active session", "لا جلسة نشطة");
        Loc.Add("Log.NoSession", "No session has been recorded yet.", "لم تُسجَّل أي جلسة بعد.");
        Loc.Add("Log.PendingRestoreTitle", "An unfinished session was detected", "تم اكتشاف جلسة غير مكتملة");
        Loc.Add("Log.PendingRestoreBody",
            "FusionNet did not close cleanly last time. Restore every change from that session before playing again.",
            "لم يُغلق فيوجن نت بشكل سليم في المرة السابقة. استعد كل تغييرات تلك الجلسة قبل اللعب مجدداً.");
        Loc.Add("Log.RestoreNow", "Restore now", "استعادة الآن");
        Loc.Add("Log.Restoring", "Restoring…", "جارٍ الاستعادة…");
        Loc.Add("Log.RestoreDone", "Restore complete. {0}", "اكتملت الاستعادة. {0}");
        Loc.Add("Log.RestoreFailed", "Restore failed: {0}", "فشلت الاستعادة: {0}");
        Loc.Add("Log.RestoreNone", "Nothing needed restoring.", "لا يوجد شيء يحتاج الاستعادة.");
        Loc.Add("Log.JournalEmpty", "The journal is empty for this session.", "سجل هذه الجلسة فارغ.");
        Loc.Add("Log.LastUpdated", "Updated {0}", "آخر تحديث {0}");
    }
}
