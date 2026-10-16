# تقرير إصلاح OFFTT — RowVersion وفرادية أرقام المعالجة

**الحالة: مرشح مصدر مُحدّث، غير معتمد للإنتاج بعد.** أُصلحت أسباب مصدرية مؤكدة وأضيفت اختبارات انحدار، لكن لا يمكن تأكيد نجاح البناء أو الاختبارات حتى تشغيلها على Windows/.NET SDK؛ بيئة Arena الحالية لا تحتوي `dotnet`.

## الخلاصة الفنية

### 1) RowVersion على SQLite

- كل الكيانات ترث `BaseEntity.RowVersion`، وهو مطلوب ورمز تزامن. إعداد النموذج يبقيه `IsConcurrencyToken`، ويضبط SQLite على `ValueGenerated.Never`، مع إبقاء SQL Server على `ValueGenerated.OnAddOrUpdate`.
- كان توليد الرمز معتمداً على `SaveChangesInterceptor` المسجل عبر DI. عدد من اختبارات الترقية والتهيئة ينشئ `DatesErpDbContext` مباشرةً من `DbContextOptions`، وبالتالي لا يمر عبر ذلك الـinterceptor. صار سياق البيانات نفسه يولّد الرمز لكل كيان `BaseEntity` مضاف/معدّل عند الحفظ المتزامن أو غير المتزامن على SQLite؛ ويتخطى SQL Server دون تغيير سلوكه.
- بقي الـinterceptor مسؤولاً عن التدقيق. وهو يملأ الرمز لسجلات `AuditLog` التي يضيفها أثناء الحفظ بعد جولة `ChangeTracker`، ولا يستبدل الرمز الذي جهّزه السياق.
- عولج مسار SQLite القديم في `SchemaMigrator`: إن غاب العمود يُضاف كـ`BLOB NOT NULL` مع قيمة ثابتة غير فارغة للسجلات القديمة، وإن وُجدت رموز فارغة تُهيّأ. لا يُحذف العمود أو القيد، ولا تُجعل الخاصية nullable. الرموز الجديدة للتعديلات اللاحقة يولّدها السياق.
- أضيفت تغطية مصدرية للكيانات المذكورة: `PermissionAuditLogs`, `UnitConversions`, `ByProducts`, `InspectionResultTypes`, `SystemSettings`, `RoleResourcePermissions` و`AuditLogs`، إضافة إلى فحص كل كيانات `BaseEntity` في بيانات نموذج EF. كما أضيف اختبار لمسار `UpgradeReferenceData` الذي يستخدم سياقاً مباشراً.

### 2) تفرد `RawTreatments.TreatmentNo`

- السبب المؤكد: `StartTreatmentCore` يطلب `Numbering.Next("TRT")`، لكن `NumberingService.GetMaxPureNumber` و`IsUsed` لم يقرآ `RawTreatments.TreatmentNo`. مع عدّاد فارغ كان الرقم النقي يرجع إلى `1` في كل بدء، ثم يصطدم بالقيد الفريد.
- أُضيف `RawTreatments` إلى استعلام أكبر رقم وإلى فحص استخدام الرقم. بقي فهرس `TreatmentNo` الفريد كما هو، وبقيت الأرقام تسلسلية وليست عشوائية. يغطي الاختبار التسلسل، وإصلاح عدّاد قديم، وبدء المعالجة الفعلي.
- أضيف اختبار تزامن يستخدم مضيفين وسياقين SQLite مستقلين على **ملف اختبار مؤقت** واحد، داخل معاملتين، للتحقق من عدم تكرار الرقم. لم يُشغّل هذا الاختبار بعد لغياب .NET في Arena.
- ضُيّق كشف إعادة المحاولة في `ServiceBase`: لا يُعامل كل `SQLITE_CONSTRAINT (19)` على أنه قيد فريد؛ تُعاد المحاولة عند قيد تفرد/مفتاح أساسي فقط، فلا يُخفى أو يُعاد خطأ `NOT NULL` أو `FOREIGN KEY` على أنه تصادم ترقيم.

### 3) مراجعة AutoBackup

في `AutoBackup.EnsurePreMigration` صار مسار ملف النسخة يُمرّر كـ`SqlParameter` (`@backupPath`) في أوامر `BACKUP` و`RESTORE VERIFYONLY`، بدلاً من دمجه داخل النص. اسم قاعدة البيانات يُقتبس كمعرّف SQL مع تهريب `]`؛ الاستثناء الموضعي من EF1002 يخص المعرّف المقتبس فقط. لم يُنشأ اتصال بقاعدة إنتاج ولم تُنفّذ أوامر النسخ هنا.

## الملفات المتغيرة في هذه الحزمة

| الملف | السبب |
|---|---|
| `src/DatesErp.Infrastructure/Persistence/DatesErpDbContext.cs` | توليد رموز SQLite عند `SaveChanges` و`SaveChangesAsync` لكل `BaseEntity`؛ إبقاء SQL Server مولّداً من المخزن. |
| `src/DatesErp.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` | تغطية `BaseEntity` كاحتياط، وتعيين رمز لسجلات التدقيق التي تُنشأ داخل الـinterceptor دون استبدال رمز موجود. |
| `src/DatesErp.Infrastructure/Persistence/SchemaMigrator.cs` | إضافة/تهيئة `RowVersion` المطلوب في جداول SQLite القديمة بلا تحويله إلى nullable. |
| `src/DatesErp.Infrastructure/DependencyInjection.cs` | إضافة `TRT` إلى قراءة أكبر رقم وفحص استخدامه من `RawTreatments`. |
| `src/DatesErp.Application/Services/ServiceBase.cs` | جعل إعادة المحاولة في SQLite خاصة بقيد التفرد/المفتاح الأساسي. |
| `src/DatesErp.Desktop/Services/AutoBackup.cs` | تمرير مسار النسخة كمعامل واقتباس معرّف قاعدة البيانات. |
| `tests/DatesErp.Tests/TestHost.cs` | خيار اتصال SQLite مؤقت مشترك لاختبار أكثر من اتصال على قاعدة اختبار واحدة. |
| `tests/DatesErp.Tests/SqliteRowVersionTests.cs` | فحص كل كيانات `BaseEntity`، والإضافات والتعديلات المباشرة والمتزامنة/غير المتزامنة، وSQL Server والمخطط القديم. |
| `tests/DatesErp.Tests/ReferenceDataUpgradeTests.cs` | تحقق من وجود الرموز بعد إضافة بيانات الترقية عبر سياق مباشر بلا interceptor. |
| `tests/DatesErp.Tests/NumberingTests.cs` | تسلسل `TRT`، تصحيح عدّاد متأخر، واختبار سياقين متزامنين. |
| `tests/DatesErp.Tests/RawTreatmentScenarioTests.cs` | تأكيد عدم تكرار الرقم في سيناريو بدء معالجة حقيقي. |

يتضمن المصدر كذلك تعديل التوثيق السابق في `src/DatesErp.Core/Common/BaseEntity.cs` حول عقد `RowVersion`؛ لم تُحذف الخاصية أو القيد.

## نتائج تحقق نُفذت فعلياً في Arena

| الفحص | النتيجة |
|---|---|
| `dotnet build DateERP.sln -c Release --no-restore --nologo -m:1` | **لم يبدأ**: `/bin/bash: line 1: dotnet: command not found`، exit 127. |
| `dotnet test DateERP.sln -c Release --no-build --no-restore --nologo -m:1 --logger "trx;LogFileName=OFFTT-release.trx" --results-directory TestResults` | **لم يبدأ** للسبب نفسه، exit 127؛ لم يُنتج TRX ولا أعداد pass/fail/skip. |
| اختبارات Python `tools/ci/test_structural_checks.py` | **10 ناجحة**. هذه فحوص أدوات وليست اختبارات xUnit. |
| `python3 tools/ci/verify_xaml_names.py` | **ناجح**. |
| تحليل XML | **54 ملف XAML** قُرئت بنجاح. |
| `tools/ci/structural_checks.py` | **فشل** في مخالفات منفصلة عن هذا الإصلاح: الأداة أبلغت عن 8 أعضاء `double` في `Production.cs` (`ProductionOrderItemAllocation.AllocatedQtyKg`, `ConsumedQtyKg`; `ProductionPlanCustomerScope.TargetQtyKg`; `ProductionPlanItemAllocation.AllocatedQtyKg`, `ConsumedQtyKg`, `ReleasedQtyKg`; `ProductionPlanSession.CapacityHours`, `UsedHours`) وعن ازدياد الألوان المضمنة في `DashboardView.xaml`, `FinishedGoodsView.xaml`, `OrdersView.xaml`, `PlanningView.xaml`, `ProductionDeliveryView.xaml`, `QualityView.xaml`, `ReportsView.xaml`, `ShipmentReportView.xaml`, ووجود ألوان مضمنة في `ThemeSettingsView.xaml`. لم أغيّر هذه الملفات أو قواعد العمل خارج النطاق. |
| اختبار سلامة ZIP والبصمة | **ناجح**؛ 455 ملف مصدر، بلا قواعد بيانات/نسخ احتياطية/مخرجات build أو TRX. |

**لا توجد نتيجة Release build أو xUnit حالية.** لم يُحذف أو يُتجاوز أي اختبار. ولا أستنتج أن جميع إخفاقات التشغيل السابق لها سبب واحد.

## النتائج السابقة التي زوّدني بها المستخدم — تاريخية فقط

لنسخة أقدم من هذه الحزمة أفاد المستخدم بأن بناء Windows على SDK 8.0.127 نجح (0 أخطاء و0 تحذيرات)، وأن xUnit سجّل **243 نجاحاً و716 فشلاً من أصل 959**. عدد الاختبارات المتجاوزة غير معلوم من البيانات المتاحة؛ لا أكرر ادعاء «0 تجاوز» الوارد في التقرير القديم. هذه الأرقام **ليست** نتيجة تشغيل الحزمة الحالية.

أسباب `RowVersion` المباشرة وسهو مصدر `TRT` عولجت في المصدر، لكن تصنيف بقية فشل الاختبارات إلى تجهيز بيانات/منطق أعمال/ترحيل/تزامن/مستقل، واستخراج أول استثناء فعلي لكل مجموعة، يتطلب ملف `.trx` والسجل الكامل من تشغيل Windows الحالي. لا يوجد أي منهما في مساحة العمل.

## إعادة التحقق المطلوبة على Windows

استخرج الحزمة إلى مجلد منفصل، ثم نفّذ من مجلد `OFFTT`:

```powershell
dotnet restore DateERP.sln
dotnet build DateERP.sln -c Release --no-restore --nologo -m:1
dotnet test DateERP.sln -c Release --no-build --no-restore --nologo -m:1 `
  --logger "trx;LogFileName=OFFTT-release.trx" --results-directory .\TestResults
```

أرسل `TestResults\OFFTT-release.trx` وسجل البناء/الاختبار الكامل؛ عندها يمكن إعطاء أعداد النجاح والفشل والتجاوز الفعلية، وتصنيف الإخفاقات حسب الجذر، وذكر أول استثناء لكل مجموعة ثم إعادة تشغيل الفاشل فقط.

## السلامة والتسليم

لم أفتح أو أعدّل `D:\DateERP_1.50.20_Full\DateERP_1.50.20`، ولم أتصل بقاعدة SQL Server للمستخدم أو أغيّرها. اختبارات التزامن المضافة مصممة لملف SQLite مؤقت معزول وتزيله عند انتهائها؛ لم تُنفّذ في Arena لغياب SDK.

- الحزمة: `/home/user/DateERP_OFFTT_20261016_ROWVERSION_TRT_FIX_SOURCE.zip`
- ملف SHA-256: `/home/user/DateERP_OFFTT_20261016_ROWVERSION_TRT_FIX_SOURCE.zip.sha256`
- SHA-256: `59a179453cf6fd4c0b4f184a0b3d62ea3fe73a490603cbfb34764d6f89fca28f`
- التقرير: `/home/user/DateERP_OFFTT_20261016_ROWVERSION_TRT_FIX_REPORT_AR.md`

**الحزمة مرشح مصدر للاختبار المنفصل، وليست نسخة إنتاج معتمدة قبل نجاح Release build والاختبارات الكاملة وإعادة الفاشلة.**
