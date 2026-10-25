namespace FusionNet.Core.Models;

/// <summary>
/// The complete set of latency / throughput registry tweaks FusionNet knows how to
/// apply and exactly restore. Order matters for display only.
/// </summary>
public static class TweakCatalog
{
    public const string CatNetwork = "Network";
    public const string CatMultimedia = "Multimedia";
    public const string CatGaming = "Gaming";
    public const string CatAdvancedTcp = "Advanced TCP";
    public const string CatPower = "Power";

    private const string MultimediaProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTask = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    private const string TcpParameters = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    private const string InterfacesRoot = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    private static readonly TweakDefinition[] Tweaks =
    [
        new("network-throttling", CatMultimedia, TweakScope.Machine,
            MultimediaProfile, "NetworkThrottlingIndex", TweakValueKind.Dword, 0xFFFFFFFF,
            TweakRisk.Moderate, false, false,
            "Disable Windows network throttling",
            "إيقاف تحديد سرعة الشبكة في ويندوز",
            "Changes legacy MMCSS network throttling where supported. Measure latency and throughput before keeping it; it cannot shorten the route to a game server or guarantee lower ping.",
            "يغيّر تقييد الشبكة القديم في MMCSS عند دعمه. قِس زمن الاستجابة والسرعة قبل اعتماده؛ لا يختصر المسار إلى سيرفر اللعبة ولا يضمن خفض البنق."),

        new("system-responsiveness", CatMultimedia, TweakScope.Machine,
            MultimediaProfile, "SystemResponsiveness", TweakValueKind.Dword, 10,
            TweakRisk.Moderate, false, false,
            "Reserve less CPU for background tasks",
            "تقليل حصة المعالج للمهام الخلفية",
            "Sets the minimum CPU share for low-priority MMCSS work to 10%. This changes scheduling for participating multimedia threads; it is not a network speed setting.",
            "يضبط الحد الأدنى لحصة المعالج لمهام MMCSS منخفضة الأولوية على ١٠٪. يغيّر جدولة خيوط الوسائط المشاركة، وليس إعداداً لسرعة الشبكة."),

        new("game-gpu-priority", CatMultimedia, TweakScope.Machine,
            GamesTask, "GPU Priority", TweakValueKind.Dword, 8,
            TweakRisk.Safe, false, false,
            "Legacy MMCSS GPU priority (unused)",
            "أولوية كرت الشاشة القديمة في MMCSS (غير مستخدمة)",
            "Microsoft documents this MMCSS value as unused. Kept so earlier changes can be inspected and restored; no GPU or network improvement is expected.",
            "توضح مايكروسوفت أن قيمة MMCSS هذه غير مستخدمة. بقيت لمراجعة التغييرات السابقة واستعادتها؛ لا يُتوقع منها تحسين كرت الشاشة أو الشبكة."),

        new("game-cpu-priority", CatMultimedia, TweakScope.Machine,
            GamesTask, "Priority", TweakValueKind.Dword, 6,
            TweakRisk.Moderate, false, false,
            "Raise CPU priority for games",
            "رفع أولوية المعالج للألعاب",
            "Sets priority 6 for threads registered with the MMCSS Games task. Windows treats this value as 2 when Scheduling Category is High. It does not change server latency.",
            "يضبط الأولوية على ٦ للخيوط المسجلة ضمن مهمة Games في MMCSS. يعاملها ويندوز كقيمة ٢ عند اختيار فئة الجدولة High. لا يغيّر زمن الاستجابة إلى السيرفر."),

        new("game-scheduling", CatMultimedia, TweakScope.Machine,
            GamesTask, "Scheduling Category", TweakValueKind.String, "High",
            TweakRisk.Moderate, false, false,
            "Use the high scheduling category for games",
            "استخدام فئة الجدولة العالية للألعاب",
            "Uses High for threads registered with the MMCSS Games task. Microsoft designs this category for Pro Audio; test responsiveness and audio before keeping it.",
            "يستخدم High للخيوط المسجلة ضمن مهمة Games في MMCSS. صُممت هذه الفئة للصوت الاحترافي؛ اختبر استجابة النظام والصوت قبل اعتمادها."),

        new("game-sfio-priority", CatMultimedia, TweakScope.Machine,
            GamesTask, "SFIO Priority", TweakValueKind.String, "High",
            TweakRisk.Safe, false, false,
            "Legacy MMCSS disk priority (unused)",
            "أولوية القرص القديمة في MMCSS (غير مستخدمة)",
            "Microsoft documents SFIO Priority as unused. Kept so earlier changes can be inspected and restored; no storage or network improvement is expected.",
            "توضح مايكروسوفت أن SFIO Priority غير مستخدمة. بقيت لمراجعة التغييرات السابقة واستعادتها؛ لا يُتوقع منها تحسين القرص أو الشبكة."),

        new("tcp-ack-frequency", CatNetwork, TweakScope.PerInterface,
            InterfacesRoot, "TcpAckFrequency", TweakValueKind.Dword, 1,
            TweakRisk.Moderate, false, false,
            "Send TCP acknowledgements immediately",
            "إرسال إقرارات TCP فوراً",
            "Requests an ACK for each TCP segment on configured adapters. It increases acknowledgement traffic, does not affect UDP, and has no guaranteed ping benefit. Microsoft recommends studying the environment before changing the default.",
            "يطلب إقراراً لكل مقطع TCP على المحولات المهيأة. يزيد حركة الإقرارات ولا يؤثر على UDP ولا يضمن خفض البنق. توصي مايكروسوفت بدراسة بيئة الشبكة قبل تغيير القيمة الافتراضية."),

        new("tcp-no-delay", CatNetwork, TweakScope.PerInterface,
            InterfacesRoot, "TCPNoDelay", TweakValueKind.Dword, 1,
            TweakRisk.Moderate, false, false,
            "Legacy TCP no-delay registry value",
            "قيمة TCP القديمة لمنع تأخير الإرسال",
            "Modern applications control Nagle with the TCP_NODELAY socket option. This legacy registry value is not a verified global override; it does not affect UDP or guarantee lower ping.",
            "تتحكم التطبيقات الحديثة بخوارزمية Nagle عبر خيار المقبس TCP_NODELAY. لا يُعد هذا المفتاح القديم تجاوزاً عاماً مؤكداً؛ لا يؤثر على UDP ولا يضمن خفض البنق."),

        new("tcp-del-ack", CatNetwork, TweakScope.PerInterface,
            InterfacesRoot, "TcpDelAckTicks", TweakValueKind.Dword, 0,
            TweakRisk.Moderate, false, false,
            "Disable delayed ACK timer",
            "تعطيل مؤقت الإقرار المتأخر",
            "Legacy delayed TCP ACK setting with support that depends on Windows version. It does not affect UDP; test a specific TCP problem before using it.",
            "إعداد قديم لتأخير إقرارات TCP يعتمد دعمه على إصدار ويندوز. لا يؤثر على UDP؛ استخدمه فقط لاختبار مشكلة محددة في TCP."),

        new("qos-bandwidth", CatNetwork, TweakScope.Machine,
            @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", TweakValueKind.Dword, 0,
            TweakRisk.Moderate, false, false,
            "Disable QoS bandwidth reservations",
            "تعطيل حجوزات عرض النطاق في QoS",
            "Sets the policy limit for application bandwidth reservations to zero. It does not unlock hidden Internet speed and may interfere with applications that rely on QoS reservations.",
            "يضبط حد حجوزات عرض النطاق للتطبيقات على صفر. لا يحرر سرعة إنترنت مخفية، وقد يؤثر على التطبيقات التي تعتمد على حجوزات QoS."),

        new("game-mode", CatGaming, TweakScope.CurrentUser,
            @"Software\Microsoft\GameBar", "AutoGameModeEnabled", TweakValueKind.Dword, 1,
            TweakRisk.Safe, true, false,
            "Enable Windows Game Mode",
            "تفعيل وضع الألعاب في ويندوز",
            "Turns on Game Mode so Windows prioritises the foreground game.",
            "يفعّل وضع الألعاب ليعطي ويندوز الأولوية للعبة في المقدمة."),

        new("game-mode-auto", CatGaming, TweakScope.CurrentUser,
            @"Software\Microsoft\GameBar", "AllowAutoGameMode", TweakValueKind.Dword, 1,
            TweakRisk.Safe, true, false,
            "Let Windows enable Game Mode automatically",
            "السماح لويندوز بتفعيل وضع الألعاب تلقائياً",
            "Allows Game Mode to switch on automatically when a game is detected.",
            "يسمح بتفعيل وضع الألعاب تلقائياً عند اكتشاف لعبة."),

        new("game-dvr-off", CatGaming, TweakScope.CurrentUser,
            @"System\GameConfigStore", "GameDVR_Enabled", TweakValueKind.Dword, 0,
            TweakRisk.Safe, false, false,
            "Disable Game DVR background recording",
            "تعطيل التسجيل الخلفي Game DVR",
            "Disables Game DVR capture for the current user. This may save resources when recording is enabled, but removes recording functionality and does not guarantee lower ping.",
            "يعطّل تسجيل Game DVR للمستخدم الحالي. قد يوفر موارد عند تفعيل التسجيل، لكنه يلغي وظيفة التسجيل ولا يضمن خفض البنق."),

        new("app-capture-off", CatGaming, TweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", TweakValueKind.Dword, 0,
            TweakRisk.Safe, false, false,
            "Disable Game Bar app capture",
            "تعطيل تسجيل التطبيقات في Game Bar",
            "Turns off Game Bar capture for the current user. Choose this only if you do not need its recording features; it is not a network optimization.",
            "يوقف تسجيل Game Bar للمستخدم الحالي. اختره فقط إذا لم تكن تحتاج ميزات التسجيل؛ ليس تحسيناً للشبكة."),

        new("win32-priority", CatAdvancedTcp, TweakScope.Machine,
            @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", TweakValueKind.Dword, 38,
            TweakRisk.Advanced, false, true,
            "Favour foreground CPU quanta",
            "إعطاء أفضلية لحصص المعالج للمقدمة",
            "Sets the classic gaming quantum profile (38). Changes scheduler behaviour system-wide; requires a restart.",
            "يضبط ملف حصص المعالج الكلاسيكي للألعاب (٣٨). يغيّر سلوك المجدول على مستوى النظام ويحتاج إعادة تشغيل."),

        new("power-throttling", CatPower, TweakScope.Machine,
            @"SYSTEM\CurrentControlSet\Control\Power", "PowerThrottlingOff", TweakValueKind.Dword, 1,
            TweakRisk.Moderate, false, true,
            "Legacy power-throttling registry value",
            "قيمة قديمة لتقييد الطاقة في السجل",
            "Kept for restoring earlier changes. This legacy path differs from Microsoft's documented power-throttling policy, so an effect is not expected. Use Windows power settings instead.",
            "بقيت لاستعادة التغييرات السابقة. يختلف هذا المسار القديم عن سياسة تقييد الطاقة الموثقة لدى مايكروسوفت، لذا لا يُتوقع تأثيره. استخدم إعدادات الطاقة في ويندوز."),

        new("tcp-timed-wait", CatAdvancedTcp, TweakScope.Machine,
            TcpParameters, "TcpTimedWaitDelay", TweakValueKind.Dword, 30,
            TweakRisk.Advanced, false, false,
            "Shorten TCP TIME_WAIT",
            "تقصير زمن TIME_WAIT في TCP",
            "Requests a shorter local TCP TIME_WAIT interval. Consider only for diagnosed local port exhaustion; it does not change router settings or reduce active game connection latency.",
            "يطلب تقليل فترة TIME_WAIT المحلية لاتصالات TCP. يُستخدم فقط عند تشخيص نفاد المنافذ المحلية؛ لا يغيّر إعدادات الراوتر ولا يقلل تأخير اتصالات اللعبة النشطة."),

        new("max-user-port", CatAdvancedTcp, TweakScope.Machine,
            TcpParameters, "MaxUserPort", TweakValueKind.Dword, 65534,
            TweakRisk.Advanced, false, true,
            "Raise the ephemeral port range",
            "رفع عدد منافذ الاتصال",
            "Legacy dynamic-port registry value. Modern Windows manages dynamic TCP/UDP port ranges separately; this is not a general gaming or ping optimization.",
            "قيمة قديمة لمنافذ الاتصال الديناميكية في السجل. يدير ويندوز الحديث نطاقات TCP وUDP الديناميكية بصورة مستقلة؛ ليست تحسيناً عاماً للألعاب أو البنق."),

        new("hw-scheduling", CatGaming, TweakScope.Machine,
            @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", TweakValueKind.Dword, 2,
            TweakRisk.Advanced, false, true,
            "Enable hardware GPU scheduling",
            "تفعيل جدولة كرت الشاشة العتادية",
            "Requests hardware-accelerated GPU scheduling on supported drivers and GPUs. Results depend on the game and driver; requires a restart and does not change network latency.",
            "يطلب جدولة كرت الشاشة المسرّعة عتادياً على التعريفات والكروت المدعومة. تعتمد النتيجة على اللعبة والتعريف؛ يحتاج إعادة تشغيل ولا يغيّر زمن استجابة الشبكة.")
    ];

    public static IReadOnlyList<TweakDefinition> All { get; } = Array.AsReadOnly(Tweaks);

    public static IReadOnlyList<TweakDefinition> Recommended { get; } =
        Array.AsReadOnly(Tweaks.Where(t => t.Recommended).ToArray());

    public static IReadOnlyList<string> RecommendedIds { get; } =
        Array.AsReadOnly(Tweaks.Where(t => t.Recommended).Select(t => t.Id).ToArray());

    public static IReadOnlyList<string> Categories { get; } =
        Array.AsReadOnly(Tweaks.Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    public static TweakDefinition? Find(string? id) =>
        Tweaks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<TweakDefinition> ByCategory(string category) =>
        Array.AsReadOnly(Tweaks.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).ToArray());

    /// <summary>Tweaks that need a reboot before they take effect.</summary>
    public static IReadOnlyList<TweakDefinition> RequiresRestart(IEnumerable<string> appliedIds) =>
        Array.AsReadOnly(Tweaks.Where(t => t.RequiresRestart &&
                          appliedIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToArray());
}
