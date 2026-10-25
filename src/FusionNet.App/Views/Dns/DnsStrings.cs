using System.Runtime.CompilerServices;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Dns;

/// <summary>Localized strings for the DNS &amp; Latency page.</summary>
internal static class DnsStrings
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ---------------------------------------------------------------- current DNS
        Loc.Add("Dns.CurrentTitle", "Current DNS", "إعدادات DNS الحالية");
        Loc.Add("Dns.Adapter", "Network adapter", "كرت الشبكة");
        Loc.Add("Dns.ModeAutomatic", "Automatic (DHCP)", "تلقائي (DHCP)");
        Loc.Add("Dns.ModeStatic", "Fixed servers reported by Windows", "خوادم ثابتة حسب ويندوز");
        Loc.Add("Dns.ModeHint",
            "Windows does not label DHCP versus static DNS; FusionNet infers this from the adapter's server list.",
            "لا يوضّح ويندوز ما إذا كانت خوادم DNS من DHCP أو ثابتة؛ يستنتج فيوجن نت ذلك من قائمة خوادم الكرت.");
        Loc.Add("Dns.ServersLabel", "Servers", "الخوادم");
        Loc.Add("Dns.NoDnsServers", "Automatic — no fixed servers configured", "تلقائي — لا خوادم ثابتة");
        Loc.Add("Dns.NoAdapter", "No active network adapter was found.", "لم يتم العثور على كرت شبكة نشط.");
        Loc.Add("Dns.AdapterDefault", "Default route", "المسار الافتراضي");
        Loc.Add("Dns.AdapterWireless", "Wi-Fi", "لاسلكي");
        Loc.Add("Dns.AdapterWired", "Ethernet", "سلكي");
        Loc.Add("Dns.AdapterIPv4", "IPv4", "IPv4");
        Loc.Add("Dns.Refreshed", "Adapter information refreshed.", "تم تحديث معلومات الكرت.");
        Loc.Add("Dns.RefreshFailed", "Could not read the network adapters.", "تعذّرت قراءة كروت الشبكة.");

        // ---------------------------------------------------------------- actions
        Loc.Add("Dns.FlushDns", "Flush DNS cache", "تفريغ ذاكرة DNS");
        Loc.Add("Dns.FlushDone", "The DNS cache was flushed.", "تم تفريغ ذاكرة DNS.");
        Loc.Add("Dns.ResetAutomatic", "Reset to automatic", "إرجاع إلى التلقائي");
        Loc.Add("Dns.ConfirmResetTitle", "Reset DNS to automatic?", "إرجاع DNS إلى التلقائي؟");
        Loc.Add("Dns.ConfirmReset",
            "Windows will obtain DNS servers for “{0}” automatically again. The previous servers are journaled and can be restored.",
            "سيحصل ويندوز على خوادم DNS لـ«{0}» تلقائياً مرة أخرى. الخوادم السابقة مسجلة في السجل ويمكن استعادتها.");
        Loc.Add("Dns.ResetDone", "DNS was reset to automatic.", "تم إرجاع DNS إلى الوضع التلقائي.");
        Loc.Add("Dns.ApplySelected", "Apply selected", "تطبيق المحدد");
        Loc.Add("Dns.ConfirmApplyTitle", "Change DNS servers?", "تغيير خوادم DNS؟");
        Loc.Add("Dns.ConfirmApply",
            "Apply {0} to “{1}”?\n\n{2}\n\nThe current setting is journaled and can be restored at any time.",
            "تطبيق {0} على «{1}»؟\n\n{2}\n\nالإعداد الحالي مسجل في السجل ويمكن استعادته في أي وقت.");
        Loc.Add("Dns.ApplyDone", "The resolver servers were updated. Flushing the cache…", "تم تحديث خوادم DNS. جارٍ تفريغ الذاكرة…");
        Loc.Add("Dns.ApplyFailed", "The DNS servers were not changed.", "لم يتم تغيير خوادم DNS.");
        Loc.Add("Dns.ApplyNeedsSelection", "Select at least one resolver first.", "اختر مُحلّلاً واحداً على الأقل أولاً.");
        Loc.Add("Dns.ApplyNeedsAdapter", "There is no adapter to apply DNS to.", "لا يوجد كرت شبكة لتطبيق DNS عليه.");
        Loc.Add("Dns.ApplyCustom", "Apply custom", "تطبيق المخصص");

        // ---------------------------------------------------------------- elevation
        Loc.Add("Dns.ElevationTitle", "Changing DNS servers needs administrator rights",
            "تغيير خوادم DNS يحتاج صلاحيات المسؤول");
        Loc.Add("Dns.ElevationBody",
            "Benchmarking, cache flushing and public IP lookup work in limited mode. Applying or resetting servers requires elevation.",
            "القياس وتفريغ الذاكرة وجلب عنوان IP العام تعمل في الوضع المحدود. أما تطبيق الخوادم أو إرجاعها فيحتاج صلاحيات المسؤول.");
        Loc.Add("Dns.ElevationCancelled", "Administrator elevation was cancelled.", "تم إلغاء طلب صلاحيات المسؤول.");

        // ---------------------------------------------------------------- benchmark
        Loc.Add("Dns.BenchmarkTitle", "Resolver benchmark", "قياس خوادم DNS");
        Loc.Add("Dns.BenchmarkBody",
            "FusionNet sends real DNS queries to every selected resolver and ranks the answers by response time. Nothing is changed until you apply.",
            "يرسل فيوجن نت استعلامات DNS حقيقية إلى كل مُحلّل محدد ويرتّب النتائج حسب زمن الاستجابة. لا يتم تغيير أي شيء حتى تضغط تطبيق.");
        Loc.Add("Dns.Benchmark", "Benchmark", "قياس");
        Loc.Add("Dns.Benchmarking", "Benchmarking…", "جارٍ القياس…");
        Loc.Add("Dns.BenchmarkProgress", "Queried {0} of {1} servers", "تم استعلام {0} من {1} خادماً");
        Loc.Add("Dns.BenchmarkIdle", "Select the resolvers you want to test, then run the benchmark.",
            "اختر الخوادم التي تريد اختبارها ثم ابدأ القياس.");
        Loc.Add("Dns.BenchmarkDone", "{0} servers responded; the fastest is at the top.", "استجاب {0} خادماً؛ الأسرع في الأعلى.");
        Loc.Add("Dns.BenchmarkNone", "No resolver responded. Check your connection and try again.",
            "لم يستجب أي خادم. تحقّق من اتصالك ثم أعد المحاولة.");
        Loc.Add("Dns.BenchmarkCanceled", "Benchmark cancelled.", "تم إلغاء القياس.");
        Loc.Add("Dns.BenchmarkFailed", "The benchmark failed: {0}", "فشل القياس: {0}");
        Loc.Add("Dns.ResultsTitle", "Results", "النتائج");
        Loc.Add("Dns.NoResults", "Run a benchmark to rank resolvers by real response time.",
            "ابدأ القياس لترتيب الخوادم حسب زمن الاستجابة الفعلي.");
        Loc.Add("Dns.ColumnProvider", "Provider", "المزوّد");
        Loc.Add("Dns.ColumnServer", "Server", "الخادم");
        Loc.Add("Dns.ColumnLatency", "Latency", "زمن الاستجابة");
        Loc.Add("Dns.ColumnStatus", "Status", "الحالة");
        Loc.Add("Dns.ColumnScore", "Score", "الدرجة");
        Loc.Add("Dns.StatusOk", "OK", "ناجح");
        Loc.Add("Dns.StatusTimeout", "Timeout", "انتهت المدة");
        Loc.Add("Dns.Best", "Fastest", "الأسرع");
        Loc.Add("Dns.BestSummary", "Best result: {0} at {1}", "أفضل نتيجة: {0} بزمن {1}");
        Loc.Add("Dns.ApplyFastest", "Apply fastest", "تطبيق الأسرع");
        Loc.Add("Dns.NoBestYet", "Benchmark first to know which resolver is fastest.",
            "ابدأ القياس أولاً لمعرفة أسرع خادم.");
        Loc.Add("Dns.ProvidersTitle", "Resolvers to test", "الخوادم المرشحة للاختبار");

        // ---------------------------------------------------------------- public IP
        Loc.Add("Dns.PublicIpTitle", "Public IP", "عنوان IP العام");
        Loc.Add("Dns.PublicIpBody", "What the internet sees: your address, provider and region.",
            "ما يراه الإنترنت: عنوانك ومزوّد الخدمة والمنطقة.");
        Loc.Add("Dns.PublicIpLabel", "IP address", "عنوان IP");
        Loc.Add("Dns.IspLabel", "Provider", "مزوّد الخدمة");
        Loc.Add("Dns.CountryLabel", "Country", "البلد");
        Loc.Add("Dns.PublicIpLoading", "Looking up your public address…", "جارٍ جلب عنوانك العام…");
        Loc.Add("Dns.PublicIpError", "Could not reach the public IP lookup service.",
            "تعذّر الوصول إلى خدمة جلب عنوان IP العام.");
        Loc.Add("Dns.PublicIpRefreshed", "Public IP information refreshed.", "تم تحديث معلومات عنوان IP العام.");

        // ---------------------------------------------------------------- custom DNS
        Loc.Add("Dns.CustomTitle", "Custom DNS", "DNS مخصص");
        Loc.Add("Dns.CustomBody",
            "Enter two IPv4 addresses; the first is used as the primary resolver.",
            "أدخل عنواني IPv4؛ يُستخدم الأول كمُحلّل رئيسي.");
        Loc.Add("Dns.CustomPrimary", "Primary", "الرئيسي");
        Loc.Add("Dns.CustomSecondary", "Secondary", "الثانوي");
        Loc.Add("Dns.CustomHint", "Saved pairs can be applied with one click.", "يمكن تطبيق الأزواج المحفوظة بنقرة واحدة.");
        Loc.Add("Dns.CustomSaved", "Custom DNS servers saved.", "تم حفظ خوادم DNS المخصصة.");
        Loc.Add("Dns.InvalidIp", "Enter a valid IPv4 address such as 1.1.1.1.", "أدخل عنوان IPv4 صحيحاً مثل 1.1.1.1.");
        Loc.Add("Dns.CustomEmpty", "Enter at least one IPv4 address first.", "أدخل عنوان IPv4 واحداً على الأقل أولاً.");
        Loc.Add("Dns.CustomSavedLabel", "Saved", "محفوظ");

        // ---------------------------------------------------------------- explainer
        Loc.Add("Dns.ExplainerTitle", "What DNS can and cannot do", "ما يستطيعه DNS وما لا يستطيعه");
        Loc.Add("Dns.ExplainerBody",
            "DNS only translates names such as store.steampowered.com into IP addresses. It does not carry your game traffic.",
            "يعمل DNS على ترجمة الأسماء مثل store.steampowered.com إلى عناوين IP فقط، ولا ينقل حركة اللعبة نفسها.");
        Loc.Add("Dns.ExplainerBullet1",
            "A faster resolver speeds up launchers, the store, patch downloads and web browsing.",
            "المُحلّل الأسرع يسرّع المشغلات والمتجر وتنزيل التحديثات وتصفح الويب.");
        Loc.Add("Dns.ExplainerBullet2",
            "It does not lower in-game ping: the server address is already known and the route is unchanged.",
            "لكنه لا يقلل البنق داخل اللعبة: عنوان السيرفر معروف مسبقاً والمسار لا يتغير.");
        Loc.Add("Dns.ExplainerBullet3",
            "If a launcher feels slow or a store page stalls, changing DNS is the right fix.",
            "إذا كان المشغّل بطيئاً أو صفحة المتجر تتوقف، فتغيير DNS هو الحل الصحيح.");
        Loc.Add("Dns.PrivacyNote",
            "FusionNet queries each resolver directly with a fixed test domain. No browsing data is collected.",
            "يستعلم فيوجن نت من كل مُحلّل مباشرةً باستخدام نطاق اختبار ثابت، دون جمع أي بيانات تصفح.");
    }
}
