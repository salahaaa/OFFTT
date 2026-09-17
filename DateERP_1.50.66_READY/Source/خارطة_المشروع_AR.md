# خارطة مشروع DateERP — الإصدار 43 (1.50.43)

دليل تصفّح سريع لمن يفتح المشروع في Visual Studio Code أو Visual Studio.
القاعدة: **كل شاشة لها خدمة، وكل خدمة تمر على طبقة واحدة للكتابة، وكل جدول له كيان في Core.**

## 1) الشاشات (25 شاشة) — المجلد: `src/DatesErp.Desktop/Views/Screens/`
| الملف | الوظيفة |
|---|---|
| DashboardView.xaml | لوحة المؤشرات الرئيسية |
| PlanningView.xaml | التخطيط الإنتاجي (بنود الخطة) |
| OrdersView.xaml | أوامر الإنتاج |
| ProductionDeliveryView.xaml | تسليم الإنتاج (المعاد تصميمها في 38) |
| DeliveryView.xaml | تسليم العملاء |
| ReceivingView.xaml | الاستلام من المزارع/الموردين |
| FGReceiveView.xaml | استلام المنتج التام |
| QualityView.xaml | الفحص والجودة وقراراتها |
| PlanClosureView.xaml | إقفال الخطط اليومية |
| ReportsView.xaml | التقارير |
| ShipmentReportView.xaml | تقرير الشحنات |
| FinishedGoodsView.xaml | مخزون المنتج التام |
| MaterialsView.xaml | المواد المساعدة |
| ItemsView.xaml | الأصناف |
| CartonView.xaml | الكراتين وعدّها |
| ShiftsView.xaml | الورديات والخطوط |
| ItemsCapacitiesView.xaml | طاقات الأصناف |
| WarehouseVariablesView.xaml | متغيرات المستودعات |
| MyTasksView.xaml | مركز المهام |
| PermissionsView.xaml | الصلاحيات |
| UsersView.xaml | المستخدمون |
| BackupView.xaml | النسخ الاحتياطي والاستعادة |
| AuditFilterView.xaml | سجل التدقيق |
| SystemInfoView.xaml | معلومات النظام والإصدار |
| GenericListView.xaml | قوائم عامة مساعدة |

## 2) النوافذ (10) — المجلد: `src/DatesErp.Desktop/Views/`
LoginWindow (الدخول)، MainWindow (الإطار الرئيسي)، ErpChrome (هوية الواجهة)،
OrderDetailWindow (تفاصيل الأمر)، PlanDetailWindow (تفاصيل الخطة)،
PrintPreviewWindow (معاينة الطباعة)، ConnectionSetupWindow (إعداد الاتصال)،
AuxAdminWindow (إدارة المساعدات)، CustomerAvailabilityWindow (إتاحة العميل)، InputDialog (حوارات الإدخال).

## 3) الجداول (74 كياناً) — تعرّف في: `src/DatesErp.Infrastructure/Persistence/DatesErpDbContext.cs`
- **أوامر الإنتاج:** ProductionOrder / ProductionOrderItem / ProductionOrderMaterial / ProductionExecution / ExecutionByProduct / ExecutionDowntime
- **التخطيط والإقفال:** ProductionPlan / ProductionPlanItem / PlanClosing / PlanClosingItem / PlanClosingByProduct / ProductShiftCapacity
- **المخزون والحركات:** Lot / StockBalance / InventoryTransaction / Warehouse / FinishedGoodsReceipt(+Item) / ProductionDelivery(+Item) / CustomerDelivery(+Item)
- **الجودة:** InspectionResult / InspectionResultType / ItemInspectionProfile / QualityByProductRecord
- **الكراتين:** CartonCountDoc(+Item) / CartonSaleDoc(+Item)
- **الأسياسات:** Customer / Supplier / Product / ByProduct / AuxiliaryMaterial / AuxGroup / AuxCustomerSpec / ItemCategory / ItemGroup / PackagingType / UnitOfMeasure / UnitConversion / ConsumptionFormula / TreatmentType
- **الشحن والمعالجة:** ShipmentJourney / ShipmentItemTreatmentPart (+متعلقاتها)
- **الأمان والتدقيق:** AppUser / UserRole / UserResourcePermission / PermissionOperation / PermissionResource / PermissionAuditLog / AuditLog / Delegation / ClientMachine
- **النظام:** CompanyInfo / SystemSetting / DbVersion / NumberingScheme / Employee / ProductionLine / WorkflowTask(+History)
- الكيانات نفسها (الأعمدة والعلاقات): `src/DatesErp.Core/Entities/`
- إنشاء القاعدة وبذرها: `DbSeeder.cs` — ترحيلات الإصدارات: `SchemaMigrator.cs`

## 4) الخدمات (منطق العمل) — المجلد: `src/DatesErp.Application/Services/`
أهمها: ProductionOrderService (دورة الأمر كاملة)، CustomerDeliveryService (تسليم العميل وبوابة الجودة QualityGate)،
PlanClosureService (إقفال الخطط)، PlanningService + PlanningCapacityEvaluator، ExecutionService،
ReceivingService + RawTreatmentService + ReceivingTreatmentLifecycle، FinishedGoodsService،
ProductionDeliveryService(+Actual)، CartonService، InspectionService، ReportService*(٤ ملفات)،
PermissionService + AuthService، BackupService + AdminRecovery، AuditService، TraceabilityService،
TaskCenterService + WorkflowTaskService، ServiceBase (المعاملة الواحدة + PostStockMovement + الحراس).

## 5) الاختبارات — `tests/DatesErp.Tests/`
PlanClosureTests (ومنها T09 الجديد)، OrderReversalAuditTests، GuardTests، ProductionOrderScreenTests،
LayeredWriteEnforcementTests، DecimalStorageParityTests، SqlServerProviderTests، AuditCredentialRedactionTests، SchemaMigratorTests.

## 6) تقارير وسجلات عربية في الجذر
AUDIT_1.50.37_COMPREHENSIVE_AR.md — PRODUCTION_CYCLE_INSPECTION_AR.md — FIXLOG_B110_AR.md —
SECURITY_UPGRADE_AR.md — خارطة_المشروع_AR.md (هذا الملف).

## 7) أدوات التحديث والبناء في الجذر ومجلد Installer
update43.bat (بناء + نسخ فوق التثبيت، ASCII آمن) — RUN.bat / UPDATE-AND-RUN.bat —
Installer/بناء_التحديث_الفوري.bat — Installer/نسخ_فوق_مجلد_التثبيت.bat — Installer/اقرأني_للتحديث_المصغر.html

## فتح المشروع في VS Code
1. File ← Open Folder ← اختر مجلد المشروع (مثلاً D:\DateERP_1.50.43_FULL_SOURCE).
2. ثبّت إضافة **C# Dev Kit** من Microsoft (تلوين وتنقل وفحص أخطاء).
3. ملفات XAML تُفتح كنص XML منسّق — لا يوجد مصمم بصري في VS Code؛
   للمعاينة البصرية والتصميم المرئي استخدم **Visual Studio** الكامل (فتح نفس المجلد عبر DateERP.sln).
4. البناء من الطرفية المدمجة: `dotnet build DateERP.sln -c Release` — والتشغيل: `dotnet run --project src\DatesErp.Desktop -c Release`.
5. التصفح آمن تماماً: فتح الملفات لا يغيّر شيئاً ولا يلمس قاعدة البيانات؛ احفظ فقط ما تنوي تعديله.
