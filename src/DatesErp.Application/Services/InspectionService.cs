using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DatesErp.Application.Services;

// ═══════════════════════════════════════════════════════════════
// §الفحص الديناميكي — أنواع نتائج قابلة للتعريف، وحدات من القاموس، وربط كامل بالأمر
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// §خدمة الفحص الديناميكي — المصدر الوحيد لأنواع النتائج والوحدات والإجماليات.
/// لا يوجد هنا اسم نتيجة مثبّت ولا وحدة مثبّتة: كل شيء من الجداول.
/// </summary>
public class InspectionService : ServiceBase, IInspectionService
{
    public InspectionService(DatesErpDbContext db, ICurrentSession session, INumberingService numbering)
        : base(db, session, numbering) { }

    // ─────────────────────── أنواع النتائج (الإعدادات) ───────────────────────

    /// <summary>§3 — تعريف/تعديل نوع نتيجة فحص من الإعدادات (لا تعديل كود لإضافة نتيجة جديدة).</summary>
    public OpResult SaveResultType(int? id, string code, string nameAr, string resultKind,
        int? unitId, bool isFinishedGood, bool isByProduct, bool entersInventory,
        bool countsAsLoss, int sortNo, bool isActive, string notes = null, bool isFinalScrap = false)
    {
        Require("products", "Edit");
        if (string.IsNullOrWhiteSpace(nameAr)) return OpResult.Fail("اسم نوع النتيجة مطلوب.");

        return RunOp(() =>
        {
            // §التصنيف يُطبع داخل RunOp حتى يعود خطأً عربياً في OpResult لا استثناءً
            resultKind = NormalizeKind(resultKind);
            var t = id == null ? new InspectionResultType() : Db.InspectionResultTypes.FirstOrDefault(x => x.Id == id);
            if (id != null && t == null) throw new DomainException("نوع النتيجة غير موجود.");
            if (Db.InspectionResultTypes.Any(x => x.NameAr == nameAr && x.Id != t.Id))
                throw new DomainException($"يوجد نوع نتيجة بالاسم «{nameAr}» — اختر اسماً آخر.");

            // §4 — الوحدة من القاموس: لا نقبل وحدة غير معرفة، ولا نثبّت وحدة في الكود
            string unitLabel = null;
            if (unitId != null)
            {
                var u = Db.UnitsOfMeasure.AsNoTracking().FirstOrDefault(x => x.Id == unitId && x.IsActive);
                if (u == null) throw new DomainException("الوحدة المختارة غير موجودة في قاموس الوحدات أو موقوفة — عرّفها أولاً من نافذة الوحدات.");
                unitLabel = u.UnitNameAr;
            }

            t.Code = string.IsNullOrWhiteSpace(code) ? $"RT-{DateTime.Now:HHmmssfff}" : code.Trim();
            t.NameAr = nameAr.Trim();
            t.ResultKind = resultKind;
            // §B95 — درجة الرفض تُحفظ للأنواع المرفوضة فقط (غير مطابق/مرفوض نهائي) — المقبول دائماً مطابق
            t.IsFinalScrap = resultKind == InspectionResultType.KindRejected && isFinalScrap;
            t.UnitId = unitId;
            t.UnitLabel = unitLabel;
            t.IsFinishedGood = isFinishedGood;
            t.IsByProduct = isByProduct;
            t.EntersInventory = entersInventory;
            t.CountsAsLoss = countsAsLoss;
            t.SortNo = sortNo;
            t.IsActive = isActive;
            t.Notes = notes;
            if (id == null) Db.InspectionResultTypes.Add(t);
            Db.SaveChanges();
            return OpResult.Success($"تم حفظ نوع النتيجة «{t.NameAr}» ({InspectionResultType.KindNameAr(t.ResultKind)}) — سيظهر في استمارة الفحص.", t.Id, t.Code);
        });
    }

    /// <summary>كل أنواع النتائج النشطة مرتبة.</summary>
    public List<AllowedResultType> GetResultTypes(bool includeInactive = false)
    {
        var q = Db.InspectionResultTypes.AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return q.OrderBy(t => t.SortNo).ThenBy(t => t.Id).ToList().Select(ToAllowed).ToList();
    }

    /// <summary>
    /// §7 — نتائج الفحص المسموحة لصنف معيّن بوحدتها المعتمدة.
    /// الأولوية: ملف الصنف ← ملف المجموعة ← الأنواع العامة النشطة.
    /// </summary>
    public List<AllowedResultType> GetAllowedResultTypesForItem(int? productId, string groupCode = null)
    {
        if (groupCode == null && productId != null)
            groupCode = Db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.GroupCode).FirstOrDefault();

        var profiles = Db.ItemInspectionProfiles.AsNoTracking().Where(p => p.IsActive).ToList();
        var types = Db.InspectionResultTypes.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.SortNo).ThenBy(t => t.Id).ToList();

        // ملف خاص بالصنف
        var itemProfiles = productId != null ? profiles.Where(p => p.ProductId == productId).ToList() : new List<ItemInspectionProfile>();
        // ملف المجموعة (بلا صنف محدد)
        var groupProfiles = groupCode != null
            ? profiles.Where(p => p.ProductId == null && p.GroupCode == groupCode).ToList()
            : new List<ItemInspectionProfile>();

        var result = new List<AllowedResultType>();
        var used = new HashSet<int>();

        foreach (var set in new[] { itemProfiles, groupProfiles })
        {
            foreach (var pr in set.OrderBy(p => p.SortNo).ThenBy(p => p.Id))
            {
                var t = types.FirstOrDefault(x => x.Id == pr.ResultTypeId);
                if (t == null || used.Contains(t.Id)) continue;
                used.Add(t.Id);
                var a = ToAllowed(t);
                if (pr.UnitId != null)
                {
                    var u = Db.UnitsOfMeasure.AsNoTracking().FirstOrDefault(x => x.Id == pr.UnitId);
                    if (u != null) { a.UnitId = u.Id; a.UnitLabel = u.UnitNameAr; }
                }
                a.DefaultQty = (double)pr.DefaultQty;
                a.IsMandatory = pr.IsMandatory;
                a.SortNo = pr.SortNo;
                result.Add(a);
            }
        }

        // ما لم يُخصّص: كل الأنواع النشطة المتاحة للجميع
        foreach (var t in types.Where(t => !used.Contains(t.Id))) result.Add(ToAllowed(t));
        return result.OrderBy(a => a.SortNo).ThenBy(a => a.ResultTypeId).ToList();
    }

    /// <summary>§7 — تحديد نتائج الفحص المسموحة لصنف (إنشاء/تحديث).</summary>
    public OpResult SetProfile(int? id, int? productId, string groupCode, int resultTypeId,
        int? unitId, decimal defaultQty, bool isMandatory, int sortNo, bool isActive)
    {
        Require("products", "Edit");
        return RunOp(() =>
        {
            if (!Db.InspectionResultTypes.Any(t => t.Id == resultTypeId))
                throw new DomainException("نوع النتيجة غير موجود — عرّفه أولاً من «أنواع نتائج الفحص».");
            if (productId == null && string.IsNullOrWhiteSpace(groupCode))
                throw new DomainException("حدّد الصنف أو مجموعة الأصناف.");

            var p = id == null ? new ItemInspectionProfile() : Db.ItemInspectionProfiles.FirstOrDefault(x => x.Id == id);
            if (id != null && p == null) throw new DomainException("التعريف غير موجود.");
            if (Db.ItemInspectionProfiles.Any(x => x.Id != p.Id && x.ResultTypeId == resultTypeId
                    && x.ProductId == productId && x.GroupCode == groupCode))
                throw new DomainException("هذا النوع معرَّف مسبقاً لهذا الصنف/المجموعة.");

            p.ProductId = productId;
            p.GroupCode = groupCode;
            p.ResultTypeId = resultTypeId;
            p.UnitId = unitId;
            p.DefaultQty = defaultQty;
            p.IsMandatory = isMandatory;
            p.SortNo = sortNo;
            p.IsActive = isActive;
            if (id == null) Db.ItemInspectionProfiles.Add(p);
            Db.SaveChanges();
            return OpResult.Success("تم حفظ تعريف نتائج الفحص للصنف.", p.Id);
        });
    }

    /// <summary>تحويل وحدات معرَّف — بدونه لا يُجمع مقداران بوحدتين مختلفتين.</summary>
    public OpResult SaveConversion(int? id, int fromUnitId, int toUnitId, decimal factor, bool isActive)
    {
        Require("products", "Edit");
        if (fromUnitId == toUnitId) return OpResult.Fail("وحدة التحويل المصدر والهدف متطابقتان.");
        if (factor <= 0) return OpResult.Fail("معامل التحويل يجب أن يكون أكبر من صفر.");
        return RunOp(() =>
        {
            var c = id == null ? new UnitConversion() : Db.UnitConversions.FirstOrDefault(x => x.Id == id);
            if (id != null && c == null) throw new DomainException("التحويل غير موجود.");
            c.FromUnitId = fromUnitId;
            c.ToUnitId = toUnitId;
            c.Factor = factor;
            c.IsActive = isActive;
            if (id == null) Db.UnitConversions.Add(c);
            Db.SaveChanges();
            return OpResult.Success($"تم حفظ التحويل: 1 {UnitName(fromUnitId)} = {factor:0.####} {UnitName(toUnitId)}.", c.Id);
        });
    }

    // ─────────────────────── سياق الأمر (رأس الشاشة) ───────────────────────

    /// <summary>
    /// §1 — بيانات الفحص تُجلب آلياً من أمر الإنتاج: رقم الأمر، الخطة، العميل، الصنف الخام،
    /// المنتج التام، الكمية المنتجة، الوحدة، التاريخ، الوردية، خط الإنتاج — بلا إعادة إدخال.
    /// </summary>
    public InspectionOrderContext GetOrderContext(int orderId)
    {
        var o = Db.ProductionOrders.AsNoTracking().Include(x => x.Items).FirstOrDefault(x => x.Id == orderId);
        if (o == null) throw new DomainException("أمر الإنتاج غير موجود.");

        var ctx = new InspectionOrderContext
        {
            OrderId = o.Id,
            OrderNo = o.DocumentNumber,
            CustomerId = o.CustomerId,
            CustomerName = Db.Customers.AsNoTracking().Where(c => c.Id == o.CustomerId).Select(c => c.CustomerName).FirstOrDefault(),
            Date = (o.ProductionDate ?? DateTime.Today).ToString(Core.Common.UiFormat.DatePattern),
            ShiftName = Db.Shifts.AsNoTracking().Where(s => s.Id == o.ShiftId).Select(s => s.ShiftNameAr).FirstOrDefault(),
            LineName = Db.ProductionLines.AsNoTracking().Where(l => l.Id == o.LineId).Select(l => l.LineNameAr).FirstOrDefault(),
            // §B95 — رأس محضر فحص الإنتاج التام: تاريخ الإنتاج + الكراتين المنتجة (وحدة التام الأساسية)
            ProductionDate = (o.ProductionDate ?? DateTime.Today).ToString(Core.Common.UiFormat.DatePattern),
            ProducedCartons = o.Items.Sum(i => i.ProducedCartons),
        };

        // §1 — الخطة من الأمر مباشرة (SourcePlanId) أو عبر بند الخطة المرتبط
        int? planId = o.SourcePlanId;
        if (planId == null)
        {
            var planItemId = o.Items.Select(i => i.PlanItemId).FirstOrDefault(x => x != null);
            if (planItemId != null)
                planId = Db.ProductionPlanItems.AsNoTracking().Where(i => i.Id == planItemId).Select(i => i.PlanId).FirstOrDefault();
        }
        if (planId != null)
            ctx.PlanNo = Db.ProductionPlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.DocumentNumber).FirstOrDefault();

        foreach (var it in o.Items)
        {
            var prod = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == it.ProductId);
            var lot = it.LotId != null ? Db.Lots.AsNoTracking().FirstOrDefault(l => l.Id == it.LotId) : null;
            double qty = it.ProducedQtyKg > 0 ? it.ProducedQtyKg : it.PlannedQtyKg;
            ctx.Items.Add((it.ProductId, prod?.ProductNameAr ?? "-", it.LotId, lot?.LotCode, it.CustomerId, qty));

            if (prod != null && prod.ItemType == "Finished" && ctx.FinishedProductId == null)
            {
                ctx.FinishedProductId = prod.Id;
                ctx.FinishedProductName = prod.ProductNameAr;
                ctx.FinishedProductCode = prod.ProductCode;
                ctx.ProducedQtyKg = qty;
                ctx.ProducedQty = qty;
                ctx.ProducedUnitLabel = prod.TradingUnit ?? prod.UnitOfMeasure;
                // §5 — الصنف الخام من التعريف الرسمي في بطاقة المنتج (لا تخمين)
                if (prod.SourceProductId != null)
                {
                    var raw = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == prod.SourceProductId);
                    ctx.RawItemName = raw?.ProductNameAr;
                    ctx.RawItemCode = raw?.ProductCode;
                }
            }
            if (lot != null && ctx.LotCode == null) { ctx.LotCode = lot.LotCode; ctx.LotId = lot.Id; }
            if (ctx.RawItemName == null && lot != null)
            {
                var lotProd = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == lot.ProductId);
                if (lotProd != null && lotProd.ItemType == "Raw") { ctx.RawItemName = lotProd.ProductNameAr; ctx.RawItemCode = lotProd.ProductCode; }
            }
        }
        if (ctx.CustomerName == null && ctx.Items.Count > 0 && ctx.Items[0].CustomerId != null)
            ctx.CustomerName = Db.Customers.AsNoTracking().Where(c => c.Id == ctx.Items[0].CustomerId).Select(c => c.CustomerName).FirstOrDefault();
        return ctx;
    }

    // ─────────────────────── التحقق والحساب ───────────────────────

    /// <summary>
    /// §8 — التحقق من النتائج قبل الحفظ: النوع معرَّف ونشط، الوحدة معرفة ومسموحة لهذا النوع،
    /// الكمية غير سالبة، والأنواع الإجبارية للصنف مُدخلة.
    /// </summary>
    public void ValidateResults(List<InspectionResultDto> results, int? orderId = null, int? productId = null)
    {
        if (results == null || results.Count == 0) throw new DomainException("أدخل نتيجة فحص واحدة على الأقل.");

        var types = Db.InspectionResultTypes.AsNoTracking().ToDictionary(t => t.Id);
        var units = Db.UnitsOfMeasure.AsNoTracking().ToDictionary(u => u.Id);
        var orderProducts = orderId != null
            ? Db.ProductionOrderItems.AsNoTracking().Where(i => i.OrderId == orderId).Select(i => i.ProductId).Distinct().ToHashSet()
            : null;

        foreach (var r in results)
        {
            if (!types.TryGetValue(r.ResultTypeId, out var t))
                throw new DomainException($"نوع النتيجة #{r.ResultTypeId} غير معرَّف في الإعدادات.");
            if (!t.IsActive)
                throw new DomainException($"نوع النتيجة «{t.NameAr}» موقوف — فعّله من الإعدادات أو اختر غيره.");
            if (r.Qty < 0)
                throw new DomainException($"كمية «{t.NameAr}» لا يمكن أن تكون سالبة.");

            // §4 — الوحدة من القاموس وقواعد النوع
            int? unitId = r.UnitId ?? t.UnitId;
            if (unitId != null)
            {
                if (!units.TryGetValue(unitId.Value, out var u))
                    throw new DomainException($"الوحدة المحددة لـ«{t.NameAr}» غير موجودة في قاموس الوحدات.");
                if (!u.IsActive)
                    throw new DomainException($"الوحدة «{u.UnitNameAr}» موقوفة في قاموس الوحدات — لا يمكن استخدامها لـ«{t.NameAr}».");
            }
            else if (r.Qty > 0)
            {
                throw new DomainException(
                    $"«{t.NameAr}» بلا وحدة معتمدة — حدّد الوحدة في تعريف نوع النتيجة من الإعدادات، أو اختر وحدة في الشاشة.");
            }

            // §5 — الربط: لا نتيجة لصنف خارج أمر الإنتاج
            if (orderProducts != null && r.ProductId != null && !orderProducts.Contains(r.ProductId.Value))
            {
                string name = Db.Products.AsNoTracking().Where(p => p.Id == r.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{r.ProductId}";
                throw new DomainException($"الصنف «{name}» ليس من بنود أمر الإنتاج — الفحص يحافظ على ربط النتائج بالأمر.");
            }
        }

        // الأنواع الإجبارية لهذا الصنف
        if (productId != null)
        {
            var mandatory = Db.ItemInspectionProfiles.AsNoTracking()
                .Where(p => p.IsActive && p.IsMandatory && p.ProductId == productId).Select(p => p.ResultTypeId).ToList();
            foreach (var m in mandatory.Where(m => !results.Any(r => r.ResultTypeId == m && r.Qty > 0)))
            {
                string name = types.TryGetValue(m, out var mt) ? mt.NameAr : $"#{m}";
                throw new DomainException($"النتيجة «{name}» إجبارية لهذا الصنف حسب تعريفه — أدخل كميتها (ولو صفراً).");
            }
        }
    }

    /// <summary>
    /// §6 — الحسابات: إجمالي المفحوص/التام/غير المطابق/المخرجات الثانوية/الفاقد والنسب.
    /// لا يجمع وحدات مختلفة في إجمالي واحد: كل وحدة إجمالي مستقل، والنسب تُحسب داخل الوحدة.
    /// </summary>
    public InspectionTotals Compute(List<InspectionResultDto> results)
    {
        var totals = new InspectionTotals();
        if (results == null || results.Count == 0) return totals;

        var types = Db.InspectionResultTypes.AsNoTracking().ToDictionary(t => t.Id);
        var units = Db.UnitsOfMeasure.AsNoTracking().ToDictionary(u => u.Id);

        var byUnit = new Dictionary<int?, UnitTotal>();
        var byType = new Dictionary<int, (string name, double qty, string unit)>();

        foreach (var r in results)
        {
            if (!types.TryGetValue(r.ResultTypeId, out var t)) continue;
            double qty = (double)r.Qty;
            int? unitId = r.UnitId ?? t.UnitId;
            string unitLabel = unitId != null && units.TryGetValue(unitId.Value, out var u) ? u.UnitNameAr : t.UnitLabel ?? "—";

            if (!byUnit.TryGetValue(unitId, out var ut))
                byUnit[unitId] = ut = new UnitTotal { UnitId = unitId, UnitLabel = unitLabel };

            ut.Checked += qty;
            switch (t.ResultKind)
            {
                case InspectionResultType.KindAccepted: ut.Accepted += qty; break;
                // §B95 — المرفوض ينقسم: غير مطابق (قابل للمعالجة) ومرفوض نهائي — مجموعهما يبقى في Rejected
                case InspectionResultType.KindRejected:
                    ut.Rejected += qty;
                    if (t.IsFinalScrap) ut.Scrap += qty; else ut.NonConforming += qty;
                    break;
                case InspectionResultType.KindByProduct: ut.ByProduct += qty; break;
                case InspectionResultType.KindLoss: ut.Loss += qty; break;
            }
            if (t.CountsAsLoss && t.ResultKind != InspectionResultType.KindLoss) ut.Loss += qty;

            if (!byType.TryGetValue(t.Id, out var agg)) byType[t.Id] = (t.NameAr, 0, unitLabel);
            byType[t.Id] = (agg.name, agg.qty + qty, unitLabel);
        }

        totals.ByUnit = byUnit.Values.OrderBy(x => x.UnitLabel).ToList();
        totals.ByResultType = byType.Select(kv => (kv.Key, kv.Value.name, kv.Value.qty, kv.Value.unit)).ToList();

        totals.TotalChecked = totals.ByUnit.Sum(x => x.Checked);
        totals.TotalAccepted = totals.ByUnit.Sum(x => x.Accepted);
        totals.TotalRejected = totals.ByUnit.Sum(x => x.Rejected);
        totals.TotalNonConforming = totals.ByUnit.Sum(x => x.NonConforming);
        totals.TotalScrap = totals.ByUnit.Sum(x => x.Scrap);
        totals.TotalByProduct = totals.ByUnit.Sum(x => x.ByProduct);
        totals.TotalLoss = totals.ByUnit.Sum(x => x.Loss);

        totals.SingleUnit = totals.ByUnit.Count <= 1;
        if (totals.SingleUnit && totals.ByUnit.Count == 1)
        {
            var u0 = totals.ByUnit[0];
            totals.PrimaryUnitLabel = u0.UnitLabel;
            totals.AcceptancePct = Pct(u0.Accepted, u0.Checked);
            totals.ByProductPct = Pct(u0.ByProduct, u0.Checked);
            totals.LossPct = Pct(u0.Loss, u0.Checked);
        }
        else if (totals.ByUnit.Count > 1)
        {
            totals.Warnings.Add(
                $"الكميات مسجّلة بـ{totals.ByUnit.Count} وحدات مختلفة ({string.Join("، ", totals.ByUnit.Select(x => x.UnitLabel))}) — " +
                "الإجماليات والنسب معروضة لكل وحدة على حدة ولا تُجمع. عرّف تحويل وحدات إن أردت إجماليّاً موحّداً.");
        }
        return totals;
    }

    /// <summary>
    /// §6 — إجمالي موحّد بوحدة هدف باستخدام التحويلات المعرَّفة فقط.
    /// إن وُجدت كمية بوحدات لا تحويل لها ← تُرفض العملية برسالة صريحة (لا تخمين).
    /// </summary>
    public double? ComputeConvertedTotal(List<InspectionResultDto> results, int toUnitId, out string failureReason)
    {
        failureReason = null;
        decimal total = 0;
        var types = Db.InspectionResultTypes.AsNoTracking().ToDictionary(t => t.Id);
        var convs = Db.UnitConversions.AsNoTracking().Where(c => c.IsActive && c.ToUnitId == toUnitId).ToList();

        foreach (var r in results)
        {
            int? unitId = r.UnitId ?? (types.TryGetValue(r.ResultTypeId, out var t) ? t.UnitId : null);
            if (unitId == null) { failureReason = $"«{(types.TryGetValue(r.ResultTypeId, out var t2) ? t2.NameAr : "-")}» بلا وحدة معتمدة."; return null; }
            if (unitId == toUnitId) { total += (decimal)r.Qty; continue; }
            var c = convs.FirstOrDefault(x => x.FromUnitId == unitId);
            if (c == null)
            {
                failureReason = $"لا يوجد تحويل معرَّف من «{UnitName(unitId.Value)}» إلى «{UnitName(toUnitId)}» — عرّفه من نافذة الوحدات.";
                return null;
            }
            total += (decimal)r.Qty * c.Factor;
        }
        return (double)Math.Round(total, 3);
    }

    // ─────────────────────── §B95: ملخص نتيجة فحص الإنتاج التام ───────────────────────

    /// <summary>
    /// §B95 — ملخص نتيجة فحص الإنتاج التام بوحدة الصنف (الكرتون عادة):
    /// مطابق + غير مطابق + مرفوض = المنتَج، والنسب تُحسب تلقائياً من المنتَج.
    /// المخرجات الثانوية والفاقد خارج المعادلة (وحدتها كجم — تُعرض مستقلة).
    /// لا يُجمع إلا بنفس الوحدة أو عبر تحويل معرَّف — وغير القابل للتحويل يُستبعد مع تنبيه.
    /// </summary>
    public GradeSummary ComputeGradeSummary(List<InspectionResultDto> results, int? orderId, int? productId)
    {
        var s = new GradeSummary();
        var grades = new[] { InspectionResultType.GradeConforming, InspectionResultType.GradeNonConforming, InspectionResultType.GradeScrap };
        foreach (var g in grades)
            s.Rows.Add(new GradeSummaryRow { Grade = g, GradeAr = InspectionResultType.GradeNameAr(g) });

        var types = Db.InspectionResultTypes.AsNoTracking().ToDictionary(t => t.Id);
        var units = Db.UnitsOfMeasure.AsNoTracking().ToList();

        // وحدة الملخص: وحدة الصنف التجارية (من بطاقة الصنف) — وإلا وحدة أول نتيجة
        int? unitId = null;
        var product = productId != null ? Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == productId) : null;
        string wantUnit = product?.TradingUnit ?? product?.UnitOfMeasure;
        if (!string.IsNullOrWhiteSpace(wantUnit))
            unitId = units.FirstOrDefault(u => u.UnitNameAr == wantUnit)?.Id;
        if (unitId == null && results != null && results.Count > 0)
        {
            var first = results.FirstOrDefault(r => r.UnitId != null);
            unitId = first?.UnitId;
            if (unitId == null && types.TryGetValue(results[0].ResultTypeId, out var t0)) unitId = t0.UnitId;
        }
        s.UnitId = unitId;
        s.UnitLabel = unitId != null ? units.FirstOrDefault(u => u.Id == unitId)?.UnitNameAr ?? "—" : "—";

        // المنتَج بوحدة الملخص
        double? produced = null;
        if (orderId != null)
        {
            var items = Db.ProductionOrderItems.AsNoTracking().Where(i => i.OrderId == orderId).ToList();
            if (productId != null) items = items.Where(i => i.ProductId == productId).ToList();
            double producedKg = items.Sum(i => i.ProducedQtyKg);
            int producedCtn = items.Sum(i => i.ProducedCartons);
            if (s.UnitLabel == UnitsPolicy.UnitCarton)
            {
                if (producedCtn > 0) produced = producedCtn;
                else if (producedKg > 0 && product != null)
                {
                    double w = UnitsPolicy.CartonWeight(Db, product.Id, null);
                    if (w > 0) produced = Math.Round(producedKg / w, 3);
                    else s.Warnings.Add("الإنتاج مسجل وزناً فقط بلا كراتين وبلا وزن كرتون معرَّف في بطاقة الصنف — عرّفه لحساب النسب من المنتَج.");
                }
            }
            else if (s.UnitLabel == UnitsPolicy.UnitKg) produced = producedKg;
            else s.Warnings.Add($"وحدة الملخص «{s.UnitLabel}» ليست كرتوناً ولا كجم — النسب تُحسب من إجمالي النتائج.");
        }
        s.ProducedQty = produced;

        // تجميع الدرجات (المقبول + المرفوض فقط — الثانوي والفاقد خارج معادلة التام)
        var notesByGrade = grades.ToDictionary(g => g, _ => new List<string>());
        if (results != null)
        {
            foreach (var r in results)
            {
                if (!types.TryGetValue(r.ResultTypeId, out var t)) continue;
                string grade = InspectionResultType.GradeOf(t.ResultKind, t.IsFinalScrap);
                if (grade == null) continue; // مخرج ثانوي/فاقد — خارج المعادلة
                int? from = r.UnitId ?? t.UnitId;
                double qty = r.Qty;
                if (unitId != null && from != unitId)
                {
                    if (from == null || !TryConvert(qty, from.Value, unitId.Value, out qty))
                    {
                        s.Warnings.Add($"«{t.NameAr}» بوحدة مختلفة بلا تحويل معرَّف — استُبعدت من ملخص الدرجات.");
                        continue;
                    }
                }
                s.Rows.First(x => x.Grade == grade).Qty += qty;
                if (!string.IsNullOrWhiteSpace(r.Notes) && !notesByGrade[grade].Contains(r.Notes.Trim()))
                    notesByGrade[grade].Add(r.Notes.Trim());
            }
        }
        foreach (var row in s.Rows)
        {
            row.Qty = Math.Round(row.Qty, 3);
            row.Notes = string.Join(" ؛ ", notesByGrade[row.Grade]);
        }
        s.TotalQty = Math.Round(s.Rows.Sum(x => x.Qty), 3);

        double basis = produced ?? s.TotalQty; // بلا مرجع إنتاج: النسب من الإجمالي
        if (basis > 0)
        {
            foreach (var row in s.Rows) row.PctOfProduced = Math.Round(row.Qty / basis * 100.0, 2);
            s.TotalPct = Math.Round(s.TotalQty / basis * 100.0, 2);
        }
        double tol = produced == null ? 0 : Math.Max(0.01, Math.Abs(produced.Value) * 0.0001);
        s.Balanced = produced == null || Math.Abs(s.TotalQty - produced.Value) <= tol;
        return s;
    }

    /// <summary>
    /// §B95 — التحقق الإجباري ضد الإنتاج: الأمر موجود، إنتاج مسجل، أصناف تامة (002) فقط،
    /// لا تجاوز للمنتَج (كراتين إن سُجلت + كيلو) — الثانوي والفاقد خارج المعادلة.
    /// </summary>
    public void ValidateAgainstProduction(List<InspectionResultDto> results, int orderId, int? productId = null)
    {
        var order = Db.ProductionOrders.AsNoTracking().Include(x => x.Items).FirstOrDefault(x => x.Id == orderId)
            ?? throw new DomainException("أمر التشغيل غير موجود — تحقق من رقم الأمر.");
        if (order.Items.Sum(i => i.ProducedQtyKg) <= 0)
            throw new DomainException($"لا يوجد إنتاج مسجل لأمر التشغيل {order.DocumentNumber} — لا يمكن الفحص قبل تسجيل الإنتاج.");

        if (results == null || results.Count == 0) return; // تُفحص في ValidateResults
        var types = Db.InspectionResultTypes.AsNoTracking().ToDictionary(t => t.Id);
        var units = Db.UnitsOfMeasure.AsNoTracking().ToDictionary(u => u.Id);
        int? ctnId = units.Values.FirstOrDefault(u => u.UnitNameAr == UnitsPolicy.UnitCarton)?.Id;

        // §B95 — فحص الإنتاج التام للمنتجات التامة (002) فقط — لا خام ولا ثانوي
        var products = results.Where(r => r.ProductId != null).Select(r => r.ProductId.Value)
            .Concat(productId != null ? new[] { productId.Value } : Enumerable.Empty<int>()).Distinct().ToList();
        foreach (var pid in products)
            UnitsPolicy.RequireItemType(Db, pid, "Finished", "نتيجة فحص الإنتاج التام");

        // سقف المنتَج لكل صنف
        foreach (var g in results.Where(r => r.Qty > 0).GroupBy(r => r.ProductId))
        {
            if (g.Key == null) continue;
            var oItems = order.Items.Where(i => i.ProductId == g.Key.Value).ToList();
            if (oItems.Count == 0) continue; // تُفحص في ValidateResults (صنف خارج الأمر)
            double producedKg = oItems.Sum(i => i.ProducedQtyKg);
            int producedCtn = oItems.Sum(i => i.ProducedCartons);
            string pname = Db.Products.AsNoTracking().Where(p => p.Id == g.Key.Value).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{g.Key}";

            double checkedCtn = 0, checkedKg = 0;
            bool hasCtn = false, hasKg = false;
            foreach (var r in g)
            {
                if (!types.TryGetValue(r.ResultTypeId, out var t)) continue;
                if (t.ResultKind != InspectionResultType.KindAccepted && t.ResultKind != InspectionResultType.KindRejected) continue;
                int? u = r.UnitId ?? t.UnitId;
                if (u == ctnId) { checkedCtn += r.Qty; hasCtn = true; }
                else if (u != null && units.TryGetValue(u.Value, out var uu) && uu.UnitNameAr == UnitsPolicy.UnitKg) { checkedKg += r.Qty; hasKg = true; }
            }
            if (hasCtn && producedCtn > 0 && checkedCtn > producedCtn + 0.001)
                throw new DomainException($"⛔ نتيجة الفحص للصنف «{pname}» ({checkedCtn:N0} كرتون) تتجاوز الكمية المنتجة ({producedCtn:N0} كرتون).");
            if (hasKg && checkedKg > producedKg + 0.01)
                throw new DomainException($"⛔ نتيجة الفحص للصنف «{pname}» ({checkedKg:N1} كجم) تتجاوز الكمية المنتجة ({producedKg:N1} كجم).");
        }
    }

    /// <summary>تحويل كمية عبر تحويل معرَّف فقط — false إن لم يوجد (لا تخمين).</summary>
    private bool TryConvert(double qty, int fromUnitId, int toUnitId, out double converted)
    {
        converted = qty;
        if (fromUnitId == toUnitId) return true;
        var f = Db.UnitConversions.AsNoTracking()
            .Where(c => c.IsActive && c.FromUnitId == fromUnitId && c.ToUnitId == toUnitId)
            .Select(c => (decimal?)c.Factor).FirstOrDefault();
        if (f == null || f <= 0) return false;
        converted = (double)Math.Round((decimal)qty * f.Value, 3);
        return true;
    }

    /// <summary>اسم وحدة من القاموس — للرسائل.</summary>
    // ═══ §v1.50.28: التدفق المعتمد — الفحص ينزل مباشرة من تسليم الإنتاج ═══

    public List<QualitySourceDto> GetDeliverySources()
    {
        Require("quality", "View");
        // مصدر الجودة هو التنفيذ الفعلي المكتمل فقط. نختار آخر تنفيذ مقفل لكل أمر
        // حتى لا تظهر جلسة قديمة مرتين، ونحصر البنود ذات الإنتاج الفعلي فقط.
        var executionRows = Db.ProductionExecutions.AsNoTracking()
            .Where(e => e.IsDayClosed)
            .OrderByDescending(e => e.Id)
            .ToList();
        var executions = executionRows
            .GroupBy(e => e.OrderId)
            .ToDictionary(g => g.Key, g => g.First());
        var orderIds = executions.Keys.ToList();
        if (orderIds.Count == 0) return new List<QualitySourceDto>();

        var orders = Db.ProductionOrders.AsNoTracking().Include(o => o.Items)
            .Where(o => orderIds.Contains(o.Id) && o.Status != DocStatuses.Cancelled)
            .ToList();
        var executionIds = executions.Values.Select(e => e.Id).ToList();
        var checks = Db.QualityChecks.AsNoTracking()
            .Where(c => c.ExecutionId != null && executionIds.Contains(c.ExecutionId.Value))
            .ToList()
            .GroupBy(c => c.ExecutionId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).First());

        // تحميل الهوية دفعة واحدة: فتح شاشة الجودة لا ينفذ استعلاماً لكل خلية،
        // كما أن غياب اسم اختياري (عميل/دفعة/عبوة) لا يفشل الشاشة كلها.
        var sourceItems = orders.SelectMany(o => o.Items
                .Where(i => i.ProducedCartons > 0 || i.ProducedQtyKg > 0.001))
            .ToList();
        var productIds = sourceItems.Select(i => i.ProductId).Distinct().ToList();
        var lotIds = sourceItems.Where(i => i.LotId != null).Select(i => i.LotId!.Value).Distinct().ToList();
        var customerIds = sourceItems.Where(i => i.CustomerId != null).Select(i => i.CustomerId!.Value)
            .Concat(orders.Where(o => o.CustomerId != null).Select(o => o.CustomerId!.Value))
            .Distinct().ToList();
        var packageIds = sourceItems.Where(i => i.PackagingTypeId != null).Select(i => i.PackagingTypeId!.Value).Distinct().ToList();
        var planItemIds = sourceItems.Where(i => i.PlanItemId != null).Select(i => i.PlanItemId!.Value).Distinct().ToList();

        var products = Db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.ProductNameAr);
        var lots = Db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .ToDictionary(l => l.Id, l => l.LotCode);
        var customers = Db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .ToDictionary(c => c.Id, c => c.CustomerName);
        var packages = Db.PackagingTypes.AsNoTracking().Where(p => packageIds.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.PackageNameAr);
        var planCustomers = Db.ProductionPlanItems.AsNoTracking().Where(p => planItemIds.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.CustomerId);
        var gradesByProduct = new Dictionary<int, List<AllowedResultType>>();
        foreach (var productId in productIds)
        {
            var grades = GetAllowedResultTypesForItem(productId, "002")
                .Where(t => t.ResultKind is InspectionResultType.KindAccepted or InspectionResultType.KindRejected)
                .ToList();
            // بعض قواعد البيانات القديمة تسمي وحدة الكرتون بصيغة مختلفة. لا نخفي
            // صفات المنتج كلها بسبب اختلاف تسمية الوحدة؛ التحقق النهائي يبقى في الخدمة.
            var cartonGrades = grades.Where(t => string.Equals(t.UnitLabel, "كرتون", StringComparison.OrdinalIgnoreCase)).ToList();
            gradesByProduct[productId] = cartonGrades.Count > 0 ? cartonGrades : grades;
        }

        var result = new List<QualitySourceDto>();
        foreach (var o in orders.OrderByDescending(x => x.Id))
        {
            if (!executions.TryGetValue(o.Id, out var exe)) continue;
            var actualItems = o.Items.Where(i => i.ProducedCartons > 0 || i.ProducedQtyKg > 0.001).ToList();
            if (actualItems.Count == 0) continue;
            var src = new QualitySourceDto
            {
                OrderId = o.Id,
                ExecutionId = exe.Id,
                OrderNumber = o.DocumentNumber,
                TotalProducedCartons = actualItems.Sum(i => i.ProducedCartons),
            };
            if (checks.TryGetValue(exe.Id, out var chk))
            {
                src.CheckId = chk.Id; src.CheckNumber = chk.DocumentNumber;
                src.CheckStatus = chk.Status; src.CheckApproved = chk.IsApproved;
            }
            foreach (var it in actualItems)
            {
                int? custId = it.CustomerId
                    ?? (it.PlanItemId is int planItemId && planCustomers.TryGetValue(planItemId, out var planCustomer) ? planCustomer : null)
                    ?? o.CustomerId;
                products.TryGetValue(it.ProductId, out var productName);
                string lotCode = it.LotId is int lotId && lots.TryGetValue(lotId, out var code) ? code : null;
                string customerName = custId is int customerId && customers.TryGetValue(customerId, out var name) ? name : null;
                string packageName = it.PackagingTypeId is int packageId && packages.TryGetValue(packageId, out var package) ? package : null;
                var grades = gradesByProduct.TryGetValue(it.ProductId, out var productGrades)
                    ? productGrades.ToList() : new List<AllowedResultType>();
                src.Items.Add(new QualitySourceItemDto
                {
                    OrderItemId = it.Id,
                    ProductId = it.ProductId,
                    LotId = it.LotId,
                    ProducedCartons = it.ProducedCartons,
                    ProductName = productName ?? $"#{it.ProductId}",
                    LotCode = lotCode,
                    CustomerName = customerName,
                    // استخدم التعريف التاريخي المحفوظ في بند الأمر متى توفر، ثم التعريف الحالي.
                    CartonWeightKg = it.CartonWeightKg > 0 ? it.CartonWeightKg : UnitsPolicy.CartonWeight(Db, it.ProductId, it.PackagingTypeId),
                    PackageName = packageName,
                    AllowedGrades = grades,
                });
            }
            src.Label = $"تسليم الإنتاج {o.DocumentNumber} — {string.Join(" + ", src.Items.Select(i => $"{i.ProductName} ({i.ProducedCartons:N0})"))}";
            result.Add(src);
        }
        // القابلة للفحص أولاً: بلا فحص معتمد.
        return result.OrderBy(s2 => s2.CheckApproved).ThenByDescending(s2 => s2.OrderId).ToList();
    }

    public OpResult SaveDeliveryCheck(QualityDeliveryCheckDto input)
    {
        Require("quality", "Create");
        if (input == null || input.Rows == null || input.Rows.Count == 0)
            return OpResult.Fail("أدخل نتيجة فحص صفة واحدة على الأقل.");
        var order = Db.ProductionOrders.AsNoTracking().Include(o => o.Items).FirstOrDefault(o => o.Id == input.OrderId);
        if (order == null) return OpResult.Fail("أمر الإنتاج غير موجود.");
        var exec = Db.ProductionExecutions.AsNoTracking()
            .Where(e => e.OrderId == input.OrderId && e.IsDayClosed)
            .OrderByDescending(e => e.Id).FirstOrDefault();
        if (exec == null)
            return OpResult.Fail("لا يوجد تسليم إنتاج مكتمل لهذا الأمر — الفحص ينزل من تسليم الإنتاج فقط.");

        var typeIds = input.Rows.Select(r => r.ResultTypeId).Distinct().ToList();
        var types = Db.InspectionResultTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionary(t => t.Id);
        if (types.Count != typeIds.Count)
            return OpResult.Fail("صفة جودة غير معروفة — اختر من قائمة الصفات المعرفة.");

        // القاعدة الصارمة: مجموع صفات كل بند = كميته المستلمة للفحص (أقل أو أكثر = رفض صريح).
        // OrderItemId يمنع خلط عميلين لهما نفس الصنف والدفعة.
        var items = new List<QualityItemDto>();
        var coveredOrderItemIds = new HashSet<int>();
        foreach (var g in input.Rows.GroupBy(r => (r.OrderItemId, r.ProductId, r.LotId)))
        {
            var oi = g.Key.OrderItemId is int explicitItemId
                ? order.Items.FirstOrDefault(x => x.Id == explicitItemId)
                : order.Items.FirstOrDefault(x => x.ProductId == g.Key.ProductId && (g.Key.LotId == null || x.LotId == g.Key.LotId))
                  ?? order.Items.FirstOrDefault(x => x.ProductId == g.Key.ProductId);
            if (oi == null || oi.ProductId != g.Key.ProductId || (g.Key.LotId != null && oi.LotId != g.Key.LotId))
            {
                string foreign = Db.Products.AsNoTracking().Where(p => p.Id == g.Key.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{g.Key.ProductId}";
                return OpResult.Fail($"الصنف «{foreign}» أو بند أمر الإنتاج المرتبط به غير صحيح — الفحص يستقبل أصناف التسليم فقط.");
            }
            if (g.Any(r => !double.IsFinite(r.Cartons) || r.Cartons < 0)) return OpResult.Fail("كميات الصفات يجب أن تكون أرقاماً صحيحة غير سالبة.");
            if (g.Any(r => !types.TryGetValue(r.ResultTypeId, out var t) || t.ResultKind is not (InspectionResultType.KindAccepted or InspectionResultType.KindRejected)))
                return OpResult.Fail("صفات المخلفات والفاقد تُسجل في تسليم الإنتاج — هذه الشاشة لصفات المنتج (سليم/منسم/غير مطابق) فقط.");
            int produced = oi.ProducedCartons;
            double sum = g.Sum(r => r.Cartons);
            string name = Db.Products.AsNoTracking().Where(p => p.Id == g.Key.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{g.Key.ProductId}";
            if (Math.Abs(sum - produced) > 0.001)
                return OpResult.Fail($"نتائج صفات «{name}» مجموعها {sum:N0} كرتون ≠ الكمية المستلمة للفحص {produced:N0} كرتون — عدّل حتى تتساوى.");
            coveredOrderItemIds.Add(oi.Id);
            double acc = g.Where(r => types[r.ResultTypeId].ResultKind == InspectionResultType.KindAccepted).Sum(r => r.Cartons);
            double rej = g.Where(r => types[r.ResultTypeId].ResultKind == InspectionResultType.KindRejected).Sum(r => r.Cartons);
            double ctnW = oi.CartonWeightKg > 0
                ? oi.CartonWeightKg
                : UnitsPolicy.CartonWeight(Db, g.Key.ProductId, oi.PackagingTypeId);
            items.Add(new QualityItemDto
            {
                OrderItemId = oi.Id,
                ProductId = g.Key.ProductId, LotId = oi.LotId,
                CheckedCartons = produced, AcceptedCartons = acc, RejectedCartons = rej,
                AcceptedQtyKg = acc * ctnW, RejectedQtyKg = rej * ctnW, CheckedQtyKg = produced * ctnW,
            });
        }

        var expectedOrderItems = order.Items
            .Where(i => i.ProducedCartons > 0 || i.ProducedQtyKg > 0.001)
            .ToList();
        // QualityCheckItem/InspectionResult في المخطط الحالي لا يحملان CustomerId
        // أو PackagingTypeId. لذلك نرفض الحفظ عندما تتكرر هوية (صنف/دفعة) على
        // أكثر من بند، بدلاً من حفظ نتيجة لا يمكن نسبتها لاحقاً دون خلط.
        var ambiguousItems = expectedOrderItems
            .GroupBy(i => new { i.ProductId, i.LotId })
            .Where(g => g.Count() > 1)
            .ToList();
        if (ambiguousItems.Count > 0)
        {
            var names = string.Join("، ", ambiguousItems.Select(g =>
                Db.Products.AsNoTracking().Where(p => p.Id == g.Key.ProductId)
                    .Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{g.Key.ProductId}"));
            return OpResult.Fail(
                $"لا يمكن حفظ الفحص: الصنف/الدفعة ({names}) موجودان في أكثر من بند (عميل/عبوة). " +
                "افصل هوية البند قبل الحفظ حتى لا تختلط النتائج.");
        }
        var missingOrderItems = expectedOrderItems.Where(i => !coveredOrderItemIds.Contains(i.Id)).ToList();
        if (missingOrderItems.Count > 0)
            return OpResult.Fail($"لا يمكن حفظ الفحص: أدخل نتائج كل بنود الإنتاج الفعلي — المتبقي {missingOrderItems.Count} بنداً.");

        // المعايير المعتمدة: قيمها تُحفظ سجلات، وتغذي حقول المواصفة القياسية بالمحضر
        // §1.50.72 P2-4: القرار قيمة صالحة أو رفض — كان أي قرار غير صالح يُسقط «Passed» بصمت.
        if (input.Decision is not ("Passed" or "Quarantine" or "Rejected"))
            return OpResult.Fail("قرار الفحص غير صالح — اختر: مطابق (Passed) | حجز (Quarantine) | مرفوض (Rejected).");
        var lab = new QualityLabDto { Decision = input.Decision, InspectorNotes = input.InspectorNotes };
        var stdById = Db.QualityStandards.AsNoTracking().Where(x => x.IsActive).ToList();
        bool measurementsRequired = Db.SystemSettings.AsNoTracking()
            .Any(s => s.SettingKey == "Quality_MeasurementsRequired" && s.SettingValue == "1");
        if (measurementsRequired)
        {
            var supplied = (input.Standards ?? new()).Select(x => x.StandardId).ToHashSet();
            var missing = stdById.Where(x => !supplied.Contains(x.Id)).Select(x => x.NameAr).ToList();
            if (missing.Count > 0)
                return OpResult.Fail("سياسة القياسات إلزامية — أدخل المعايير التالية قبل حفظ الفحص: " + string.Join("، ", missing));
        }
        foreach (var st in input.Standards ?? new())
        {
            var def = stdById.FirstOrDefault(x => x.Id == st.StandardId);
            if (def == null) continue;
            if (!double.IsFinite(st.Value)
                || (def.MinValue.HasValue && st.Value < def.MinValue.Value)
                || (def.MaxValue.HasValue && st.Value > def.MaxValue.Value))
                return OpResult.Fail($"قيمة معيار «{def.NameAr}» خارج الحدود المعتمدة أو غير صالحة.");
            switch (def.Code)
            {
                case "MOIST": lab.MoisturePct = st.Value; break;
                case "BRIX": lab.BrixDeg = st.Value; break;
                case "SKIN": lab.SkinSeparationPct = st.Value; break;
                case "IMP": lab.ImpuritiesPct = st.Value; break;
            }
        }

        // QC-05 — رأس الفحص + بنوده + النتائج الديناميكية + المعايير معاملة واحدة.
        // QualityService ينضم إلى المعاملة الأم بدلاً من اعتماد الرأس منفرداً ثم
        // كتابة النتائج في معاملة لاحقة.
        return RunOp(() =>
        {
        var qc = new QualityService(Db, Session, Numbering, new AuditService(Db, Session))
        {
            JoinParentTransaction = true
        };
        var r = qc.SaveCheck(input.OrderId, exec.Id, input.CheckDate, "نهائي — بعد التبريد", items, null, lab);
        if (!r.Ok) throw new DomainException(r.Message);

        // النتائج الديناميكية: صف لكل صفة (الصفة ليست صنفاً — ResultTypeId هو الهوية)
        Db.InspectionResults.RemoveRange(Db.InspectionResults.Where(x => x.CheckId == r.Id));
        foreach (var g in input.Rows.GroupBy(r => (r.OrderItemId, r.ProductId, r.LotId)))
        {
            var oi = g.Key.OrderItemId is int explicitItemId
                ? order.Items.First(x => x.Id == explicitItemId)
                : order.Items.FirstOrDefault(x => x.ProductId == g.Key.ProductId && (g.Key.LotId == null || x.LotId == g.Key.LotId))
                  ?? order.Items.First(x => x.ProductId == g.Key.ProductId);
            foreach (var row in g)
            {
                var t = types[row.ResultTypeId];
                Db.InspectionResults.Add(new InspectionResult
                {
                    CheckId = r.Id, ProductId = g.Key.ProductId, LotId = oi.LotId, ResultTypeId = row.ResultTypeId,
                    Qty = (decimal)row.Cartons, UnitId = t.UnitId, UnitLabel = t.UnitLabel,
                });
            }
        }
        Db.QualityStandardRecords.RemoveRange(Db.QualityStandardRecords.Where(x => x.CheckId == r.Id));
        foreach (var st in input.Standards ?? new())
        {
            var def = stdById.FirstOrDefault(x => x.Id == st.StandardId);
            if (def == null) continue;
            // لا نغيّر المخطط: Notes حقل قائم، ونحفظ فيه لقطة الحدود/الاسم
            // حتى لا تعيد الطباعة قراءة تعريف تغيّر بعد تاريخ الفحص.
            var snapshot = JsonSerializer.Serialize(new
            {
                NameAr = def.NameAr, UnitLabel = def.UnitLabel,
                MinValue = def.MinValue, MaxValue = def.MaxValue
            });
            Db.QualityStandardRecords.Add(new QualityStandardRecord
            { CheckId = r.Id, StandardId = st.StandardId, Value = st.Value, Notes = snapshot });
        }

        // SaveDeliveryCheck تحقّق من تغطية كل كمية الإنتاج؛ لذلك يجب أن يبقى المحضر قابلاً للاعتماد.
        var savedCheck = Db.QualityChecks.FirstOrDefault(c => c.Id == r.Id);
        if (savedCheck != null && savedCheck.Status != DocStatuses.Completed)
            savedCheck.Status = DocStatuses.Completed;
        Db.SaveChanges();
        // §1.50.72 P2-4: محضر بقرار «مطابق» رغم وجود مرفوضات — يُحفظ (قرار الفاحص) لكنه لا يمرّ بصمت.
        double totalRejected = items.Sum(i => i.RejectedCartons);
        string decWarn = input.Decision == "Passed" && totalRejected > 0
            ? $"\n⚠ تنبيه: سُجّل في المحضر {totalRejected:N0} كرتون مرفوضة وقراره «مطابق» — إن كان المقصود «مرفوض/حجز» فصحّحه بتصحيح معتمد."
            : "";
        return OpResult.Success($"تم حفظ فحص الجودة {r.DocumentNumber} — مجموع الصفات يطابق الكمية المستلمة للفحص.{decWarn}", r.Id, r.DocumentNumber);
        });
    }

    public string UnitName(int unitId)
        => Db.UnitsOfMeasure.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.UnitNameAr).FirstOrDefault() ?? $"#{unitId}";

    // ─────────────────────── أدوات ───────────────────────

    private static double? Pct(double part, double whole)
        => whole > 0 ? Math.Round(part / whole * 100.0, 2) : null;

    private static string NormalizeKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return InspectionResultType.KindAccepted;
        var k = kind.Trim();
        if (new[] { InspectionResultType.KindAccepted, InspectionResultType.KindRejected,
                    InspectionResultType.KindByProduct, InspectionResultType.KindLoss }
                .Contains(k, StringComparer.OrdinalIgnoreCase)) return k;
        // قبول التسمية العربية أيضاً — الشاشة قد ترسل أياً منهما
        return k switch
        {
            "مقبول" or "مقبول للإفراج" or "تام" => InspectionResultType.KindAccepted,
            "مرفوض" or "غير مطابق" => InspectionResultType.KindRejected,
            "مخرج ثانوي" or "ثانوي" => InspectionResultType.KindByProduct,
            "فاقد" or "هالك" => InspectionResultType.KindLoss,
            _ => throw new DomainException(
                $"تصنيف النتيجة «{k}» غير معتمد — المسموح: مقبول للإفراج | مرفوض | مخرج ثانوي | فاقد.")
        };
    }

    private AllowedResultType ToAllowed(InspectionResultType t)
    {
        string grade = InspectionResultType.GradeOf(t.ResultKind, t.IsFinalScrap);
        return new()
        {
            ResultTypeId = t.Id,
            Code = t.Code,
            NameAr = t.NameAr,
            ResultKind = t.ResultKind,
            ResultKindAr = InspectionResultType.KindNameAr(t.ResultKind),
            UnitId = t.UnitId,
            UnitLabel = t.UnitLabel,
            IsFinishedGood = t.IsFinishedGood,
            IsByProduct = t.IsByProduct,
            EntersInventory = t.EntersInventory,
            CountsAsLoss = t.CountsAsLoss,
            IsFinalScrap = t.IsFinalScrap,
            QualityGrade = grade,
            QualityGradeAr = grade == null ? "—" : InspectionResultType.GradeNameAr(grade),
            SortNo = t.SortNo
        };
    }
}
