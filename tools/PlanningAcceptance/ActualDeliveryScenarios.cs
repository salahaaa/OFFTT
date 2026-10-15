using DatesErp.Core.Common;
using DatesErp.Application.Services;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Views.Screens;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Real persisted actual delivery through official receiving/plan/order/execution/receipt services, on either provider.</summary>
public static class ActualDeliveryScenarios
{
    public static void Run(IServiceProvider services, Action<bool, string> check, bool concurrency = false)
    {
        using var scope = services.CreateScope(); var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<DatesErpDbContext>();
        var master = sp.GetRequiredService<MasterDataService>(); var plans = sp.GetRequiredService<IPlanningService>();
        var orders = sp.GetRequiredService<IProductionOrderService>(); var receiving = sp.GetRequiredService<IReceivingService>();
        var delivery = sp.GetRequiredService<IProductionDeliveryService>();
        var goods = sp.GetRequiredService<IFinishedGoodsService>(); // §شاشة تسليم الإنتاج للمخازن
        var day = db.BusinessNow.Date; string D(DateTime x) => x.ToString("dd/MM/yyyy");
        void Ok(OpResult r, string label) => check(r.Ok, label + (r.Ok ? "" : ": " + r.Message));
        bool Eq(double a, double b) => Math.Abs(a - b) < .001;
        check(delivery.GetActualDeliveryOrders().Count == 0, "بلا أوامر اليوم: شاشة فارغة، لا كتالوج أصناف تامة");
        var shift = sp.GetRequiredService<IShiftService>().SaveShift(1, "وردية الاختبار", "06:00", "14:00", 8, 0, 8); Ok(shift, "تعريف الوردية الفعلية");
        var raw = master.SaveProductFull(null, "ACT-RAW", "سكري خام", "001", "Raw", "كجم", 20, 0, 0, null!); Ok(raw, "Master: الخام");
        var finished = master.SaveProductFull(null, "ACT-8", "سكري", "002", "Finished", "كرتون", 8, 1, 8, new() { (1, null!, 5000) }, raw.Id); Ok(finished, "Master: سكري 8 كجم مرتبط بالخام");
        var second = master.SaveProductFull(null, "ACT-4", "سكري عبوة أربعة", "002", "Finished", "كرتون", 4, 1, 4, new() { (1, null!, 5000) }, raw.Id); Ok(second, "Master: الصنف الثاني للاختبار المتعدد");
        var bp = master.SaveByProduct(null, "مخرج مختار من تعريف الاختبار", "كجم"); Ok(bp, "تعريف مخرج ثانوي باسم اختباري غير مثبت بالشاشة");
        check(delivery.GetActualByProducts().Any(b => b.Id == bp.Id && b.Unit == "كجم"), "قائمة الشاشة تجلب المخرج الجديد ووحدته من التعريفات");
        var shipment = receiving.SaveShipment(1, null!, null!, new() { new() { ProductId = raw.Id, QtyKg = 100000, PackageCount = 5000, UnitWeightKg = 20, TreatmentRequired = false } });
        Ok(shipment, "استلام خام حقيقي بقرار لا معالجة"); Ok(receiving.ApproveShipment(shipment.Id), "اعتماد الاستلام");
        int lot = db.Lots.AsNoTracking().Single(l => l.ShipmentId == shipment.Id).Id;
        PlanItemDto Line(int product, int cartons, int sourceLot = 0, int customer = 1) => new()
        {
            ProductId = product, SelectedRawProductId = raw.Id, SourceType = "FromReceiving", LotId = sourceLot == 0 ? lot : sourceLot,
            ShipmentId = sourceLot == 0 ? shipment.Id : db.Lots.AsNoTracking().Single(l => l.Id == sourceLot).ShipmentId,
            CustomerId = customer, PlannedCartons = cartons, PlannedQtyKg = cartons * (product == finished.Id ? 8 : 4),
            ScheduledDate = D(day), SuggestedShiftId = 1, SuggestedLineId = 1
        };
        int Create(string name, List<PlanItemDto> lines, bool start = true)
        {
            var plan = plans.SavePlan(name, "Daily", D(day), D(day), 1, 1, lines); Ok(plan, "حفظ " + name); Ok(plans.ApprovePlan(plan.Id), "اعتماد " + name);
            Ok(orders.IssueTodayOrders(), "إصدار اليوم دون تغيير خطة " + name);
            int id = db.ProductionOrders.AsNoTracking().Single(o => o.SourcePlanId == plan.Id).Id;
            Ok(orders.ApproveOrder(id), "اعتماد الأمر " + name); if (start) Ok(orders.StartOrder(id), "بدء الأمر " + name); return id;
        }
        int orderId = Create("مثال سكري 3000", new() { Line(finished.Id, 3000) });
        var source = delivery.GetActualDeliveryOrders().Single(o => o.OrderId == orderId);
        check(source.Items.Count == 1 && source.Items[0].PlannedCartons == 3000 && source.Customer.Length > 0 && source.CanRecord, "سياق الشاشة: العميل وسكري والمخطط 3000 تلقائيًا");
        var ui = new ActualProductionRow(source.Items[0], false);
        check(ui.Actual == "" && ui.Difference == "—", "نموذج صف WPF الفعلي يبدأ فارغًا، لا 3000 ولا صفر مفترض");
        foreach (var invalid in new[] { "", "NaN", "Infinity", "-1", "3001", "2700.5" }) { ui.Actual = invalid; check(!ui.TryQuantity(out _) && ui.Error.Length > 0, "نموذج صف الشاشة يرفض: «" + invalid + "»"); }
        ui.Actual = "2700"; check(ui.TryQuantity(out var q) && q == 2700 && ui.Difference == "300", "محدد صف الشاشة يحسب الفرق فورًا 300 دون تعديل المخطط");
        ActualProductionDto Input(int id, int actual = 2700, double consumed = 22000) => new()
        {
            OrderId = id, Items = db.ProductionOrderItems.AsNoTracking().Where(i => i.OrderId == id).OrderBy(i => i.Id).ToList().Select(i => new ActualProductionItemDto { OrderItemId = i.Id, ActualCartons = actual }).ToList(),
            ConsumedRawKg = consumed, DowntimeHours = 1, DowntimeReason = "عطل ماكينة", Notes = "قبول قاعدة بيانات — الخام 22000 كجم قيمة اختبار صريحة", ByProducts = new() { new() { ByProductId = bp.Id, QtyKg = 200 } }
        };
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(new {
            stock = db.StockBalances.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QtyKg, x.PackageCount }).ToList(),
            lots = db.Lots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.InStockQtyKg, x.ProducedQtyKg, x.ReservedQtyKg }).ToList(),
            items = db.ProductionOrderItems.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PlannedCartons, x.ProducedCartons, x.ProducedQtyKg }).ToList(),
            executions = db.ProductionExecutions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsDayClosed, x.ConsumedRawKg, x.ActualCartons }).ToList(),
            quality = db.QualityChecks.Count(), receipts = db.FinishedGoodsReceipts.Count(), movements = db.InventoryTransactions.Count(), byproducts = db.ExecutionByProducts.Count(), stops = db.ExecutionDowntimes.Count()
        });
        void Reject(Action<ActualProductionDto> mutate, string label)
        {
            var input = Input(orderId); mutate(input); var before = Snapshot(); var result = delivery.SaveActualProduction(input);
            check(!result.Ok && Snapshot() == before, "رفض بلا أثر جزئي: " + label + (result.Ok ? "" : " — " + result.Message));
        }
        Reject(i => i.ConsumedRawKg = 0, "الخام فارغ/صفر، لا افتراض من الخطة");
        Reject(i => i.ConsumedRawKg = double.NaN, "الخام NaN");
        Reject(i => i.ConsumedRawKg = double.PositiveInfinity, "الخام Infinity");
        Reject(i => i.Items[0].ActualCartons = -1, "فعلي سالب");
        Reject(i => i.Items[0].ActualCartons = 3001, "فعلي فوق المخطط");
        Reject(i => i.Items[0].ActualCartons = 0, "لا إنتاج قابل للتسليم");
        Reject(i => i.Items.Add(i.Items[0]), "تكرار بند");
        Reject(i => i.Items.Clear(), "إسقاط بند");
        Reject(i => i.Items[0].OrderItemId = int.MaxValue, "إضافة صنف/بند خارج الأمر");
        Reject(i => i.DowntimeHours = -1, "توقف سالب"); Reject(i => i.DowntimeHours = double.NaN, "توقف NaN");
        Reject(i => i.DowntimeHours = 25, "توقف يتجاوز اليوم"); Reject(i => i.DowntimeReason = " ", "سبب التوقف مفقود");
        Reject(i => i.ByProducts[0].ByProductId = int.MaxValue, "تعريف مخرج مجهول");
        Reject(i => i.ByProducts[0].QtyKg = double.NaN, "مخلفات NaN"); Reject(i => i.ByProducts[0].QtyKg = -200, "مخلفات سالبة");
        Reject(i => i.ByProducts.Add(i.ByProducts[0]), "تكرار المخرج");
        var def = db.ByProducts.Single(b => b.Id == bp.Id); def.IsActive = false; db.SaveChanges();
        Reject(_ => { }, "مخرج موقوف"); def = db.ByProducts.Single(b => b.Id == bp.Id); def.IsActive = true; db.SaveChanges();
        int fgId = db.Warehouses.AsNoTracking().Single(w => w.WarehouseCode == "WFG").Id;
        check(goods.SearchDeliverableOrders("", true).All(o => o.OrderId != orderId), "شاشة التسليم: أمر بلا إنتاج فعلي لا يظهر في بحث الأوامر القابلة للتسليم");
        double rawBefore = db.Lots.AsNoTracking().Single(l => l.Id == lot).InStockQtyKg;
        Ok(delivery.SaveActualProduction(Input(orderId)), "حفظ المثال الكامل في معاملة واحدة");
        db.ChangeTracker.Clear();
        var stored = db.ProductionOrderItems.AsNoTracking().Single(i => i.OrderId == orderId);
        var planned = db.ProductionPlanItems.AsNoTracking().Single(i => i.Id == stored.PlanItemId);
        check(stored.PlannedCartons == 3000 && planned.PlannedCartons == 3000 && Eq(planned.PlannedQtyKg, 24000), "قاعدة البيانات: الخطة والأمر ما زالا 3000 كرتون / 24000 كجم");
        check(stored.ProducedCartons == 2700 && Eq(stored.ProducedQtyKg, 21600) && Eq(planned.ProducedQtyKg, 21600), "الفعلي محفوظ 2700 كرتون / 21600 كجم ومتزامن مع تقدم الخطة");
        var exe = db.ProductionExecutions.AsNoTracking().Single(e => e.OrderId == orderId && e.IsDayClosed);
        check(Eq(exe.ConsumedRawKg, 22000) && Eq(exe.RemainingInHallKg, 0) && Eq(rawBefore - db.Lots.AsNoTracking().Single(l => l.Id == lot).InStockQtyKg, 22000), "الخام الفعلي المصروف 22000 بالضبط: لا مرتجع مختلق من فرق الوزن");
        var stop = db.ExecutionDowntimes.AsNoTracking().Where(d => d.ExecutionId == exe.Id).ToList();
        check(stop.Count == 1 && Eq(stop[0].Hours, 1) && stop[0].ReasonAr == "عطل ماكينة", "توقف واحد ساعة واحدة بسبب عطل ماكينة، لا تكرار لكل صنف");
        var waste = db.ExecutionByProducts.AsNoTracking().Single(b => b.ExecutionId == exe.Id);
        check(waste.ByProductId == bp.Id && waste.Qty == 200 && exe.HashfKg == 0 && exe.NawaKg == 0 && exe.WastageQtyKg == 0, "المخلفات المختارة 200 كجم محفوظة بتعريفها مرة واحدة دون أسماء ثابتة أو مضاعفة");
        var qc = db.QualityChecks.AsNoTracking().Include(c => c.Items).Single(c => c.ExecutionId == exe.Id);
        check(qc.Status == DocStatuses.Submitted && !qc.IsApproved && qc.AcceptedKg == 0 && qc.TotalCheckedCartons == 2700 && Eq(qc.TotalCheckedKg, 21600), "إحالة فورية للجودة: 2700 بانتظار الفحص، ليس اعتمادًا وهميًا");
        check(qc.Items.Count == 1 && qc.Items[0].LotId == lot && qc.Items[0].ProductId == finished.Id && qc.Items[0].AcceptedQtyKg == 0, "الجودة مرتبطة بالمنتج والدفعة الفعليين دون قبول مختلق");
        check(db.FinishedGoodsReceipts.Count(r => r.OrderId == orderId) == 0 && !db.StockBalances.AsNoTracking().Any(b => b.WarehouseId == fgId && b.ProductId == finished.Id), "تسجيل الفعلي لا ينشئ سند تسليم ولا يحرّك مخزون التام — التسليم قرار صريح من شاشته");
        // ── شاشة تسليم الإنتاج للمخازن: بحث ← سياق ← مسودة ← اعتماد (جزئي ثم مكمل) ──
        var found = goods.SearchDeliverableOrders("سكري", true).SingleOrDefault(o => o.OrderId == orderId);
        check(found != null && Eq(found.ProducedKg, 21600) && Eq(found.DeliveredKg, 0) && Eq(found.RemainingKg, 21600) && found.Customers.Length > 0 && found.Products.Contains("سكري"), "بحث الأوامر: رقم/عملاء/أصناف/فعلي 21600/مسلَّم 0/متبقي 21600 (لا رقم فقط)");
        var ctx = goods.GetOrderDeliveryContext(orderId);
        check(ctx != null && ctx.HasActual && ctx.Lines.Count == 1 && Eq(ctx.Lines[0].ProducedKg, 21600) && Eq(ctx.Lines[0].RemainingKg, 21600) && ctx.Lines[0].LotId == lot && ctx.Lines[0].CustomerId == 1 && ctx.Warehouses.Count > 0, "سياق الأمر: سطر واحد بالدفعة والعميل والمتبقي من الحركات المعتمدة");
        check(ctx.ByProducts.Count == 1 && ctx.ByProducts[0].ByProductId == bp.Id && Eq(ctx.ByProducts[0].ProducedQty, 200) && Eq(ctx.ByProducts[0].RemainingQty, 200), "المخرجات الثانوية تُقرأ من جلسة الإقفال: 200 كجم متبقية للتسليم");
        FgDeliveryDraftDto Draft(int id, double kg, int cartons, int? wh, params FgByProductInputDto[] bps) => new()
        {
            OrderId = id, DeliveryDate = D(day), Notes = "قبول — تسليم من الشاشة الجديدة",
            Items = db.ProductionOrderItems.AsNoTracking().Where(i => i.OrderId == id).OrderBy(i => i.Id).ToList()
                .Select(i => new FinishedGoodsItemDto { ProductId = i.ProductId, LotId = i.LotId, CustomerId = i.CustomerId ?? 1, PackagingTypeId = i.PackagingTypeId, NetWeightKg = kg, PackageCount = cartons, WarehouseId = wh }).ToList(),
            ByProducts = bps.ToList()
        };
        var beforeDraft = Snapshot();
        check(!goods.SaveDraft(Draft(orderId, 21608, 2701, fgId), null).Ok && Snapshot() == beforeDraft, "رفض تجاوز الفعلي: 2701 كرتون فوق 2700 المنتَج");
        check(!goods.SaveDraft(Draft(orderId, 8000, 1000, null), null).Ok && Snapshot() == beforeDraft, "رفض بند بلا مخزن إدخال — لا مخزن ثابت افتراضي");
        check(!goods.SaveDraft(Draft(orderId, 8000, 1000, int.MaxValue), null).Ok && Snapshot() == beforeDraft, "رفض مخزن غير موجود/موقوف");
        check(!goods.SaveDraft(Draft(orderId, 8000, 1000, fgId, new FgByProductInputDto { ByProductId = bp.Id, Qty = 201, WarehouseId = fgId }), null).Ok && Snapshot() == beforeDraft, "رفض مخرج ثانوي يتجاوز المسجَّل 200");
        var denied = new FinishedGoodsService(db, new DenyWarehouse(), sp.GetRequiredService<INumberingService>());
        bool permission = false; try { denied.SaveDraft(Draft(orderId, 8000, 1000, fgId), null); } catch (PermissionDeniedException) { permission = true; }
        check(permission && Snapshot() == beforeDraft, "صلاحيات المخزن محفوظة: بلا صلاحية finishedgoods لا مسودة ولا حركة");
        var draft1 = goods.SaveDraft(Draft(orderId, 8000, 1000, fgId, new FgByProductInputDto { ByProductId = bp.Id, Qty = 200, WarehouseId = fgId, Notes = "مخلفات اليوم" }), null);
        Ok(draft1, "حفظ مسودة تسليم جزئي 1000 كرتون / 8000 كجم + المخرج الثانوي 200");
        var rcpt = db.FinishedGoodsReceipts.AsNoTracking().Include(r => r.Items).Include(r => r.ByProducts).Single(r => r.Id == draft1.Id);
        check(rcpt.Status == DocStatuses.Draft && !rcpt.IsApproved && rcpt.Items.Count == 1 && rcpt.Items[0].WarehouseId == fgId && rcpt.ByProducts.Count == 1 && rcpt.ByProducts[0].ReceivedQty == 0 && !string.IsNullOrWhiteSpace(rcpt.DocumentNumber), "المسودة محفوظة برقم مستند ومخزن لكل بند وبلا أي استلام");
        check(db.InventoryTransactions.Count(t => t.OrderId == orderId && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 0 && !db.StockBalances.AsNoTracking().Any(b => b.WarehouseId == fgId && b.ProductId == finished.Id), "المسودة لا تحرّك المخزون");
        var draft1b = goods.SaveDraft(Draft(orderId, 8000, 1000, fgId, new FgByProductInputDto { ByProductId = bp.Id, Qty = 200, WarehouseId = fgId }), draft1.Id);
        check(draft1b.Ok && draft1b.Id == draft1.Id && db.FinishedGoodsReceipts.Count(r => r.OrderId == orderId) == 1, "تعديل المسودة يحدّث نفس السند ولا ينشئ سنداً ثانياً");
        Ok(goods.Approve(draft1.Id), "اعتماد التسليم الجزئي: مستند + حركات في عملية واحدة");
        rcpt = db.FinishedGoodsReceipts.AsNoTracking().Include(r => r.Items).Include(r => r.ByProducts).Single(r => r.Id == draft1.Id);
        check(rcpt.IsApproved && rcpt.ReceiptStatus == "Full" && !string.IsNullOrWhiteSpace(rcpt.ReceiptNumber) && Eq(rcpt.Items[0].ReceivedQtyKg, 8000) && rcpt.ByProducts[0].ReceivedQty == 200, "السند معتمد ومستلَم كاملاً ومرقّم، والمخرج الثانوي مثبَّت فيه");
        var stock = db.StockBalances.AsNoTracking().Single(b => b.WarehouseId == fgId && b.ProductId == finished.Id && b.LotId == lot && b.CustomerId == 1);
        check(stock.PackageCount == 1000 && Eq(stock.QtyKg, 8000), "مخزون التام بعد الجزئي: العميل الصحيح، الدفعة الصحيحة، 1000 كرتون / 8000 كجم");
        check(db.InventoryTransactions.AsNoTracking().Count(t => t.OrderId == orderId && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 1, "حركة توريد واحدة فقط للتام (المخرج بلا صنف 003 مطابق يُوثَّق في السند فقط دون حركة)");
        var afterApprove = Snapshot();
        check(!goods.Approve(draft1.Id).Ok && Snapshot() == afterApprove, "الاعتماد المزدوج مرفوض ولا يضاعف الحركات");
        check(!goods.SaveDraft(Draft(orderId, 8000, 1000, fgId), draft1.Id).Ok && !goods.CancelDraft(draft1.Id).Ok && Snapshot() == afterApprove, "لا تعديل ولا حذف لسند معتمد");
        var card = goods.GetReceipt(draft1.Id);
        check(card != null && card.IsApproved && card.Lines.Count == 1 && card.Lines[0].WarehouseId == fgId && Eq(card.Lines[0].BalanceAfter ?? -1, 8000) && card.ByProducts.Count == 1 && !card.ByProducts[0].PostsStock, "بطاقة السند: مخزن البند والرصيد بعد الحركة 8000 وتوثيق المخرج الثانوي");
        ctx = goods.GetOrderDeliveryContext(orderId);
        check(Eq(ctx.DeliveredKg, 8000) && Eq(ctx.RemainingKg, 13600) && Eq(ctx.ByProducts[0].RemainingQty, 0), "المتبقي يُحسب من الحركات المعتمدة: 13600 كجم، والمخرج الثانوي صفر");
        check(goods.SearchDeliverableOrders("", true).Single(o => o.OrderId == orderId).StatusAr == "مسلَّم جزئياً", "حالة الأمر في البحث: مسلَّم جزئياً");
        check(!goods.SaveDraft(Draft(orderId, 13608, 1701, fgId), null).Ok, "التسليم الثاني لا يتجاوز المتبقي 13600 (1701 كرتون مرفوض)");
        var cancelMe = goods.SaveDraft(Draft(orderId, 800, 100, fgId), null); Ok(cancelMe, "مسودة ثانية للإلغاء");
        Ok(goods.CancelDraft(cancelMe.Id), "إلغاء مسودة بلا حركات");
        check(db.FinishedGoodsReceipts.AsNoTracking().Single(r => r.Id == cancelMe.Id).Status == DocStatuses.Cancelled && Eq(goods.GetOrderDeliveryContext(orderId).RemainingKg, 13600), "السند الملغى لا يُحتسب من المتبقي");
        var draft2 = goods.SaveDraft(Draft(orderId, 13600, 1700, fgId), null); Ok(draft2, "مسودة التسليم المكمِّل 1700 كرتون");
        Ok(goods.Approve(draft2.Id), "اعتماد التسليم المكمِّل");
        stock = db.StockBalances.AsNoTracking().Single(b => b.WarehouseId == fgId && b.ProductId == finished.Id && b.LotId == lot && b.CustomerId == 1);
        check(stock.PackageCount == 2700 && Eq(stock.QtyKg, 21600), "مخزون التام الحقيقي بعد التسليمين: 2700 كرتون / 21600 كجم");
        check(db.InventoryTransactions.AsNoTracking().Count(t => t.OrderId == orderId && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 2, "حركتا توريد فقط (سند لكل تسليم) — لا ازدواج");
        ctx = goods.GetOrderDeliveryContext(orderId);
        check(Eq(ctx.RemainingKg, 0) && goods.SearchDeliverableOrders("", true).All(o => o.OrderId != orderId) && goods.SearchDeliverableOrders("", false).Single(o => o.OrderId == orderId).StatusAr == "مكتمل التسليم", "الأمر مكتمل التسليم: يختفي من «المتبقي فقط» ويظهر بحالته في البحث الشامل");
        check(!goods.SaveDraft(Draft(orderId, 8, 1, fgId), null).Ok, "لا تسليم بعد اكتمال الفعلي");
        Ok(goods.Unapprove(draft2.Id), "تراجع عن اعتماد السند الثاني (قيد عكسي موثق)");
        stock = db.StockBalances.AsNoTracking().Single(b => b.WarehouseId == fgId && b.ProductId == finished.Id && b.LotId == lot && b.CustomerId == 1);
        check(stock.PackageCount == 1000 && Eq(stock.QtyKg, 8000) && Eq(goods.GetOrderDeliveryContext(orderId).RemainingKg, 13600), "بعد التراجع: الرصيد 1000 كرتون والمتبقي 13600 من الحركات");
        Ok(goods.Approve(draft2.Id), "إعادة اعتماد السند الثاني بعد التراجع");
        stock = db.StockBalances.AsNoTracking().Single(b => b.WarehouseId == fgId && b.ProductId == finished.Id && b.LotId == lot && b.CustomerId == 1);
        check(stock.PackageCount == 2700 && Eq(stock.QtyKg, 21600), "إعادة الاعتماد تعيد الرصيد 2700 / 21600 بلا ازدواج");
        var qcLinked = db.FinishedGoodsReceipts.AsNoTracking().Single(r => r.Id == draft1.Id);
        check(qcLinked.QualityCheckId == qc.Id && qcLinked.OrderId == orderId, "كل سند مرتبط بأمر الإنتاج وفحصه");
        check(!QualityGate.CustomerDeliveryAllowed(db, orderId, lot, finished.Id).ok, "بوابة التسليم للعميل تمنع البيع قبل اعتماد الجودة");
        check(!sp.GetRequiredService<IQualityService>().ApproveCheck(qc.Id).Ok, "حتى المدير لا يعتمد محضرًا مرسلًا بلا نتائج مكتملة");
        var snapshot = Snapshot(); check(!delivery.SaveActualProduction(Input(orderId)).Ok && Snapshot() == snapshot, "نقرة الحفظ الثانية لا تكرر الفعلي أو المخزون أو الجودة");
        check(db.ProductionDeliveries.Count() == 0 && db.CustomerDeliveries.Count() == 0, "لا مستند تجاوز جودة أو تسليم عميل خفي أثناء التسجيل");
        var reload = delivery.GetActualDeliveryOrders().Single(o => o.OrderId == orderId);
        check(reload.Recorded && !reload.CanRecord && reload.Items[0].DifferenceCartons == 300 && !string.IsNullOrWhiteSpace(reload.ReceiptNumber) && reload.DowntimeReason == "عطل ماكينة", "إعادة فتح سياق الشاشة من القاعدة: فرق 300 وآخر سند تسليم معتمد والسبب ومنع التحرير المكرر");
        var report = sp.GetRequiredService<IReportService>().Run("daily_production", new());
        int Index(string name) => report.Columns.IndexOf(name);
        var reportRow = report.Rows.Single(r => r[1]?.ToString() == exe.DocumentNumber);
        check(reportRow[Index("الفرق (كرتون)")]?.ToString()?.Replace(",", "") == "300" && reportRow[Index("سبب التوقف")]?.ToString() == "عطل ماكينة", "تقرير الإنتاج اليومي يقرأ الفرق 300 والسبب من الحفظ الحقيقي");
        check(reportRow[Index("مخرج مختار من تعريف الاختبار (كجم)")]?.ToString()?.StartsWith("200") == true && report.Rows.All(r => r.Length == report.Columns.Count), "التقرير يعرض المخلفات 200 في عمود تعريفها مع اتساق الأعمدة");
        def = db.ByProducts.Single(b => b.Id == bp.Id); def.IsActive = false; db.SaveChanges();
        report = sp.GetRequiredService<IReportService>().Run("daily_production", new());
        check(report.Columns.Contains("مخرج مختار من تعريف الاختبار (كجم)"), "إيقاف تعريف لاحقًا لا يخفي المخلفات التاريخية من التقرير");
        def = db.ByProducts.Single(b => b.Id == bp.Id); def.IsActive = true; db.SaveChanges();
        // Water gain remains legal; input is independent of output + waste.
        int waterOrder = Create("قاعدة زيادة الوزن بالماء", new() { Line(finished.Id, 100) });
        var waterInput = Input(waterOrder, 90, 600); waterInput.ByProducts[0].QtyKg = 20;
        Ok(delivery.SaveActualProduction(waterInput), "الماء: 720 كجم إنتاج +20 مخرج من600 خام لا يُرفض");
        check(Eq(db.ProductionExecutions.AsNoTracking().Single(e => e.OrderId == waterOrder && e.IsDayClosed).ConsumedRawKg, 600), "قاعدة الماء لا تغير الخام المدخل إلى مجموع الخارج");
        int multi = Create("صنفان وتوقف واحد", new() { Line(finished.Id, 20), Line(second.Id, 20) });
        var multiInput = Input(multi, 18, 200); multiInput.ByProducts.Clear();
        Ok(delivery.SaveActualProduction(multiInput), "حفظ صنفين من الخطة دون إعادة اختيار مع توقف واحد");
        var multiExec = db.ProductionExecutions.AsNoTracking().Single(e => e.OrderId == multi && e.IsDayClosed);
        check(db.ExecutionDowntimes.Count(d => d.ExecutionId == multiExec.Id) == 1 && db.ProductionOrderItems.AsNoTracking().Count(i => i.OrderId == multi && i.ProducedCartons == 18) == 2, "الصنفان محفوظان ولا تتضاعف ساعة التوقف");
        var multiCtx = goods.GetOrderDeliveryContext(multi);
        var multiDraft = new FgDeliveryDraftDto { OrderId = multi, DeliveryDate = D(day), Items = multiCtx.Lines.Select(l => new FinishedGoodsItemDto { ProductId = l.ProductId, LotId = l.LotId, CustomerId = l.CustomerId, PackagingTypeId = l.PackagingTypeId, NetWeightKg = l.RemainingKg, PackageCount = 18, WarehouseId = fgId }).ToList() };
        var multiSave = goods.SaveDraft(multiDraft, null); Ok(multiSave, "تسليم صنفين في سند واحد"); Ok(goods.Approve(multiSave.Id), "اعتماد سند الصنفين");
        check(db.FinishedGoodsReceipts.Include(r => r.Items).Single(r => r.OrderId == multi).Items.Count == 2 && db.InventoryTransactions.Count(t => t.OrderId == multi && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 2, "الصنفان موردان بحركتين مستقلتين");
        // Same product on two different raw lots: exact owner/lot and no rounded-away actual input.
        var receipt2 = receiving.SaveShipment(1, null!, null!, new() { new() { ProductId = raw.Id, QtyKg = 2000, PackageCount = 100, UnitWeightKg = 20, TreatmentRequired = false } });
        Ok(receipt2, "استلام الدفعة الثانية"); Ok(receiving.ApproveShipment(receipt2.Id), "اعتماد الدفعة الثانية");
        int lot2 = db.Lots.AsNoTracking().Single(l => l.ShipmentId == receipt2.Id).Id;
        int twoLots = Create("نفس الصنف من دفعتين", new() { Line(finished.Id, 10), Line(finished.Id, 10, lot2) });
        var lotsInput = Input(twoLots, 9, 123.456); lotsInput.ByProducts.Clear();
        Ok(delivery.SaveActualProduction(lotsInput), "صرف إجمالي خام 123.456 وتوريد نفس الصنف من دفعتين");
        var rawMoves = db.InventoryTransactions.AsNoTracking().Where(t => t.OrderId == twoLots && t.ReferenceDocType == ReferenceDocType.ProductionExecution && t.MovementType == MovementType.Outbound).ToList();
        check(rawMoves.Count == 2 && Eq(-rawMoves.Sum(t => t.QtyKg), 123.456) && rawMoves.All(t => t.CustomerId == 1), "توزيع الخام على الدفعتين وفق الدورة يحفظ إجمالي الاستهلاك بالضبط وهوية المالك");
        var lotsCtx = goods.GetOrderDeliveryContext(twoLots);
        var lotsSave = goods.SaveDraft(new FgDeliveryDraftDto { OrderId = twoLots, DeliveryDate = D(day), Items = lotsCtx.Lines.Select(l => new FinishedGoodsItemDto { ProductId = l.ProductId, LotId = l.LotId, CustomerId = l.CustomerId, PackagingTypeId = l.PackagingTypeId, NetWeightKg = l.RemainingKg, PackageCount = 9, WarehouseId = fgId }).ToList() }, null);
        Ok(lotsSave, "تسليم نفس الصنف من دفعتين في سند واحد"); Ok(goods.Approve(lotsSave.Id), "اعتماد سند الدفعتين");
        var secondReceipt = db.FinishedGoodsReceipts.AsNoTracking().Include(r => r.Items).Single(r => r.OrderId == twoLots);
        check(secondReceipt.Items.Select(i => i.LotId).ToHashSet().SetEquals(new int?[] { lot, lot2 }) && secondReceipt.Items.All(i => Eq(i.ReceivedQtyKg, 72)), "سند التام يحفظ هويتي الدفعتين ولا يخلط الصنف المشترك");
        var customer2 = master.SaveCustomer(null, "ACT-C2", "عميل الاختبار الثاني", "Customer", "", "", true); Ok(customer2, "تعريف العميل الثاني رسميًا");
        var otherReceipt = receiving.SaveShipment(customer2.Id, null!, null!, new() { new() { ProductId = raw.Id, QtyKg = 2000, PackageCount = 100, UnitWeightKg = 20, TreatmentRequired = false } });
        Ok(otherReceipt, "استلام خام العميل الثاني"); Ok(receiving.ApproveShipment(otherReceipt.Id), "اعتماد خام العميل الثاني");
        int otherLot = db.Lots.AsNoTracking().Single(l => l.ShipmentId == otherReceipt.Id).Id;
        int otherOrder = Create("العميل الثاني", new() { Line(finished.Id, 10, otherLot, customer2.Id) });
        var otherInput = Input(otherOrder, 9, 70); otherInput.ByProducts.Clear(); Ok(delivery.SaveActualProduction(otherInput), "تسجيل فعلي للعميل الثاني من نفس شاشة أوامر اليوم");
        var otherCtx = goods.GetOrderDeliveryContext(otherOrder);
        check(otherCtx.Lines.Single().CustomerId == customer2.Id, "سطر التسليم يحمل عميل البند لا أول عميل");
        var otherSave = goods.SaveDraft(new FgDeliveryDraftDto { OrderId = otherOrder, DeliveryDate = D(day), Items = otherCtx.Lines.Select(l => new FinishedGoodsItemDto { ProductId = l.ProductId, LotId = l.LotId, CustomerId = l.CustomerId, PackagingTypeId = l.PackagingTypeId, NetWeightKg = l.RemainingKg, PackageCount = 9, WarehouseId = fgId }).ToList() }, null);
        Ok(otherSave, "تسليم العميل الثاني"); Ok(goods.Approve(otherSave.Id), "اعتماد تسليم العميل الثاني");
        check(db.StockBalances.AsNoTracking().Any(b => b.WarehouseId == fgId && b.ProductId == finished.Id && b.LotId == otherLot && b.CustomerId == customer2.Id && b.PackageCount == 9), "لا خلط مخزون العميلين عند اشتراك الصنف");
        // Continue the pending check through the existing QC service; approval is explicit and separate.
        var quality = sp.GetRequiredService<IQualityService>();
        var inspected = quality.SaveCheck(orderId, exe.Id, D(day), "فحص فعلي مستقل", new() { new() {
            ProductId = finished.Id, LotId = lot, CheckedQtyKg = 21600, AcceptedQtyKg = 21600,
            CheckedCartons = 2700, AcceptedCartons = 2700 } });
        Ok(inspected, "الجودة تستكمل المحضر المعلق نفسه بنتيجة فعلية مستقلة");
        check(inspected.Id == qc.Id && db.QualityChecks.Count(c => c.ExecutionId == exe.Id) == 1, "استكمال الفحص لا يولد محضرًا مكررًا أو يفصل سند التام عنه");
        Ok(quality.ApproveCheck(qc.Id), "اعتماد الجودة الرسمي بعد تسجيل النتائج");
        check(QualityGate.CustomerDeliveryAllowed(db, orderId, lot, finished.Id).ok, "بوابة العميل تتحول إلى السماح فقط بعد اعتماد النتيجة الفعلية");
        // No starting a production order from the actual screen.
        int notStarted = Create("لا بدء ضمني", new() { Line(finished.Id, 10) }, false);
        snapshot = Snapshot(); check(!delivery.SaveActualProduction(Input(notStarted, 9, 72)).Ok && Snapshot() == snapshot, "رفض أمر لم يبدأ، دون إنشاء مسار بدء جديد");
        if (concurrency)
        {
            int race = Create("حفظ من جهازين", new() { Line(finished.Id, 10) }); var input = Input(race, 9, 72); input.ByProducts.Clear();
            using var gate = new Barrier(2);
            var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
                using var s = services.CreateScope(); var svc = s.ServiceProvider.GetRequiredService<IProductionDeliveryService>();
                gate.SignalAndWait(); return svc.SaveActualProduction(input);
            })).ToArray(); Task.WaitAll(tasks);
            check(tasks.Count(t => t.Result.Ok) == 1, "SQL Server: من جهازين متزامنين نجح تسجيل واحد فقط");
            check(db.ProductionExecutions.AsNoTracking().Count(e => e.OrderId == race && e.IsDayClosed) == 1 && db.FinishedGoodsReceipts.Count(r => r.OrderId == race) == 0
                && db.InventoryTransactions.Count(t => t.OrderId == race && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 0, "SQL Server: لا ازدواج تنفيذ عند التزامن ولا سند/حركة تام قبل التسليم الصريح");
            var raceDraft = goods.SaveDraft(new FgDeliveryDraftDto { OrderId = race, DeliveryDate = D(day), Items = goods.GetOrderDeliveryContext(race).Lines.Select(l => new FinishedGoodsItemDto { ProductId = l.ProductId, LotId = l.LotId, CustomerId = l.CustomerId, PackagingTypeId = l.PackagingTypeId, NetWeightKg = l.RemainingKg, PackageCount = 9, WarehouseId = fgId }).ToList() }, null);
            Ok(raceDraft, "مسودة تسليم لاختبار الاعتماد المتزامن");
            using var gate2 = new Barrier(2);
            var approves = Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
                using var s = services.CreateScope(); var svc = s.ServiceProvider.GetRequiredService<IFinishedGoodsService>();
                gate2.SignalAndWait(); return svc.Approve(raceDraft.Id);
            })).ToArray(); Task.WaitAll(approves);
            check(approves.Count(t => t.Result.Ok) == 1 && db.InventoryTransactions.Count(t => t.OrderId == race && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 1, "SQL Server: اعتماد التسليم من جهازين = حركة واحدة فقط");
        }
    }
    private sealed class DenyWarehouse : ICurrentSession
    {
        public int UserId => 1; public string UserName { get; set; } = "test-deny-warehouse"; public string MachineName => "acceptance";
        public bool IsInRole(string role) => false; public bool Can(string module, string action) => module != "finishedgoods";
    }
}
