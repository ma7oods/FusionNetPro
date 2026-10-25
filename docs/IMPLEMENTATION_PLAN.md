# خطة تنفيذ Game Boost + AI Security Monitor

**تدقيق3.2.2:** الحزم ونسختا الجذر متحققة؛380 وحدة و16 عرض/موافقة وfixtures الاسترجاع والتغطية نجحت. [مصفوفة المتطلبات والحدود](COMPLETION_AUDIT_3_2_2.md) تميز التنفيذ عن قدرات Startup/LHM/usage/backup المحدودة والتحقق الحي غير المنفذ. العلامات أدناه تخص التنفيذ المعزول ضمن حدوده، لا قبول كل المتطلبات حيًا.

الحالة المحدثة 2026-10-24: **تنفيذ المصدر والاختبارات المعزولة مكتمل؛ التحقق الحي غير منفذ**. البنود المؤشرة تصف كود المصدر ضمن الحدود أدناه، ولا تعني نجاحه على جهاز المستخدم. طلب المستخدم يتضمن الخطة ثم التنفيذ المرحلي؛ لا تفرض أسئلة موافقة إضافية على تعديلات المصدر والاختبارات المعزولة المأذون بها. تبقى الموافقة مطلوبة للتغييرات الحقيقية للجهاز، والإجراءات التي تنفذها واجهة المنتج على المستخدم.

> قرارات التنفيذ: اختار المستخدم Windows Firewall مع مهمة SYSTEM مستقلة للاسترجاع، وقد يتأخر الاسترجاع إلى محاولة المجدول التالية. ETW اختياري مع UAC للمساعد فقط؛ مصدر Microsoft TraceEvent3.2.8، TCP/UDP IPv4/IPv6. التفعيل القديم متوقف عند startup دون مسح التفضيلات. LHM تكامل WMI خارجي للقراءة، دون DLL/driver مضمّن؛ هذا نطاق أضيق من دمج المكتبة. لا سجل موثوق لاستخدام التطبيقات، ولا وجهة/تاريخ نسخة احتياطية موثقة: تُعرض Unknown وإرشادات التحقق. وصف وظيفة التطبيق وضرورة عناصر Startup لا يزالان يحتاجان مراجعة بشرية؛ لا تصنيف قطعي. الصيانة الشهرية checklist يدوية فقط. لا تعطيل Startup أو uninstall أو حذف ملفات. اختبارات crash/reboot تخص المنطق والبنية فقط؛ المسار الحي لم يُختبر.
## خريطة القدرات واتجاه الاعتماد

| الوحدة | المسؤولية | تعتمد على |
|---|---|---|
| monitoring-contracts | هوية العملية، قياس nullable، availability، الأدلة والثقة، نتائج العمليات | نماذج Core الحالية بدون كسرها |
| local-monitoring | عينة CPU/RAM، عمليات/اتصالات/Startup/services/tasks/Defender، حساسات LHM | monitoring-contracts |
| local-assessment | تحليل مؤشرات متعددة وإصدار تنبيهات مسندة، لا أوامر أو تعديلات | monitoring-contracts |
| safe-game-session | معاينة وموافقة وقواعد outbound بملكية دقيقة واستعادة موثوقة | monitoring-contracts، بيانات مراقبة الهوية |
| optional-ai | payload minimization ومراجعة وموافقة ومزود ومخزن أسرار آمن | monitoring-contracts، local-assessment |
| application-ui | صفحتا Game Boost وSecurity Monitor والتصدير ومعاينة التغييرات | القدرات السابقة + Shell/Loc/Theme |
| maintenance-extras | تخزين/تطبيقات غير مستخدمة/نسخ احتياطي/checklist أو جدولة بموافقة | local-monitoring + UI |
| release | حزمة EXE/Setup، إشعارات التراخيص، بصمات، تعليمات تشغيل | مراحل متحققة ومتكاملة |

لا تضف مشاريع أو طبقات جديدة لمجرد هذا الجدول؛ استخدم Core/Windows/App الحالية والملفات المناسبة لكل مسؤولية.

## المرحلة 0 — تثبيت العقد والأمان قبل أي تغيير نظام

- [x] قراءة خريطة المشروع والصفحات ونمط MVVM والخدمات والاختبارات؛ المراجعات محفوظة في docs/handoff.
- [x] حفظ المتطلبات الدقيقة والصور والأعمال السابقة والقيود وحالة الحزم.
- [x] تحديد عقود صغيرة في Core للمراقبة والتقارير والتنبيهات والجلسة والموافقة وموفر AI؛ الحفاظ على ProcessSnapshot وDiagnosticReport الحاليين أو توسيعهما فقط بسبب واضح ومتوافق.
- [x] فصل process CPU priority عن network rate limiting في التسميات.
- [x] تحديد سياسة حماية قابلة للاختبار: مسار/هوية/وقت بدء، ألعاب/نظام/DNS/أمان/anti-cheat، والهدف المجهول يفشل بصورة آمنة.
- [x] تعريف consent immutable يرتبط بالمعاينة الدقيقة والمسارات والقواعد والمدة، لا boolean عالمي يمكن إعادة استخدامه لأي هدف.
- [x] حسم ما يحقق شرط crash/reboot فعليًا مع الحفاظ على مطلب Windows Firewall outbound. وثّق tradeoff ولا تستبدل WFP أو scheduled task بصمت.
- [x] تدقيق legacy automation ووضع البداية قراءة فقط دون تبديل اختيارات المستخدم المحفوظة بصمت.

**التحقق:** pure tests للهوية والحماية والموافقة والأهداف المجهولة، ولا استدعاءات Windows حية.

## المرحلة 1 — شريحة قراءة محلية داخل UI

- [x] قياس CPU بعينتين من TotalProcessorTime ووقت monotonic وعدد الأنوية؛ RAM بوحدات حقيقية ونسبة موضحة.
- [x] PID + start time للتحقق من إعادة استعمال PID؛ unavailable عند انتهاء العملية أو رفض الوصول.
- [x] جداول TCP/UDP المالكة للـPID للعرض، مع فصل الاتصال عن نقل bytes.
- [x] مصدر قياس صحيح لرفع/تنزيل العملية (ETW أو آلية Microsoft موثقة بعد البحث)، مع بيان متطلبات admin/تغطية UDP/فترة العينة؛ لا قيم بديلة مشتقة من إجمالي المحول.
- [x] إضافة صفحة Game Boost بالموارد الحالية، Scan وإلغاء وتصدير، بلا حظر في هذه الشريحة.
- [x] دمج LHM الرسمي للقراءة فقط بعد مراجعة الحزمة والتراخيص والتبعيات؛ لا driver تلقائي أو ضبط حساسات.

**معايير القبول:** البيانات غير المتاحة لا تُعرض صفرًا، لا ارتفاعات وهمية عند PID reuse، Scan بلا API، لا admin إجباري للتشغيل، استجابة UI، ولا كسر DNS.

## المرحلة 2 — Security Monitor محلي بالأدلة

- [x] collector محدود وقابل للإلغاء لكل قسم، بأخطاء وتغطية معلنة؛ استعمل APIs الرسمية ولا تخرج command lines أو أسرارًا إلى log.
- [x] Defender: حالة المنتج وكشوفاته الحالية/التاريخية بتمييز واضح، دون Start-MpScan أو remediation تلقائي.
- [x] Startup Registry views/folders، services، scheduled task actions/principals، بيانات العمليات والاتصالات.
- [x] تحقق توقيع صحيح وbounded؛ إذا كان المسار غير قابل للوصول أو revocation يحتاج شبكة، أظهر availability ولا تصفه غير موقع/ضارًا تلقائيًا.
- [x] classifier خالص لمؤشرات متعددة، مع مصادر وأسباب وثقة وصفية، دون تشخيص malware نهائي من مؤشر واحد.
- [x] دعم اختياري لفتح الأدوات الرسمية Autoruns/Process Explorer بعد اختيار المستخدم، بلا تحميل أو قبول EULA صامت.
- [x] صفحة Security Monitor وعرض أقسام partial/unavailable وتنبيهات وتصدير محلي مع مراجعة حساسية التقرير.

**الاختبارات:** high CPU سليم، ملف unsigned سليم، كشف Defender تاريخي معالج، Defender unavailable، مؤشرات mining متعددة، مهمة encoded تحتاج مراجعة وليست إدانة، timeout/cancellation/caps، وغياب صلاحية الأدوات.

## المرحلة 3 — جلسة Game Mode آمنة

- [x] لا وصول لأي mutation من مجرد Scan/select/فتح صفحة.
- [x] معاينة واضحة للقواعد outbound فقط، EXE scope، الأهداف المحمية، المدة، آلية الاستعادة وأي task/watchdog سيُنشأ إن تقرر استعماله.
- [x] تحقق من الهوية والسياسة والصلاحية مجددًا عند التطبيق. لا تمرير ServiceName أو مسار مجهول لتجاوز الحماية.
- [x] كتابة durable snapshot قبل أول تغيير؛ failure to save => صفر أوامر. لا تعتمد على Journal الذي يخفي الإخفاق.
- [x] قفل وملكية وأسماء قواعد خاصة بالجلسة، مع منع التعارض مع Fusion القديم أو cleanup العام.
- [x] أقل elevation ممكنة، ولا Start Mode بدون consent وUAC للإجراء المحتاج فقط.
- [x] فشل جزئي => rollback للقواعد التي أضيفت فقط؛ فشل rollback يبقى ظاهرًا ومعلقًا.
- [x] انتهاء اللعبة/المدة/Stop/خروج عادي/تعطل/إعادة تشغيل له paths موثقة واختبارات؛ لا تُعلن success قبل التحقق من غياب القواعد الخاصة بالجلسة.
- [x] عرض elapsed time والأسباب والأهداف ونتائج restore.

**الاختبارات أولًا:** fake firewall/state store/clock/process resolver/elevation. لا موافقة => صفر تغييرات؛ لا admin => نتيجة واضحة؛ PID reuse => رفض؛ save failure => صفر أوامر؛ مستخدم له قواعد سابقة => لا مساس؛ فشل query/delete => pending؛ تعارض جلستين؛ expiry/game exit/crash/restart.

اختبارات Firewall/مهام/watchdog الحية تُنفّذ لاحقًا في VM/حساب اختبار أو بعد موافقة المستخدم، وتوثق منفصلة. لا تستبدلها بإعلان نجاح الاختبارات الوهمية.

## المرحلة 4 — AI اختياري آمن

- [x] IAiSecurityAnalyzer أو عقد مماثل محدود بنتيجة نصية advisory وCancellationToken، بلا أدوات تنفيذ.
- [x] إعداد endpoint/model مع تحقق HTTPS، وAPI key في مخزن DPAPI CurrentUser منفصل عن FusionSettings.
- [x] payload مصغر immutable؛ عرض **الحمولة الفعلية** والمزود قبل موافقة كل إرسال، وعدم إرسال fields خفية بعد المراجعة.
- [x] حظر redirect وتسريب auth، مهلات وحجوم محدودة، إلغاء، أخطاء لا تحتوي مفتاحًا.
- [x] عدم تفسير مخرجات AI أو محتوى ملف أو اسم عملية كأوامر نظام؛ لا remediation من AI.

**الاختبارات:** Scan يعمل بلا key؛ no consent => no HTTP؛ redaction؛ تغير الحمولة يلغي consent؛ endpoint change لا يعيد استخدام key بلا قرار؛ DPAPI failure؛ request/response bounds؛ HTML/instruction-like AI output يبقى نصًا.

## المرحلة 5 — بنود الصور الإضافية

- [x] فحص performance >5% بوحدات صحيحة ووصف التطبيقات قدر المتاح.
- [x] أكبر20 ملفًا/مجلدًا ضمن نطاق مختار، تنزيلات قديمة وduplicate photos بالhash عند الحاجة؛ deep scan يدوي منفصل مع موافقة ولا حذف تلقائي.
- [x] بيانات عدم استخدام التطبيقات حيث تتوفر فقط، وإلا Unknown؛ لا إلغاء تثبيت تلقائي.
- [x] backups: الوجهة وآخر نجاح بأدلة، لا افتراض OneDrive=backup؛ إرشادات إعداد إذا لا دليل.
- [x] تقرير/checklist شهري؛ جدولة داخل المنتج فقط إذا اختار المستخدم وراجع تفاصيل المهمة، لا Automation Codex ضمنية.

## المرحلة 6 — تحقق وتسليم

- [x] اختبارات Core كاملة:362/362، والفحوص المعزولة لـWindows/VMs ناجحة؛ لا Windows VM حي.
- [x] `dotnet build FusionNet.slnx --no-restore --nologo -v minimal -m:1`، بلا أخطاء أو تحذيرات جديدة.
- [ ] التحقق من binding/RTL/keyboard/loading/cancel/export؛ فحص UI ببيانات fixtures دون بدء النظام الآلي أو scans حية.
- [x] مراجعة مستقلة لمسارات التعديل/الاستعادة/المفاتيح والخصوصية والتغطية الجزئية.
- [x] تدقيق الحزم والتراخيص وتثبيت versions؛ no API keys/secrets.
- [x] بناء EXE وSetup متطابقين من آخر مصدر، والتحقق من hashes وحمولة ZIP والإصدار.
- [ ] التجربة الحية للتثبيت/الترقية/الإزالة وفحص عتاد/Firewall فقط بعد الموافقة المناسبة؛ تسجيل ما لم يُختبر.
- [x] تحديث DEVELOPMENT_STATUS وPROJECT_HANDOFF وتعليمات التثبيت العربية. لا إعلان اكتمال أي بند غير منفذ أو غير متحقق.

## قرارات حُسمت وحدود التحقق الباقية

1. Firewall persistent + مهمة SYSTEM boot/كل دقيقة قبل أول قاعدة، باختيار المستخدم. لا ضمان restore فوري إذا تعطل المجدول.
2. ETW5ثوانٍ اختياري بمساعد UAC وTraceEvent3.2.8، TCP/UDP4/6 وfinal-loss stats؛ calibration/performance على العتاد الفعلي غير مختبرين.
3. protection conservative: known paths/signatures/categories، identities، service/DNS PIDs/paths، Windows SecurityCenter +gamecatalog/Riot. لا fail-open عند نقص مصدر الحماية.
4. WinVerifyTrust cached embedded فقط؛ revocation freshness/catalog غير متحققين.
5. LHM external WMI فقط، لا library/driver مضمّن. العتاد الحي غير مختبر.
6. legacy autoengage paused startup دون مسح preferences، وقفل بدء مشترك يمنع تداخل الجلستين.
7. فحص keyboard/loading/cancel/export التفاعلي وOS live ليس بديلًا عنه rendering/HTTP fixtures؛ البند يبقى غير مؤشّر حتى يتحقق.

المراجع والتفاصيل: docs/handoff/IMPLEMENTATION_DECISIONS_3_1.md وDEVELOPMENT_STATUS.md. الخطة الأصلية محفوظة في docs/handoff/IMPLEMENTATION_PLAN_INITIAL.md.

