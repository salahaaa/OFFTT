using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

public partial class ProductionDeliveryService
{
    protected override bool RetryTransactionDeadlocks => true;
    protected override System.Data.IsolationLevel TransactionIsolation => System.Data.IsolationLevel.Serializable;

    public List<ActualByProductDefinitionDto> GetActualByProducts()
    {
        if (Session == null || !Session.Can("production", "View")) throw new PermissionDeniedException("عرض الإنتاج");
        // The configured secondary-output definitions (ByProducts master), NOT unrelated Products IDs.
        return Db.ByProducts.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id)
            .Select(b => new ActualByProductDefinitionDto { Id = b.Id, Name = b.ByProductNameAr, Unit = b.UnitOfMeasure }).ToList();
    }

    public List<ActualDeliveryOrderDto> GetActualDeliveryOrders()
    {
        var sheet = new ProductionOrderService(Db, Session, Numbering).GetTodayProduction();
        var result = new List<ActualDeliveryOrderDto>();
        foreach (var group in sheet.Rows.Where(r => r.OrderId != null).GroupBy(r => r.OrderId.Value))
        {
            var order = Db.ProductionOrders.AsNoTracking().Single(o => o.Id == group.Key);
            var lines = Db.ProductionOrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).OrderBy(i => i.Id).ToList();
            var exe = Db.ProductionExecutions.AsNoTracking().Include(e => e.Downtimes).Include(e => e.ByProducts)
                .FirstOrDefault(e => e.OrderId == order.Id && e.IsDayClosed);
            var qc = exe == null ? null : Db.QualityChecks.AsNoTracking().FirstOrDefault(q => q.ExecutionId == exe.Id);
            var receipt = qc == null ? null : Db.FinishedGoodsReceipts.AsNoTracking().FirstOrDefault(r => r.QualityCheckId == qc.Id && r.ReceiptStatus == "Full" && r.Status != DocStatuses.Cancelled);
            var first = group.First();
            // §v1.50.24: أمر مسودة معتمد مقبول للتسجيل — الحفظ يبدأ تنفيذه تلقائياً،
            // فلا يحتاج المستخدم خطوة «بدء التنفيذ» من شاشة الأوامر.
            bool can = exe == null && order.IsApproved && !order.IsClosed;
            result.Add(new ActualDeliveryOrderDto
            {
                OrderId = order.Id, Label = $"{order.DocumentNumber} — {first.CustomerName} — {first.ShiftName}",
                Customer = first.CustomerName, Shift = first.ShiftName, PlanNumber = first.PlanNumber,
                Recorded = exe != null, CanRecord = can,
                Status = receipt != null ? "تم تسجيل الفعلي واستلامه مخزنيًا — نتيجة الجودة مستقلة"
                    : exe != null ? "تنفيذ محفوظ سابقًا — لا إعادة تسجيل أو ترحيل تلقائي للسجلات السابقة"
                    : can ? "أدخل الفعلي فقط؛ المخطط ثابت من خطة اليوم" : "يلزم أمر اليوم المعتمد غير المقفل — اختر الأمر الصحيح من القائمة",
                ReceiptNumber = receipt?.ReceiptNumber, QualityNumber = qc?.DocumentNumber,
                ConsumedRawKg = exe?.ConsumedRawKg ?? 0, DowntimeHours = exe?.Downtimes.Sum(d => d.Hours) ?? 0,
                DowntimeReason = exe == null ? null : string.Join("؛ ", exe.Downtimes.Select(d => d.ReasonAr)), Notes = exe?.ClosingNotes,
                RecordedByProductDefinitions = exe == null ? new() : exe.ByProducts.Select(b => Db.ByProducts.AsNoTracking()
                    .Where(d => d.Id == b.ByProductId).Select(d => new ActualByProductDefinitionDto { Id = d.Id, Name = d.ByProductNameAr, Unit = d.UnitOfMeasure }).Single()).ToList(),
                ByProducts = exe?.ByProducts.Select(b => new ByProductQtyDto { ByProductId = b.ByProductId, QtyKg = (double)b.Qty }).ToList() ?? new(),
                Items = lines.Select(i => new ActualDeliveryItemDto
                {
                    OrderItemId = i.Id, Product = group.Single(r => r.PlanItemId == i.PlanItemId).ProductName,
                    Customer = first.CustomerName, PlannedCartons = i.PlannedCartons, ActualCartons = i.ProducedCartons
                }).ToList()
            });
        }
        return result;
    }

    public OpResult SaveActualProduction(ActualProductionDto input)
    {
        // One operation, not an implicit grant of warehouse or quality permissions.
        Require("production", "Create"); Require("execution", "Edit");
        Require("finishedgoods", "Create"); Require("finishedgoods", "Approve");
        return RunOp(() =>
        {
            if (input == null) throw new DomainException("بيانات التنفيذ غير موجودة.");
            if (!double.IsFinite(input.ConsumedRawKg) || input.ConsumedRawKg <= 0)
                throw new DomainException("أدخل الخام المستهلك فعليًا بالكجم؛ لا يُستنتج من المخطط أو الإنتاج.");
            if (!double.IsFinite(input.DowntimeHours) || input.DowntimeHours < 0 || input.DowntimeHours > 24)
                throw new DomainException("ساعات التوقف يجب أن تكون بين صفر و24 ساعة.");
            if (input.DowntimeHours > 0 && string.IsNullOrWhiteSpace(input.DowntimeReason))
                throw new DomainException("أدخل سبب التوقف في خانة السبب الواحدة.");
            if (input.DowntimeHours == 0 && !string.IsNullOrWhiteSpace(input.DowntimeReason))
                throw new DomainException("أدخل ساعات التوقف أو أفرغ سببه عندما لا يوجد توقف.");
            // Fresh authoritative read under SERIALIZABLE: stale UI/context and competing clients cannot repost.
            Db.ChangeTracker.Clear();
            var canonical = new ProductionOrderService(Db, Session, Numbering).GetTodayProduction().Rows
                .Where(r => r.OrderId == input.OrderId).ToList();
            if (canonical.Count == 0) throw new DomainException("الأمر ليس نسخة مطابقة لخطة معتمدة مجدولة لليوم؛ حدّث الشاشة.");
            var order = Db.ProductionOrders.Include(o => o.Items).Single(o => o.Id == input.OrderId);
            // §v1.50.24: أمر مسودة معتمد يبدأ تنفيذه تلقائياً ضمن نفس العملية —
            // لا مطالبة المستخدم بالذهاب إلى شاشة الأوامر لبدء التنفيذ أولاً.
            if (order.Status == DocStatuses.Draft && order.IsApproved && !order.IsClosed)
                order.Status = DocStatuses.InProgress;
            if (!order.IsApproved || order.IsClosed || order.Status is not (DocStatuses.InProgress or DocStatuses.Stopped))
                throw new DomainException("يلزم أمر اليوم المعتمد غير المقفل (الملغى أو المقفل لا يُسجَّل).");
            if (Db.ProductionExecutions.Any(e => e.OrderId == order.Id && e.IsDayClosed)
                || order.Items.Any(i => i.ProducedCartons != 0 || i.ProducedQtyKg != 0 || i.IsClosed)
                || Db.QualityChecks.Any(q => q.OrderId == order.Id)
                || Db.FinishedGoodsReceipts.Any(r => r.OrderId == order.Id && r.Status != DocStatuses.Cancelled))
                throw new DomainException("يوجد تنفيذ أو فحص أو استلام سابق للأمر؛ لا يسمح بتكرار التسجيل أو مزج الدورة السابقة.");
            if (input.Items == null || input.Items.Any(i => i == null)
                || input.Items.Select(i => i.OrderItemId).Distinct().Count() != input.Items.Count
                || !input.Items.Select(i => i.OrderItemId).ToHashSet().SetEquals(order.Items.Select(i => i.Id)))
                throw new DomainException("يلزم تسجيل بنود الأمر نفسها مرة واحدة، دون إضافة صنف أو إسقاط بند.");
            if (order.Items.Any(i => i.LotId == null || i.PlannedCartons <= 0 || !double.IsFinite(i.PlannedQtyKg) || i.PlannedQtyKg <= 0))
                throw new DomainException("بيانات الخطة لا تتضمن دفعات الخام وكميات كرتونية سليمة؛ أصلح المرجع رسميًا دون اختيار صنف هنا.");
            var actual = new List<CloseItemQtyDto>();
            foreach (var row in input.Items)
            {
                var source = order.Items.Single(i => i.Id == row.OrderItemId);
                if (row.ActualCartons < 0 || row.ActualCartons > source.PlannedCartons)
                    throw new DomainException("الفعلي لا يكون سالبًا ولا يتجاوز المخطط؛ لا تعديل تلقائي للخطة.");
                actual.Add(new CloseItemQtyDto { OrderItemId = source.Id, ProducedCartons = row.ActualCartons,
                    ProducedKg = row.ActualCartons * (source.PlannedQtyKg / source.PlannedCartons) });
            }
            if (actual.Sum(i => (long)i.ProducedCartons) is <= 0 or > int.MaxValue)
                throw new DomainException("أدخل كمية إنتاج فعلية قابلة للتسليم.");
            var secondary = input.ByProducts ?? new();
            if (secondary.Any(b => b == null || !double.IsFinite(b.QtyKg) || b.QtyKg <= 0 || b.QtyKg >= 1e15 || Math.Abs(b.QtyKg - Math.Round(b.QtyKg, 4)) > 1e-9 || Math.Round(b.QtyKg, 4) <= 0)
                || secondary.Select(b => b.ByProductId).Distinct().Count() != secondary.Count)
                throw new DomainException("اختر كل مخرج ثانوي مرة واحدة بكمية موجبة صالحة للتخزين.");
            var definitions = GetActualByProducts().Select(b => b.Id).ToHashSet();
            if (secondary.Any(b => !definitions.Contains(b.ByProductId)))
                throw new DomainException("المخرج الثانوي غير موجود أو موقوف في قائمة التعريفات.");
            var execution = new ExecutionService(Db, Session, Numbering, new PlanningService(Db, Session, Numbering))
                { JoinParentTransaction = true, RecordingActualDelivery = true };
            void Must(OpResult r) { if (!r.Ok) throw new DomainException(r.Message); }
            Must(execution.CloseProductionDay(order.Id, actual.Sum(i => i.ProducedKg), actual.Sum(i => i.ProducedCartons),
                0, 0, 0, false, input.DowntimeHours > 0 ? new() { new DowntimeDto { Hours = input.DowntimeHours, ReasonAr = input.DowntimeReason.Trim() } } : new(),
                true, input.Notes?.Trim(), secondary, input.ConsumedRawKg, actual));
            var exe = Db.ProductionExecutions.Single(e => e.OrderId == order.Id && e.IsDayClosed);
            var qc = Db.QualityChecks.Single(q => q.ExecutionId == exe.Id);
            qc.TotalCheckedCartons = actual.Sum(i => i.ProducedCartons);
            qc.CheckDate = Db.BusinessNow; qc.ExpectedCheckDate = Db.BusinessNow.Date.AddDays(2);
            var received = actual.Where(i => i.ProducedCartons > 0).Select(i =>
            {
                var source = order.Items.Single(s => s.Id == i.OrderItemId);
                return new FinishedGoodsItemDto { ProductId = source.ProductId, LotId = source.LotId,
                    CustomerId = source.CustomerId ?? order.CustomerId, PackagingTypeId = source.PackagingTypeId,
                    PackageCount = i.ProducedCartons, NetWeightKg = i.ProducedKg };
            }).GroupBy(i => new { i.ProductId, i.LotId, i.CustomerId, i.PackagingTypeId })
            .Select(g => new FinishedGoodsItemDto { ProductId = g.Key.ProductId, LotId = g.Key.LotId,
                CustomerId = g.Key.CustomerId, PackagingTypeId = g.Key.PackagingTypeId,
                PackageCount = g.Sum(i => i.PackageCount), NetWeightKg = g.Sum(i => i.NetWeightKg) }).ToList();
            // Pending inspection is not a passed result. No accepted quantity is invented.
            foreach (var row in received)
                qc.Items.Add(new QualityCheckItem { ProductId = row.ProductId, LotId = row.LotId,
                    CheckedCartons = 0, CheckedQtyKg = 0,
                    Notes = $"مرسل للفحص: {row.PackageCount} كرتون / {row.NetWeightKg} كجم — لا كمية مفحوصة أو مقبولة قبل إدخال النتائج" });
            Db.SaveChanges();
            var goods = new FinishedGoodsService(Db, Session, Numbering) { JoinParentTransaction = true };
            var receipt = goods.SaveReceipt(order.Id, qc.Id, Db.BusinessNow.ToString("dd/MM/yyyy"), received); Must(receipt);
            Must(goods.Issue(receipt.Id));
            var lines = Db.FinishedGoodsReceiptItems.Where(i => i.ReceiptId == receipt.Id).ToDictionary(i => i.Id, i => i.NetWeightKg);
            var posted = goods.Receive(receipt.Id, lines); Must(posted);
            _audit.Log("تسليم الإنتاج", "تسجيل الفعلي واستلامه وإرساله للفحص", "ProductionExecution", exe.DocumentNumber, exe.Id,
                newValues: new { order.Id, PlannedCartons = order.Items.Sum(i => i.PlannedCartons), exe.ActualCartons,
                    Difference = order.Items.Sum(i => i.PlannedCartons) - exe.ActualCartons, exe.ConsumedRawKg, ReceiptId = receipt.Id, QualityId = qc.Id });
            return OpResult.Success($"تم تسجيل {exe.ActualCartons:N0} كرتون؛ الفرق {order.Items.Sum(i => i.PlannedCartons) - exe.ActualCartons:N0}. سند الاستلام {posted.DocumentNumber} — الفحص {qc.DocumentNumber} قيد الانتظار، وليس تصريحًا للبيع.", exe.Id, exe.DocumentNumber);
        });
    }
}
