using System.Runtime.CompilerServices;

namespace FusionNet.App.Localization;

/// <summary>Shell-wide strings. Page-specific strings are registered by each page's own file.</summary>
internal static class ShellStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        Loc.Add("Shell.PrepareGame", "Prepare gaming session", "تجهيز جلسة اللعب");
        Loc.Add("Shell.AdvancedFusion", "Advanced Fusion mode", "وضع Fusion المتقدم");
        Loc.Add("Shell.LegacyHint", "Legacy tuning workflow; review its settings before starting.", "مسار الضبط القديم؛ راجع إعداداته قبل التفعيل.");
        // ---------------------------------------------------------------- brand / shell
        Loc.Add("App.Name", "FusionNet Pro", "فيوجن نت برو");
        Loc.Add("App.Tagline", "Unified Network Optimizer & Gaming Shield", "منظّم الشبكة المتكامل ودرع الألعاب");
        Loc.Add("App.Version", "Version {0}", "الإصدار {0}");
        Loc.Add("App.Ready", "Ready", "جاهز");
        Loc.Add("App.Loading", "Starting FusionNet Pro…", "جارٍ تشغيل فيوجن نت برو…");

        // ---------------------------------------------------------------- navigation groups
        Loc.Add("Nav.Group.Control", "CONTROL", "التحكم");
        Loc.Add("Nav.Group.Optimize", "OPTIMIZE", "التحسين");
        Loc.Add("Nav.Group.Insights", "INSIGHTS", "المراقبة");
        Loc.Add("Nav.Group.System", "SYSTEM", "النظام");

        // ---------------------------------------------------------------- page titles
        Loc.Add("Page.Dashboard", "Dashboard", "لوحة القيادة");
        Loc.Add("Page.Gaming", "Games & Profiles", "الألعاب والملفات");
        Loc.Add("Page.AppShield", "App Shield", "درع التطبيقات");
        Loc.Add("Page.Tuner", "Network Tuner", "محسّن الشبكة");
        Loc.Add("Page.Dns", "DNS & Latency", "DNS وزمن الاستجابة");
        Loc.Add("Page.Monitor", "Live Monitor", "المراقبة المباشرة");
        Loc.Add("Page.Activity", "Activity Log", "سجل النشاط");
        Loc.Add("Page.Settings", "Settings", "الإعدادات");

        Loc.Add("Page.Dashboard.Subtitle", "One switch protects your connection and focuses your PC.", "مفتاح واحد يحمي اتصالك ويركّز حاسوبك على اللعب.");
        Loc.Add("Page.Gaming.Subtitle", "Pick the games FusionNet should detect and shield automatically.", "اختر الألعاب التي يكتشفها ويحميها فيوجن نت تلقائياً.");
        Loc.Add("Page.AppShield.Subtitle", "Decide what background apps may do while you play.", "حدّد ما يمكن لتطبيقات الخلفية فعله أثناء اللعب.");
        Loc.Add("Page.Tuner.Subtitle", "Reversible Windows latency tweaks, each one explained.", "تحسينات ويندوز لزمن الاستجابة، كل واحدة مشروحة وقابلة للتراجع.");
        Loc.Add("Page.Dns.Subtitle", "Benchmark resolvers and switch to the fastest one safely.", "قِس سرعة خوادم DNS وانتقل إلى الأسرع بأمان.");
        Loc.Add("Page.Monitor.Subtitle", "Latency, jitter, packet loss, throughput and adapter health in real time.", "زمن الاستجابة والتذبذب وفقدان الحزم والسرعة وصحة الاتصال في الوقت الحقيقي.");
        Loc.Add("Page.Activity.Subtitle", "Everything FusionNet did, with a full undo path.", "كل ما فعله فيوجن نت، مع إمكانية التراجع الكامل.");
        Loc.Add("Page.Settings.Subtitle", "Language, accent, startup, hotkey and data management.", "اللغة والمظهر والبدء التلقائي والاختصار وإدارة البيانات.");

        // ---------------------------------------------------------------- common actions
        Loc.Add("Common.Apply", "Apply", "تطبيق");
        Loc.Add("Common.ApplyAll", "Apply all", "تطبيق الكل");
        Loc.Add("Common.Cancel", "Cancel", "إلغاء");
        Loc.Add("Common.Save", "Save", "حفظ");
        Loc.Add("Common.SaveSettings", "Save settings", "حفظ الإعدادات");
        Loc.Add("Common.Reset", "Reset", "إعادة تعيين");
        Loc.Add("Common.Restore", "Restore", "استعادة");
        Loc.Add("Common.Undo", "Undo", "تراجع");
        Loc.Add("Common.Refresh", "Refresh", "تحديث");
        Loc.Add("Common.Reload", "Reload", "إعادة تحميل");
        Loc.Add("Common.Start", "Start", "بدء");
        Loc.Add("Common.Stop", "Stop", "إيقاف");
        Loc.Add("Common.Add", "Add", "إضافة");
        Loc.Add("Common.Remove", "Remove", "إزالة");
        Loc.Add("Common.Delete", "Delete", "حذف");
        Loc.Add("Common.Clear", "Clear", "مسح");
        Loc.Add("Common.Browse", "Browse…", "استعراض…");
        Loc.Add("Common.Export", "Export", "تصدير");
        Loc.Add("Common.Copy", "Copy", "نسخ");
        Loc.Add("Common.Close", "Close", "إغلاق");
        Loc.Add("Common.Restart", "Restart", "إعادة تشغيل");
        Loc.Add("Common.Details", "Details", "التفاصيل");
        Loc.Add("Common.Search", "Search", "بحث");
        Loc.Add("Common.Filter", "Filter", "تصفية");
        Loc.Add("Common.All", "All", "الكل");
        Loc.Add("Common.None", "None", "لا شيء");
        Loc.Add("Common.Done", "Done", "تم");
        Loc.Add("Common.Failed", "Failed", "فشل");
        Loc.Add("Common.Success", "Success", "نجاح");
        Loc.Add("Common.Warning", "Warning", "تحذير");
        Loc.Add("Common.Error", "Error", "خطأ");
        Loc.Add("Common.Notice", "Notice", "تنبيه");
        Loc.Add("Common.Loading", "Loading…", "جارٍ التحميل…");
        Loc.Add("Common.Working", "Working…", "جارٍ التنفيذ…");
        Loc.Add("Common.Seconds", "seconds", "ثانية");
        Loc.Add("Common.Second", "second", "ثانية");
        Loc.Add("Common.Ever", "ever", "أبداً");
        Loc.Add("Common.Unknown", "Unknown", "غير معروف");
        Loc.Add("Common.NotAvailable", "Not available", "غير متاح");
        Loc.Add("Common.Optional", "Optional", "اختياري");
        Loc.Add("Common.Required", "Required", "مطلوب");
        Loc.Add("Common.Recommended", "Recommended", "موصى به");
        Loc.Add("Common.Advanced", "Advanced", "متقدّم");
        Loc.Add("Common.Safe", "Safe", "آمن");
        Loc.Add("Common.Moderate", "Moderate", "متوسط");
        Loc.Add("Common.Custom", "Custom", "مخصص");
        Loc.Add("Common.Default", "Default", "افتراضي");
        Loc.Add("Common.Auto", "Automatic", "تلقائي");
        Loc.Add("Common.Manual", "Manual", "يدوي");
        Loc.Add("Common.Enabled", "Enabled", "مفعّل");
        Loc.Add("Common.Disabled", "Disabled", "معطّل");
        Loc.Add("Common.Active", "Active", "نشط");
        Loc.Add("Common.Inactive", "Inactive", "غير نشط");
        Loc.Add("Common.Idle", "Idle", "خامل");
        Loc.Add("Common.Running", "Running", "يعمل");
        Loc.Add("Common.Stopped", "Stopped", "متوقف");
        Loc.Add("Common.Paused", "Paused", "موقوف مؤقتاً");
        Loc.Add("Common.Live", "LIVE", "مباشر");
        Loc.Add("Common.Total", "Total", "الإجمالي");
        Loc.Add("Common.Today", "Today", "اليوم");
        Loc.Add("Common.Now", "now", "الآن");
        Loc.Add("Common.Settings", "Settings", "الإعدادات");
        Loc.Add("Common.Language", "Language", "اللغة");
        Loc.Add("Common.Arabic", "العربية", "العربية");
        Loc.Add("Common.English", "English", "English");
        Loc.Add("Common.Loaded", "Loaded", "تم التحميل");

        // ---------------------------------------------------------------- elevation
        Loc.Add("Elevation.Elevated", "Administrator", "صلاحيات المسؤول");
        Loc.Add("Elevation.NotElevated", "Limited mode", "وضع محدود");
        Loc.Add("Elevation.RestartElevated", "Restart as administrator", "إعادة التشغيل كمسؤول");
        Loc.Add("Elevation.BannerTitle", "Administrator rights are required for Fusion Mode", "وضع Fusion يحتاج صلاحيات المسؤول");
        Loc.Add("Elevation.BannerBody", "Blocking apps with Windows Firewall, pausing services and machine-wide tweaks need elevation. Monitoring, tuning preview and DNS benchmarks keep working in limited mode.", "حجب التطبيقات بجدار ويندوز وإيقاف الخدمات وتعديلات النظام تحتاج صلاحيات المسؤول. المراقبة ومعاينة التحسينات وقياس DNS تعمل في الوضع المحدود.");
        Loc.Add("Elevation.Restarting", "Restarting with administrator rights…", "جارٍ إعادة التشغيل بصلاحيات المسؤول…");

        // ---------------------------------------------------------------- fusion mode
        Loc.Add("Fusion.Mode", "Fusion Mode", "وضع Fusion");
        Loc.Add("Fusion.Engage", "Activate Fusion Mode", "تفعيل وضع Fusion");
        Loc.Add("Fusion.Disengage", "Stop Fusion Mode", "إيقاف وضع Fusion");
        Loc.Add("Fusion.Active", "Fusion Mode is active", "وضع Fusion نشط");
        Loc.Add("Fusion.Inactive", "Fusion Mode is off", "وضع Fusion متوقف");
        Loc.Add("Fusion.Arming", "Arming…", "جارٍ التفعيل…");
        Loc.Add("Fusion.Restoring", "Restoring…", "جارٍ الاستعادة…");
        Loc.Add("Fusion.ErrorState", "Fusion Mode hit an error", "حدث خطأ في وضع Fusion");
        Loc.Add("Fusion.ProtectedTitle", "PROTECTED", "محمي");
        Loc.Add("Fusion.OffTitle", "STANDBY", "في الانتظار");
        Loc.Add("Fusion.ArmingTitle", "ARMING", "جارٍ التفعيل");
        Loc.Add("Fusion.RestoringTitle", "RESTORING", "جارٍ الاستعادة");
        Loc.Add("Fusion.ErrorTitle", "ATTENTION", "انتباه");
        Loc.Add("Fusion.NoGame", "No game detected", "لم يتم اكتشاف لعبة");
        Loc.Add("Fusion.GameRunning", "Detected: {0}", "اللعبة المكتشفة: {0}");
        Loc.Add("Fusion.AutoEngaged", "Auto-engaged because {0} started", "تم التفعيل تلقائياً لبدء {0}");
        Loc.Add("Fusion.ManualEngaged", "Fusion Mode activated manually", "تم تفعيل وضع Fusion يدوياً");
        Loc.Add("Fusion.Disengaged", "Fusion Mode stopped, everything restored", "تم إيقاف وضع Fusion واستعادة كل شيء");
        Loc.Add("Fusion.RestoredCrash", "Recovered a previous session and restored {0} changes", "تم استرجاع جلسة سابقة واستعادة {0} تغيير");
        Loc.Add("Fusion.RequiresAdmin", "Fusion Mode needs administrator rights.", "وضع Fusion يحتاج صلاحيات المسؤول.");
        Loc.Add("Fusion.SessionTime", "Session", "الجلسة");
        Loc.Add("Fusion.QuickToggle", "Quick toggle", "تبديل سريع");
        Loc.Add("Fusion.HotkeyHint", "Hotkey: {0}", "الاختصار: {0}");
        Loc.Add("Fusion.SessionCount", "{0} firewall rules · {1} services paused · {2} tweaks applied", "{0} قاعدة جدار حماية · {1} خدمة موقوفة · {2} تحسين مطبّق");
        Loc.Add("Shell.PendingRestore", "A previous session left {0} changes to restore.", "هناك جلسة سابقة تركت {0} تغييراً بحاجة إلى استعادة.");
        Loc.Add("Elevation.Cancelled", "Administrator restart was cancelled.", "تم إلغاء إعادة التشغيل كمسؤول.");

        // ---------------------------------------------------------------- title bar / tray
        Loc.Add("TitleBar.Minimize", "Minimize", "تصغير");
        Loc.Add("TitleBar.Maximize", "Maximize", "تكبير");
        Loc.Add("TitleBar.Restore", "Restore down", "استعادة الحجم");
        Loc.Add("TitleBar.Close", "Close", "إغلاق");
        Loc.Add("TitleBar.SwitchLanguage", "العربية / English", "العربية / English");
        Loc.Add("TitleBar.Settings", "Settings", "الإعدادات");
        Loc.Add("TitleBar.Help", "Help (F1)", "المساعدة (F1)");
        Loc.Add("Tray.Open", "Open FusionNet Pro", "فتح فيوجن نت برو");
        Loc.Add("Tray.Exit", "Exit", "خروج");
        Loc.Add("Tray.Tooltip", "FusionNet Pro — network shield", "فيوجن نت برو — درع الشبكة");
        Loc.Add("Tray.RunningHint", "FusionNet Pro is still running in the notification area.", "فيوجن نت برو لا يزال يعمل في منطقة الإشعارات.");
        Loc.Add("Exit.Title", "Fusion Mode is still active", "وضع Fusion ما زال نشطاً");
        Loc.Add("Exit.Body", "Closing now restores every change FusionNet made. Continue?", "الإغلاق الآن سيعيد كل تغيير أجراه فيوجن نت. هل تريد المتابعة؟");
        Loc.Add("Exit.Stay", "Stay", "البقاء");
        Loc.Add("Exit.ExitAndRestore", "Exit and restore", "خروج واستعادة");

        // ---------------------------------------------------------------- toast / dialogs
        Loc.Add("Toast.Restored", "Everything was restored.", "تمت استعادة كل شيء.");
        Loc.Add("Toast.Copied", "Copied to clipboard.", "تم النسخ إلى الحافظة.");
        Loc.Add("Dialog.SaveReportTitle", "Save diagnostics report", "حفظ تقرير التشخيص");
        Loc.Add("Dialog.ExportLogTitle", "Export activity log", "تصدير سجل النشاط");
        Loc.Add("Dialog.SelectExeTitle", "Choose an executable", "اختر ملفاً تنفيذياً");
        Loc.Add("Dialog.ExeFilter", "Applications (*.exe)|*.exe|All files (*.*)|*.*", "التطبيقات (*.exe)|*.exe|كل الملفات (*.*)|*.*");
        Loc.Add("Dialog.TextFilter", "Text files (*.txt)|*.txt|All files (*.*)|*.*", "ملفات نصية (*.txt)|*.txt|كل الملفات (*.*)|*.*");
        Loc.Add("Dialog.JsonFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*", "ملفات JSON (*.json)|*.json|كل الملفات (*.*)|*.*");

        // ---------------------------------------------------------------- units & metrics
        Loc.Add("Unit.Ms", "ms", "مللي ثانية");
        Loc.Add("Unit.Ping", "Ping", "البنق");
        Loc.Add("Unit.Jitter", "Jitter", "التذبذب");
        Loc.Add("Unit.Loss", "Loss", "الفقدان");
        Loc.Add("Unit.Download", "Download", "التنزيل");
        Loc.Add("Unit.Upload", "Upload", "الرفع");
        Loc.Add("Unit.Quality", "Quality", "الجودة");
        Loc.Add("Metric.Exellent", "Excellent", "ممتاز");
        Loc.Add("Metric.Good", "Good", "جيد");
        Loc.Add("Metric.Fair", "Fair", "مقبول");
        Loc.Add("Metric.Poor", "Poor", "ضعيف");
        Loc.Add("Metric.Unknown", "Unknown", "غير معروف");
        Loc.Add("Metric.NoReply", "No reply", "لا يوجد رد");

        // ---------------------------------------------------------------- shared UI labels
        Loc.Add("Label.Status", "Status", "الحالة");
        Loc.Add("Label.Actions", "Actions", "الإجراءات");
        Loc.Add("Label.Name", "Name", "الاسم");
        Loc.Add("Label.Category", "Category", "الفئة");
        Loc.Add("Label.Description", "Description", "الوصف");
        Loc.Add("Label.Risk", "Risk", "الخطورة");
        Loc.Add("Label.Impact", "Impact", "التأثير");
        Loc.Add("Label.Target", "Target", "الهدف");
        Loc.Add("Label.Current", "Current", "الحالي");
        Loc.Add("Label.Recommended", "Recommended value", "القيمة الموصى بها");
        Loc.Add("Label.LastUpdated", "Updated", "آخر تحديث");
        Loc.Add("Label.EmptyTitle", "Nothing here yet", "لا يوجد شيء هنا بعد");
        Loc.Add("Label.EmptyBody", "Data will appear as soon as it is available.", "ستظهر البيانات بمجرد توفرها.");
        Loc.Add("Label.NeedsAdmin", "Needs administrator", "يحتاج صلاحيات المسؤول");
        Loc.Add("Label.RestartRequired", "Restart required", "يحتاج إعادة تشغيل");
        Loc.Add("Label.SearchApps", "Search applications…", "ابحث عن تطبيق…");

        // ---------------------------------------------------------------- legacy migration
        Loc.Add("Migration.Imported", "Imported settings from an earlier FusionNet build.", "تم استيراد الإعدادات من إصدار سابق.");
    }
}
