OFFTT / DatesErp 1.50.73
FULL SOURCE BUILD PACKAGE
==========================

هذه الحزمة هي المصدر الكامل القابل للبناء، وليست PATCH جزئية.
تم تضمين الحل الكامل DatesErp.sln وكل المشاريع والمراجع اللازمة:

  src/DatesErp.Core
  src/DatesErp.Application
  src/DatesErp.Infrastructure
  src/DatesErp.Desktop
  tests/DatesErp.Tests
  tools/* الموجودة في DatesErp.sln

كما تتضمن ملفات الحل و Directory.Build.props وملفات المصدر وXAML والاختبارات
والأدوات والوثائق اللازمة. لا تتضمن هذه الحزمة bin أو obj أو EXE أو DLL أو PDB
أو حزم ZIP متداخلة.

إصلاح زر حفظ خطة الإنتاج
-------------------------
الإصدار الموجود داخل الحزمة يتضمن الإصلاح النهائي في:

  src/DatesErp.Desktop/Views/Screens/PlanningView.Capacity.cs
  src/DatesErp.Desktop/Views/Screens/PlanningView.xaml.cs

ويشمل:
- تفعيل الحفظ حسب _locked فقط.
- إبقاء EvaluateDraft و _capacityValid وفحص الطاقة.
- عرض رسالة التحقق عند الضغط بدلاً من تعطيل زر الحفظ مسبقاً.
- استخدام Permission Module = planning مع PermissionGate.

طريقة البناء على Windows مع .NET 8 SDK
---------------------------------------
من مجلد الحزمة بعد فك الضغط:

  dotnet restore DatesErp.sln
  dotnet publish DatesErp.sln -c Release -r win-x64 --self-contained true

ولاختبار الحل:

  dotnet test DatesErp.sln -c Release --no-restore

ملاحظة: المشروع WPF ويحتاج Windows/.NET 8 SDK عند تنفيذ publish النهائي.
هذه الحزمة مصدر فقط ولا تحتوي مخرجات build.
