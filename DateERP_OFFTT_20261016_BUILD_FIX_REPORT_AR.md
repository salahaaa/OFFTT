# تقرير إصلاح أخطاء البناء — DateERP OFFTT

**الحالة:** عولجت الأسباب الخمسة المحددة في الشيفرة وأُعدّت حزمة مصدر جديدة، لكن **لم يتحقق معيار القبول النهائي بعد**: لم يتوفر .NET SDK في بيئة التنفيذ، لذلك لم يُنفّذ بناء C# أو اختبارات xUnit، ولا أستطيع إثبات نجاحهما.

## نطاق العمل والحفاظ على البيانات

- عُدّلت نسخة العمل المتاحة في Arena: `/home/user/OFFTT`. مسار Windows `D:\DateERP_OFFTT_20261016\OFFTT` غير متاح مباشرةً من هذه البيئة، لذلك لا أدّعي فحص محتويات قرص D: الفعلية.
- لم أفتح أو أعدّل النسخة القديمة `D:\DateERP_1.50.20_Full\DateERP_1.50.20`، ولم أتصل بقاعدة SQL Server أو أغيّرها.
- لم أغيّر إعداد اتصال أو قاعدة عمل.

## الأخطاء الخمسة والإصلاحات

1. **CS0234 — تعارض `Application.Current`:** في `CustomerScopePickerWindow.cs` استُخدم الاسم المؤهل `global::System.Windows.Application.Current`؛ راجعت الملف ولم يبقَ فيه استخدام غير مؤهل مماثل.
2. **CS0103 — غياب `CClosedQty`:** أُضيف عنصر XAML مستقل بالاسم `CClosedQty` لعرض `ClosedTotal`. بقي `CClosed` منفصلاً لعرض `ClosedOrders`؛ لم يُستبدل عدد الأوامر بكمية الإنتاج. أُضيف اختبار تصميمي يثبت الفصل بينهما.
3. **CS1501 — توقيع `WithEdit`:** تعريف `ErpToolbar.WithEdit` يقبل `RoutedEventHandler` واحداً فقط. حُذف المعامل الثاني غير المدعوم من الاستدعاء، مع الإبقاء على `EditSelected()` وحراس حالة المسودة وصلاحية `production/Edit` دون تجاوزها.
4. **CS0104 — غموض `UiFormat`:** استُخدم `DatesErp.Core.Common.UiFormat.TryParseDate` صراحةً. بقي محلل التاريخ والتنسيق كما هما؛ لم يتغير تنسيق العرض.
5. **CS0103 — غياب `DeliverySources`:** النوع الفعلي هو `DatesErp.Core.Domain.Entities.DeliverySources`. أُضيف مرجع مساحة الأسماء في `QualityGateTests.cs`، وبقي اختبار بوابة الجودة واستدعاؤه الفعلي دون حذف أو نتيجة ثابتة.

## الملفات المعدّلة

- `src/DatesErp.Desktop/Views/CustomerScopePickerWindow.cs`
- `src/DatesErp.Desktop/Views/Screens/PlanClosureView.xaml`
- `src/DatesErp.Desktop/Views/Screens/ProductionDeliveryView.xaml.cs`
- `tests/DatesErp.Tests/QualityGateTests.cs`
- `tests/DatesErp.Tests/ProductionScreensDesignTests.cs`

## نتائج التحقق

### بناء الحل واختبارات .NET

نُفّذ الأمران من `/home/user/OFFTT` بعد الإصلاح:

```text
dotnet build "DateERP.sln" -c Release --nologo
/bin/bash: line 1: dotnet: command not found
exit code: 127
```

```text
dotnet test "DateERP.sln" -c Release --no-build
/bin/bash: line 1: dotnet: command not found
exit code: 127
```

لم يبدأ MSBuild أو مشغّل الاختبارات. لذلك عدد اختبارات xUnit الناجحة والفاشلة والمتجاوزة **غير معلوم؛ الاختبارات لم تُشغّل**. لا تمثل نتيجة `dotnet build` أعلاه خطأ ترجمة جديداً في المشروع، بل غياب أداة SDK من بيئة Arena.

### الفحوص المتاحة

- `tools/ci/verify_xaml_names.py`: **نجح**، ولم يجد مراجع لعناصر XAML غير موجودة.
- تحليل XML: **نجح لـ54 ملف XAML، بلا أخطاء تحليل**.
- فحص أسماء العناصر/الأحداث المستهدف: `PlanClosureView` (33 اسماً و9 أحداث، بلا أحداث غير محلولة)، و`ProductionDeliveryView` (17 اسماً و5 أحداث، بلا أحداث غير محلولة).
- اختبارات حارس Python البنيوية: **10 ناجحة**؛ هذه ليست اختبارات .NET ولا تُثبت البناء.
- `tools/ci/structural_checks.py`: **فشل** بمخالفات بنيوية خارج إصلاحات الأخطاء الخمسة: 8 أعضاء `double` في `Production.cs`، وانحراف ألوان مضمنة عن الأساس في `DashboardView`, `FinishedGoodsView`, `OrdersView`, `PlanningView`, `ProductionDeliveryView`, `QualityView`, `ReportsView`, `ShipmentReportView`، وألوان مضمنة في `ThemeSettingsView`. لم أغيّر هذه السياسات/الشاشات ضمن إصلاح أخطاء الترجمة.
- **تحذيرات المترجم ومراجعة NuGet الأمنية غير متاحة** لأن البناء والاستعادة لم يبدآ. يحتوي `Directory.Build.props` على NuGet Audit ويعامل التحذيرات `NU1901`–`NU1904` كأخطاء عند تشغيل البناء؛ يلزم فحصها على بيئة Windows/SDK متصلة بحزم NuGet.

## محتويات الحزمة والأمان

- الأرشيف يضم مجلد `OFFTT/` وفيه الحل والمشاريع والمصادر وXAML والاختبارات وأدوات البناء والتثبيت والوثائق: **454 ملفاً و14 ملف مشروع `.csproj`**.
- استُبعد `.git`، ومخرجات `bin/obj/TestResults`، والنسخ الاحتياطية، وقواعد البيانات، وملفات الإعدادات المحلية، والمفاتيح/الشهادات، والأرشيفات السابقة.
- لا توجد ملفات قاعدة بيانات أو إعدادات اتصال محلية ضمن الحزمة. بقيت ثوابت التهيئة المؤقتة وكلمات المرور الاختبارية الموجودة أصلاً داخل الشيفرة/الاختبارات؛ لم أغيّر سلوك تهيئة المستخدم الأول أو الاستعادة ضمن إصلاح الترجمة. الحسابات الأولية تفرض تغيير كلمة المرور عند أول دخول. هذه قيم تطبيق/اختبار وليست بيانات اتصال شخصية أو قاعدة بيانات إنتاج.

## ملف المصدر

- المسار في مساحة العمل: `/home/user/DateERP_OFFTT_20261016_FIXED_SOURCE.zip`
- الحجم: **1,476,515 بايت**
- بصمة SHA-256: `f576a540f6aae99130c888673025e49f5b3be237306d5b275d910d92d2e5f9dd`
- اجتاز `unzip -t` وفحص `sha256sum -c`.
- **الحزمة مصدر مرشح بعد الإصلاحات، وليست نسخة معتمدة ببناء ناجح.**

## خطوات البناء على Windows

1. استخرج الأرشيف إلى `D:\DateERP_OFFTT_20261016_FIXED_SOURCE`؛ سيظهر مجلد `OFFTT` بداخله.
2. افتح Developer PowerShell أو PowerShell مع تثبيت .NET SDK 8.0.127، ثم:

   ```powershell
   cd D:\DateERP_OFFTT_20261016_FIXED_SOURCE\OFFTT
   dotnet restore "DateERP.sln"
   dotnet build "DateERP.sln" -c Release --nologo
   dotnet test "DateERP.sln" -c Release --no-build
   ```

3. لا تعتبر النتيجة نهائية حتى يكتمل البناء بلا أخطاء، وتُسجّل نتائج الاختبارات والتحذيرات من مخرجات Windows الفعلية.
