# تقرير تنفيذ معالجة بنود الاستلام داخل الجدول

**الإصدار:** 1.50.3 — `2026-09-09 ReceivingInline2`
**المرجع:** نص المتطلبات الأخير للمستخدم، لا مواصفة الوجهة/الدرجات السابقة.
**النتيجة:** تنفيذ وحفظ وترحيل مخزني وحراس خلفية وتقارير واختبارات؛ ليس فحصاً نظرياً فقط.

## 1. نتيجة الفحص السابق للتعديل

لم تكن 1.50.2 مطابقة للنص الحالي: كان القرار مستمداً من `Product.RequiresTreatment`، والإدخال وجهةً ونافذة تقسيم بمدد ثابتة، ولم يكن في `ShipmentItem` قرار صريح وتاريخ نهاية مستقلان.

شمل فحص الأثر قبل التعديل كيانات الشحنة والبنود والدفعات والمعالجة، DTOs والواجهات، الحفظ/الاعتماد/الإلغاء والاستلام الجزئي، حركة المخزون وحارس الصرف، خدمات المعالجة والتخطيط، خرائط EF والترحيل والتعبئة التاريخية، التسجيل في حاوية الخدمات والتنقل والثيم، والتقارير والاختبارات. لذلك لم يُكتف بإضافة ComboBox ظاهري مع بقاء الخلفية على العقد القديم.

## 2. واجهة الاستلام

- الجدول يعرض اسم الصنف والوحدة والكمية والعبوة و**المعالجة: نعم/لا**، مع بيانات التتبع والعبوات والحالة القائمة.
- الاختيار فارغ أولاً، ولا تُفترض «لا» ولا يُنسخ علم الصنف إليه.
- **نعم:** يظهر عنوان **حتى تاريخ** وDatePicker في الصف نفسه.
- **لا:** يختفي العنوان والتقويم ويُمسح التاريخ السابق.
- إشعارات `INotifyPropertyChanged` تحدث الصف دون إعادة تحميل جدول يجري تحريره.
- القرار والتاريخ والحالة المستعادة خاصة بمعرف البند؛ يجوز تكرار الصنف نفسه بقرارات مختلفة.
- المسودة قابلة للتحرير صراحة، والمعتمد مقفل. الحالة وختم الإكمال ليسا مدخلين يرسلهما المستخدم.
- أزيلت ملفات `TreatmentSplitDialog` و`TreatmentStartDialog` و`RawTreatmentView` و`TreatmentSplitRow`. أزيل مدخل شاشة المعالجة المستقلة، وأصبح رمز التنقل القديم يحيل إلى الاستلام مع بوابة صلاحية الاستلام.
- لم تُنشأ نافذة فحص استلام أو معالجة أو مدة مستقلة. شاشات جودة الإنتاج اللاحقة ووظيفتها بقيت كما هي.

## 3. تغييرات قاعدة البيانات

لا جدول أعمال جديد. الإضافات إلى الجداول القائمة:

| الجدول | العمود | نوع النموذج | الغرض |
|---|---|---|---|
| `ShipmentItems` | `TreatmentRequired` | `bool?` | القرار الصريح لكل بند؛ NULL محجوز للبيانات السابقة، وليس افتراض «لا» للحفظ الجديد |
| `ShipmentItems` | `TreatmentUntilDate` | `DateTime?` | اليوم المختار عند نعم؛ NULL عند لا |
| `ShipmentItems` | `TreatmentCompletedAt` | `DateTime?` | ختم الإكمال الفعلي، ورمز تزامن EF |
| `RawTreatments` | `ReceivingItemId` | `int?` | ربط العملية الآلية ببند الاستلام الحديث؛ NULL للعمليات التاريخية |

أضيف الفهرس الفريد المرشح `IX_RawTreatments_ReceivingItemId` حيث الرابط غير NULL. يُنشأ في القاعدة الجديدة وفي ترحيل القواعد القائمة، فلا تسمح القاعدة بعمليتين حديثتين لنفس البند.

يستخدم الترحيل الأنواع المقابلة في SQL Server وSQLite. لا يمسح جدول أجزاء المعالجة القديم ولا الحركات السابقة، ولا يستنتج قراراً أو تاريخاً تاريخياً غير مسجل. جرى تحصين `BackfillTreatmentReadiness` ليستبعد الدفعات ذات القرار الحديث، حتى لا يعتبر حجزاً جديداً مخزوناً سابقاً جاهزاً. اختُبر حذف الأعمدة لمحاكاة قاعدة قديمة ثم إضافتها وإعادة الترحيل على SQLite.

التحقق من اكتمال الاختيار وتناسبه مع تاريخ رأس السند في **خدمة الحفظ والاعتماد**؛ ليس هناك ادعاء بأن CHECK constraint عابراً للجداول يمنع تعديلات مدير قاعدة البيانات المباشرة. NULL مدعوم بنيوياً لصون البيانات السابقة فقط.

## 4. الحفظ والاعتماد

1. `ReceivingLineTreatmentPolicy.Validate` حارس مشترك تستخدمه الواجهة والخدمة.
2. `SaveShipment` يرفض غياب القرار، وغياب تاريخ نعم، والتاريخ السابق للاستلام، والكميات غير الصالحة. عند لا يتجاهل التاريخ ويمسحه.
3. الوجهة مشتقة من القرار؛ لا يمكن فرض وجهة معاكسة عبر DTO قديم. تُرفض الأجزاء ذات الدرجات/المدد القديمة غير الفارغة بدلاً من تطبيقها صامتاً.
4. يثبت القرار والتاريخ في كل `ShipmentItem`؛ لا ينتقل أحدهما إلى رأس المستند بديلاً عن البنود.
5. الاعتماد يعيد فحص البيانات المحفوظة والأصناف والوحدات والكميات قبل إنشاء المخزون.
6. لكل بند مستلم دفعة مستقلة. بند لا يدخل مخزن الاستلام بلا معالجة. بند نعم يُقيد وارداً ثم ينتقل إلى `WTRT` وتزداد كميته المحجوزة.
7. إنشاء المستند/تعديله والاعتماد وحركاته داخل معاملات؛ فشل بند متأخر لا يترك حركات البنود السابقة نصف محفوظة.
8. في الاستلام اللاحق تُراجع قرارات البنود المعلقة ومواعيدها في نفس الجدول قبل الحفظ. يحفظ الربط بالأصل والمخزن والوحدة ولا يمدد التاريخ القديم آلياً.

## 5. التاريخ والمتابعة والانتقال التالي

**الدلالة المحددة:** الاستحقاق من بداية يوم «حتى تاريخ»، الساعة **00:00**، ويجب أن يكون `until.Date >= receipt.Date`.

- الموعد هو اليوم الذي اختاره المستخدم، وليس وقت الاعتماد + 5/7/10 أيام.
- الساعات في سجل `RawTreatment` أثر مشتق من الفرق بين تاريخ الاستلام واليوم المختار، وليست خياراً يقيّد المستخدم.
- الاستلام 08/09/2026 حتى 15/09/2026 يسجل نهاية 15/09/2026، حتى لو اعتُمد متأخراً. اليوم نفسه مسموح أيضاً.
- `ReceivingTreatmentLifecycle` يُكمل البنود الحديثة المستحقة فقط. يتحقق من تطابق الموعد والأرصدة، وينقل الكمية كاملة من `WTRT` إلى مخزن الاستلام الفعلي، ويحدث الجاهز وختم الإكمال.
- الحالة **مكتملة — جاهز للإجراء التالي** لا تنتج عن تغيير نص في الواجهة؛ تتبع الإكمال المحاسبي. لا يُنشأ أمر إنتاج أو فحص أو تسليم تلقائياً.
- المتابعة كل 60 ثانية أثناء فتح التطبيق، وعند الدخول/قراءة الحالات والعمليات التشغيلية المرتبطة، مع زر تحديث لا يقبل موعداً أو حالة بديلة.
- عند إغلاق جميع العملاء لا يعمل عامل مستقل؛ عند التشغيل التالي تُعالج الاستحقاقات قبل إتاحة المخزون. مدة التأخر في تسجيل الحركة لا تغير تاريخ النهاية المختار.
- SQL Server هو مرجع الوقت عبر `GETDATE()`؛ SQLite يستخدم ساعة المضيف، مع ساعة قابلة للحقن في الاختبارات لا ضمن مدخلات المستخدم.

## 6. حراس منع التجاوز والمحافظة على الدورة

- رفض تعديل/حذف المعتمد، وعدم السماح بإلغاء الحجز أو تغييره إلى لا بعد دخوله دورة المعالجة.
- خدمة المعالجة التاريخية ترفض البدء المنفصل لبند حديث، وترفض إفراجه أو إلغاءه أو رفضه يدوياً؛ لا مسار API قديم يختصر الموعد.
- حارس الصرف يتحقق من قرار البند وتاريخه وختم الإكمال، لا من علم الصنف وحده ولا من عداد جاهز يمكن أن يكون غير متسق.
- اعتماد أمر الإنتاج وبدؤه واستئنافه محميان؛ الإقفال محمي أيضاً عبر حارس استهلاك الخام.
- التخطيط يقرأ القرار لكل دفعة ويعرض المتاح المتوقع لتاريخ مقصود؛ ذلك لا يعطي إذناً بالإنتاج قبل الموعد.
- جرى تصحيح خصم المنتج سابقاً من حساب الجاهز حتى لا يحتسب مرتين.
- الإقفال يصرف خام البنود الحديثة من مخزن استلامها الفعلي؛ السجلات القديمة تبقى على مسارها التاريخي.
- لا تغيير في صيغة `Lot.AvailableQtyKg`: `Max(0, InStockQtyKg - ReservedQtyKg - UnderTreatmentQtyKg)`.
- الإكمال لا يغير إجمالي مخزون المنشأة أو عدد عبواتها؛ هو تحويل بين مخزنين. رقم إفراج ثابت `/DUE`، وفهرس الرابط الفريد، ورموز التزامن والمعاملة تحمي من إعادة القيد.
- فشل حركة الإكمال يترك البند غير مكتمل ويرجع المعاملة. اختُبر الإصلاح ثم إعادة المحاولة وعدم تضاعف الحركات. اختُبر تعارض ختم إكمال قديم بين نطاقي EF؛ لم يُنفذ اختبار ضغط متعدد أجهزة SQL Server حي.

## 7. التاريخ السابق والتقارير

السجلات القديمة لا تتحول إلى العقد الجديد بأثر رجعي. علم الصنف وأنواع المعالجة ومواعيدها وجودتها تظل مرجعها التاريخي. من صف الاستلام يمكن إكمال العمليات التاريخية المستحقة، مع نفس صلاحية الإفراج وفحص الجودة إن كان مشترطاً، دون إدخال فترة في نافذة أخرى. المسودة القديمة تتطلب استكمال نعم/لا والتاريخ قبل اعتمادها بالنظام الحالي.

أضيف تقرير **معالجة بنود الاستلام — نعم/لا وحتى تاريخ**، الرمز `receiving_line_treatment`. يشمل السند وتاريخ الاستلام والعميل ومعرف البند والصنف والوحدة والكمية والعبوة والقرار والنهاية والحالة والإكمال الفعلي، وروابط إلى تفاصيل الاستلام. التقارير الإجمالية وسجلات المخزون القديمة لم تُستبدل. هذا التقرير الجديد هو العرض التفصيلي للمعالجة؛ لم يُعاد تصميم نموذج فاتورة الاستلام المطبوع القديم.

## 8. نتائج التحقق الفعلي

| التحقق | النتيجة |
|---|---|
| بناء Release للحل، 8 مشاريع، `-t:Rebuild -warnaserror` | **0 تحذيرات، 0 أخطاء** |
| كامل اختبارات xUnit | **705 ناجحة / 705، بلا تخطي** |
| الاختبارات الموجهة للعقد الحالي ونموذج/بنية الواجهة | **42 ناجحة / 42** |
| AcceptanceRunner: دورة العمل الفعلية | **45 ناجحة / 45** |
| UnitRuleAudit | **14 مطابقة / 14** |
| اختبارات حراس Python | **7 ناجحة / 7** |
| فحص RTL/المعالجات الفارغة/هويات double القديمة | ناجح؛ 105 هويات قديمة بلا هويات جديدة |
| actionlint | ناجح |
| فحص حزم NuGet المباشرة والمتعدية | لا تطابقات ثغرات معروفة حسب المصادر الحالية، للمشاريع الثمانية |
| اختبار WPF الحقيقي | 12 تحققاً جرى **بناؤها فقط**؛ غير منفذة على Linux |
| SQL Server حي / أجهزة المستخدم | **لم يُختبرا**؛ اختبارات الخدمات والترحيل الفعلية نُفذت على SQLite |

### الحالات العملية المطلوبة

| الحالة | ما اختُبر والنتيجة |
|---|---|
| لا، بتاريخ استلام 08/09/2026 | لا عملية معالجة ولا نهاية محفوظة؛ متاح بعد الاعتماد، حتى لو علم الصنف نعم |
| نعم لأسبوع حتى 15/09/2026 | محفوظ باليوم المختار؛ غير متاح حتى آخر لحظة قبل الموعد، ويكتمل عند بدايته |
| سند مختلط: نعم 15/09، لا، نعم 20/09 لنفس الصنف | ثلاثة بنود/دفعات مستقلة؛ لا يحرر استحقاق الأول الثالث |
| نعم بلا تاريخ | رفض في نموذج الواجهة والخدمة، ولا حفظ جزئي |
| نهاية أسبق من الاستلام | رفض في الواجهة والخدمة وإعادة فحص عند الاعتماد |
| نفس يوم الاستلام / أسبوعان / شهر / 43 يوماً | مقبولة بلا تحويل إلى مدة ثابتة؛ اليوم نفسه يستكمل إذا حل موعده |
| نعم ثم لا، ثم إعادة فتح المسودة | مسح النهاية واستعادة القرار الصحيح؛ فشل التعديل لا يمحو النسخة المحفوظة |
| تقديم الجاهزية يدوياً، أو بدء/اعتماد إنتاج مبكر | مرفوض؛ حتى اختبار عداد جاهز غير متسق لم يتجاوز تاريخ البند |
| فشل مخزني وإعادة محاولة | تراجع ذري، ثم إكمال واحد فقط بعد تصحيح الرصيد |
| إقفال إنتاج بعد الاستحقاق من مخزن استلام آخر | نجح المسار الحقيقي وانخفض رصيد المخزن الصحيح |
| استلام معلق بعد انتهاء موعده القديم | لا تمديد صامت؛ مراجعة اختيار مستقل لكل بند في السند اللاحق |
| ترقية قاعدة قديمة وإعادة الترحيل | إضافة الأعمدة والفهرس مع حفظ NULL التاريخي وعدم تحرير حجز حديث |

لم تُضعف قاعدة «الاختيار إلزامي» لإرضاء اختبارات قديمة. حدّثت أمثلة الاستلام غير المعالج في الاختبارات وأدوات القبول لتختار **لا صراحة**. استُبدلت اختبارات ربط الدرجات/نافذة التقسيم المتعارضة باختبارات العقد الحالي، وأبقيت اختبارات المحرك التاريخي على fixtures تاريخية صريحة، بما فيها ضوابط الوقت والجودة. أصل الاختبارات السابقة متاح في حزمة 1.50.2 المحفوظة.

## 9. الملفات الأساسية

- **النطاق والعقد:** `Receiving.cs`، `IWorkflowServices.cs`، `ReceivingTreatmentStateDto.cs`، `ReceivingLineTreatmentPolicy.cs`.
- **الخدمات:** `ReceivingService.cs`، `ReceivingTreatmentLifecycle.cs`، `ServiceBase.cs`، `RawTreatmentService.cs`، `PlanningService.cs`، `ProductionOrderService.cs`، `ExecutionService.cs`، `ReportService.cs`.
- **قاعدة البيانات:** `DatesErpDbContext.cs`، `SchemaMigrator.cs`.
- **الواجهة:** `ReceivingView.xaml` وملفها البرمجي، `ReceivingItemRow.cs`، `MainWindow.xaml.cs`، `ScreenFactory.cs`، `ScreenCatalog.cs`.
- **التحقق:** `ReceivingInlineTreatmentTests.cs`، `ReceivingScreenRepairTests.cs`، `TestHost.cs`، أداة `ReceivingWpfSmoke`، وأمثلة DTO في اختبارات المشروع وأدوات القبول.
- **الإصدار والتوثيق:** ملف مشروع Desktop، `BuildInfo.cs`، README، هذا التقرير ودليل الاستخدام؛ وُسمت وثيقتا Receiving1 بأنهما تاريخيتان.

يلي ذلك سجل كامل لمسارات الملفات التي تغيرت مقارنة بالحزمة المسلّمة 1.50.2، بما فيها تحديثات أمثلة الاختبارات. الحزمة المصدرية تراكمية وتشمل إصلاحات Repair1 وReceiving1 غير المتعارضة. لم يحدث commit أو push إلى GitHub.

## 10. الترقية وحدود التسليم

خذ نسخة احتياطية واختبر نسخة من قاعدة العمل، ثم حدّث جميع العملاء معاً؛ النسخ القديمة لا تعرف القرار الإلزامي الجديد. راجع دليل `Documentation/RECEIVING_INLINE_TREATMENT_AR.md` لاختبار Windows وتجربة المستخدم. الحزمة **مصدر المشروع** وليست مثبّت Windows تم تشغيله أو اعتماده على بيانات المستخدم. لا ادعاء باختبار رسومي أو SQL Server حي أو تشغيل CI بعيد.

## ملحق: سجل الملفات مقارنة بالإصدار 1.50.2

عدد الملفات المختلفة: **98**. غالبية تغييرات اختبارات الدورة العامة هي إضافة `TreatmentRequired = false` صريحة في أمثلة الاستلام غير المعالج، وليست تغييراً في منطق الإنتاج.

| الحالة | المسار |
|---|---|
| مضاف | `Documentation/RECEIVING_INLINE_TREATMENT_AR.md` |
| معدل | `Documentation/RECEIVING_REPAIR_AR.md` |
| معدل | `FIXLOG_RECEIVING1_AR.md` |
| مضاف | `FIXLOG_RECEIVING_INLINE2_AR.md` |
| معدل | `README.md` |
| معدل | `audit/UnitRuleAudit/Program.cs` |
| معدل | `src/DatesErp.Application/Services/ExecutionService.cs` |
| معدل | `src/DatesErp.Application/Services/PlanningService.cs` |
| معدل | `src/DatesErp.Application/Services/ProductionOrderService.cs` |
| معدل | `src/DatesErp.Application/Services/RawTreatmentService.cs` |
| معدل | `src/DatesErp.Application/Services/ReceivingService.cs` |
| مضاف | `src/DatesErp.Application/Services/ReceivingTreatmentLifecycle.cs` |
| معدل | `src/DatesErp.Application/Services/ReportService.cs` |
| معدل | `src/DatesErp.Application/Services/ServiceBase.cs` |
| مضاف | `src/DatesErp.Core/Common/ReceivingLineTreatmentPolicy.cs` |
| معدل | `src/DatesErp.Core/Common/ReceivingTreatmentPolicy.cs` |
| معدل | `src/DatesErp.Core/Domain/Entities/Receiving.cs` |
| معدل | `src/DatesErp.Core/Interfaces/Services/IWorkflowServices.cs` |
| مضاف | `src/DatesErp.Core/Interfaces/Services/ReceivingTreatmentStateDto.cs` |
| معدل | `src/DatesErp.Desktop/DatesErp.Desktop.csproj` |
| معدل | `src/DatesErp.Desktop/Mvvm/ReceivingItemRow.cs` |
| محذوف | `src/DatesErp.Desktop/Mvvm/TreatmentSplitRow.cs` |
| معدل | `src/DatesErp.Desktop/Screens/ScreenCatalog.cs` |
| معدل | `src/DatesErp.Desktop/Services/BuildInfo.cs` |
| معدل | `src/DatesErp.Desktop/Views/MainWindow.xaml.cs` |
| محذوف | `src/DatesErp.Desktop/Views/Screens/RawTreatmentView.xaml` |
| محذوف | `src/DatesErp.Desktop/Views/Screens/RawTreatmentView.xaml.cs` |
| معدل | `src/DatesErp.Desktop/Views/Screens/ReceivingView.xaml` |
| معدل | `src/DatesErp.Desktop/Views/Screens/ReceivingView.xaml.cs` |
| معدل | `src/DatesErp.Desktop/Views/Screens/ScreenFactory.cs` |
| محذوف | `src/DatesErp.Desktop/Views/Screens/TreatmentSplitDialog.cs` |
| محذوف | `src/DatesErp.Desktop/Views/Screens/TreatmentStartDialog.cs` |
| معدل | `src/DatesErp.Infrastructure/Persistence/DatesErpDbContext.cs` |
| معدل | `src/DatesErp.Infrastructure/Persistence/SchemaMigrator.cs` |
| معدل | `tests/DatesErp.Tests/AuxFlexTests.cs` |
| معدل | `tests/DatesErp.Tests/B100AvailabilityTests.cs` |
| معدل | `tests/DatesErp.Tests/B104BackfillTests.cs` |
| محذوف | `tests/DatesErp.Tests/B107ReceivingTreatmentLinkTests.cs` |
| معدل | `tests/DatesErp.Tests/B80FixTests.cs` |
| معدل | `tests/DatesErp.Tests/B96DeliveryTests.cs` |
| معدل | `tests/DatesErp.Tests/B98RunTests.cs` |
| معدل | `tests/DatesErp.Tests/CapacityDesignTests.cs` |
| معدل | `tests/DatesErp.Tests/CapacityMatrixTests.cs` |
| معدل | `tests/DatesErp.Tests/CapacityPerShiftTests.cs` |
| معدل | `tests/DatesErp.Tests/CloseB88Tests.cs` |
| معدل | `tests/DatesErp.Tests/ClosingParityTests.cs` |
| معدل | `tests/DatesErp.Tests/ClosingPreservesPlanBaselineTests.cs` |
| معدل | `tests/DatesErp.Tests/ConcurrencyAndAuditTests.cs` |
| معدل | `tests/DatesErp.Tests/DatesErp.Tests.csproj` |
| معدل | `tests/DatesErp.Tests/DeliveryAuditProbeTests.cs` |
| معدل | `tests/DatesErp.Tests/DeliveryLineMatchingTests.cs` |
| معدل | `tests/DatesErp.Tests/DiagnosticIntegrityTests.cs` |
| معدل | `tests/DatesErp.Tests/ExecutionB57Tests.cs` |
| معدل | `tests/DatesErp.Tests/FairDistributionTests.cs` |
| معدل | `tests/DatesErp.Tests/FullCycleMultiCustomerTests.cs` |
| معدل | `tests/DatesErp.Tests/FullWorkflowTests.cs` |
| معدل | `tests/DatesErp.Tests/GuardTests.cs` |
| معدل | `tests/DatesErp.Tests/InspectionScreenTests.cs` |
| معدل | `tests/DatesErp.Tests/InventoryLedgerAuditTests.cs` |
| معدل | `tests/DatesErp.Tests/ItemTraceabilityTests.cs` |
| معدل | `tests/DatesErp.Tests/LongTermPlanTests.cs` |
| معدل | `tests/DatesErp.Tests/MasterLinkageTests.cs` |
| معدل | `tests/DatesErp.Tests/MultiCustomerPlanTests.cs` |
| معدل | `tests/DatesErp.Tests/NumberingTests.cs` |
| معدل | `tests/DatesErp.Tests/OrderReversalAuditTests.cs` |
| معدل | `tests/DatesErp.Tests/PackagingCapacityTests.cs` |
| معدل | `tests/DatesErp.Tests/PartialDelegationTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanB77Tests.cs` |
| معدل | `tests/DatesErp.Tests/PlanCapacityAccumulationTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanClosingTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanClosureTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanHeaderFixTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanLevelClosingTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanOverlapCapacityTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanUpdateAndCapacityTests.cs` |
| معدل | `tests/DatesErp.Tests/PlanningB56Tests.cs` |
| معدل | `tests/DatesErp.Tests/PlanningDeepAuditTests.cs` |
| معدل | `tests/DatesErp.Tests/ProductLotAvailabilityTests.cs` |
| معدل | `tests/DatesErp.Tests/ProductionBalanceTests.cs` |
| معدل | `tests/DatesErp.Tests/ProductionOrderScreenTests.cs` |
| معدل | `tests/DatesErp.Tests/QualityGateTests.cs` |
| معدل | `tests/DatesErp.Tests/QualityScreenTests.cs` |
| معدل | `tests/DatesErp.Tests/RawTreatmentPhase1Tests.cs` |
| معدل | `tests/DatesErp.Tests/RawTreatmentPhase3Tests.cs` |
| معدل | `tests/DatesErp.Tests/RawTreatmentScenarioTests.cs` |
| مضاف | `tests/DatesErp.Tests/ReceivingInlineTreatmentTests.cs` |
| معدل | `tests/DatesErp.Tests/ReceivingScreenRepairTests.cs` |
| محذوف | `tests/DatesErp.Tests/ReceivingTreatmentGuardTests.cs` |
| معدل | `tests/DatesErp.Tests/ReportsEngineTests.cs` |
| معدل | `tests/DatesErp.Tests/ShipmentFullReportTests.cs` |
| معدل | `tests/DatesErp.Tests/SingleCustomerEnforcementTests.cs` |
| معدل | `tests/DatesErp.Tests/TestHost.cs` |
| معدل | `tests/DatesErp.Tests/TreatmentReportsTests.cs` |
| معدل | `tests/DatesErp.Tests/UnitRuleTests.cs` |
| معدل | `tests/DatesErp.Tests/UnitsPolicyTests.cs` |
| معدل | `tests/DatesErp.Tests/WarehouseReportsTests.cs` |
| معدل | `tools/AcceptanceRunner/Program.cs` |
| معدل | `tools/ReceivingWpfSmoke/Program.cs` |
