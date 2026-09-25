using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// §7 — الدورة القانونية لاستلام الإنتاج التام:
/// أمر التسليم (متعدد الأصناف) ← الإصدار للمخزن (بلا أثر على الأرصدة)
/// ← سند الاستلام المخزني هو وحده ما يحرّك أرصدة مخزن التام (كلي/جزئي لكل صنف).
/// </summary>
public class FinishedGoodsService : ServiceBase, IFinishedGoodsService
{
    // الاستلامات المتوازية على نفس بند أمر التسليم يجب أن ترى حالة المصدر نفسها
    // قبل تعديل ReceivedQtyKg؛ العزل التسلسلي يمنع تجاوز السقف بسبب سباق مستخدمين.
    protected override System.Data.IsolationLevel TransactionIsolation => System.Data.IsolationLevel.Serializable;
    protected override bool RetryTransactionDeadlocks => true;

    public FinishedGoodsService(DatesErpDbContext db, ICurrentSession session, INumberingService numbering)
        : base(db, session, numbering) { }

    public OpResult SaveReceipt(int orderId, int? qualityCheckId, string deliveryDate, List<FinishedGoodsItemDto> items, int? deliveryId = null)
    {
        Require("finishedgoods", "Create");
        if (items == null || items.Count == 0) return OpResult.Fail("أدخل بنداً واحداً على الأقل.");
        if (deliveryId == null)
            return OpResult.Fail("لا يمكن إنشاء أمر استلام الإنتاج مباشرة من أمر الإنتاج أو الفعلي. اختر أمر تسليم إنتاج محرراً من مدير الإنتاج.");
        var order = Db.ProductionOrders.Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
        if (order == null) return OpResult.Fail("أمر الإنتاج غير موجود.");
        // المسار الرسمي الوحيد: أمين مخزن التام ينشئ أمر الاستلام من أمر تسليم فعلي محرر.
        var delivery = Db.ProductionDeliveries.Include(d => d.Items).FirstOrDefault(d => d.Id == deliveryId.Value);
        if (delivery == null) return OpResult.Fail("أمر تسليم الإنتاج غير موجود — تحقق من الرقم.");
        if (delivery.SourceType != DeliverySources.FromActual)
            return OpResult.Fail("أمر الاستلام لا يُنشأ إلا من أمر تسليم نازل من الإنتاج الفعلي.");
        var sourceExecution = Db.ProductionExecutions.AsNoTracking().FirstOrDefault(e => e.Id == delivery.SourceId);
        if (sourceExecution == null || sourceExecution.OrderId != orderId)
            return OpResult.Fail("أمر التسليم لا يرتبط بالتنفيذ الفعلي لأمر الإنتاج المحدد.");
        if (delivery.Status == DocStatuses.Draft) return OpResult.Fail("أمر التسليم مسودة — يجب تحريره من مدير الإنتاج أولاً.");
        if (delivery.Status == DocStatuses.Cancelled) return OpResult.Fail("أمر التسليم ملغى.");
        if (delivery.Status == DocStatuses.Completed) return OpResult.Fail("أمر التسليم مستلم بالكامل مسبقاً.");
        var delOrders = delivery.Items.Where(i => i.OrderId != null).Select(i => i.OrderId.Value).Distinct().ToList();
        if (!delOrders.Contains(orderId)) return OpResult.Fail("الأمر المحدد ليس من أوامر أمر التسليم المحدد.");
        if (delivery.Status != DocStatuses.Issued)
            return OpResult.Fail("أمر التسليم ليس محرراً للمخزن.");

        return RunOp(() =>
        {
            // لا تعتمد المعاملة على نسخة الرأس التي فُحصت قبل فتحها: أعد قراءة
            // أمر التسليم وحالته وبنوده داخل المعاملة حتى لا يمر الإلغاء المتزامن.
            Db.ChangeTracker.Clear();
            var currentDelivery = Db.ProductionDeliveries.AsNoTracking().Include(d => d.Items)
                .FirstOrDefault(d => d.Id == deliveryId.Value)
                ?? throw new DomainException("أمر تسليم الإنتاج غير موجود.", "DELIVERY_MISSING");
            if (currentDelivery.SourceType != DeliverySources.FromActual)
                throw new DomainException("أمر الاستلام لا يُنشأ إلا من أمر تسليم نازل من الإنتاج الفعلي.", "DELIVERY_SOURCE");
            if (currentDelivery.Status == DocStatuses.Cancelled)
                throw new DomainException("أمر التسليم ملغى.", "DELIVERY_CANCELLED");
            if (currentDelivery.Status == DocStatuses.Completed)
                throw new DomainException("أمر التسليم مستلم بالكامل مسبقاً.", "DELIVERY_COMPLETED");
            if (currentDelivery.Status != DocStatuses.Issued)
                throw new DomainException("أمر التسليم ليس محرراً للمخزن.", "DELIVERY_NOT_ISSUED");
            if (!currentDelivery.Items.Any(i => i.OrderId == orderId))
                throw new DomainException("الأمر المحدد ليس من أوامر أمر التسليم المحدد.", "ORDER_MISMATCH");
            delivery = currentDelivery;

            var rcpt = new FinishedGoodsReceipt
            {
                DocumentNumber = Numbering.Next("FGR"),
                OrderId = orderId,
                QualityCheckId = qualityCheckId,
                DeliveryId = deliveryId,
                DeliveryDate = UiFormat.TryParseDate(deliveryDate, out var d) ? d : DateTime.Now,
                WarehouseId = WarehouseId("WFG"),
                Status = DocStatuses.Draft,
                ReceiptStatus = "None"
            };
            // §B96 — حارس التكرار يمنع بندين بنفس (الصنف + الدفعة) في سند واحد: رفض مبكر برسالة واضحة
            // (لعملاء مختلفين على نفس الدفعة: استلم كل بند تسليم في سند مستقل — فالترقيم مختلف ولا تعارض)
            var dupLine = items.GroupBy(i => new { i.ProductId, i.LotId, i.PackagingTypeId, i.DeliveryItemId }).FirstOrDefault(g => g.Count() > 1);
            if (dupLine != null)
                throw new DomainException(
                    "⛔ بندَان مكرران لنفس هوية الصنف/الدفعة/العبوة في سند واحد — وحّدهما في بند واحد.\n" +
                    "لعملاء مختلفين على نفس الدفعة: استلم كل بند تسليم في سند مستقل.",
                    "DUP_LINE");
            foreach (var it in items)
            {
                // §نظام الوحدات: استلام التام للمنتجات التامة فقط (002).
                UnitsPolicy.RequireItemType(Db, it.ProductId, "Finished", "استلام الإنتاج التام");

                // §PRD-01 — بند التسليم هو المصدر الوحيد لهوية العبوة/الكراتين.
                int? effCust = null;
                int? effLine = null;
                if (it.DeliveryItemId == null)
                    throw new DomainException("حدد بند أمر التسليم لكل صنف في السند المربوط.", "NO_DELIVERY_LINE");
                var line = delivery.Items.FirstOrDefault(l => l.Id == it.DeliveryItemId.Value)
                    ?? throw new DomainException("بند التسليم غير تابع لأمر التسليم المحدد.", "NO_DELIVERY_LINE");
                if (line.ProductId != it.ProductId)
                    throw new DomainException("الصنف لا يطابق بند أمر التسليم المحدد.", "LINE_MISMATCH");
                if (it.LotId != null && it.LotId != line.LotId)
                    throw new DomainException("الدفعة لا تطابق بند أمر التسليم المحدد.", "LOT_MISMATCH");
                if (it.LotId == null) it.LotId = line.LotId;
                if (it.CustomerId != null && it.CustomerId != line.CustomerId)
                    throw new DomainException("العميل لا يطابق عميل بند أمر التسليم المحدد.", "CUSTOMER_MISMATCH");
                if (it.PackagingTypeId != line.PackagingTypeId)
                    throw new DomainException("العبوة لا تطابق عبوة بند أمر التسليم المحدد.", "PACKAGING_MISMATCH");
                if (it.PackageCount <= 0 || line.PackageCount <= 0)
                    throw new DomainException("عدد كراتين بند التسليم وسند الاستلام يجب أن يكون أكبر من صفر.", "PACKAGE_COUNT_REQUIRED");
                if (it.PackageCount > line.PackageCount)
                    throw new DomainException("عدد كراتين سند الاستلام يتجاوز عدد كراتين بند أمر التسليم.", "PACKAGE_COUNT_OVER");
                UnitsPolicy.RequireCartonWeight(Db, it.ProductId, it.PackagingTypeId, it.PackageCount, "استلام الإنتاج التام");
                double cartonWeight = UnitsPolicy.CartonWeight(Db, it.ProductId, it.PackagingTypeId);
                double expectedKg = Math.Round(it.PackageCount * cartonWeight, 1);
                if (Math.Abs(it.NetWeightKg - expectedKg) > 0.001)
                    throw new DomainException(
                        $"وزن السند ({it.NetWeightKg:N1} كجم) لا يطابق {it.PackageCount:N0} كرتوناً من العبوة المحددة ({expectedKg:N1} كجم).",
                        "PACKAGE_WEIGHT_MISMATCH");
                // §B86/H8 بالمثل: المسودات لا تحجب بعضها — السقف على المستلَم ويُعاد فحصه عند الاستلام
                double lineRemaining = line.QtyKg - line.ReceivedQtyKg;
                if (it.NetWeightKg > lineRemaining + 0.001)
                    throw new DomainException(
                        $"⛔ كمية البند ({it.NetWeightKg:N1} كجم) تتجاوز المتبقي في بند أمر التسليم ({lineRemaining:N1} كجم).",
                        "OVER_DELIVERY");
                effCust = line.CustomerId;
                effLine = line.Id;

                rcpt.Items.Add(new FinishedGoodsReceiptItem
                {
                    ProductId = it.ProductId,
                    LotId = it.LotId,
                    CustomerId = effCust,
                    DeliveryItemId = effLine,
                    PackagingTypeId = it.PackagingTypeId,
                    PackageCount = it.PackageCount,
                    NetWeightKg = it.NetWeightKg,
                    ReceivedQtyKg = 0,
                    // §القاعدة 7: وزن الكرتون وقت الاستلام — لا يتغير بتعريف العبوة لاحقاً
                    CartonWeightKg = UnitsPolicy.CartonWeight(Db, it.ProductId, it.PackagingTypeId)
                });
            }
            Db.FinishedGoodsReceipts.Add(rcpt);
            Db.SaveChanges();
            // الإصدار لا يمس الأرصدة؛ الترحيل الفعلي يتم لاحقاً من إجراء الاستلام فقط.
            rcpt.CreatedBy = Session?.UserId;
            return OpResult.Success($"تم إنشاء أمر استلام الإنتاج {rcpt.DocumentNumber} من أمر التسليم المحرر {delivery.DocumentNumber} — أصدره ثم نفّذ الاستلام (المستخدم: {Session?.UserName} — التاريخ: {DateTime.Now:dd/MM/yyyy}).", rcpt.Id, rcpt.DocumentNumber);
        });
    }

    /// <summary>الإصدار إلى المخزن — لا يمس أي رصيد (§7).</summary>
    public OpResult Issue(int receiptId)
    {
        Require("finishedgoods", "Approve");
        var rcpt = Db.FinishedGoodsReceipts.FirstOrDefault(r => r.Id == receiptId);
        if (rcpt == null) return OpResult.Fail("أمر الاستلام غير موجود.");
        if (rcpt.DeliveryId == null)
            return OpResult.Fail("أمر الاستلام لا يملك مرجع أمر تسليم إنتاج.");
        if (!Db.ProductionDeliveries.AsNoTracking().Any(d => d.Id == rcpt.DeliveryId.Value
            && d.SourceType == DeliverySources.FromActual))
            return OpResult.Fail("لا يمكن إصدار استلام مصدره أمر تسليم قديم غير نازل من الإنتاج الفعلي.");
        if (rcpt.Status == DocStatuses.Issued) return OpResult.Fail("أمر الاستلام مُصدر مسبقاً.");

        return RunOp(() =>
        {
            Db.ChangeTracker.Clear();
            var currentRcpt = Db.FinishedGoodsReceipts.AsNoTracking().FirstOrDefault(r => r.Id == receiptId)
                ?? throw new DomainException("أمر الاستلام غير موجود.", "RECEIPT_MISSING");
            var currentDelivery = Db.ProductionDeliveries.AsNoTracking().FirstOrDefault(d => d.Id == currentRcpt.DeliveryId)
                ?? throw new DomainException("أمر التسليم المرتبط غير موجود.", "DELIVERY_MISSING");
            if (currentDelivery.SourceType != DeliverySources.FromActual)
                throw new DomainException("لا يمكن إصدار استلام مصدره أمر تسليم قديم.", "DELIVERY_SOURCE");
            if (currentDelivery.Status == DocStatuses.Cancelled)
                throw new DomainException("أمر التسليم المرتبط ملغى.", "DELIVERY_CANCELLED");
            if (currentDelivery.Status == DocStatuses.Completed)
                throw new DomainException("أمر التسليم المرتبط مستلم بالكامل.", "DELIVERY_COMPLETED");
            if (currentRcpt.Status == DocStatuses.Issued)
                throw new DomainException("أمر الاستلام مُصدر مسبقاً.", "RECEIPT_ISSUED");
            rcpt = Db.FinishedGoodsReceipts.First(r => r.Id == receiptId);
            rcpt.Status = DocStatuses.Issued;
            Db.SaveChanges();
            return OpResult.Success("تم إصدار أمر التسليم إلى المخزن — بانتظار سند الاستلام.");
        });
    }

    /// <summary>§7/§8 — سند الاستلام المخزني: وحده يؤثر على الأرصدة، كلياً أو جزئياً لكل صنف.</summary>
    public OpResult Receive(int receiptId, Dictionary<int, double> receivedByItemId)
    {
        Require("finishedgoods", "Approve");
        var rcpt = Db.FinishedGoodsReceipts.Include(r => r.Items).FirstOrDefault(r => r.Id == receiptId);
        if (rcpt == null) return OpResult.Fail("أمر الاستلام غير موجود.");
        if (rcpt.DeliveryId == null)
            return OpResult.Fail("لا يمكن ترحيل استلام بلا أمر تسليم إنتاج مرتبط.");
        if (!Db.ProductionDeliveries.AsNoTracking().Any(d => d.Id == rcpt.DeliveryId.Value
            && d.SourceType == DeliverySources.FromActual))
            return OpResult.Fail("لا يمكن ترحيل استلام مصدره أمر تسليم قديم غير نازل من الإنتاج الفعلي.");
        if (rcpt.ReceiptStatus == "Full") return OpResult.Fail("السند منفذ بالكامل مسبقاً.");
        if (rcpt.Items.All(i => i.NetWeightKg - i.ReceivedQtyKg <= 0.001))
            return OpResult.Fail("لا توجد كمية متبقية للاستلام — لم يُحجز رقم سند متابعة.");
        if (rcpt.Status != DocStatuses.Issued && rcpt.Status != DocStatuses.Completed)
            return OpResult.Fail("لا يمكن الاستلام قبل إصدار أمر التسليم.");

        return RunOp(() =>
        {
            // PRD-03: أعد تحميل رأس السند والمصدر داخل المعاملة، لا تستخدم نسخة قبلية.
            Db.ChangeTracker.Clear();
            rcpt = Db.FinishedGoodsReceipts.Include(r => r.Items).FirstOrDefault(r => r.Id == receiptId)
                ?? throw new DomainException("أمر الاستلام غير موجود.", "RECEIPT_MISSING");
            var currentDelivery = rcpt.DeliveryId != null
                ? Db.ProductionDeliveries.Include(d => d.Items).FirstOrDefault(d => d.Id == rcpt.DeliveryId.Value)
                : null;
            if (currentDelivery == null || currentDelivery.SourceType != DeliverySources.FromActual)
                throw new DomainException("لا يمكن ترحيل استلام مصدره أمر تسليم غير صالح.", "DELIVERY_SOURCE");
            if (currentDelivery.Status == DocStatuses.Cancelled)
                throw new DomainException("أمر التسليم المرتبط ملغى.", "DELIVERY_CANCELLED");
            if (rcpt.ReceiptStatus == "Full")
                throw new DomainException("السند منفذ بالكامل مسبقاً.", "RECEIPT_FULL");
            if (rcpt.Status != DocStatuses.Issued && rcpt.Status != DocStatuses.Completed)
                throw new DomainException("لا يمكن الاستلام قبل إصدار أمر التسليم.", "RECEIPT_NOT_ISSUED");
            var whFg = rcpt.WarehouseId;
            var orderCust = Db.ProductionOrders.Where(o => o.Id == rcpt.OrderId).Select(o => o.CustomerId).FirstOrDefault();
            double totalReceived = 0;
            // لا نحجز رقماً ولا نزيد عداد المتابعة قبل التأكد من وجود كمية موجبة
            // على بند ما زال له متبقي — وهذا الفحص داخل المعاملة أيضاً لمعالجة
            // سباق استلامين وصلا بعد أن أكمل أحدهما السند.
            if (receivedByItemId != null && !receivedByItemId.Any(x => x.Value > 0.001))
                return OpResult.Fail("لم تُدخل أي كمية مستلمة.");
            if (receivedByItemId != null && receivedByItemId.Keys.Any(id => rcpt.Items.All(i => i.Id != id)))
                throw new DomainException("توجد كمية مرتبطة ببند سند استلام غير تابع لهذا السند.", "RECEIPT_ITEM_MISMATCH");
            bool hasPositiveRemaining = receivedByItemId == null
                ? rcpt.Items.Any(i => i.NetWeightKg - i.ReceivedQtyKg > 0.001)
                : rcpt.Items.Any(i => i.NetWeightKg - i.ReceivedQtyKg > 0.001
                    && receivedByItemId.TryGetValue(i.Id, out var requested) && requested > 0.001);
            if (!hasPositiveRemaining)
                return OpResult.Fail("لا توجد كمية موجبة متبقية للاستلام — لم يُحجز رقم سند متابعة.");
            rcpt.ReceiveCount++;
            rcpt.ReceiptNumber ??= Numbering.Next("RCV");
            var voucher = $"{rcpt.ReceiptNumber}#{rcpt.ReceiveCount}"; // لكل سند استلام (متابعة) ترقيم متسلسل
            var recvAcc = new Dictionary<int, double>(); // §B86/H8: مستلَم هذه الدفعة لكل صنف — بندَان لصنف واحد لا يتجاوزا السقف معاً
            // §B96 — المربوط: بنود التسليم للتحديث + مجمّع لكل بند (سندان لبند واحد لا يتجاوزاه معاً)
            var delLines = rcpt.DeliveryId != null
                ? Db.ProductionDeliveryItems.Where(i => i.DeliveryId == rcpt.DeliveryId.Value).ToList()
                : new List<ProductionDeliveryItem>();
            var recvAccLine = new Dictionary<int, double>();
            foreach (var item in rcpt.Items)
            {
                double remaining = item.NetWeightKg - item.ReceivedQtyKg;
                if (remaining <= 0.001) continue;
                // null يبقى توافقاً مع استدعاء «استلام كامل» الصريح في الخدمات القديمة.
                // أما القاموس الجزئي فالسطر الغائب = صفر، وهو ما ترسله شاشة المخزن.
                double recv = receivedByItemId == null
                    ? remaining
                    : (receivedByItemId.TryGetValue(item.Id, out var v) ? v : 0);
                if (recv <= 0) continue;
                if (recv > remaining + 0.001)
                    throw new DomainException($"الكمية المستلمة أكبر من المتبقي للبند ({remaining:N1} كجم).", "OVER_RECEIPT");
                // §B96 — المربوط: سقف بند التسليم أولاً (رسالة دقيقة) ثم السقف الفيزيائي الموحد (شبكة أمان ضد المباشر)
                ProductionDeliveryItem delLine = null;
                double recvLineBeforeThisItem = 0;
                double delLineReceivedBeforeThisItem = 0;
                if (item.DeliveryItemId != null)
                {
                    delLine = delLines.FirstOrDefault(l => l.Id == item.DeliveryItemId.Value)
                        ?? throw new DomainException("بند أمر التسليم المربوط غير موجود.", "NO_DELIVERY_LINE");
                    delLineReceivedBeforeThisItem = delLine.ReceivedQtyKg;
                    recvAccLine.TryGetValue(delLine.Id, out recvLineBeforeThisItem);
                    if (delLine.ReceivedQtyKg + recvLineBeforeThisItem + recv > delLine.QtyKg + 0.001)
                        throw new DomainException(
                            $"⛔ الاستلام يتجاوز بند أمر التسليم.\nالبند: {delLine.QtyKg:N1} كجم | المستلَم منه: {delLine.ReceivedQtyKg + recvLineBeforeThisItem:N1} | المطلوب: {recv:N1}",
                            "OVER_DELIVERY");
                    recvAccLine[delLine.Id] = recvLineBeforeThisItem + recv;
                    if (delLine.OrderId is int capOrder)
                    {
                        double producedCap = Db.ProductionOrderItems.AsNoTracking()
                            .Where(o => o.OrderId == capOrder && o.ProductId == item.ProductId)
                            .Sum(o => o.ProducedQtyKg);
                        // §بنود التسليم لنفس الأمر (مجموعة محلية — Contains تُترجم إلى IN)
                        var capLineIds = delLines.Where(l => l.OrderId == capOrder).Select(l => l.Id).ToHashSet();
                        double receivedAll = Db.FinishedGoodsReceiptItems.AsNoTracking()
                            .Join(Db.FinishedGoodsReceipts.AsNoTracking(), i => i.ReceiptId, r => r.Id, (i, r) => new { i, r })
                            .Where(x => x.r.Status != DocStatuses.Cancelled && x.i.Id != item.Id
                                && ((x.i.DeliveryItemId != null && capLineIds.Contains(x.i.DeliveryItemId.Value))
                                    || (x.i.DeliveryItemId == null && x.r.OrderId == capOrder)))
                            .Where(x => x.i.ProductId == item.ProductId)
                            .Sum(x => x.i.ReceivedQtyKg);
                        recvAcc.TryGetValue(item.ProductId, out var recvThisCall2);
                        if (receivedAll + item.ReceivedQtyKg + recvThisCall2 + recv > producedCap + 0.001)
                            throw new DomainException(
                                $"الاستلام يتجاوز المنتَج الفعلي للصنف (مباشر + مربوط معاً).\nالمنتَج: {producedCap:N1} كجم | المستلَم: {receivedAll + item.ReceivedQtyKg + recvThisCall2:N1} | المطلوب: {recv:N1}",
                                "EXCEED_ORDER_QTY");
                        recvAcc[item.ProductId] = recvThisCall2 + recv;
                    }
                    delLine.ReceivedQtyKg += recv;
                }
                else
                {
                // §B86/H8: سقف المنتَج يُفحص عند الاستلام أيضاً — مسودتان معاً قد تتجاوزا المنتَج الفعلي
                double receivedOthers = Db.FinishedGoodsReceiptItems
                    .Join(Db.FinishedGoodsReceipts, i => i.ReceiptId, r => r.Id, (i, r) => new { i, r })
                    .Where(x => x.r.OrderId == rcpt.OrderId && x.i.ProductId == item.ProductId
                        && x.r.Status != DocStatuses.Cancelled && x.i.Id != item.Id)
                    .Sum(x => x.i.ReceivedQtyKg);
                double producedCap = Db.ProductionOrderItems.AsNoTracking()
                    .Where(o => o.OrderId == rcpt.OrderId && o.ProductId == item.ProductId)
                    .Sum(o => o.ProducedQtyKg);
                recvAcc.TryGetValue(item.ProductId, out var recvThisCall);
                if (receivedOthers + item.ReceivedQtyKg + recvThisCall + recv > producedCap + 0.001)
                    throw new DomainException(
                        $"الاستلام يتجاوز المنتَج الفعلي للصنف.\nالمنتَج: {producedCap:N1} كجم | المستلَم في سندات أخرى: {receivedOthers:N1} | هذا السند بعد الاستلام: {item.ReceivedQtyKg + recvThisCall + recv:N1}",
                        "EXCEED_ORDER_QTY");

                recvAcc[item.ProductId] = recvThisCall + recv;
                }
                // PRD-06 — الكراتين تُرحّل بالفرق بين هدفين تراكميين على بند التسليم،
                // لا بتقريب مستقل لكل استلام. لذلك 1.5 + 1.5 كرتوناً لا تصبح 2 + 2.
                double priorLineKg = 0;
                int linePackages = 0;
                double lineQtyKg = 0;
                if (delLine != null)
                {
                    priorLineKg = delLineReceivedBeforeThisItem + recvLineBeforeThisItem;
                    linePackages = delLine.PackageCount;
                    lineQtyKg = delLine.QtyKg;
                }
                int previousTarget = linePackages > 0 && lineQtyKg > 0
                    ? (int)Math.Round(linePackages * priorLineKg / lineQtyKg, MidpointRounding.AwayFromZero) : 0;
                item.ReceivedQtyKg += recv;
                totalReceived += recv;
                double cumulativeLineKg = priorLineKg + recv;
                int currentTarget = linePackages > 0 && lineQtyKg > 0
                    ? (int)Math.Round(linePackages * cumulativeLineKg / lineQtyKg, MidpointRounding.AwayFromZero)
                    : (item.PackageCount > 0 && item.NetWeightKg > 0
                        ? (int)Math.Round(item.PackageCount * item.ReceivedQtyKg / item.NetWeightKg, MidpointRounding.AwayFromZero) : 0);
                int pkgRecv = Math.Max(0, currentTarget - previousTarget);
                PostStockMovement(whFg, MovementType.Inbound, recv, pkgRecv,
                    ReferenceDocType.FinishedGoodsReceipt, voucher,
                    productId: item.ProductId, lotId: item.LotId, orderId: rcpt.OrderId,
                    customerId: item.CustomerId ?? orderCust, packagingTypeId: item.PackagingTypeId,
                    notes: $"استلام إنتاج تام — سند {rcpt.ReceiptNumber}");
            }

            if (totalReceived <= 0.001) return OpResult.Fail("لم تُدخل أي كمية مستلمة.");

            bool full = rcpt.Items.All(i => i.ReceivedQtyKg + 0.001 >= i.NetWeightKg);
            rcpt.ReceiptStatus = full ? "Full" : "Partial";
            rcpt.IsApproved = true;
            rcpt.Status = DocStatuses.Completed;
            // §B96 — عكس التقدم على أمر التسليم المربوط
            if (rcpt.DeliveryId != null)
            {
                var delivery = Db.ProductionDeliveries.Include(d => d.Items).FirstOrDefault(d => d.Id == rcpt.DeliveryId.Value);
                if (delivery != null)
                {
                    bool dFull = delivery.Items.Count > 0 && delivery.Items.All(i => i.ReceivedQtyKg + 0.001 >= i.QtyKg);
                    bool dAny = delivery.Items.Any(i => i.ReceivedQtyKg > 0.001);
                    delivery.ReceiptStatus = dFull ? "Full" : (dAny ? "Partial" : "None");
                    if (dFull) delivery.Status = DocStatuses.Completed;
                }
            }
            rcpt.ApprovedBy = Session?.UserId;
            rcpt.ApprovedDate = DateTime.Now;

            // إغلاق أمر الإنتاج تلقائياً عند الاكتمال الكامل
            if (full)
            {
                var order = Db.ProductionOrders.Include(o => o.Items).FirstOrDefault(o => o.Id == rcpt.OrderId);
                if (order != null && order.Items.All(i => i.IsClosed || i.ProducedQtyKg + 0.001 >= i.PlannedQtyKg))
                {
                    order.Status = DocStatuses.Completed;
                    order.IsClosed = true;
                    order.ClosedDate = DateTime.Now;
                }
                else if (order != null) order.Status = DocStatuses.PendingDelivery; // §B85/M9: ثابت معتمد بدل القيمة الحرة
            }

            Db.SaveChanges();
            return OpResult.Success(full
                ? "تم الاستلام الكامل وتقييد كامل الكمية في مخزن الإنتاج التام."
                : $"تم الاستلام الجزئي ({totalReceived:N1} كجم) وتقييدها في مخزن التام.", rcpt.Id, rcpt.ReceiptNumber);
        });
    }

    /// <summary>إلغاء السند يعكس الأرصدة بدقة ويحذف حركاته (§6).</summary>
    public OpResult Unapprove(int receiptId)
    {
        Require("finishedgoods", "Cancel");
        var rcpt = Db.FinishedGoodsReceipts.Include(r => r.Items).FirstOrDefault(r => r.Id == receiptId);
        if (rcpt == null) return OpResult.Fail("السند غير موجود.");
        if (!rcpt.IsApproved) return OpResult.Fail("السند غير معتمد.");

        return RunOp(() =>
        {
            Db.ChangeTracker.Clear();
            rcpt = Db.FinishedGoodsReceipts.Include(r => r.Items).FirstOrDefault(r => r.Id == receiptId)
                ?? throw new DomainException("السند غير موجود.", "RECEIPT_MISSING");
            if (!rcpt.IsApproved) throw new DomainException("السند غير معتمد.", "RECEIPT_NOT_APPROVED");
            var delivery = rcpt.DeliveryId != null
                ? Db.ProductionDeliveries.Include(d => d.Items).FirstOrDefault(d => d.Id == rcpt.DeliveryId.Value)
                : null;
            if (delivery == null || delivery.Status == DocStatuses.Cancelled)
                throw new DomainException("أمر التسليم المرتبط غير موجود أو ملغى.", "DELIVERY_CANCELLED");
            var whFg = rcpt.WarehouseId;
            var orderCust = Db.ProductionOrders.Where(o => o.Id == rcpt.OrderId).Select(o => o.CustomerId).FirstOrDefault();
            var prefix = rcpt.ReceiptNumber ?? rcpt.DocumentNumber;
            // §إصلاح حرج — الإلغاء بقيد عكسي لا بحذف دفتر الأستاذ.
            // كان يحذف الحركات بـ StartsWith(prefix):
            //  • يدمّر سجلّاً إلحاقياً (قرارهم #48: «إلحاقي غير قابل للتعديل»)
            //  • وتصادم بادئات: عند السند رقم 10000 يصبح RCV-...-1000 بادئة له فيحذف حركاته
            //  • وكان يبحث الرصيد بلا CustomerId بينما Receive يكتب به ← قد يطرح من صف آخر
            // §B96 — أمر التسليم المربوط محمّل قبل تصفير الكميات ليُعكس عنه المستلَم بدقة.
            int seq = 0;
            foreach (var item in rcpt.Items.Where(i => i.ReceivedQtyKg > 0))
            {
                int pkgBack = Db.InventoryTransactions
                    .Where(t => t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt
                        && t.MovementType == MovementType.Inbound
                        && t.WarehouseId == whFg
                        && t.ReferenceDocNumber.StartsWith(prefix + "#")
                        && t.ProductId == item.ProductId && t.LotId == item.LotId
                        && t.CustomerId == (item.CustomerId ?? orderCust)
                        && t.PackagingTypeId == item.PackagingTypeId)
                    .Sum(t => t.PackageCount);
                seq++;
                PostStockMovement(whFg, MovementType.Outbound, item.ReceivedQtyKg, pkgBack,
                    ReferenceDocType.FinishedGoodsReceipt, $"{prefix}#REV{seq}",
                    productId: item.ProductId, lotId: item.LotId, orderId: rcpt.OrderId,
                    customerId: item.CustomerId ?? orderCust, packagingTypeId: item.PackagingTypeId,
                    notes: $"إلغاء سند الاستلام {rcpt.ReceiptNumber} — قيد عكسي");
                if (delivery != null && item.DeliveryItemId != null)
                {
                    var back = delivery.Items.FirstOrDefault(l => l.Id == item.DeliveryItemId.Value);
                    if (back != null) back.ReceivedQtyKg = Math.Max(0, back.ReceivedQtyKg - item.ReceivedQtyKg);
                }
                item.ReceivedQtyKg = 0;
            }
            rcpt.IsApproved = false;
            rcpt.ReceiptStatus = "None";
            rcpt.Status = DocStatuses.Issued;
            // §B96 — إعادة احتساب حالة أمر التسليم المربوط وإعادة فتحه إن اكتمل سابقاً
            string delReopenMsg = "";
            if (delivery != null)
            {
                bool dAny = delivery.Items.Any(i => i.ReceivedQtyKg > 0.001);
                delivery.ReceiptStatus = dAny ? "Partial" : "None";
                if (delivery.Status == DocStatuses.Completed)
                {
                    delivery.Status = DocStatuses.Issued;
                    delReopenMsg = " وأُعيد فتح أمر التسليم (استلامه لم يعد مكتملاً).";
                }
            }
            // §B86/H8: إلغاء آخر سند كامل يعيد فتح الأمر المغلق تلقائياً (التلقائي = Completed+مقفل؛ اليدوي = Closed فلا يُمس)
            string reopenMsg = "";
            bool otherFull = Db.FinishedGoodsReceipts.AsNoTracking()
                .Any(r => r.OrderId == rcpt.OrderId && r.Id != rcpt.Id && r.ReceiptStatus == "Full");
            if (!otherFull)
            {
                var ord = Db.ProductionOrders.FirstOrDefault(o => o.Id == rcpt.OrderId);
                if (ord != null && ord.IsClosed && ord.Status == DocStatuses.Completed)
                {
                    ord.IsClosed = false;
                    ord.ClosedDate = null;
                    reopenMsg = " وأُعيد فتح أمر الإنتاج (تسليمه لم يعد مكتملاً).";
                }
            }
            Db.SaveChanges();
            return OpResult.Success("تم إلغاء السند وعكس أرصدة مخزن التام بالكامل." + reopenMsg + delReopenMsg);
        });
    }
}
