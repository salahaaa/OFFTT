using DatesErp.Core.Domain.Entities;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// §إصلاح حرج — بوابة جودة مركزية واحدة.
///
/// الوضع في B18: قرار الجودة كان مُهمَلاً في أربع حلقات متتالية، وأُثبت بالتشغيل أن
/// دفعة قرارها «مرفوض تماماً» اعتُمدت ودخلت مخزن التام وسُلّمت للعميل:
///   1) ExecutionService.ApproveCheck        — لا يفحص Decision إطلاقاً
///   2) FinishedGoodsService.SaveReceipt     — تشترط وجود فحص فقط
///   3) DeliveryView.Save                    — تمرر orderId = null
///   4) CustomerDeliveryService.Approve      — البوابة داخل if (OrderId is int) فلا تُنفَّذ أبداً
///
/// الإصلاح: بوابة واحدة تفحص القرار (لا مجرد الاعتماد)، وتُشتق الأوامر من الدفعة/الصنف
/// إن غاب معرّف الأمر — فلا يمكن تجاوزها بتمرير null.
/// </summary>
public static class QualityGate
{
    public const string Passed = "Passed";
    public const string Quarantine = "Quarantine";
    public const string Rejected = "Rejected";

    /// <summary>
    /// هل يُسمح بتسليم هذه البضاعة للعميل؟
    /// يفحص: وجود فحص ← اعتماده ← قراره (مرفوض/محجوز يمنع التسليم).
    /// </summary>
    public static (bool ok, string reason) CustomerDeliveryAllowed(
        DatesErpDbContext db, int? orderId, int? lotId, int? productId, int? packagingTypeId = null)
    {
        var orderIds = new List<int>();
        if (orderId != null) orderIds.Add(orderId.Value);

        // الهوية التشغيلية = أمر + بند/صنف + دفعة + عبوة. لا نبحث عن «أي فحص
        // للصنف» لأن ذلك يخلط دفعتين أو عبوتين متشابهتين.
        if (lotId != null || orderIds.Count == 0 || packagingTypeId != null)
        {
            var q = db.ProductionOrderItems.AsNoTracking()
                .Where(i => (productId == null || i.ProductId == productId)
                    && i.LotId == lotId);
            if (packagingTypeId != null) q = q.Where(i => i.PackagingTypeId == packagingTypeId);
            var candidates = q.Select(i => new { i.OrderId, i.PackagingTypeId }).ToList();
            if (packagingTypeId == null && candidates.Select(x => x.PackagingTypeId).Distinct().Count() > 1)
                return (false, "⛔ لا يمكن التسليم: توجد أكثر من عبوة لنفس الصنف/الدفعة — حدّد عبوة السطر قبل الإفراج.");
            if (orderId != null && !candidates.Any(x => x.OrderId == orderId.Value))
                return (false, "⛔ لا يمكن التسليم: عبوة/دفعة السطر لا تطابق بند أمر الإنتاج المحدد.");
            if (orderIds.Count == 0) orderIds = candidates.Select(x => x.OrderId).Distinct().ToList();
        }

        if (orderIds.Count == 0)
            return (false,
                "⛔ لا يمكن التسليم للعميل: لا يوجد بند أمر إنتاج مرتبط بهذه الدفعة والعبوة للتحقق من نتيجة فحص الجودة.\n" +
                "اربط السند بأمر الإنتاج وحدّد الدفعة والعبوة.");

        foreach (var oid in orderIds)
        {
            // QualityCheckItem لا يحمل عمود عبوة في البيانات التاريخية؛ إذا كان
            // الأمر نفسه يقسم نفس (الصنف/الدفعة) على عبوتين نرفض بدلاً من خلطهما.
            var packageIdentities = db.ProductionOrderItems.AsNoTracking()
                .Where(i => i.OrderId == oid && i.ProductId == productId && i.LotId == lotId)
                .Select(i => i.PackagingTypeId).Distinct().ToList();
            if (packageIdentities.Count > 1
                || (packagingTypeId != null && !packageIdentities.Contains(packagingTypeId)))
                return (false, "⛔ لا يمكن الإفراج: هوية الجودة موزعة على أكثر من عبوة لنفس البند، ولا يسمح النظام بخلطها.");
            if (db.ProductionOrderItems.AsNoTracking()
                    .Count(i => i.OrderId == oid && i.ProductId == productId && i.LotId == lotId) != 1)
                return (false, "⛔ لا يمكن الإفراج: توجد عدة بنود متشابهة للصنف/الدفعة ولا يمكن إثبات مالك المقبول.");
            var checks = db.QualityChecks.AsNoTracking().Where(c => c.OrderId == oid && c.IsApproved).ToList();
            if (checks.Count == 0)
                return (false,
                    "⛔ لا يمكن التسليم للعميل: لا يوجد فحص جودة معتمد للبند المرتبط.\n" +
                    "نفّذ الفحص واعتمد نتيجته قبل الإفراج.");
            var checkIds = checks.Select(c => c.Id).ToList();
            var relevant = db.QualityCheckItems.AsNoTracking()
                .Where(i => checkIds.Contains(i.CheckId) && i.ProductId == productId && i.LotId == lotId)
                .ToList();
            if (relevant.Count == 0 || relevant.Sum(i => i.AcceptedQtyKg) <= 0.001)
                return (false,
                    "⛔ لا يمكن التسليم للعميل: المقبول المعتمد لهذا الصنف/الدفعة/العبوة يساوي صفراً.\n" +
                    "لا تُعتبر نتيجة صنف أو دفعة أخرى إفراجاً لهذا السطر.");

            var rejected = checks.FirstOrDefault(c => c.Decision == Rejected && relevant.Any(i => i.CheckId == c.Id));
            if (rejected != null)
                return (false,
                    $"⛔ لا يمكن التسليم للعميل: قرار فحص الجودة «مرفوض تماماً / عوادم».\n" +
                    $"الفحص: {rejected.DocumentNumber} — اعتمد قرار الإتلاف أو إعادة التصنيع قبل أي تسليم.");
            var quarantined = checks.FirstOrDefault(c => c.Decision == Quarantine && relevant.Any(i => i.CheckId == c.Id));
            if (quarantined != null)
                return (false,
                    $"⛔ لا يمكن التسليم للعميل: البضاعة تحت «حجز وتحريز مؤقت».\n" +
                    $"الفحص: {quarantined.DocumentNumber} — الإفراج يتطلب قرار جودة «مطابق ومقبول».");
        }

        return (true, null);
    }

    /// <summary>
    /// §B105/P4 — جاهزية سطر رصيد للعرض في شاشة التسليم: ✔ معتمد / ⏳ بانتظار الفحص / ⛔ مرفوض أو محجوز.
    /// للعرض والمنع المبكر فقط — القرار النهائي يبقى لبوابة CustomerDeliveryAllowed عند الاعتماد.
    /// </summary>
    public static (bool ready, string label) DeliveryReadiness(
        DatesErpDbContext db, int? lotId, int productId)
    {
        var (ok, reason) = CustomerDeliveryAllowed(db, null, lotId, productId);
        if (ok) return (true, "✔ معتمد");
        if (reason != null && reason.Contains("مرفوض")) return (false, "⛔ مرفوض");
        if (reason != null && reason.Contains("حجز")) return (false, "⛔ محجوز");
        return (false, "⏳ بانتظار الفحص");
    }

    /// <summary>
    /// §B95 — سقف الكمية المسلَّمة (تكملة بوابة القرار): لا يُسلَّم للعميل أكثر من «المطابق المعتمد» —
    /// مجموع المقبول في الفحوصات المعتمدة لأوامر البضاعة، ناقصاً ما سُلِّم معتمداً سابقاً لنفس النطاق.
    /// يُفحص بعد رصيد المخزن (لا قبله) لتبقى رسائل الرصيد القائمة على حالها،
    /// ويُتجاوز بصمت عند غياب بنود فحص (فحوصات بلا تفصيل) — فبوابة القرار هي صاحبة الرفض هناك.
    /// </summary>
    public static (bool ok, string reason) CustomerDeliveryQtyAllowed(
        DatesErpDbContext db, CustomerDelivery dlv, CustomerDeliveryItem item)
    {
        // سقف المقبول المعتمد لنفس الهوية، لا لمجرد ProductId.
        var orderItems = db.ProductionOrderItems.AsNoTracking()
            .Where(i => i.ProductId == item.ProductId && i.LotId == item.LotId
                && i.PackagingTypeId == item.PackagingTypeId
                && (i.CustomerId == null || i.CustomerId == dlv.CustomerId))
            .ToList();
        var orderIds = dlv.OrderId != null
            ? new List<int> { dlv.OrderId.Value }
            : orderItems.Select(i => i.OrderId).Distinct().ToList();
        if (orderIds.Count == 0) return (false, "⛔ لا يمكن التسليم: لم يُعثر على بند إنتاج مطابق لهوية السطر.");
        if (dlv.OrderId == null && orderIds.Count != 1)
            return (false, "⛔ لا يمكن التسليم دون أمر محدد: هوية السطر تطابق أكثر من أمر إنتاج.");
        if (orderIds.Any(oid => orderItems.Count(i => i.OrderId == oid) != 1))
            return (false, "⛔ لا يمكن التسليم: توجد عدة بنود متشابهة للصنف/الدفعة/العبوة ولا يمكن إثبات هوية المقبول.");

        var checkIds = db.QualityChecks.AsNoTracking()
            .Where(c => c.OrderId != null && orderIds.Contains(c.OrderId.Value) && c.IsApproved)
            .Select(c => c.Id).ToList();
        double approved = db.QualityCheckItems.AsNoTracking()
            .Where(q => checkIds.Contains(q.CheckId) && q.ProductId == item.ProductId && q.LotId == item.LotId)
            .Sum(q => q.AcceptedQtyKg);
        // QC-01: غياب التفصيل أو المقبول الصفري رفض، لا «سماح» افتراضي.
        if (approved <= 0.001)
            return (false,
                "⛔ لا يمكن التسليم: المقبول المعتمد يساوي صفراً لهذه الهوية (الصنف/الدفعة/العبوة).");

        double delivered = (from di in db.CustomerDeliveryItems.AsNoTracking()
                            join dd in db.CustomerDeliveries.AsNoTracking() on di.DeliveryId equals dd.Id
                            where dd.Id != dlv.Id && dd.IsApproved
                                && dd.CustomerId == dlv.CustomerId
                                && di.ProductId == item.ProductId && di.LotId == item.LotId
                                && di.PackagingTypeId == item.PackagingTypeId
                            select di.QtyKg).Sum();
        double inCurrent = dlv.Items
            .Where(x => x.ProductId == item.ProductId && x.LotId == item.LotId
                && x.PackagingTypeId == item.PackagingTypeId)
            .Sum(x => x.QtyKg);
        if (delivered + inCurrent > approved + 0.01)
        {
            string pname = db.Products.AsNoTracking().Where(p => p.Id == item.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"صنف #{item.ProductId}";
            return (false,
                $"⛔ لا يمكن تسليم {inCurrent:N1} كجم من «{pname}»: المقبول المعتمد لنفس الدفعة/العبوة {approved:N1} كجم" +
                (delivered > 0.001 ? $" وسُلِّم منه {delivered:N1} كجم سابقاً" : "") + ".\n" +
                "لا يُسلَّم للعميل إلا الكمية المطابقة المعتمدة لنفس هوية البند.");
        }
        return (true, null);
    }

    /// <summary>هل سُمح بالإفراج لمخزن التام؟ (يكفي إرسال الإنتاج للفحص — قرارهم #19/#20).</summary>
    public static (bool ok, string reason) FinishedGoodsIssueAllowed(DatesErpDbContext db, int orderId)
    {
        var checks = db.QualityChecks.AsNoTracking()
            .Where(c => c.OrderId == orderId && c.IsApproved).ToList();
        if (checks.Count == 0)
            return (false,
                "لا يمكن تسليم الإنتاج قبل اعتماد فحص الجودة — نفّذ الإقفال والفحص أولاً.");
        var blocked = checks.FirstOrDefault(c => c.Decision is Rejected or Quarantine);
        if (blocked != null)
        {
            string decisionAr = blocked.Decision == Rejected ? "مرفوض" : "حجز وتحريز مؤقت";
            return (false, $"لا يمكن تسليم الإنتاج: قرار الفحص {blocked.DocumentNumber} هو «{decisionAr}».");
        }
        double accepted = db.QualityCheckItems.AsNoTracking()
            .Where(i => checks.Select(c => c.Id).Contains(i.CheckId))
            .Sum(i => i.AcceptedQtyKg);
        if (accepted <= 0.001)
            return (false, "لا يمكن تسليم الإنتاج: المقبول المعتمد يساوي صفراً.");
        return (true, null);
    }
}
