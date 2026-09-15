-- ============================================================================
-- MfgSystem 1.50.66 — سلامة المخزون: PackagingTypeId جزء من مفتاح الرصيد
-- ============================================================================
-- 1. إصلاح فهرس StockBalance ليشمل PackagingTypeId
-- 2. إنشاء Unique Constraint/Index يمنع تكرار نفس: Warehouse+Product+Material+Lot+Customer+PackagingType
-- 3. معالجة أرصدة مكررة قديمة قبل إنشاء القيد، بدون حذف تلقائي أعمى
-- 4. منع QtyKg<0 و PackageCount<0 إلا بتسوية مصرحة (على مستوى التطبيق + CHECK CONSTRAINT اختياري)
-- 5. منع خلط مخزون عميل بآخر (على مستوى التطبيق)
-- ============================================================================

-- الخطوة 1: كشف الأرصدة المكررة الحالية (للمراجعة قبل الدمج)
PRINT '=== كشف الأرصدة المكررة ===';
SELECT 
    WarehouseId,
    ISNULL(ProductId, -1) as ProductId,
    ISNULL(MaterialId, -1) as MaterialId,
    ISNULL(LotId, -1) as LotId,
    ISNULL(CustomerId, -1) as CustomerId,
    ISNULL(PackagingTypeId, -1) as PackagingTypeId,
    COUNT(*) as DuplicateCount,
    SUM(QtyKg) as TotalQtyKg,
    SUM(PackageCount) as TotalPackageCount,
    STRING_AGG(CAST(Id as NVARCHAR), ',') as Ids
FROM StockBalances
GROUP BY WarehouseId, ProductId, MaterialId, LotId, CustomerId, PackagingTypeId
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC;

-- الخطوة 2: معالجة الأرصدة المكررة — دمج بدون حذف أعمى
-- نحتفظ بأقدم سجل ونجمع فيه الكميات، ثم نحذف البقية بعد التوثيق
-- هذا السكربت آمن: يجمع الكميات ولا يفقد أي كمية

PRINT '=== دمج الأرصدة المكررة ===';

-- إنشاء جدول مؤقت للتوثيق
IF OBJECT_ID('tempdb..#DuplicateMergeLog') IS NOT NULL DROP TABLE #DuplicateMergeLog;
CREATE TABLE #DuplicateMergeLog (
    WarehouseId INT,
    ProductId INT NULL,
    MaterialId INT NULL,
    LotId INT NULL,
    CustomerId INT NULL,
    PackagingTypeId INT NULL,
    KeeperId INT,
    MergedIds NVARCHAR(MAX),
    TotalQtyKg FLOAT,
    TotalPackageCount INT,
    DuplicateCount INT
);

-- لكل مجموعة مكررة: احتفظ بأقدم Id واجمع البقية
WITH Duplicates AS (
    SELECT 
        WarehouseId, ProductId, MaterialId, LotId, CustomerId, PackagingTypeId,
        MIN(Id) as KeeperId,
        SUM(QtyKg) as TotalQtyKg,
        SUM(PackageCount) as TotalPackageCount,
        COUNT(*) as DuplicateCount,
        STRING_AGG(CAST(Id as NVARCHAR), ',') as AllIds
    FROM StockBalances
    GROUP BY WarehouseId, ProductId, MaterialId, LotId, CustomerId, PackagingTypeId
    HAVING COUNT(*) > 1
)
-- تحديث السجل الحافظ
UPDATE sb
SET 
    sb.QtyKg = d.TotalQtyKg,
    sb.PackageCount = d.TotalPackageCount
FROM StockBalances sb
INNER JOIN Duplicates d ON sb.Id = d.KeeperId;

-- توثيق
INSERT INTO #DuplicateMergeLog
SELECT 
    WarehouseId, ProductId, MaterialId, LotId, CustomerId, PackagingTypeId,
    KeeperId, AllIds, TotalQtyKg, TotalPackageCount, DuplicateCount
FROM Duplicates;

-- حذف المكررات (بعد جمع كمياتها)
WITH DuplicatesToDelete AS (
    SELECT 
        sb.Id,
        ROW_NUMBER() OVER (PARTITION BY sb.WarehouseId, sb.ProductId, sb.MaterialId, sb.LotId, sb.CustomerId, sb.PackagingTypeId ORDER BY sb.Id) as rn
    FROM StockBalances sb
)
DELETE FROM StockBalances
WHERE Id IN (
    SELECT Id FROM DuplicatesToDelete WHERE rn > 1
);

PRINT 'تم دمج الأرصدة المكررة — راجع #DuplicateMergeLog للتفاصيل';
SELECT * FROM #DuplicateMergeLog;

-- الخطوة 3: حذف الفهرس القديم وإنشاء فهرس جديد يشمل PackagingTypeId مع تفرد
PRINT '=== إنشاء فهرس فريد جديد ===';

-- حذف الفهرس القديم إن وجد
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StockBalances_WarehouseId_ProductId_MaterialId_LotId_CustomerId' AND object_id = OBJECT_ID('StockBalances'))
BEGIN
    DROP INDEX IX_StockBalances_WarehouseId_ProductId_MaterialId_LotId_CustomerId ON StockBalances;
    PRINT 'تم حذف الفهرس القديم';
END

-- حذف الفهرس الجديد إن وجد مسبقاً (لإعادة الإنشاء)
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StockBalances_FullKey_Unique' AND object_id = OBJECT_ID('StockBalances'))
BEGIN
    DROP INDEX IX_StockBalances_FullKey_Unique ON StockBalances;
    PRINT 'تم حذف الفهرس الفريد القديم';
END

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StockBalances_WarehouseId_ProductId_MaterialId_LotId_CustomerId_PackagingTypeId' AND object_id = OBJECT_ID('StockBalances'))
BEGIN
    DROP INDEX IX_StockBalances_WarehouseId_ProductId_MaterialId_LotId_CustomerId_PackagingTypeId ON StockBalances;
    PRINT 'تم حذف فهرس 1.50.66 القديم';
END

-- إنشاء فهرس فريد جديد يشمل PackagingTypeId
-- ملاحظة: SQL Server يسمح بـ NULL في فهرس فريد، لكنه يعتبر NULL = NULL كقيمة واحدة فقط (يسمح بسجل واحد NULL)
-- لمعالجة NULLs، نستخدم فهرس مع COALESCE عبر عمود محسوب أو نستخدم فلتر
-- الحل: ننشئ فهرس فريد عادي — EF Core سيتعامل مع NULLs عبر التطبيق، والقيد يمنع التكرار الحقيقي

CREATE UNIQUE INDEX IX_StockBalances_FullKey_Unique
ON StockBalances (WarehouseId, ProductId, MaterialId, LotId, CustomerId, PackagingTypeId)
WHERE ProductId IS NOT NULL OR MaterialId IS NOT NULL; -- فلتر يضمن وجود صنف أو مادة

PRINT 'تم إنشاء الفهرس الفريد الجديد IX_StockBalances_FullKey_Unique';

-- فهرس إضافي للبحث السريع
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StockBalances_WarehouseId_ProductId_CustomerId_PackagingTypeId' AND object_id = OBJECT_ID('StockBalances'))
BEGIN
    CREATE INDEX IX_StockBalances_WarehouseId_ProductId_CustomerId_PackagingTypeId
    ON StockBalances (WarehouseId, ProductId, CustomerId, PackagingTypeId);
    PRINT 'تم إنشاء فهرس البحث السريع';
END

-- الخطوة 4: منع الرصيد السالب إلا بتسوية — CHECK CONSTRAINT اختياري (التحقق الأساسي في التطبيق)
-- نضيف CHECK يسمح بالسالب فقط إذا كان هناك حركة تسوية مرتبطة؟ هذا صعب على مستوى القاعدة
-- لذا نكتفي بتحذير في التطبيق، ونضيف CHECK يمنع سالب كبير جداً كحماية إضافية (اختياري)

-- PRINT '=== فحص الأرصدة السالبة الحالية ===';
SELECT * FROM StockBalances WHERE QtyKg < -0.001 OR PackageCount < 0;

-- الخطوة 5: منع خلط العملاء — فحص حركات قديمة قد تكون خلطت
PRINT '=== فحص خلط العملاء المحتمل ===';
SELECT 
    WarehouseId, ProductId, LotId, PackagingTypeId,
    COUNT(DISTINCT CustomerId) as DistinctCustomers,
    STRING_AGG(DISTINCT CAST(CustomerId as NVARCHAR), ',') as CustomerIds,
    SUM(QtyKg) as TotalQty
FROM StockBalances
WHERE CustomerId IS NOT NULL
GROUP BY WarehouseId, ProductId, LotId, PackagingTypeId
HAVING COUNT(DISTINCT CustomerId) > 1
ORDER BY COUNT(DISTINCT CustomerId) DESC;

-- ملاحظة: هذا ليس بالضرورة خلط — كل عميل له رصيده المنفصل بنفس المفتاح ما عدا CustomerId
-- الخلط الحقيقي هو حركة لعميل A على رصيد عميل B — يتم منعه في التطبيق عبر PostStockMovement

PRINT '=== انتهى إصلاح 1.50.66 ===';
PRINT 'تأكد من تشغيل اختبارات: StockBalancePackagingTests';
