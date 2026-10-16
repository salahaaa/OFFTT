using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Exceptions;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// محرك قاعدة تحويل المنتج التام إلى الخام.
/// لا يقبل هذا المحرك رقماً خاماً من واجهة المستخدم؛ الكمية الوحيدة التي يدخلها
/// المستخدم هي كمية المنتج التام، ثم تُحسب كمية الخام من تعريف رسمي.
/// </summary>
public sealed class RawRequirementResult
{
    public bool IsConfigured { get; init; }
    public string ErrorCode { get; init; }
    public string Error { get; init; }
    public int FinishedProductId { get; init; }
    public int? RawProductId { get; init; }
    public decimal FinishedQtyKg { get; init; }
    public decimal RawKgPerFinishedKg { get; init; }
    public decimal RawRequiredQtyKg { get; init; }
    public IReadOnlyList<int> AvailableRawProductIds { get; init; } = Array.Empty<int>();

    public static RawRequirementResult Failure(int finishedProductId, decimal finishedQtyKg, string code, string error,
        IReadOnlyList<int> rawIds = null)
        => new()
        {
            FinishedProductId = finishedProductId,
            FinishedQtyKg = finishedQtyKg,
            ErrorCode = code,
            Error = error,
            AvailableRawProductIds = rawIds ?? Array.Empty<int>()
        };
}

/// <summary>مصدر خام رسمي مع معامل محسوب بصيغة كجم خام لكل كجم تام.</summary>
public sealed class RawSourceRule
{
    public int RawProductId { get; init; }
    public int? PackagingTypeId { get; init; }
    public decimal RawKgPerFinishedKg { get; init; }
}

public static class RawRequirementEngine
{
    /// <summary>
    /// يعيد المصادر الفعالة. ProductRawSource هو النموذج الجديد متعدد المصادر؛
    /// Product.SourceProductId/YieldFactor هو مسار قراءة توافقياً للبيانات القديمة.
    /// YieldFactor الحالي معرف في بطاقة الصنف بصيغة الخارج/الداخل، لذلك يُعكس هنا.
    /// </summary>
    public static List<RawSourceRule> GetRules(DatesErpDbContext db, int finishedProductId, int? packagingTypeId = null)
    {
        var explicitRules = db.ProductRawSources.AsNoTracking()
            .Where(x => x.FinishedProductId == finishedProductId && x.IsActive && x.RawKgPerFinishedKg > 0)
            .ToList();
        if (explicitRules.Count > 0)
        {
            var exact = packagingTypeId is int pack
                ? explicitRules.Where(x => x.PackagingTypeId == pack).ToList()
                : new List<ProductRawSource>();
            var generic = explicitRules.Where(x => x.PackagingTypeId == null).ToList();
            var selected = packagingTypeId == null
                ? explicitRules
                : exact.Count > 0 ? exact : generic;
            return selected
                .GroupBy(x => x.RawProductId)
                .Select(g =>
                {
                    var row = g.OrderBy(x => x.PriorityNo).ThenBy(x => x.Id).First();
                    return new RawSourceRule
                    {
                        RawProductId = row.RawProductId,
                        PackagingTypeId = row.PackagingTypeId,
                        RawKgPerFinishedKg = row.RawKgPerFinishedKg
                    };
                })
                .ToList();
        }

        var product = db.Products.AsNoTracking().FirstOrDefault(x => x.Id == finishedProductId);
        if (product?.SourceProductId is not int rawId || product.YieldFactor is not double yield
            || !double.IsFinite(yield) || yield <= 0)
            return new List<RawSourceRule>();

        return new List<RawSourceRule>
        {
            new RawSourceRule
            {
                RawProductId = rawId,
                RawKgPerFinishedKg = 1m / (decimal)yield
            }
        };
    }

    public static RawRequirementResult Calculate(DatesErpDbContext db, int finishedProductId,
        double finishedQtyKg, int? selectedRawProductId = null, int? packagingTypeId = null)
    {
        if (!double.IsFinite(finishedQtyKg) || finishedQtyKg <= 0)
            return RawRequirementResult.Failure(finishedProductId, 0, "FINISHED_QTY_REQUIRED", "كمية المنتج التام يجب أن تكون أكبر من صفر.");

        var product = db.Products.AsNoTracking().FirstOrDefault(x => x.Id == finishedProductId);
        if (product == null || !product.IsActive || product.ItemType != "Finished")
            return RawRequirementResult.Failure(finishedProductId, (decimal)finishedQtyKg, "FINISHED_PRODUCT_REQUIRED", "الصنف التام غير موجود أو غير نشط.");

        var rules = GetRules(db, finishedProductId, packagingTypeId);
        var rawIds = rules.Select(x => x.RawProductId).Distinct().ToList();
        if (rules.Count == 0)
            return RawRequirementResult.Failure(
                finishedProductId, (decimal)finishedQtyKg, "NO_RAW_FORMULA",
                $"لا توجد قاعدة تحويل رسمية للصنف التام «{product.ProductNameAr}». عرّف مصدر الخام ومعامل التحويل قبل التخطيط.");

        RawSourceRule rule;
        if (selectedRawProductId is int selected)
        {
            rule = rules.FirstOrDefault(x => x.RawProductId == selected);
            if (rule == null)
                return RawRequirementResult.Failure(
                    finishedProductId, (decimal)finishedQtyKg, "RAW_SOURCE_NOT_ALLOWED",
                    $"الصنف الخام المحدد لا يملك قاعدة تحويل رسمية للصنف التام «{product.ProductNameAr}». اختر مصدراً مسموحاً.", rawIds);
        }
        else if (rules.Count == 1)
        {
            rule = rules[0];
        }
        else
        {
            return RawRequirementResult.Failure(
                finishedProductId, (decimal)finishedQtyKg, "RAW_SOURCE_REQUIRED",
                $"الصنف التام «{product.ProductNameAr}» له أكثر من مصدر خام مستقل. حدد المصدر الخام لكل تخصيص.", rawIds);
        }

        var raw = db.Products.AsNoTracking().FirstOrDefault(x => x.Id == rule.RawProductId);
        if (raw == null || !raw.IsActive || raw.ItemType != "Raw")
            return RawRequirementResult.Failure(finishedProductId, (decimal)finishedQtyKg, "RAW_SOURCE_INVALID", "مصدر الخام في قاعدة التحويل غير موجود أو غير نشط.", rawIds);
        if (rule.RawKgPerFinishedKg <= 0)
            return RawRequirementResult.Failure(finishedProductId, (decimal)finishedQtyKg, "RAW_FACTOR_INVALID", "معامل التحويل الرسمي يجب أن يكون أكبر من صفر.", rawIds);

        var finished = (decimal)finishedQtyKg;
        var required = decimal.Round(finished * rule.RawKgPerFinishedKg, 4, MidpointRounding.AwayFromZero);
        return new RawRequirementResult
        {
            IsConfigured = true,
            FinishedProductId = finishedProductId,
            RawProductId = rule.RawProductId,
            FinishedQtyKg = finished,
            RawKgPerFinishedKg = rule.RawKgPerFinishedKg,
            RawRequiredQtyKg = required,
            AvailableRawProductIds = rawIds
        };
    }

    public static void EnsureConfigured(DatesErpDbContext db, int finishedProductId, double finishedQtyKg,
        int? selectedRawProductId = null, int? packagingTypeId = null)
    {
        var result = Calculate(db, finishedProductId, finishedQtyKg, selectedRawProductId, packagingTypeId);
        if (!result.IsConfigured) throw new DomainException(result.Error, result.ErrorCode);
    }
}

/// <summary>حالة خام واحدة على مفتاح المخزن/الدفعة كما يراها التخطيط.</summary>
public sealed class RawAvailabilitySnapshot
{
    public int WarehouseId { get; init; }
    public int LotId { get; init; }
    public double ActualStockKg { get; init; }
    public double PlannedReservationKg { get; init; }
    public double OtherCommitmentsKg { get; init; }
    public double NotReadyKg { get; init; }
    public double AvailableForPlanningKg { get; init; }
    public double NewPlanNeedKg { get; init; }
    public double RemainingAfterPlanningKg => Math.Max(0, AvailableForPlanningKg - NewPlanNeedKg);
    public bool IsSufficient => NewPlanNeedKg <= AvailableForPlanningKg + 0.001;
}

/// <summary>
/// دفتر الالتزامات الخام المركزي. كل خدمة تخطيط/أمر تستخدم نفس الحساب:
/// الحركات/StockBalance = فعلي، تخصيصات الخطط = حجز تخطيطي، والأمر المستقل = التزام آخر.
/// أمر ناتج من خطة لا يُجمع مرة ثانية لأنه مغطى بحجز تخصيص خطته.
/// </summary>
public sealed class RawPlanningAvailabilityEngine
{
    private readonly DatesErpDbContext _db;

    /// <summary>توافق للخطط القديمة بلا تخصيص: يوزع الخام المتبقي بنسبة إنتاج التام.</summary>
    public static double LegacyRawRemainingKg(ProductionPlanItem item)
    {
        if (item == null || item.RawRequiredQtyKg <= 0) return 0;
        if (item.PlannedQtyKg <= 0) return (double)item.RawRequiredQtyKg;
        var ratio = Math.Max(0, Math.Min(1, item.ProducedQtyKg / item.PlannedQtyKg));
        return Math.Max(0, (double)item.RawRequiredQtyKg * (1 - ratio));
    }

    public static double LegacyRawRemainingKg(DatesErpDbContext db, ProductionPlanItem item)
    {
        if (item.RawRequiredQtyKg > 0) return LegacyRawRemainingKg(item);
        int? rawId = item.LotId is int lotId
            ? db.Lots.AsNoTracking().Where(x => x.Id == lotId).Select(x => (int?)x.ProductId).FirstOrDefault()
            : item.SelectedRawProductId;
        var result = RawRequirementEngine.Calculate(db, item.ProductId, item.PlannedQtyKg, rawId, item.PackagingTypeId);
        if (!result.IsConfigured) return 0;
        var ratio = item.PlannedQtyKg > 0 ? Math.Max(0, Math.Min(1, item.ProducedQtyKg / item.PlannedQtyKg)) : 0;
        return Math.Max(0, (double)result.RawRequiredQtyKg * (1 - ratio));
    }
    public RawPlanningAvailabilityEngine(DatesErpDbContext db) => _db = db;

    private double StandaloneRawRemaining(ProductionOrderItem item)
    {
        if (item == null || item.PlannedQtyKg <= 0) return 0;
        int? rawId = item.LotId is int lotId
            ? _db.Lots.AsNoTracking().Where(x => x.Id == lotId).Select(x => (int?)x.ProductId).FirstOrDefault()
            : null;
        var result = RawRequirementEngine.Calculate(_db, item.ProductId, item.PlannedQtyKg, rawId, item.PackagingTypeId);
        if (!result.IsConfigured) return 0;
        var ratio = Math.Max(0, Math.Min(1, item.ProducedQtyKg / item.PlannedQtyKg));
        return Math.Max(0, (double)result.RawRequiredQtyKg * (1 - ratio));
    }

    private bool IsTreatmentReadyBy(Lot lot, DateTime date)
    {
        var receiptUntil = _db.ShipmentItems.AsNoTracking()
            .Where(x => x.Id == lot.ShipmentItemId && x.TreatmentRequired == true)
            .Select(x => (DateTime?)x.TreatmentUntilDate).FirstOrDefault();
        var activeReady = _db.RawTreatments.AsNoTracking()
            .Where(x => x.LotId == lot.Id && x.Status == TreatmentStatuses.InProgress)
            .Select(x => (DateTime?)x.ExpectedReadyAt)
            .ToList();
        var readyAt = receiptUntil;
        if (activeReady.Count > 0)
        {
            var latest = activeReady.Max();
            if (!readyAt.HasValue || latest > readyAt) readyAt = latest;
        }
        return lot.UnderTreatmentQtyKg <= 0
            || (readyAt.HasValue && date.Date >= readyAt.Value.Date);
    }

    public RawAvailabilitySnapshot ForLot(int warehouseId, int lotId, double newPlanNeedKg = 0,
        int? excludePlanId = null, int? excludeOrderId = null, DateTime? asOfDate = null)
    {
        var lot = _db.Lots.AsNoTracking().FirstOrDefault(x => x.Id == lotId);
        if (lot == null) return new RawAvailabilitySnapshot { WarehouseId = warehouseId, LotId = lotId };

        var actual = _db.StockBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.LotId == lotId)
            .Sum(x => (double?)x.QtyKg) ?? 0;
        var receiptWarehouse = _db.Shipments.AsNoTracking().Where(x => x.Id == lot.ShipmentId)
            .Select(x => x.ReceivingWarehouseId).FirstOrDefault();
        var hasAnyBalance = _db.StockBalances.AsNoTracking().Any(x => x.LotId == lotId);
        // بيانات ما قبل StockBalance: لا نخترع مخزناً آخر، ونستخدم دفتر الدفعة فقط
        // عندما لا توجد أي أرصدة موزعة ويكون المفتاح هو مخزن الاستلام.
        if (!hasAnyBalance && actual <= 0 && receiptWarehouse == warehouseId) actual = lot.InStockQtyKg;

        var planRows = _db.ProductionPlanItemAllocations.AsNoTracking()
            .Where(a => a.WarehouseId == warehouseId && a.LotId == lotId && a.Status != "Released" && a.Status != "Consumed")
            .Join(_db.ProductionPlanItems.AsNoTracking(), a => a.PlanItemId, i => i.Id, (a, i) => new { a, i })
            .Join(_db.ProductionPlans.AsNoTracking(), x => x.i.PlanId, p => p.Id, (x, p) => new { x.a, x.i, p })
            .Where(x => x.p.Status != DocStatuses.Closed && x.p.Status != DocStatuses.Cancelled && !x.p.IsClosed && !x.i.IsClosed)
            .Where(x => excludePlanId == null || x.i.PlanId != excludePlanId)
            .AsEnumerable()
            .Sum(x => Math.Max(0, x.a.AllocatedQtyKg - x.a.ConsumedQtyKg - x.a.ReleasedQtyKg));

        // Fallback للخطط التاريخية التي لم تُنشئ صف تخصيص. لا نضيفه للبند الذي لديه
        // تخصيص، وبذلك لا تُحسب مرآة Lot.ReservedQtyKg مع التخصيص مرتين.
        var allocationItemIds = _db.ProductionPlanItemAllocations.AsNoTracking()
            .Select(x => x.PlanItemId).ToHashSet();
        var legacyPlanRows = _db.ProductionPlanItems.AsNoTracking()
            .Where(i => i.LotId == lotId && !allocationItemIds.Contains(i.Id) && !i.IsClosed)
            .Join(_db.ProductionPlans.AsNoTracking(), i => i.PlanId, p => p.Id, (i, p) => new { i, p })
            .Where(x => x.p.Status != DocStatuses.Closed && x.p.Status != DocStatuses.Cancelled && !x.p.IsClosed
                && (excludePlanId == null || x.i.PlanId != excludePlanId))
            .ToList()
            .Sum(x => LegacyRawRemainingKg(_db, x.i));
        var planned = planRows + legacyPlanRows;

        // فقط الأوامر المستقلة التزام آخر. أوامر الخطة لها حجز في التخصيصات ولا تكرر.
        var standaloneOrderItems = _db.ProductionOrderItems.AsNoTracking()
            .Where(i => i.LotId == lotId && i.PlanItemId == null && !i.IsClosed)
            .Join(_db.ProductionOrders.AsNoTracking(), i => i.OrderId, o => o.Id, (i, o) => new { i, o })
            .Where(x => x.o.Status != DocStatuses.Cancelled && x.o.Status != DocStatuses.Closed
                && (excludeOrderId == null || x.i.OrderId != excludeOrderId))
            .ToList();
        var standaloneIds = standaloneOrderItems.Select(x => x.i.Id).ToHashSet();
        var allOrderAllocationRows = _db.Set<ProductionOrderItemAllocation>().AsNoTracking()
            .Where(a => standaloneIds.Contains(a.OrderItemId) && a.LotId == lotId)
            .ToList();
        var orderAllocationRows = allOrderAllocationRows
            .Where(a => a.WarehouseId == warehouseId).ToList();
        var allocatedOrderItemIds = allOrderAllocationRows.Select(a => a.OrderItemId).ToHashSet();
        double other = orderAllocationRows.Sum(a => Math.Max(0, a.AllocatedQtyKg - a.ConsumedQtyKg))
            + standaloneOrderItems
                .Where(x => !allocatedOrderItemIds.Contains(x.i.Id))
                .Sum(x => StandaloneRawRemaining(x.i));

        var treatmentWarehouse = _db.Warehouses.AsNoTracking()
            .Where(x => x.WarehouseCode == "WTRT").Select(x => (int?)x.Id).FirstOrDefault();
        var isTreatmentLocation = receiptWarehouse == warehouseId || treatmentWarehouse == warehouseId;
        bool readyByDate = asOfDate.HasValue && IsTreatmentReadyBy(lot, asOfDate.Value);
        var notReady = isTreatmentLocation && !readyByDate
            ? Math.Min(Math.Max(0, actual), Math.Max(0, lot.UnderTreatmentQtyKg))
            : 0;
        var available = Math.Max(0, actual - planned - other - notReady);
        return new RawAvailabilitySnapshot
        {
            WarehouseId = warehouseId,
            LotId = lotId,
            ActualStockKg = Math.Max(0, actual),
            PlannedReservationKg = Math.Max(0, planned),
            OtherCommitmentsKg = Math.Max(0, other),
            NotReadyKg = notReady,
            AvailableForPlanningKg = available,
            NewPlanNeedKg = Math.Max(0, newPlanNeedKg)
        };
    }

    public List<RawAvailabilitySnapshot> ForLotAllWarehouses(int lotId, double newPlanNeedKg = 0,
        int? excludePlanId = null, int? excludeOrderId = null, DateTime? asOfDate = null)
    {
        var warehouseIds = _db.StockBalances.AsNoTracking().Where(x => x.LotId == lotId).Select(x => x.WarehouseId).Distinct().ToList();
        var receiptWarehouse = _db.Lots.Where(x => x.Id == lotId).Join(_db.Shipments, l => l.ShipmentId, s => s.Id, (l, s) => s.ReceivingWarehouseId).FirstOrDefault();
        if (receiptWarehouse is int receipt && !warehouseIds.Contains(receipt)) warehouseIds.Add(receipt);
        return warehouseIds.Select(w => ForLot(w, lotId, newPlanNeedKg, excludePlanId, excludeOrderId, asOfDate)).ToList();
    }
}
