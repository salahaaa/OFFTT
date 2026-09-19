OFFTT / DatesErp — UPDATE 1.50.74 DEV
PRODUCTION ORDERS SOURCE UPDATE
================================

هذه حزمة مصدر كاملة للتطوير بعد Baseline Stable 1.50.73.
ليست نسخة Windows مستقرة ولم تُعتمد كتسليم نهائي بعد.

المرجع المستقر الذي لا يتم تغييره:
  Git tag: stable-1.50.73
  ZIP: BASELINE_1.50.73_STABLE_FULL_SOURCE_BUILDABLE.zip

المحتوى
-------
- DatesErp.sln
- DatesErp.Core
- DatesErp.Application
- DatesErp.Infrastructure
- DatesErp.Desktop
- tests/DatesErp.Tests
- tools والمراجع الموجودة في الحل
- ملفات المصدر وXAML والاختبارات والوثائق

تحديث أوامر الإنتاج
-------------------
- فحص وتوثيق تكرار أزرار الشاشة.
- عرض مجموعات الخطط المعتمدة لليوم والأيام القادمة.
- إصدار أمر بتاريخ الخطة المجدول من خلال IssuePlanGroup.
- الحفاظ على PlanItemId والعميل والصنف والدفعة والشحنة والعبوة والكميات.
- منع تكرار الأمر لنفس بند الخطة.
- السماح باعتماد أمر مستقبلي، مع إبقاء بدء التنفيذ مقيداً بيومه الفعلي.
- استخدام PermissionModule = production.
- تصحيح إجمالي الكمية في ملخص IssueOrdersFromPlan.

ملفات الفحص الرئيسية
--------------------
- src/DatesErp.Desktop/Views/Screens/OrdersView.xaml.cs
- src/DatesErp.Desktop/Views/Screens/OrdersView.xaml
- src/DatesErp.Desktop/Views/Screens/OrdersWindows.cs
- src/DatesErp.Application/Services/ProductionOrderService.cs
- src/DatesErp.Application/Services/ProductionOrderService.Today.cs
- src/DatesErp.Core/Interfaces/Services/IWorkflowServices.cs
- src/DatesErp.Core/Interfaces/Services/TodayProductionDto.cs
- PRODUCTION_ORDERS_UI_AUDIT_1.50.74_DEV.md

البناء على Windows مع .NET 8 SDK
--------------------------------
  dotnet restore DatesErp.sln
  dotnet publish DatesErp.sln -c Release -r win-x64 --self-contained true
  dotnet test DatesErp.sln -c Release --no-restore

لا تحتوي الحزمة على bin أو obj أو EXE أو DLL أو PDB أو ZIP متداخلة.
يجب اختبار البناء والتشغيل على Windows قبل اعتماد 1.50.74 كتحديث مستقر.
