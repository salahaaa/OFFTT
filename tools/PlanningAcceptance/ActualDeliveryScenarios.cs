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
        // Permission denial must not silently acquire warehouse authority.
        var denied = new ProductionDeliveryService(db, new DenyWarehouse(), sp.GetRequiredService<INumberingService>(), sp.GetRequiredService<IAuditService>());
        var beforeDenied = Snapshot(); bool permission = false;
        try { denied.SaveActualProduction(Input(orderId)); } catch (PermissionDeniedException) { permission = true; }
        check(permission && Snapshot() == beforeDenied, "صلاحيات المخزن محفوظة: لا تصعيد تلقائي ولا ترحيل جزئي");
        // Force a late failure AFTER execution, raw debit and pending QC have been saved in the transaction.
        var fg = db.Warehouses.Single(w => w.WarehouseCode == "WFG"); int fgId = fg.Id; fg.WarehouseCode = "WFG-UNAVAILABLE"; db.SaveChanges();
        Reject(_ => { }, "فشل متأخر عند مخزن التام يعيد التنفيذ والخام والجودة والتوقفات والمخلفات كلها");
        fg = db.Warehouses.Single(w => w.Id == fgId); fg.WarehouseCode = "WFG"; db.SaveChanges();
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
        var rcpt = db.FinishedGoodsReceipts.AsNoTracking().Include(r => r.Items).Single(r => r.OrderId == orderId);
        check(rcpt.QualityCheckId == qc.Id && rcpt.ReceiptStatus == "Full" && rcpt.IsApproved && !string.IsNullOrWhiteSpace(rcpt.ReceiptNumber), "سند الاستلام القائم أُصدر واستُلم كامل الفعلي ورُبط بفحصه");
        var stock = db.StockBalances.AsNoTracking().Single(b => b.WarehouseId == fgId && b.ProductId == finished.Id && b.LotId == lot && b.CustomerId == 1);
        check(stock.PackageCount == 2700 && Eq(stock.QtyKg, 21600), "مخزون التام الحقيقي: العميل الصحيح، الدفعة الصحيحة، 2700 كرتون / 21600 كجم");
        check(db.InventoryTransactions.AsNoTracking().Count(t => t.OrderId == orderId && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 1, "حركة توريد التام واحدة فقط عبر سند الاستلام");
        check(!QualityGate.CustomerDeliveryAllowed(db, orderId, lot, finished.Id).ok, "بوابة التسليم للعميل تمنع البيع قبل اعتماد الجودة");
        check(!sp.GetRequiredService<IQualityService>().ApproveCheck(qc.Id).Ok, "حتى المدير لا يعتمد محضرًا مرسلًا بلا نتائج مكتملة");
        var snapshot = Snapshot(); check(!delivery.SaveActualProduction(Input(orderId)).Ok && Snapshot() == snapshot, "نقرة الحفظ الثانية لا تكرر الفعلي أو المخزون أو الجودة");
        check(db.ProductionDeliveries.Count() == 0 && db.CustomerDeliveries.Count() == 0, "لا مستند تجاوز جودة أو تسليم عميل خفي أثناء التسجيل");
        var reload = delivery.GetActualDeliveryOrders().Single(o => o.OrderId == orderId);
        check(reload.Recorded && !reload.CanRecord && reload.Items[0].DifferenceCartons == 300 && reload.ReceiptNumber == rcpt.ReceiptNumber && reload.DowntimeReason == "عطل ماكينة", "إعادة فتح سياق الشاشة من القاعدة: فرق 300 وسند الاستلام والسبب ومنع التحرير المكرر");
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
        check(db.ExecutionDowntimes.Count(d => d.ExecutionId == multiExec.Id) == 1 && db.FinishedGoodsReceipts.Include(r => r.Items).Single(r => r.OrderId == multi).Items.Count == 2, "الصنفان محفوظان وموردان ولا تتضاعف ساعة التوقف");
        // Same product on two different raw lots: exact owner/lot and no rounded-away actual input.
        var receipt2 = receiving.SaveShipment(1, null!, null!, new() { new() { ProductId = raw.Id, QtyKg = 2000, PackageCount = 100, UnitWeightKg = 20, TreatmentRequired = false } });
        Ok(receipt2, "استلام الدفعة الثانية"); Ok(receiving.ApproveShipment(receipt2.Id), "اعتماد الدفعة الثانية");
        int lot2 = db.Lots.AsNoTracking().Single(l => l.ShipmentId == receipt2.Id).Id;
        int twoLots = Create("نفس الصنف من دفعتين", new() { Line(finished.Id, 10), Line(finished.Id, 10, lot2) });
        var lotsInput = Input(twoLots, 9, 123.456); lotsInput.ByProducts.Clear();
        Ok(delivery.SaveActualProduction(lotsInput), "صرف إجمالي خام 123.456 وتوريد نفس الصنف من دفعتين");
        var rawMoves = db.InventoryTransactions.AsNoTracking().Where(t => t.OrderId == twoLots && t.ReferenceDocType == ReferenceDocType.ProductionExecution && t.MovementType == MovementType.Outbound).ToList();
        check(rawMoves.Count == 2 && Eq(-rawMoves.Sum(t => t.QtyKg), 123.456) && rawMoves.All(t => t.CustomerId == 1), "توزيع الخام على الدفعتين وفق الدورة يحفظ إجمالي الاستهلاك بالضبط وهوية المالك");
        var secondReceipt = db.FinishedGoodsReceipts.AsNoTracking().Include(r => r.Items).Single(r => r.OrderId == twoLots);
        check(secondReceipt.Items.Select(i => i.LotId).ToHashSet().SetEquals(new int?[] { lot, lot2 }) && secondReceipt.Items.All(i => Eq(i.ReceivedQtyKg, 72)), "سند التام يحفظ هويتي الدفعتين ولا يخلط الصنف المشترك");
        var customer2 = master.SaveCustomer(null, "ACT-C2", "عميل الاختبار الثاني", "Customer", "", "", true); Ok(customer2, "تعريف العميل الثاني رسميًا");
        var otherReceipt = receiving.SaveShipment(customer2.Id, null!, null!, new() { new() { ProductId = raw.Id, QtyKg = 2000, PackageCount = 100, UnitWeightKg = 20, TreatmentRequired = false } });
        Ok(otherReceipt, "استلام خام العميل الثاني"); Ok(receiving.ApproveShipment(otherReceipt.Id), "اعتماد خام العميل الثاني");
        int otherLot = db.Lots.AsNoTracking().Single(l => l.ShipmentId == otherReceipt.Id).Id;
        int otherOrder = Create("العميل الثاني", new() { Line(finished.Id, 10, otherLot, customer2.Id) });
        var otherInput = Input(otherOrder, 9, 70); otherInput.ByProducts.Clear(); Ok(delivery.SaveActualProduction(otherInput), "تسجيل فعلي للعميل الثاني من نفس شاشة أوامر اليوم");
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
            check(db.ProductionExecutions.AsNoTracking().Count(e => e.OrderId == race && e.IsDayClosed) == 1 && db.FinishedGoodsReceipts.Count(r => r.OrderId == race) == 1
                && db.InventoryTransactions.Count(t => t.OrderId == race && t.ReferenceDocType == ReferenceDocType.FinishedGoodsReceipt) == 1, "SQL Server: لا ازدواج تنفيذ أو سند أو حركة مخزون عند التزامن");
        }
    }
    private sealed class DenyWarehouse : ICurrentSession
    {
        public int UserId => 1; public string UserName { get; set; } = "test-deny-warehouse"; public string MachineName => "acceptance";
        public bool IsInRole(string role) => false; public bool Can(string module, string action) => module != "finishedgoods";
        // §R2 — أعضاء المصفوفة الحية (نفس دلالة الرفض: لا صلاحيات مخزنة)
        public System.Collections.Generic.Dictionary<(string module, string action), bool> PermissionCache => new();
        public System.Collections.Generic.HashSet<string> Roles => new();
        public DateTime CacheBuiltAt { get; set; } = DateTime.Now;
    }
}
