using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// §تتبع الصنف — حراس الهوية: الصنف المستلم هو هوية المادة حتى نهاية الدورة.
/// كل تحويل (خام ← تام) يجب أن يستند إلى تعريف رسمي في بطاقة المنتج
/// (ProductRawSource متعدد المصادر أو SourceProductId/YieldFactor القديم):
/// لا إنتاج خلاص من دفعة سكري، ولا تسليم خلاص من مخزون سكري، في أي مرحلة من المراحل.
/// </summary>
public static class ProductIdentityGuard
{
    public static void EnsurePlanningLink(DatesErpDbContext db, int finishedId, int? lotId, int? selectedRawId = null)
    {
        var finished = db.Products.AsNoTracking().FirstOrDefault(p => p.Id == finishedId);
        if (finished == null || !finished.IsActive || finished.ItemType != "Finished")
            throw new DomainException("اختر صنفاً تاماً نشطاً من شاشة الأصناف.");
        int? lotRaw = lotId == null ? null : db.Lots.AsNoTracking().Where(l => l.Id == lotId).Select(l => (int?)l.ProductId).FirstOrDefault();
        if (lotId != null && lotRaw == null) throw new DomainException("الدفعة المحددة غير موجودة.");
        if (selectedRawId != null && lotRaw != null && selectedRawId != lotRaw)
            throw new DomainException("الصنف الخام المختار لا يطابق خام الدفعة الفعلي.", "WRONG_CONVERSION");
        // Receipt-backed plans use the lot, never a client-supplied override. The new
        // ProductRawSource table permits several independent raw sources; the legacy
        // Product.SourceProductId remains a compatibility source when no rows exist.
        var allowedRawIds = RawRequirementEngine.GetRules(db, finishedId)
            .Select(x => x.RawProductId).Distinct().ToHashSet();
        int? rawId = lotRaw ?? selectedRawId ?? (allowedRawIds.Count == 1 ? allowedRawIds.Single() : null);
        if (rawId == null || !allowedRawIds.Contains(rawId.Value)
            || !db.Products.AsNoTracking().Any(p => p.Id == rawId && p.IsActive && p.ItemType == "Raw"))
            throw new DomainException("منع تحويل خاطئ: الصنف التام غير مرتبط بالصنف الخام المختار بقاعدة تحويل رسمية. راجع مصادر الخام ومعامل التحويل في بطاقة الصنف.", "WRONG_CONVERSION");
        EnsureConversionAllowed(db, finishedId, lotId);
    }

    /// <summary>
    /// يتحقق أن المنتج (التام) مسموح إنتاجه/تسليمه من الدفعة المحددة حسب بطاقة المنتج.
    /// - تعريف رسمي مطابق ← مسموح.
    /// - تعريف رسمي مختلف ← رفض قاطع (محاولة تحويل صنف إلى صنف آخر).
    /// - لا تعريف والدفعة خام والمنتج تام ← رفض حتى يُضاف التعريف الرسمي للبطاقة.
    /// </summary>
    public static void EnsureConversionAllowed(DatesErpDbContext db, int productId, int? lotId)
    {
        if (lotId == null) return;
        var product = db.Products.AsNoTracking().FirstOrDefault(p => p.Id == productId)
                      ?? throw new DomainException("الصنف غير موجود في بطاقة الأصناف.");
        var lot = db.Lots.AsNoTracking().FirstOrDefault(l => l.Id == lotId)
                  ?? throw new DomainException("الدفعة غير موجودة.");
        var raw = db.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);

        var allowedRawIds = RawRequirementEngine.GetRules(db, productId)
            .Select(x => x.RawProductId).ToHashSet();
        if (allowedRawIds.Contains(lot.ProductId)) return; // التعريف الرسمي مطابق ✓

        if (raw != null && raw.ItemType == "Raw" && product.ItemType == "Finished")
        {
            var allowedNames = db.Products.AsNoTracking()
                .Where(p => allowedRawIds.Contains(p.Id))
                .Select(p => p.ProductNameAr).ToList();
            throw new DomainException(
                $"⛔ منع تحويل خاطئ: المنتج «{product.ProductNameAr}» لا يملك قاعدة إنتاج من خام «{raw.ProductNameAr}».\n"
                + (allowedNames.Count > 0
                    ? $"المصادر الرسمية المسموحة: {string.Join("، ", allowedNames)}."
                    : "لا توجد قاعدة تحويل رسمية — عرّف مصدر الخام ومعامل التحويل أولاً."),
                allowedNames.Count > 0 ? "WRONG_CONVERSION" : "NO_CONVERSION_DEF");
        }
    }

    /// <summary>نسخة لا ترمي استثناء — تعيد رسالة الخطأ أو null عند السلامة (للمسارات التي لا تستخدم معاملات).</summary>
    public static string CheckConversion(DatesErpDbContext db, int productId, int? lotId)
    {
        try { EnsureConversionAllowed(db, productId, lotId); return null; }
        catch (DomainException ex) { return ex.Message; }
    }
}

/// <summary>
/// §تتبع الصنف: رحلة الصنف كاملة من الاستلام حتى الفاتورة.
/// للصنف الخام: استلامه ثم كل ما بُني عليه (الخطط، الأوامر، الإنتاج، الفحص، التام، التسليم).
/// للصنف التام: خططه وأوامره وإنتاجه وفحصه ومخزونه وتسليمه وفوترته، مع مصدره الخام.
/// </summary>
public class TraceabilityService : ServiceBase, ITraceabilityService
{
    public TraceabilityService(DatesErpDbContext db, ICurrentSession session, INumberingService numbering)
        : base(db, session, numbering) { }

    public List<ProductJourneyDto> GetJourneys(int? customerId = null, int? productId = null)
    {
        List<Product> targets;
        if (productId != null)
        {
            var p = Db.Products.AsNoTracking().FirstOrDefault(x => x.Id == productId);
            targets = p == null ? new List<Product>() : new List<Product> { p };
        }
        else
        {
            // كل الأصناف ذات النشاط (استلام/تخطيط/إنتاج/تسليم) — مع تصفية العميل عبر النشاط نفسه
            var activeIds = new HashSet<int>();
            foreach (var id in Db.Lots.Where(l => customerId == null || l.CustomerId == customerId).Select(l => l.ProductId)) activeIds.Add(id);
            foreach (var id in Db.ProductionPlanItems.Where(i => customerId == null || i.CustomerId == customerId).Select(i => i.ProductId)) activeIds.Add(id);
            foreach (var id in Db.ProductionOrderItems.Where(i => customerId == null || i.CustomerId == customerId).Select(i => i.ProductId)) activeIds.Add(id);
            foreach (var id in Db.CustomerDeliveryItems
                .Join(Db.CustomerDeliveries, i => i.DeliveryId, d => d.Id, (i, d) => new { i, d })
                .Where(x => customerId == null || x.d.CustomerId == customerId).Select(x => x.i.ProductId)) activeIds.Add(id);
            targets = Db.Products.AsNoTracking().Where(p => activeIds.Contains(p.Id)).OrderBy(p => p.Id).ToList();
        }

        return targets.Select(t => BuildJourney(t, customerId)).Where(j => j.Stages.Count > 0).ToList();
    }

    public ProductJourneyDto GetLotJourney(int lotId)
    {
        var lot = Db.Lots.AsNoTracking().FirstOrDefault(l => l.Id == lotId)
                  ?? throw new DomainException("الدفعة غير موجودة.");
        var product = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);
        if (product == null) return null;
        var j = BuildJourney(product, lot.CustomerId);
        // إبراز الدفعة المطلوبة في مقدمة الرحلة
        j.Stages.Insert(0, new TraceStageDto
        {
            StageAr = "🎯 الدفعة محور التتبع",
            DocNumber = lot.LotCode,
            Date = lot.LotDate?.ToString("dd/MM/yyyy"),
            CustomerName = CustName(lot.CustomerId),
            ProductName = product.ProductNameAr,
            LotCode = lot.LotCode,
            QtyKg = lot.InitialQtyKg,
            StatusAr = $"متبقي خام: {lot.InStockQtyKg:N1} كجم",
            Detail = $"أُنتج منها: {lot.ProducedQtyKg:N1} كجم | سُلّم: {lot.DeliveredQtyKg:N1} كجم"
        });
        return j;
    }

    // ─────────────────────────── البناء الداخلي ───────────────────────────

    private ProductJourneyDto BuildJourney(Product product, int? customerId)
    {
        var j = new ProductJourneyDto
        {
            ProductId = product.Id,
            ProductName = product.ProductNameAr,
            ItemTypeAr = product.ItemType switch { "Raw" => "خام", "Finished" => "تام", "ByProduct" => "ثانوي", _ => "مساعد" },
            CustomerId = customerId,
            CustomerName = customerId != null ? CustName(customerId) : "كل العملاء"
        };

        // المنتج التام ← مصدره الخام حسب بطاقة المنتج. الخام ← المنتجات التامة المبنية عليه
        Product raw = null;
        List<Product> finishedChain = new();
        if (product.ItemType == "Finished")
        {
            j.SourceProductId = product.SourceProductId;
            j.SourceProductName = product.SourceProductId != null ? ProdName(product.SourceProductId.Value) : null;
            raw = product.SourceProductId != null ? Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == product.SourceProductId) : null;
            finishedChain.Add(product);
        }
        else if (product.ItemType == "Raw")
        {
            raw = product;
            finishedChain = Db.Products.AsNoTracking()
                .Where(p => p.SourceProductId == product.Id && p.ItemType == "Finished").ToList();
        }

        // 1) الاستلام: دفعات الخام (لصنف المصدر إن كان المنتج تاماً)
        if (raw != null)
        {
            var lots = Db.Lots.AsNoTracking()
                .Where(l => l.ProductId == raw.Id && (customerId == null || l.CustomerId == customerId))
                .OrderBy(l => l.Id).ToList();
            foreach (var l in lots)
            {
                j.ReceivedKg += l.InitialQtyKg;
                j.Stages.Add(new TraceStageDto
                {
                    StageAr = "1⃣ استلام خام",
                    DocNumber = Db.Shipments.AsNoTracking().Where(s => s.Id == l.ShipmentId).Select(s => s.DocumentNumber).FirstOrDefault() ?? "-",
                    Date = l.LotDate?.ToString("dd/MM/yyyy"),
                    CustomerName = CustName(l.CustomerId),
                    ProductName = raw.ProductNameAr,
                    LotCode = l.LotCode,
                    QtyKg = l.InitialQtyKg,
                    StatusAr = $"المتبقي خام: {l.InStockQtyKg:N1} كجم",
                    Detail = $"أُنتج منها: {l.ProducedQtyKg:N1} كجم"
                });
            }
        }

        // بقية الرحلة لكل منتج في السلسلة (المنتج نفسه، أو المنتجات التامة المبنية على الخام)
        var chain = finishedChain.Count > 0 ? finishedChain : new List<Product> { product };
        foreach (var fin in chain)
        {
            AddPlanStages(j, fin, customerId);
            AddOrderStages(j, fin, customerId);
            AddQualityStages(j, fin, customerId);
            AddStockStages(j, fin, customerId);
            AddDeliveryStages(j, fin, customerId);
        }

        j.RemainingKg = j.InStockKg;
        return j;
    }

    private void AddPlanStages(ProductJourneyDto j, Product fin, int? customerId)
    {
        var items = Db.ProductionPlanItems.AsNoTracking()
            .Where(i => i.ProductId == fin.Id && (customerId == null || i.CustomerId == customerId))
            .OrderBy(i => i.Id).ToList();
        foreach (var i in items)
        {
            j.PlannedKg += i.PlannedQtyKg;
            j.Stages.Add(new TraceStageDto
            {
                StageAr = "2⃣ خطة الإنتاج",
                DocNumber = Db.ProductionPlans.AsNoTracking().Where(p => p.Id == i.PlanId).Select(p => p.DocumentNumber).FirstOrDefault() ?? "-",
                Date = i.ScheduledDate?.ToString("dd/MM/yyyy") ?? "-",
                CustomerName = CustName(i.CustomerId),
                ProductName = fin.ProductNameAr,
                LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == i.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                QtyKg = i.PlannedQtyKg,
                Cartons = i.PlannedCartons,
                StatusAr = $"أُنتج: {i.ProducedQtyKg:N1} | قُبل: {i.AcceptedQtyKg:N1} | سُلّم: {i.DeliveredQtyKg:N1}",
                Detail = i.ExecutionStatus
            });
        }
    }

    private void AddOrderStages(ProductJourneyDto j, Product fin, int? customerId)
    {
        var items = Db.ProductionOrderItems.AsNoTracking()
            .Where(i => i.ProductId == fin.Id && (customerId == null || i.CustomerId == customerId))
            .OrderBy(i => i.Id).ToList();
        foreach (var i in items)
        {
            string orderNo = Db.ProductionOrders.AsNoTracking().Where(o => o.Id == i.OrderId).Select(o => o.DocumentNumber).FirstOrDefault() ?? "-";
            j.Stages.Add(new TraceStageDto
            {
                StageAr = "3⃣ أمر الإنتاج",
                DocNumber = orderNo,
                Date = Db.ProductionOrders.AsNoTracking().Where(o => o.Id == i.OrderId).Select(o => o.ProductionDate).FirstOrDefault()?.ToString("dd/MM/yyyy") ?? "-",
                CustomerName = CustName(i.CustomerId),
                ProductName = fin.ProductNameAr,
                LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == i.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                QtyKg = i.PlannedQtyKg,
                Cartons = i.PlannedCartons,
                StatusAr = Core.Common.DocStatuses.ToArabic(i.Status),
                Detail = $"المخطط: {i.PlannedQtyKg:N1} كجم"
            });
            if (i.ProducedQtyKg > 0)
            {
                j.ProducedKg += i.ProducedQtyKg;
                j.Stages.Add(new TraceStageDto
                {
                    StageAr = "4⃣ إنتاج فعلي",
                    DocNumber = orderNo,
                    CustomerName = CustName(i.CustomerId),
                    ProductName = fin.ProductNameAr,
                    LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == i.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                    QtyKg = i.ProducedQtyKg,
                    Cartons = i.ProducedCartons,
                    StatusAr = "تم الإنتاج",
                    Detail = $"من خام: {ProdName(Db.Lots.AsNoTracking().Where(l => l.Id == i.LotId).Select(l => l.ProductId).FirstOrDefault())}"
                });
            }
        }
    }

    private void AddQualityStages(ProductJourneyDto j, Product fin, int? customerId)
    {
        var rows = Db.QualityCheckItems.AsNoTracking()
            .Where(q => q.ProductId == fin.Id)
            .Join(Db.QualityChecks.AsNoTracking(), q => q.CheckId, c => c.Id, (q, c) => new { q, c })
            .Where(x => customerId == null
                || Db.ProductionOrders.Any(o => o.Id == x.c.OrderId && o.CustomerId == customerId))
            .OrderBy(x => x.c.Id)
            .ToList();
        foreach (var x in rows)
        {
            j.AcceptedKg += x.q.AcceptedQtyKg;
            j.Stages.Add(new TraceStageDto
            {
                StageAr = "5⃣ فحص الجودة",
                DocNumber = x.c.DocumentNumber,
                Date = x.c.CheckDate?.ToString("dd/MM/yyyy"),
                CustomerName = CustName(Db.ProductionOrders.AsNoTracking().Where(o => o.Id == x.c.OrderId).Select(o => o.CustomerId).FirstOrDefault()),
                ProductName = fin.ProductNameAr,
                LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == x.q.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                QtyKg = x.q.CheckedQtyKg,
                StatusAr = x.c.IsApproved
                    ? (x.q.RejectedQtyKg > 0 ? $"مقبول جزئياً — مرفوض {x.q.RejectedQtyKg:N1}" : "ناجح ✅")
                    : "تحت الفحص ⏳",
                Detail = $"مقبول: {x.q.AcceptedQtyKg:N1} كجم"
            });
        }
    }

    private void AddStockStages(ProductJourneyDto j, Product fin, int? customerId)
    {
        var whFg = Db.Warehouses.AsNoTracking().FirstOrDefault(w => w.WarehouseCode == "WFG")?.Id ?? 0;
        var balances = Db.StockBalances.AsNoTracking()
            .Where(b => b.WarehouseId == whFg && b.ProductId == fin.Id && (customerId == null || b.CustomerId == customerId)
                        && (b.QtyKg != 0 || b.PackageCount != 0))
            .ToList();
        foreach (var b in balances)
        {
            j.InStockKg += b.QtyKg;
            j.Stages.Add(new TraceStageDto
            {
                StageAr = "6⃣ مخزن التام",
                DocNumber = Db.Lots.AsNoTracking().Where(l => l.Id == b.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                CustomerName = CustName(b.CustomerId),
                ProductName = fin.ProductNameAr,
                LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == b.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                QtyKg = b.QtyKg,
                Cartons = b.PackageCount,
                StatusAr = "جاهز للتسليم",
                Detail = "رصيد باسم العميل والصنف — لا دمج في مخزون عام"
            });
        }
    }

    private void AddDeliveryStages(ProductJourneyDto j, Product fin, int? customerId)
    {
        var rows = Db.CustomerDeliveryItems.AsNoTracking()
            .Where(i => i.ProductId == fin.Id)
            .Join(Db.CustomerDeliveries.AsNoTracking(), i => i.DeliveryId, d => d.Id, (i, d) => new { i, d })
            .Where(x => customerId == null || x.d.CustomerId == customerId)
            .OrderBy(x => x.d.Id)
            .ToList();
        foreach (var x in rows.Where(r => r.d.IsApproved)) j.DeliveredKg += x.i.QtyKg;
        foreach (var x in rows)
        {
            // الفوترة على المسلَّم فعلياً — تُوزع على البنود تناسبياً حتى لا تُحتسب مرتين في سند متعدد الأصناف
            double invoicedShare = x.d.IsApproved && x.d.TotalQtyKg > 0
                ? x.i.QtyKg / x.d.TotalQtyKg * x.d.InvoicedQtyKg : 0;
            if (invoicedShare > 0.001) j.InvoicedKg += invoicedShare;

            j.Stages.Add(new TraceStageDto
            {
                StageAr = "7⃣ تسليم العميل",
                DocNumber = x.d.DocumentNumber,
                Date = x.d.DeliveryDate?.ToString("dd/MM/yyyy"),
                CustomerName = CustName(x.d.CustomerId),
                ProductName = fin.ProductNameAr,
                LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == x.i.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "-",
                QtyKg = x.i.QtyKg,
                Cartons = x.i.PackageCount,
                StatusAr = x.d.IsApproved ? "مسلَّم ✅" : "مسودة",
                Detail = invoicedShare > 0.001 ? $"المفوتر لهذا البند: {invoicedShare:N1} كجم" : "غير مفوتر بعد"
            });
            if (invoicedShare > 0.001)
                j.Stages.Add(new TraceStageDto
                {
                    StageAr = "8⃣ الفاتورة",
                    DocNumber = x.d.DocumentNumber,
                    Date = x.d.DeliveryDate?.ToString("dd/MM/yyyy") ?? "-",
                    CustomerName = CustName(x.d.CustomerId),
                    ProductName = fin.ProductNameAr,
                    QtyKg = Math.Round(invoicedShare, 1),
                    StatusAr = "مفوتر",
                    Detail = $"إجمالي السند: {x.d.TotalQtyKg:N1} كجم — الفوترة على المسلَّم فقط"
                });
        }
    }

    private string ProdName(int id) => Db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.ProductNameAr).FirstOrDefault() ?? "-";
    private string CustName(int? id) => id == null ? "-" : Db.Customers.AsNoTracking().Where(c => c.Id == id).Select(c => c.CustomerName).FirstOrDefault() ?? "-";
}
