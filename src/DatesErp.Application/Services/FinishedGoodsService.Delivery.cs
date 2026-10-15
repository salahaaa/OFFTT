using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// §تسليم الإنتاج للمخازن — الشاشة التشغيلية:
/// بحث أمر إنتاج له إنتاج فعلي ← سطور التام (صنف + عبوة + عميل + دفعة) بالمخطط/المنتَج/المسلَّم/المتبقي
/// ← مسودة (بلا أثر) ← اعتماد = إصدار + استلام + قيد الحركات في معاملة واحدة.
/// المتبقي يُحسب دائماً من حركات الاستلام المعتمدة (ReceivedQtyKg للسندات غير الملغاة) لا من قيمة مخزنة.
/// المخرجات الثانوية: من ExecutionByProducts للأمر، وتُقيَّد مخزنياً بصنف 003 المطابق إن وُجد.
/// </summary>
public partial class FinishedGoodsService
{
    // ───────────── مساعدات المخازن ─────────────

    /// <summary>مخزن الرأس: المحدد صراحةً إن كان نشطاً ← الافتراضي لمخازن التام ← WFG ← أول مخزن نشط.</summary>
    private int ResolveHeaderWarehouse(int? requested)
    {
        if (requested != null && Db.Warehouses.Any(w => w.Id == requested.Value && w.IsActive)) return requested.Value;
        var def = Db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive && (w.WarehouseType == "Finished" || w.WarehouseCode == "WFG"))
            .OrderBy(w => w.IsDefault ? 0 : 1).ThenBy(w => w.WarehouseCode == "WFG" ? 0 : 1).ThenBy(w => w.Id)
            .Select(w => (int?)w.Id).FirstOrDefault();
        return def ?? Db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Id).Select(w => (int?)w.Id).FirstOrDefault()
            ?? throw new DomainException("لا يوجد مخزن نشط معرَّف — أضف المخازن من شاشة «المخازن وإضافاتها» أولاً.");
    }

    private int? ValidateWarehouse(int? warehouseId)
    {
        if (warehouseId == null) return null;
        if (!Db.Warehouses.Any(w => w.Id == warehouseId.Value && w.IsActive))
            throw new DomainException($"المخزن المحدد (#{warehouseId}) غير موجود أو موقوف — اختر مخزناً نشطاً.", "NO_WAREHOUSE");
        return warehouseId;
    }

    private List<FgWarehouseOption> WarehouseOptions()
        => Db.Warehouses.AsNoTracking().Where(w => w.IsActive)
            .OrderBy(w => w.WarehouseType == "Finished" || w.WarehouseCode == "WFG" ? 0 : w.WarehouseType == "General" ? 1 : 2)
            .ThenBy(w => w.IsDefault ? 0 : 1).ThenBy(w => w.Id)
            .Select(w => new FgWarehouseOption { Id = w.Id, Name = $"{w.WarehouseNameAr} ({w.WarehouseCode})", IsDefault = w.IsDefault || w.WarehouseCode == "WFG" })
            .ToList();

    // ───────────── المخرجات الثانوية ─────────────

    private static string ByProductKind(string name)
        => !string.IsNullOrWhiteSpace(name) && (name.Contains("مخلف") || name.Contains("هالك") || name.Contains("نفاي") || name.Contains("تالف"))
            ? "مخلفات" : "منتج ثانوي";

    /// <summary>صنف المجموعة 003 المطابق للمخرج الثانوي (بالكود ثم بالاسم) — به تُقيَّد حركة المخزون.</summary>
    private int? MatchByProductProduct(ByProduct bp)
    {
        if (bp == null) return null;
        var q = Db.Products.AsNoTracking().Where(p => p.IsActive && (p.ItemType == "ByProduct" || p.GroupCode == UnitsPolicy.GroupByProduct));
        int? id = null;
        if (!string.IsNullOrWhiteSpace(bp.ByProductCode))
            id = q.Where(p => p.ProductCode == bp.ByProductCode).Select(p => (int?)p.Id).FirstOrDefault();
        id ??= q.Where(p => p.ProductNameAr == bp.ByProductNameAr).Select(p => (int?)p.Id).FirstOrDefault();
        return id;
    }

    /// <summary>كمية المخرج الثانوي المسجَّلة فعلياً على الأمر (جلسات الإقفال) — المصدر الواحد للفعلي.</summary>
    private Dictionary<int, double> ProducedByProducts(int orderId)
    {
        var exeIds = Db.ProductionExecutions.AsNoTracking().Where(e => e.OrderId == orderId).Select(e => e.Id).ToList();
        if (exeIds.Count == 0) return new();
        return Db.ExecutionByProducts.AsNoTracking().Where(b => exeIds.Contains(b.ExecutionId)).ToList()
            .GroupBy(b => b.ByProductId).ToDictionary(g => g.Key, g => (double)g.Sum(x => x.Qty));
    }

    /// <summary>المسلَّم من كل مخرج ثانوي عبر سندات التسليم المعتمدة (غير الملغاة) — باستثناء سند اختياري.</summary>
    private Dictionary<int, double> DeliveredByProducts(int orderId, int? excludeReceiptId = null)
        => Db.FinishedGoodsReceiptByProducts.AsNoTracking()
            .Join(Db.FinishedGoodsReceipts.AsNoTracking(), b => b.ReceiptId, r => r.Id, (b, r) => new { b, r })
            .Where(x => x.r.OrderId == orderId && x.r.Status != DocStatuses.Cancelled && x.r.Id != excludeReceiptId)
            .ToList().GroupBy(x => x.b.ByProductId).ToDictionary(g => g.Key, g => (double)g.Sum(x => x.b.ReceivedQty));

    /// <summary>قيد المخرجات الثانوية مخزنياً عند الاستلام (مرة واحدة — المستلَم منها يُثبَّت).</summary>
    private double PostByProducts(FinishedGoodsReceipt rcpt, string voucher)
    {
        double posted = 0;
        if (rcpt.ByProducts == null || rcpt.ByProducts.Count == 0) return 0;
        var produced = ProducedByProducts(rcpt.OrderId);
        var delivered = DeliveredByProducts(rcpt.OrderId, rcpt.Id);
        foreach (var bp in rcpt.ByProducts)
        {
            if (bp.ReceivedQty > 0 || bp.Qty <= 0) continue;
            produced.TryGetValue(bp.ByProductId, out var prod);
            delivered.TryGetValue(bp.ByProductId, out var del);
            string name = Db.ByProducts.AsNoTracking().Where(b => b.Id == bp.ByProductId).Select(b => b.ByProductNameAr).FirstOrDefault() ?? $"#{bp.ByProductId}";
            if ((double)bp.Qty > prod - del + 0.0001)
                throw new DomainException(
                    $"⛔ كمية المخرج الثانوي «{name}» ({bp.Qty:N2}) تتجاوز المتبقي الفعلي ({Math.Max(0, prod - del):N2}).\n" +
                    $"المسجَّل فعلياً: {prod:N2} | المسلَّم سابقاً: {del:N2}", "OVER_BYPRODUCT");
            int wh = bp.WarehouseId ?? rcpt.WarehouseId;
            if (bp.ProductId != null)
                PostStockMovement(wh, MovementType.Inbound, (double)bp.Qty, 0,
                    ReferenceDocType.FinishedGoodsReceipt, voucher,
                    productId: bp.ProductId, orderId: rcpt.OrderId,
                    notes: $"استلام مخرج ثانوي «{name}» — سند {rcpt.ReceiptNumber}");
            bp.ReceivedQty = bp.Qty;
            posted += (double)bp.Qty;
        }
        return posted;
    }

    private void ReverseByProducts(FinishedGoodsReceipt rcpt, string prefix)
    {
        if (rcpt.ByProducts == null) return;
        int seq = 0;
        foreach (var bp in rcpt.ByProducts.Where(b => b.ReceivedQty > 0))
        {
            seq++;
            if (bp.ProductId != null)
                PostStockMovement(bp.WarehouseId ?? rcpt.WarehouseId, MovementType.Outbound, (double)bp.ReceivedQty, 0,
                    ReferenceDocType.FinishedGoodsReceipt, $"{prefix}BP{seq}",
                    productId: bp.ProductId, orderId: rcpt.OrderId,
                    notes: $"إلغاء سند الاستلام {rcpt.ReceiptNumber} — قيد عكسي لمخرج ثانوي");
            bp.ReceivedQty = 0;
        }
    }

    // ───────────── الحسابات المركزية (مصدر واحد للمتبقي) ─────────────

    /// <summary>المسلَّم المعتمد لكل (صنف + دفعة + عميل) في الأمر — من سندات الاستلام غير الملغاة.</summary>
    private List<(int ProductId, int? LotId, int? CustomerId, double ReceivedKg, int ReceivedBoxes)> ReceivedByLine(int orderId)
        => Db.FinishedGoodsReceiptItems.AsNoTracking()
            .Join(Db.FinishedGoodsReceipts.AsNoTracking(), i => i.ReceiptId, r => r.Id, (i, r) => new { i, r })
            .Where(x => x.r.OrderId == orderId && x.r.Status != DocStatuses.Cancelled && x.i.ReceivedQtyKg > 0)
            .Select(x => new { x.i.ProductId, x.i.LotId, x.i.CustomerId, x.i.ReceivedQtyKg, x.i.NetWeightKg, x.i.PackageCount })
            .ToList()
            .GroupBy(x => new { x.ProductId, x.LotId, x.CustomerId })
            .Select(g => (g.Key.ProductId, g.Key.LotId, g.Key.CustomerId, g.Sum(x => x.ReceivedQtyKg),
                g.Sum(x => x.NetWeightKg > 0 ? (int)Math.Round(x.ReceivedQtyKg / x.NetWeightKg * x.PackageCount) : 0)))
            .ToList();

    private string PackSpec(int? packagingTypeId, int productId, double cartonWeight)
    {
        var pack = packagingTypeId != null ? Db.PackagingTypes.AsNoTracking().FirstOrDefault(p => p.Id == packagingTypeId.Value) : null;
        var prod = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == productId);
        double w = cartonWeight > 0 ? cartonWeight : pack?.UnitWeightKg ?? prod?.CartonWeightKg ?? 0;
        int molds = pack?.MoldsCount ?? prod?.MoldsCount ?? 0;
        string name = pack?.PackageNameAr ?? prod?.TradingUnit ?? "كرتون";
        return $"{name} {w:0.##} كجم" + (molds > 0 ? $" × {molds} قالب" : "");
    }

    private List<FgDeliveryLineDto> BuildOrderLines(ProductionOrder order)
    {
        var received = ReceivedByLine(order.Id);
        var lines = new List<FgDeliveryLineDto>();
        foreach (var oi in order.Items.OrderBy(i => i.Id))
        {
            int? cust = oi.CustomerId ?? order.CustomerId;
            // §المسلَّم يُطابَق على (صنف + دفعة + عميل) — سندات قديمة بلا عميل تُنسب لعميل الأمر
            double deliveredKg = received.Where(r => r.ProductId == oi.ProductId && r.LotId == oi.LotId && (r.CustomerId ?? order.CustomerId) == cust).Sum(r => r.ReceivedKg);
            lines.Add(new FgDeliveryLineDto
            {
                OrderItemId = oi.Id,
                ProductId = oi.ProductId,
                ProductName = Db.Products.AsNoTracking().Where(p => p.Id == oi.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"صنف #{oi.ProductId}",
                PackagingTypeId = oi.PackagingTypeId,
                CartonWeightKg = oi.CartonWeightKg > 0 ? oi.CartonWeightKg : UnitsPolicy.CartonWeight(Db, oi.ProductId, oi.PackagingTypeId),
                PackSpec = PackSpec(oi.PackagingTypeId, oi.ProductId, oi.CartonWeightKg),
                CustomerId = cust,
                CustomerName = cust != null ? Db.Customers.AsNoTracking().Where(c => c.Id == cust.Value).Select(c => c.CustomerName).FirstOrDefault() ?? $"عميل #{cust}" : "بدون عميل",
                LotId = oi.LotId,
                LotCode = oi.LotId != null ? Db.Lots.AsNoTracking().Where(l => l.Id == oi.LotId.Value).Select(l => l.LotCode).FirstOrDefault() ?? $"#{oi.LotId}" : "—",
                PlannedKg = oi.PlannedQtyKg,
                PlannedCartons = oi.PlannedCartons,
                ProducedKg = oi.ProducedQtyKg,
                ProducedCartons = oi.ProducedCartons,
                DeliveredKg = deliveredKg,
                RemainingKg = Math.Max(0, oi.ProducedQtyKg - deliveredKg)
            });
        }
        // §بندان لنفس (صنف + دفعة + عميل): المسلَّم يُنسب للبند الأول حتى لا يُحتسب مرتين
        foreach (var g in lines.GroupBy(l => new { l.ProductId, l.LotId, l.CustomerId }).Where(g => g.Count() > 1))
        {
            double total = g.First().DeliveredKg; double left = total;
            foreach (var l in g) { double take = Math.Min(left, l.ProducedKg); l.DeliveredKg = take; l.RemainingKg = Math.Max(0, l.ProducedKg - take); left -= take; }
        }
        return lines;
    }

    private string OrderCustomers(ProductionOrder order)
    {
        var ids = order.Items.Select(i => i.CustomerId ?? order.CustomerId).Where(c => c != null).Select(c => c.Value).Distinct().ToList();
        if (ids.Count == 0 && order.CustomerId != null) ids.Add(order.CustomerId.Value);
        if (ids.Count == 0) return "بدون عميل";
        return string.Join("، ", Db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.CustomerName).ToList());
    }

    // ───────────── واجهة الخدمة ─────────────

    public List<FgOrderSearchRow> SearchDeliverableOrders(string term, bool onlyWithRemaining)
    {
        if (Session != null && !Session.Can("finishedgoods", "View") && !Session.Can("production", "View"))
            throw new PermissionDeniedException("عرض تسليم الإنتاج");
        term = (term ?? "").Trim();
        var q = Db.ProductionOrders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.Status != DocStatuses.Cancelled && o.Items.Any(i => i.ProducedQtyKg > 0));
        if (!string.IsNullOrEmpty(term))
            q = q.Where(o => o.DocumentNumber.Contains(term)
                || Db.Customers.Any(c => c.CustomerName.Contains(term) && (o.CustomerId == c.Id || o.Items.Any(i => i.CustomerId == c.Id)))
                || Db.Products.Any(p => p.ProductNameAr.Contains(term) && o.Items.Any(i => i.ProductId == p.Id)));
        var orders = q.OrderByDescending(o => o.Id).Take(300).ToList();
        var rows = new List<FgOrderSearchRow>();
        foreach (var o in orders)
        {
            var lines = BuildOrderLines(o);
            double produced = lines.Sum(l => l.ProducedKg), delivered = lines.Sum(l => l.DeliveredKg);
            double remaining = Math.Max(0, produced - delivered);
            if (onlyWithRemaining && remaining <= 0.001) continue;
            var prodIds = o.Items.Select(i => i.ProductId).Distinct().ToList();
            rows.Add(new FgOrderSearchRow
            {
                OrderId = o.Id,
                OrderNumber = o.DocumentNumber,
                OrderDate = UiFormat.D(o.ProductionDate ?? o.CreatedDate),
                Customers = OrderCustomers(o),
                Products = string.Join("، ", Db.Products.AsNoTracking().Where(p => prodIds.Contains(p.Id)).Select(p => p.ProductNameAr).ToList()),
                PlannedKg = lines.Sum(l => l.PlannedKg),
                ProducedKg = produced,
                DeliveredKg = delivered,
                RemainingKg = remaining,
                StatusAr = remaining <= 0.001 ? "مكتمل التسليم" : delivered > 0.001 ? "مسلَّم جزئياً" : DocStatuses.ToArabic(o.Status)
            });
        }
        return rows;
    }

    public FgOrderDeliveryContext GetOrderDeliveryContext(int orderId)
    {
        if (Session != null && !Session.Can("finishedgoods", "View") && !Session.Can("production", "View"))
            throw new PermissionDeniedException("عرض تسليم الإنتاج");
        var o = Db.ProductionOrders.AsNoTracking().Include(x => x.Items).FirstOrDefault(x => x.Id == orderId);
        if (o == null) return null;
        var lines = BuildOrderLines(o);
        var ctx = new FgOrderDeliveryContext
        {
            OrderId = o.Id,
            OrderNumber = o.DocumentNumber,
            OrderDate = UiFormat.D(o.ProductionDate ?? o.CreatedDate),
            Customers = OrderCustomers(o),
            PlanNumber = o.SourcePlanId != null ? Db.ProductionPlans.AsNoTracking().Where(p => p.Id == o.SourcePlanId.Value).Select(p => p.DocumentNumber).FirstOrDefault() : null,
            Shift = o.ShiftId != null ? Db.Shifts.AsNoTracking().Where(s => s.Id == o.ShiftId.Value).Select(s => s.ShiftNameAr).FirstOrDefault() : null,
            Line = o.LineId != null ? Db.ProductionLines.AsNoTracking().Where(l => l.Id == o.LineId.Value).Select(l => l.LineNameAr).FirstOrDefault() : null,
            StatusAr = DocStatuses.ToArabic(o.Status) + (o.IsClosed ? " (مقفل)" : ""),
            HasActual = lines.Any(l => l.ProducedKg > 0),
            ProducedKg = lines.Sum(l => l.ProducedKg),
            DeliveredKg = lines.Sum(l => l.DeliveredKg),
            Lines = lines,
            Warehouses = WarehouseOptions()
        };
        ctx.RemainingKg = Math.Max(0, ctx.ProducedKg - ctx.DeliveredKg);
        var produced = ProducedByProducts(orderId);
        var delivered = DeliveredByProducts(orderId);
        var producedIds = produced.Keys.ToList();
        var defs = Db.ByProducts.AsNoTracking().Where(b => b.IsActive || producedIds.Contains(b.Id)).OrderBy(b => b.Id).ToList();
        foreach (var d in defs)
        {
            ctx.ByProductOptions.Add(new FgByProductOption { Id = d.Id, Name = d.ByProductNameAr, Unit = d.UnitOfMeasure ?? "كجم", KindAr = ByProductKind(d.ByProductNameAr) });
            produced.TryGetValue(d.Id, out var prod); delivered.TryGetValue(d.Id, out var del);
            if (prod <= 0 && del <= 0) continue;
            ctx.ByProducts.Add(new FgByProductLineDto
            {
                ByProductId = d.Id, Name = d.ByProductNameAr, KindAr = ByProductKind(d.ByProductNameAr), Unit = d.UnitOfMeasure ?? "كجم",
                ProductId = MatchByProductProduct(d), ProducedQty = prod, DeliveredQty = del, RemainingQty = Math.Max(0, prod - del)
            });
        }
        return ctx;
    }

    public OpResult SaveDraft(FgDeliveryDraftDto draft, int? receiptId)
    {
        Require("finishedgoods", "Create");
        if (draft == null) return OpResult.Fail("لا توجد بيانات للحفظ.");
        var items = (draft.Items ?? new()).Where(i => i.NetWeightKg > 0.0001).ToList();
        var bps = (draft.ByProducts ?? new()).Where(b => b.Qty > 0.0001).ToList();
        if (items.Count == 0 && bps.Count == 0) return OpResult.Fail("أدخل كمية تسليم لبند واحد على الأقل (إنتاج تام أو مخرج ثانوي).");
        var order = Db.ProductionOrders.AsNoTracking().Include(o => o.Items).FirstOrDefault(o => o.Id == draft.OrderId);
        if (order == null) return OpResult.Fail("أمر الإنتاج غير موجود.");
        if (order.Status == DocStatuses.Cancelled) return OpResult.Fail("أمر الإنتاج ملغى — لا يُسلَّم منه.");
        if (!order.Items.Any(i => i.ProducedQtyKg > 0) && items.Count > 0)
            return OpResult.Fail("لا يوجد إنتاج فعلي مسجَّل على هذا الأمر — سجِّل الفعلي (إقفال اليوم) أولاً ثم سلِّم.");
        foreach (var it in items)
            if (it.WarehouseId == null) return OpResult.Fail("حدد مخزن الإدخال لكل بند تسليم — لا مخزن افتراضي إلزامي.");
        foreach (var b in bps)
            if (b.WarehouseId == null) return OpResult.Fail("حدد مخزن الاستلام لكل مخرج ثانوي.");

        // §التحقق المبكر من السقوف (الرسالة نفسها تظهر عند الاعتماد أيضاً — مصدر واحد للحساب)
        var lines = BuildOrderLines(order);
        foreach (var g in items.GroupBy(i => new { i.ProductId, i.LotId, i.CustomerId }))
        {
            var line = lines.Where(l => l.ProductId == g.Key.ProductId && l.LotId == g.Key.LotId && l.CustomerId == g.Key.CustomerId).ToList();
            if (line.Count == 0) return OpResult.Fail("أحد البنود لا يطابق بنود أمر الإنتاج (صنف/دفعة/عميل).");
            double remaining = line.Sum(l => l.RemainingKg);
            double asked = g.Sum(i => i.NetWeightKg);
            if (asked > remaining + 0.001)
                return OpResult.Fail($"⛔ كمية التسليم ({asked:N1} كجم) للصنف «{line[0].ProductName}» — {line[0].CustomerName} تتجاوز المتبقي القابل للتسليم ({remaining:N1} كجم).\nالمنتَج فعلياً: {line.Sum(l => l.ProducedKg):N1} | سبق تسليمه: {line.Sum(l => l.DeliveredKg):N1}");
        }
        var producedBp = ProducedByProducts(order.Id); var deliveredBp = DeliveredByProducts(order.Id, receiptId);
        foreach (var b in bps)
        {
            var def = Db.ByProducts.AsNoTracking().FirstOrDefault(x => x.Id == b.ByProductId);
            if (def == null) return OpResult.Fail("المخرج الثانوي غير معرَّف.");
            producedBp.TryGetValue(b.ByProductId, out var prod); deliveredBp.TryGetValue(b.ByProductId, out var del);
            if (b.Qty > prod - del + 0.0001)
                return OpResult.Fail($"⛔ كمية «{def.ByProductNameAr}» ({b.Qty:N2}) تتجاوز المتبقي الفعلي ({Math.Max(0, prod - del):N2}) — المخرجات تُسجَّل في إقفال اليوم، لا من هنا.");
        }

        if (receiptId == null || receiptId == 0)
        {
            // §إنشاء: عبر SaveReceipt (كل حراس الوحدات والهوية والسقف) ثم إلحاق المخرجات الثانوية
            foreach (var it in items)
            {
                var src = order.Items.FirstOrDefault(i => i.ProductId == it.ProductId && i.LotId == it.LotId && (i.CustomerId ?? order.CustomerId) == it.CustomerId)
                          ?? order.Items.First(i => i.ProductId == it.ProductId);
                it.PackagingTypeId ??= src.PackagingTypeId;
                if (it.PackageCount <= 0 && src.CartonWeightKg > 0) it.PackageCount = (int)Math.Round(it.NetWeightKg / src.CartonWeightKg);
            }
            int? qcId = Db.QualityChecks.AsNoTracking().Where(c => c.OrderId == order.Id).OrderByDescending(c => c.IsApproved).ThenByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefault();
            OpResult created;
            if (items.Count > 0)
            {
                // §تعدد العملاء على نفس الدفعة: SaveReceipt يرفض (صنف+دفعة) مكرراً — يُجمَّع هنا لعميل واحد لكل سند
                var dup = items.GroupBy(i => new { i.ProductId, i.LotId }).FirstOrDefault(g => g.Select(x => x.CustomerId).Distinct().Count() > 1);
                if (dup != null) return OpResult.Fail("نفس الصنف والدفعة لعميلين مختلفين لا يُجمعان في سند واحد — سلِّم كل عميل في سند مستقل.");
                created = SaveReceipt(order.Id, qcId, draft.DeliveryDate, items, null, items[0].WarehouseId);
                if (!created.Ok) return created;
            }
            else
            {
                // §سند مخرجات ثانوية فقط (بلا إنتاج تام) — يُنشأ مباشرة
                created = RunOp(() =>
                {
                    var r = new FinishedGoodsReceipt
                    {
                        DocumentNumber = Numbering.Next("FGR"), OrderId = order.Id, QualityCheckId = qcId,
                        DeliveryDate = UiFormat.TryParseDate(draft.DeliveryDate, out var d) ? d : DateTime.Now,
                        WarehouseId = ResolveHeaderWarehouse(bps[0].WarehouseId), Status = DocStatuses.Draft, ReceiptStatus = "None", CreatedBy = Session?.UserId
                    };
                    Db.FinishedGoodsReceipts.Add(r); Db.SaveChanges();
                    return OpResult.Success("تم إنشاء سند مخرجات ثانوية.", r.Id, r.DocumentNumber);
                });
                if (!created.Ok) return created;
            }
            return RunOp(() =>
            {
                var r = Db.FinishedGoodsReceipts.Include(x => x.ByProducts).First(x => x.Id == created.Id);
                r.Notes = draft.Notes?.Trim();
                r.CreatedBy ??= Session?.UserId;
                foreach (var b in bps)
                    r.ByProducts.Add(new FinishedGoodsReceiptByProduct
                    {
                        ByProductId = b.ByProductId, ProductId = MatchByProductProduct(Db.ByProducts.AsNoTracking().First(x => x.Id == b.ByProductId)),
                        Qty = (decimal)Math.Round(b.Qty, 4), WarehouseId = ValidateWarehouse(b.WarehouseId), Notes = b.Notes?.Trim()
                    });
                Db.SaveChanges();
                return OpResult.Success($"تم حفظ مسودة سند التسليم {r.DocumentNumber} — لا أثر على الأرصدة حتى الاعتماد.", r.Id, r.DocumentNumber);
            });
        }

        // §تعديل مسودة قائمة: استبدال البنود بنفس الحراس — المعتمد يُرفض
        var rcpt = Db.FinishedGoodsReceipts.Include(x => x.Items).Include(x => x.ByProducts).FirstOrDefault(x => x.Id == receiptId.Value);
        if (rcpt == null) return OpResult.Fail("السند غير موجود.");
        if (rcpt.IsApproved || rcpt.ReceiptStatus is "Partial" or "Full") return OpResult.Fail("السند معتمد — لا يُعدَّل؛ استخدم «تراجع/إلغاء الاعتماد» (قيد عكسي موثق) ثم عدِّل.");
        if (rcpt.Status == DocStatuses.Cancelled) return OpResult.Fail("السند ملغى.");
        if (rcpt.OrderId != order.Id) return OpResult.Fail("لا يمكن تغيير أمر الإنتاج لسند محفوظ — أنشئ سنداً جديداً.");
        return RunOp(() =>
        {
            Db.FinishedGoodsReceiptItems.RemoveRange(rcpt.Items); rcpt.Items.Clear();
            Db.FinishedGoodsReceiptByProducts.RemoveRange(rcpt.ByProducts); rcpt.ByProducts.Clear();
            rcpt.DeliveryDate = UiFormat.TryParseDate(draft.DeliveryDate, out var d) ? d : rcpt.DeliveryDate;
            rcpt.Notes = draft.Notes?.Trim();
            rcpt.ModifiedBy = Session?.UserId; rcpt.ModifiedDate = DateTime.Now;
            if (items.Count > 0) rcpt.WarehouseId = ResolveHeaderWarehouse(items[0].WarehouseId);
            foreach (var it in items)
            {
                UnitsPolicy.RequireItemType(Db, it.ProductId, "Finished", "تسليم الإنتاج للمخازن");
                var src = order.Items.FirstOrDefault(i => i.ProductId == it.ProductId && i.LotId == it.LotId && (i.CustomerId ?? order.CustomerId) == it.CustomerId)
                          ?? order.Items.First(i => i.ProductId == it.ProductId);
                it.PackagingTypeId ??= src.PackagingTypeId;
                if (it.PackageCount <= 0 && src.CartonWeightKg > 0) it.PackageCount = (int)Math.Round(it.NetWeightKg / src.CartonWeightKg);
                it.NetWeightKg = UnitsPolicy.EnsureCartonKgConsistency(Db, it.ProductId, it.PackagingTypeId, it.NetWeightKg, it.PackageCount, "تسليم الإنتاج للمخازن");
                rcpt.Items.Add(new FinishedGoodsReceiptItem
                {
                    ProductId = it.ProductId, LotId = it.LotId, CustomerId = it.CustomerId, PackagingTypeId = it.PackagingTypeId,
                    PackageCount = it.PackageCount, NetWeightKg = it.NetWeightKg, ReceivedQtyKg = 0,
                    CartonWeightKg = UnitsPolicy.CartonWeight(Db, it.ProductId, it.PackagingTypeId), WarehouseId = ValidateWarehouse(it.WarehouseId)
                });
            }
            foreach (var b in bps)
                rcpt.ByProducts.Add(new FinishedGoodsReceiptByProduct
                {
                    ByProductId = b.ByProductId, ProductId = MatchByProductProduct(Db.ByProducts.AsNoTracking().First(x => x.Id == b.ByProductId)),
                    Qty = (decimal)Math.Round(b.Qty, 4), WarehouseId = ValidateWarehouse(b.WarehouseId), Notes = b.Notes?.Trim()
                });
            Db.SaveChanges();
            return OpResult.Success($"تم تحديث مسودة السند {rcpt.DocumentNumber}.", rcpt.Id, rcpt.DocumentNumber);
        });
    }

    public OpResult Approve(int receiptId)
    {
        Require("finishedgoods", "Approve");
        var rcpt = Db.FinishedGoodsReceipts.AsNoTracking().FirstOrDefault(r => r.Id == receiptId);
        if (rcpt == null) return OpResult.Fail("السند غير موجود.");
        if (rcpt.Status == DocStatuses.Cancelled) return OpResult.Fail("السند ملغى — لا يُعتمد.");
        // §منع الاعتماد المزدوج: معتمد/مستلم كلياً يُرفض صراحةً (النقر المتكرر لا ينشئ حركات)
        if (rcpt.IsApproved || rcpt.ReceiptStatus == "Full") return OpResult.Fail($"السند {rcpt.DocumentNumber} معتمد مسبقاً — لا يُعاد اعتماده.");
        if (rcpt.ReceiptStatus == "Partial") return OpResult.Fail("السند مستلم جزئياً — استكمل الاستلام من «استلام المتبقي».");
        // §عملية واحدة متسقة: الإصدار + الاستلام + الحركات تحت معاملة واحدة (فشل أي خطوة = لا شيء)
        return RunOp(() =>
        {
            var issued = Db.FinishedGoodsReceipts.First(r => r.Id == receiptId);
            if (issued.Status == DocStatuses.Draft) { issued.Status = DocStatuses.Issued; Db.SaveChanges(); }
            var inner = new FinishedGoodsService(Db, Session, Numbering) { JoinParentTransaction = true };
            var r = inner.Receive(receiptId, null);
            if (!r.Ok) throw new DomainException(r.Message);
            return OpResult.Success("تم اعتماد التسليم وقيد الحركات المخزنية: " + r.Message, receiptId, r.DocumentNumber);
        });
    }

    public OpResult CancelDraft(int receiptId)
    {
        Require("finishedgoods", "Cancel");
        var rcpt = Db.FinishedGoodsReceipts.Include(r => r.Items).FirstOrDefault(r => r.Id == receiptId);
        if (rcpt == null) return OpResult.Fail("السند غير موجود.");
        if (rcpt.Status == DocStatuses.Cancelled) return OpResult.Fail("السند ملغى مسبقاً.");
        if (rcpt.IsApproved || rcpt.Items.Any(i => i.ReceivedQtyKg > 0))
            return OpResult.Fail("السند معتمد وله حركات — استخدم «إلغاء الاعتماد» (قيد عكسي) بدل الإلغاء.");
        return RunOp(() =>
        {
            rcpt.Status = DocStatuses.Cancelled;
            rcpt.ModifiedBy = Session?.UserId; rcpt.ModifiedDate = DateTime.Now;
            Db.SaveChanges();
            return OpResult.Success($"تم إلغاء السند {rcpt.DocumentNumber} (لم يكن له أي حركة مخزنية).");
        });
    }

    public FgReceiptCard GetReceipt(int receiptId)
    {
        var d = Db.FinishedGoodsReceipts.AsNoTracking().Include(x => x.Items).Include(x => x.ByProducts).FirstOrDefault(x => x.Id == receiptId);
        if (d == null) return null;
        var order = Db.ProductionOrders.AsNoTracking().FirstOrDefault(o => o.Id == d.OrderId);
        int? ordCust = order?.CustomerId;
        string User(int? id) => id == null ? null : Db.Users.AsNoTracking().Where(u => u.Id == id.Value).Select(u => u.UserName).FirstOrDefault();
        var card = new FgReceiptCard
        {
            Id = d.Id, DocumentNumber = d.DocumentNumber, ReceiptNumber = d.ReceiptNumber, OrderId = d.OrderId, OrderNumber = order?.DocumentNumber,
            DeliveryDate = UiFormat.D(d.DeliveryDate), Status = d.Status, ReceiptStatus = d.ReceiptStatus, Notes = d.Notes, IsApproved = d.IsApproved,
            StatusAr = d.Status == DocStatuses.Cancelled ? "ملغى" : d.ReceiptStatus == "Full" ? "مكتمل التسليم" : d.ReceiptStatus == "Partial" ? "مسلَّم جزئياً" : d.IsApproved ? "معتمد" : d.Status == DocStatuses.Issued ? "مُصدر (بانتظار الاعتماد)" : "مسودة",
            ApprovedBy = User(d.ApprovedBy), ApprovedDate = d.ApprovedDate != null ? UiFormat.D(d.ApprovedDate) : null,
            CreatedBy = User(d.CreatedBy), CreatedDate = UiFormat.D(d.CreatedDate)
        };
        string Wh(int? id) => Db.Warehouses.AsNoTracking().Where(w => w.Id == (id ?? d.WarehouseId)).Select(w => w.WarehouseNameAr).FirstOrDefault() ?? "—";
        foreach (var i in d.Items.OrderBy(i => i.Id))
        {
            int wh = i.WarehouseId ?? d.WarehouseId;
            double? balance = i.ReceivedQtyKg > 0
                ? Db.StockBalances.AsNoTracking().Where(b => b.WarehouseId == wh && b.ProductId == i.ProductId && b.LotId == i.LotId && b.PackagingTypeId == i.PackagingTypeId
                    && b.CustomerId == (i.CustomerId ?? ordCust)).Select(b => (double?)b.QtyKg).FirstOrDefault()
                : null;
            card.Lines.Add(new FgReceiptLineRow
            {
                ItemId = i.Id, ProductId = i.ProductId,
                ProductName = Db.Products.AsNoTracking().Where(p => p.Id == i.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{i.ProductId}",
                PackagingTypeId = i.PackagingTypeId, PackSpec = PackSpec(i.PackagingTypeId, i.ProductId, i.CartonWeightKg),
                LotId = i.LotId, LotCode = i.LotId != null ? Db.Lots.AsNoTracking().Where(l => l.Id == i.LotId.Value).Select(l => l.LotCode).FirstOrDefault() : "—",
                CustomerId = i.CustomerId ?? order?.CustomerId,
                CustomerName = (i.CustomerId ?? order?.CustomerId) is int c ? Db.Customers.AsNoTracking().Where(x => x.Id == c).Select(x => x.CustomerName).FirstOrDefault() : "بدون عميل",
                PackageCount = i.PackageCount, NetWeightKg = i.NetWeightKg, ReceivedQtyKg = i.ReceivedQtyKg,
                WarehouseId = wh, WarehouseName = Wh(wh), BalanceAfter = balance
            });
        }
        foreach (var b in d.ByProducts.OrderBy(b => b.Id))
        {
            var def = Db.ByProducts.AsNoTracking().FirstOrDefault(x => x.Id == b.ByProductId);
            card.ByProducts.Add(new FgReceiptByProductRow
            {
                Id = b.Id, ByProductId = b.ByProductId, Name = def?.ByProductNameAr ?? $"#{b.ByProductId}", KindAr = ByProductKind(def?.ByProductNameAr),
                Unit = def?.UnitOfMeasure ?? "كجم", Qty = (double)b.Qty, ReceivedQty = (double)b.ReceivedQty,
                WarehouseId = b.WarehouseId ?? d.WarehouseId, WarehouseName = Wh(b.WarehouseId), Notes = b.Notes, PostsStock = b.ProductId != null
            });
        }
        return card;
    }

    public List<FgReceiptCard> SearchReceipts(string term)
    {
        term = (term ?? "").Trim();
        var q = Db.FinishedGoodsReceipts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(term))
            q = q.Where(r => r.DocumentNumber.Contains(term) || (r.ReceiptNumber != null && r.ReceiptNumber.Contains(term))
                || Db.ProductionOrders.Any(o => o.Id == r.OrderId && o.DocumentNumber.Contains(term)));
        return q.OrderByDescending(r => r.Id).Take(200).Select(r => r.Id).ToList().Select(GetReceipt).Where(c => c != null).ToList();
    }
}
