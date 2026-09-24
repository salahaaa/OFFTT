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

    public List<ActualDeliveryOrderDto> GetActualDeliveryOrders(int? selectedOrderId = null)
    {
        // الشاشة العامة تعرض أوامر اليوم غير المقفلة فقط. أما بطاقة أمر الإنتاج التي
        // فتحت منها نافذة الإقفال فتمرر رقم الأمر، حتى لو كان تاريخ الجدولة سابقاً،
        // ثم يبقى الأمر المقفول ظاهراً مرة واحدة لإتاحة إنشاء أمر تسليم الإنتاج.
        var orderService = new ProductionOrderService(Db, Session, Numbering);
        var sheet = selectedOrderId.HasValue
            ? orderService.GetScheduledProduction()
            : orderService.GetTodayProduction();
        var rows = sheet.Rows.Where(r => r.OrderId != null
            && (!r.DayClosed || (selectedOrderId.HasValue && r.OrderId == selectedOrderId.Value))
            && (!selectedOrderId.HasValue || r.OrderId == selectedOrderId.Value)).ToList();
        var result = new List<ActualDeliveryOrderDto>();
        foreach (var group in rows.GroupBy(r => r.OrderId.Value))
        {
            var order = Db.ProductionOrders.AsNoTracking().Single(o => o.Id == group.Key);
            var lines = Db.ProductionOrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).OrderBy(i => i.Id).ToList();
            var exe = Db.ProductionExecutions.AsNoTracking().Include(e => e.Downtimes).Include(e => e.ByProducts)
                .FirstOrDefault(e => e.OrderId == order.Id && e.IsDayClosed);
            var qc = exe == null ? null : Db.QualityChecks.AsNoTracking().FirstOrDefault(q => q.ExecutionId == exe.Id);
            var productionDelivery = exe == null ? null : Db.ProductionDeliveries.AsNoTracking()
                .FirstOrDefault(d => d.SourceType == DeliverySources.FromActual && d.SourceId == exe.Id && d.Status != DocStatuses.Cancelled);
            var first = group.First();
            var customerNames = group.Select(r => r.CustomerName).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal).ToList();
            var customerSummary = customerNames.Count <= 1
                ? customerNames.FirstOrDefault() ?? "—"
                : $"عدة عملاء ({customerNames.Count}): {string.Join("، ", customerNames)}";
            // §v1.50.24: أمر مسودة معتمد مقبول للتسجيل — الحفظ يبدأ تنفيذه تلقائياً،
            // فلا يحتاج المستخدم خطوة «بدء التنفيذ» من شاشة الأوامر.
            bool can = exe == null && order.IsApproved && !order.IsClosed;
            result.Add(new ActualDeliveryOrderDto
            {
                OrderId = order.Id, Label = $"{order.DocumentNumber} — {customerSummary} — {first.ShiftName}",
                Customer = customerSummary, Shift = first.ShiftName, PlanNumber = first.PlanNumber,
                ExecutionId = exe?.Id ?? 0, ProductionDeliveryId = productionDelivery?.Id ?? 0,
                Recorded = exe != null, CanRecord = can,
                CanCreateDelivery = exe != null && productionDelivery == null,
                Status = productionDelivery != null ? $"تم إنشاء أمر تسليم الإنتاج {productionDelivery.DocumentNumber} — {DocStatuses.ToArabic(productionDelivery.Status)}"
                    : exe != null ? "إنتاج فعلي محفوظ — لا يوجد استلام مخزني تلقائي؛ أنشئ أمر التسليم من هنا"
                    : can ? "أدخل الفعلي فقط؛ المخطط ثابت من خطة اليوم" : "يلزم أمر اليوم المعتمد غير المقفل — اختر الأمر الصحيح من القائمة",
                ReceiptNumber = null, QualityNumber = qc?.DocumentNumber,
                ProductionDeliveryNumber = productionDelivery?.DocumentNumber,
                ProductionDeliveryStatus = productionDelivery == null ? null : DocStatuses.ToArabic(productionDelivery.Status),
                ConsumedRawKg = exe?.ConsumedRawKg ?? 0, DowntimeHours = exe?.Downtimes.Sum(d => d.Hours) ?? 0,
                DowntimeReason = exe == null ? null : string.Join("؛ ", exe.Downtimes.Select(d => d.ReasonAr)), Notes = exe?.ClosingNotes,
                RecordedByProductDefinitions = exe == null ? new() : exe.ByProducts.Select(b => Db.ByProducts.AsNoTracking()
                    .Where(d => d.Id == b.ByProductId).Select(d => new ActualByProductDefinitionDto { Id = d.Id, Name = d.ByProductNameAr, Unit = d.UnitOfMeasure }).Single()).ToList(),
                ByProducts = exe?.ByProducts.Select(b => new ByProductQtyDto { ByProductId = b.ByProductId, QtyKg = (double)b.Qty }).ToList() ?? new(),
                Items = lines.Select(i =>
                {
                    var planRow = group.Single(r => r.PlanItemId == i.PlanItemId);
                    return new ActualDeliveryItemDto
                    {
                        OrderItemId = i.Id,
                        Product = planRow.ProductName,
                        Customer = planRow.CustomerName,
                        PlannedCartons = i.PlannedCartons,
                        ActualCartons = i.ProducedCartons
                    };
                }).ToList()
            });
        }
        return result;
    }

    public OpResult SaveActualProduction(ActualProductionDto input)
    {
        // One operation, not an implicit grant of warehouse or quality permissions.
        Require("production", "Create"); Require("execution", "Edit");
        // تسجيل الفعلي ينتهي عند جلسة التنفيذ. لا يمنح ضمنياً صلاحيات المخازن
        // ولا ينشئ أمر/سند استلام تام؛ تلك مرحلة مستقلة بعد تحرير أمر التسليم.
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
            var canonical = new ProductionOrderService(Db, Session, Numbering).GetScheduledProduction().Rows
                .Where(r => r.OrderId == input.OrderId && !r.DayClosed).ToList();
            if (canonical.Count == 0)
                throw new DomainException("الأمر ليس نسخة مطابقة لخطة معتمدة مجدولة وغير مقفلة؛ حدّث الشاشة.");
            if (canonical.Any(r => !UiFormat.TryParseDate(r.ScheduledDate, out var day) || day.Date > Db.BusinessNow.Date))
                throw new DomainException("لا يمكن تسجيل تنفيذ قبل يوم الخطة؛ يمكن تسجيل الأمر المتأخر حتى تاريخ اليوم.");
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
            var execution = new ExecutionService(Db, Session, Numbering)
                { JoinParentTransaction = true, RecordingActualDelivery = true };
            void Must(OpResult r) { if (!r.Ok) throw new DomainException(r.Message); }
            Must(execution.CloseProductionDay(order.Id, actual.Sum(i => i.ProducedKg), actual.Sum(i => i.ProducedCartons),
                0, 0, 0, false, input.DowntimeHours > 0 ? new() { new DowntimeDto { Hours = input.DowntimeHours, ReasonAr = input.DowntimeReason.Trim() } } : new(),
                true, input.Notes?.Trim(), secondary, input.ConsumedRawKg, actual));
            var exe = Db.ProductionExecutions.Single(e => e.OrderId == order.Id && e.IsDayClosed);
            var qc = Db.QualityChecks.SingleOrDefault(q => q.ExecutionId == exe.Id);
            if (qc != null)
            {
                qc.TotalCheckedCartons = actual.Sum(i => i.ProducedCartons);
                qc.CheckDate = Db.BusinessNow;
                qc.ExpectedCheckDate = Db.BusinessNow.Date.AddDays(2);
                foreach (var row in actual.Where(i => i.ProducedCartons > 0))
                {
                    var source = order.Items.Single(s => s.Id == row.OrderItemId);
                    qc.Items.Add(new QualityCheckItem
                    {
                        ProductId = source.ProductId, LotId = source.LotId,
                        CheckedCartons = 0, CheckedQtyKg = 0,
                        Notes = $"مرسل للفحص: {row.ProducedCartons} كرتون / {row.ProducedKg:N1} كجم — لا كمية مقبولة قبل إدخال نتيجة الجودة"
                    });
                }
                Db.SaveChanges();
            }
            // لا يُنشأ هنا سند استلام مخزني ولا تُرحّل أرصدة مخزن التام.
            // أمر تسليم الإنتاج يُنشأ لاحقاً صراحةً من التنفيذ الفعلي بواسطة مدير الإنتاج.
            _audit.Log("الإنتاج الفعلي", "تسجيل الإنتاج الفعلي فقط — دون إنشاء استلام مخزني", "ProductionExecution", exe.DocumentNumber, exe.Id,
                newValues: new { order.Id, PlannedCartons = order.Items.Sum(i => i.PlannedCartons), exe.ActualCartons,
                    Difference = order.Items.Sum(i => i.PlannedCartons) - exe.ActualCartons, exe.ConsumedRawKg, QualityId = qc?.Id });
            return OpResult.Success($"تم تسجيل الإنتاج الفعلي {exe.ActualCartons:N0} كرتون؛ الفرق {order.Items.Sum(i => i.PlannedCartons) - exe.ActualCartons:N0}. لا يوجد استلام مخزني تلقائي — أنشئ أمر تسليم الإنتاج من التنفيذ ثم حرره للمخزن.", exe.Id, exe.DocumentNumber);
        });
    }
}
