# تقرير إصلاح فشل اختبارات OFFTT — RowVersion/SQLite

## الحالة

عولج سبب خطأ `NOT NULL constraint failed: AuditLogs.RowVersion` في مصدر المشروع، وأُعدّت حزمة مصدر مرشحة جديدة. **لم أتمكن من إعادة بناء الحل أو تشغيل xUnit في Arena** لأن بيئة التنفيذ لا تحتوي على `dotnet`؛ لذلك لا أستطيع تأكيد أن أخطاء الاختبارات الـ716 اختفت كلها أو أن بقية الاختبارات ناجحة. لا تُعد هذه النسخة جاهزة للإنتاج حتى يُعاد تشغيل البناء والاختبارات على Windows.

## نتيجة Windows السابقة

الأرقام التالية هي نتائج التشغيل التي زوّدني بها المستخدم للنسخة السابقة، وليست إعادة تشغيل بعد الإصلاح:

- Build على .NET SDK 8.0.127: **نجح، 0 أخطاء و0 تحذيرات**.
- الاختبارات: **243 ناجحاً، 716 فاشلاً، 0 متجاوزاً من أصل 959**.
- الاستثناء المتكرر: `SQLite Error 19: 'NOT NULL constraint failed: AuditLogs.RowVersion'` أثناء `DbSeeder.Seed`.

## السبب الجذري والإصلاح

- `AuditLog` يرث `BaseEntity` مباشرة، ولا يرث `AuditableEntity`. وسيط الحفظ كان يولّد رمز SQLite للكيانات `AuditableEntity` فقط، ثم كان يتخطى `AuditLog` قبل توليد الرمز. كما أن سجلات التدقيق التي ينشئها الوسيط تُضاف بعد جولة المرور على `ChangeTracker`، وكانت تُضاف بلا `RowVersion`.
- SQLite لا يولّد نوع `rowversion` مثل SQL Server؛ عمود `RowVersion` يبقى إلزامياً، ولذلك يجب أن يزوّده التطبيق برمز قبل الإدراج أو التعديل.
- في `OnModelCreating` حُفظ سلوك SQL Server (`OnAddOrUpdate`) كما هو، وحدد سلوك المزود الآخر صراحةً على أنه `ValueGenerated.Never` مع إبقاء الخاصية رمز تزامن غير قابل لـNULL.
- في `AuditSaveChangesInterceptor` صار توليد رمز التطبيق يشمل كل `BaseEntity` المضاف أو المعدّل على SQLite، بما في ذلك `AuditLog` و`PermissionAuditLog`. وتُسند قيمة كذلك لسجلات التدقيق الجديدة التي ينشئها الوسيط بعد جولة ChangeTracker.
- لم أغيّر `DbSeeder` أو `TestHost`: موضع الخلل كان توليد رمز التزامن في وسيط الحفظ وإعداد المزود، وليس منطق البذر. لم يتغير مسار SQL Server، ولا يُسنَد إليه رمز من التطبيق.

## الملفات المعدّلة والاختبارات المضافة

- `src/DatesErp.Core/Common/BaseEntity.cs` — توضيح سلوك الرمز على SQL Server وSQLite.
- `src/DatesErp.Infrastructure/Persistence/DatesErpDbContext.cs` — فصل قيمة التوليد حسب المزود مع الحفاظ على `IsConcurrencyToken` وعدم جعل الحقل قابلاً لـNULL.
- `src/DatesErp.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` — توليد الرمز لجميع كيانات `BaseEntity` على SQLite، بما فيها سجلات التدقيق الجديدة.
- `tests/DatesErp.Tests/SqliteRowVersionTests.cs` — أضيفت 4 اختبارات انحدار: سجلات التدقيق المبذورة، إدخال/تعديل كيان أساسي، رفض تعديل متزامن قديم على SQLite، والتحقق من بقاء RowVersion مولّداً من SQL Server.

**لم تُشغّل اختبارات xUnit الجديدة أو القديمة**؛ لذا لا يوجد عدد ناجح/فاشل/متجاوز موثوق للنسخة الحالية. كما لا أفترض أن كل إخفاقات النسخة السابقة لها سبب واحد؛ لا يمكن تصنيف الإخفاقات الأخرى دون إعادة تشغيل المجموعة كاملة بعد إزالة العائق الأول.

## نتائج التحقق في Arena

الأوامر المطلوبة أُعيدت بعد التعديل، لكن `dotnet` غير مثبت في هذه البيئة؛ لم يصل التنفيذ إلى MSBuild أو مشغّل xUnit:

```text
dotnet build DateERP.sln -c Release --no-restore --nologo -m:1
/bin/bash: line 1: dotnet: command not found
exit code: 127
```

```text
dotnet test DateERP.sln -c Release --no-build --no-restore --nologo -m:1
/bin/bash: line 1: dotnet: command not found
exit code: 127
```

فحوص غير .NET المتاحة:

- فحص أسماء عناصر XAML: **نجح**.
- تحليل XML: **54 ملف XAML، بلا أخطاء**.
- اختبارات الحارس البنيوي Python: **10 ناجحة**؛ لا تمثل اختبارات المشروع.
- `tools/ci/structural_checks.py`: **فشل** في مخالفات منفصلة موجودة في المصدر: 8 أعضاء `double` ضمن `Production.cs`، وانحراف الألوان المضمنة في 8 شاشات، و`ThemeSettingsView`. هذه ليست نتيجة بناء C# أو اختبار xUnit ولم أغيّر قواعد التخطيط/الألوان ضمن إصلاح RowVersion.

لا توجد نتيجة build جديدة للتحذيرات الأمنية. نتائج البناء السابقة ذات 0 تحذيرات تخص النسخة السابقة كما أفاد المستخدم، ولا أستطيع نقلها للنسخة الحالية دون تنفيذها. كما أن إعدادات SQL Server لم تُختبر باتصال حقيقي هنا؛ الاختبار المضاف يتحقق فقط من بيانات نموذج EF ولا ينشئ اتصالاً.

## الحفاظ على النسخ والبيانات

عملت على نسخة Arena في `/home/user/OFFTT`. مسار Windows القديم `D:\DateERP_1.50.20_Full\DateERP_1.50.20` وقواعد SQL Server غير متاحين من هذه البيئة؛ لم أفتحها أو أعدّلها أو أتصل بها. لم أغيّر سلسلة اتصال أو أجرِ تعديلاً على قاعدة بيانات. الأرشيف يستبعد `.git` ومخرجات البناء وقواعد البيانات والنسخ الاحتياطية وملفات الاتصال المحلية.

## حزمة المصدر

- الاسم: `DateERP_OFFTT_20261016_SQLITE_ROWVERSION_FIX_SOURCE.zip`
- المسار في مساحة العمل: `/home/user/DateERP_OFFTT_20261016_SQLITE_ROWVERSION_FIX_SOURCE.zip`
- الحجم: **1,478,212 بايت** — 455 ملفاً، منها 14 ملف مشروع.
- بصمة SHA-256: `ed4f5f2101ca0e5713c644bc618dbda763183c5aecef6a3b5c6f22c839174727`
- اجتازت الحزمة فحص `unzip -t` والتحقق من البصمة.

## إعادة التحقق على Windows

استخرج ZIP إلى مجلد منفصل، ثم من مجلد `OFFTT` شغّل باستخدام .NET SDK 8.0.127:

```powershell
cd D:\DateERP_OFFTT_20261016_SQLITE_ROWVERSION_FIX\OFFTT
dotnet restore DateERP.sln
dotnet build DateERP.sln -c Release --no-restore --nologo -m:1
dotnet test DateERP.sln -c Release --no-build --no-restore --nologo -m:1
```

يجب إرفاق المخرجات الكاملة الجديدة لتحديد أي إخفاق مستقل بقي بعد إصلاح RowVersion. إلى حين نجاح هذه الخطوات وتوثيق أعداد الاختبارات الفعلية، تظل الحزمة مرشح مصدر للإصلاح وليست نسخة إنتاج معتمدة.
