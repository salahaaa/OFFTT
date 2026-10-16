using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §تعدد المخازن في تسليم العملاء — خطة قبول:
///  1) الحفظ يثبّت المخزن المختار؛ المخزن غير الموجود/المعطّل/غير التام مرفوض.
///  2) الاعتماد يصرف من مخزن السند (لا WFG الثابت) والحركة مسجلة عليه.
///  3) الاعتماد يفشل إن كان رصيد العميل في المخزن المختار غير كافٍ ولو كان كافياً في WFG.
///  4) الإلغاء يعيد الكمية إلى المخزن نفسه الذي صُرف منه، والإعادة المكررة مرفوضة.
///  5) تعديل المسودة لا يحرك المخزون؛ تغيير مخزن سند معتمد مرفوض.
///  6) سند قديم (WarehouseId = null) معتمد سابقاً على WFG: الإلغاء يعيد إلى WFG (دليل الحركات) ويوثّق المخزن.
/// </summary>
public class CustomerDeliveryWarehouseTests
{
    private static T Svc<T>(TestHost host) => (T)host.Services.CreateScope().ServiceProvider.GetService(typeof(T));
    private static DatesErpDbContext Db(TestHost host)
        => new(new DbContextOptionsBuilder<DatesErpDbContext>().UseSqlite(host.Connection).Options);

    private sealed record Fx(int OrderId, int LotId, int Wfg, int Wfg2, int Customer, int Product);

    /// <summary>استلام ← خطة ← أمر ← إقفال ← جودة ← استلام تام: 5000 كجم / 667 كرتون للعميل 1 في WFG، ثم مخزن تام ثانٍ WFG2 يُنقل إليه 2000 كجم بسند تحويل معتمد.</summary>
    private static Fx Build(TestHost host)
    {
        using (var db = Db(host))
        {
            var whAux = db.Warehouses.Single(w => w.WarehouseCode == "WAUX").Id;
            foreach (var matId in new[] { 1, 2 })
            {
                db.StockBalances.Add(new StockBalance { WarehouseId = whAux, MaterialId = matId, QtyKg = 20000, RowVersion = Guid.NewGuid().ToByteArray() });
                db.InventoryTransactions.Add(new InventoryTransaction
                {
                    TxnNumber = $"OPEN-{matId}", TxnDate = DateTime.Now, WarehouseId = whAux, MaterialId = matId,
                    MovementType = MovementType.Inbound, QtyKg = 20000, ReferenceDocType = ReferenceDocType.Adjustment,
                    ReferenceDocNumber = "OPENING-BALANCE", IsApproved = true, RowVersion = Guid.NewGuid().ToByteArray()
                });
            }
            db.Warehouses.Add(new Warehouse { WarehouseCode = "WFG2", WarehouseNameAr = "مخزن تام فرعي", WarehouseType = "Finished", IsActive = true, RowVersion = Guid.NewGuid().ToByteArray() });
            db.SaveChanges();
        }
        var receiving = Svc<IReceivingService>(host);
        var s1 = receiving.SaveShipment(1, "2026-08-10", "2026-08-10",
            new List<ShipmentItemDto> { new() { TreatmentRequired = false, ProductId = 1, PackagingTypeId = 2, PackageCount = 400, UnitWeightKg = 20, QtyKg = 8000 } });
        Assert.True(s1.Ok, s1.Message);
        Assert.True(receiving.ApproveShipment(s1.Id).Ok);
        int lotId; using (var db = Db(host)) lotId = db.Lots.Single().Id;

        var planning = Svc<IPlanningService>(host);
        var plan = planning.SavePlan("خطة مخازن", "Daily", "2026-08-20", "2026-08-20", 1, 1, new List<PlanItemDto>
        { new() { SourceType = "FromReceiving", LotId = lotId, CustomerId = 1, ProductId = 3, PlannedQtyKg = 5000, PlannedCartons = 667, ScheduledDate = "2026-08-20", SuggestedShiftId = 1, SuggestedLineId = 1, PriorityNo = 1 } });
        Assert.True(plan.Ok, plan.Message);
        Assert.True(planning.ApprovePlan(plan.Id).Ok);

        var orders = Svc<IProductionOrderService>(host);
        host.SetBusinessDate("2026-08-20");
        var o = orders.SaveOrder("FromPlan", plan.Id, 1, "2026-08-20", 1, 1, TestOrderFixture.Items(host, plan.Id, 1, "2026-08-20"));
        Assert.True(o.Ok, o.Message);
        Assert.True(orders.ApproveOrder(o.Id).Ok);
        orders.StartOrder(o.Id);
        var ec = Svc<IExecutionService>(host).CloseProductionDay(o.Id, 5000, 666, 0, 0, 0, false, new List<DowntimeDto>(), false, null, null, 5000);
        Assert.True(ec.Ok, ec.Message);
        int e1Id; using (var db = Db(host)) e1Id = db.ProductionExecutions.Single(x => x.OrderId == o.Id).Id;
        var quality = Svc<IQualityService>(host);
        var q = quality.SaveCheck(o.Id, e1Id, "2026-08-20", "نهائي",
            new List<QualityItemDto> { new() { ProductId = 3, LotId = lotId, AcceptedQtyKg = 5000, RejectedQtyKg = 0 } },
            new List<(int, double)> { (1, 20.0) });
        Assert.True(q.Ok, q.Message);
        Assert.True(quality.ApproveCheck(q.Id).Ok);
        var fg = Svc<IFinishedGoodsService>(host);
        var f = fg.SaveReceipt(o.Id, q.Id, "2026-08-20", new List<FinishedGoodsItemDto> { new() { ProductId = 3, LotId = lotId, PackageCount = 667, NetWeightKg = 5000 } });
        Assert.True(f.Ok, f.Message);
        Assert.True(fg.Issue(f.Id).Ok);
        Assert.True(fg.Receive(f.Id, new Dictionary<int, double>()).Ok);

        int wfg, wfg2;
        using (var db = Db(host))
        {
            wfg = db.Warehouses.Single(w => w.WarehouseCode == "WFG").Id;
            wfg2 = db.Warehouses.Single(w => w.WarehouseCode == "WFG2").Id;
        }
        // نقل 2000 كجم / 267 كرتون إلى المخزن الفرعي بسند تحويل حقيقي (نفس مفتاح الرصيد: عميل 1 + الدفعة)
        StockBalance src;
        using (var db = Db(host)) src = db.StockBalances.Single(b => b.WarehouseId == wfg && b.ProductId == 3 && b.CustomerId == 1);
        var wt = Svc<IWarehouseTransferService>(host);
        var t = wt.SaveTransfer("21/08/2026", wfg, wfg2, new()
        { new() { ProductId = 3, LotId = src.LotId, CustomerId = src.CustomerId, PackagingTypeId = src.PackagingTypeId, QtyKg = 2000, PackageCount = 267 } }, null);
        Assert.True(t.Ok, t.Message);
        Assert.True(wt.ApproveTransfer(t.Id).Ok);
        Assert.Equal(3000, Bal(host, wfg), 1);
        Assert.Equal(2000, Bal(host, wfg2), 1);
        return new Fx(o.Id, lotId, wfg, wfg2, 1, 3);
    }

    private static double Bal(TestHost host, int wh)
    {
        using var db = Db(host);
        return db.StockBalances.Where(b => b.WarehouseId == wh && b.ProductId == 3 && b.CustomerId == 1).Sum(b => b.QtyKg);
    }

    private static List<CustomerDeliveryItemDto> Items(Fx fx, double kg, int cartons)
        => new() { new() { ProductId = fx.Product, LotId = fx.LotId, QtyKg = kg, PackageCount = cartons } };

    [Fact]
    public void Save_Stores_Selected_Warehouse_And_Rejects_Invalid_Ones()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);

        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 500, 67), fx.Wfg2);
        Assert.True(r.Ok, r.Message);
        using (var db = Db(host)) Assert.Equal(fx.Wfg2, db.CustomerDeliveries.Single(d => d.Id == r.Id).WarehouseId);

        // null ⇒ الافتراضي للتام (WFG) ويُثبَّت لا يبقى فارغاً
        var r0 = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 100, 13));
        Assert.True(r0.Ok, r0.Message);
        using (var db = Db(host)) Assert.Equal(fx.Wfg, db.CustomerDeliveries.Single(d => d.Id == r0.Id).WarehouseId);

        Assert.False(svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 100, 13), 999999).Ok);          // غير موجود
        int raw; using (var db = Db(host)) raw = db.Warehouses.Single(w => w.WarehouseCode == "WRM").Id;
        Assert.False(svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 100, 13), raw).Ok);             // ليس مخزن تام
        using (var db = Db(host)) { var w = db.Warehouses.Single(x => x.Id == fx.Wfg2); w.IsActive = false; db.SaveChanges(); }
        Assert.False(svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 100, 13), fx.Wfg2).Ok);         // معطّل
        using (var db = Db(host)) Assert.Equal(2, db.CustomerDeliveries.Count());                              // المرفوضة لم تُحفظ
    }

    [Fact]
    public void Approve_Issues_From_Document_Warehouse_Not_From_Default()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);
        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 1500, 200), fx.Wfg2);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3000, Bal(host, fx.Wfg), 1); // المسودة بلا أثر
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1);

        var a = svc.Approve(r.Id);
        Assert.True(a.Ok, a.Message);
        Assert.Equal(3000, Bal(host, fx.Wfg), 1);  // WFG لم يُمس
        Assert.Equal(500, Bal(host, fx.Wfg2), 1);  // الصرف من مخزن السند
        using var db = Db(host);
        var outs = db.InventoryTransactions.Where(t => t.ReferenceDocType == ReferenceDocType.CustomerDelivery && t.MovementType == MovementType.Outbound && t.ReferenceDocNumber == r.DocumentNumber).ToList();
        Assert.Single(outs);
        Assert.Equal(fx.Wfg2, outs[0].WarehouseId);
        Assert.Equal(-1500, outs[0].QtyKg, 1);
        Assert.Equal(1500, db.Lots.Single(l => l.Id == fx.LotId).DeliveredQtyKg, 1);
    }

    [Fact]
    public void Approve_Fails_When_Chosen_Warehouse_Lacks_Balance_Even_If_Default_Has_It()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);
        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 2500, 334), fx.Wfg2); // WFG2 فيه 2000 فقط، WFG فيه 3000
        Assert.True(r.Ok, r.Message);
        var a = svc.Approve(r.Id);
        Assert.False(a.Ok);
        Assert.Contains("مخزن تام فرعي", a.Message);
        Assert.Equal(3000, Bal(host, fx.Wfg), 1);
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1);
        using var db = Db(host);
        Assert.False(db.InventoryTransactions.Any(t => t.ReferenceDocNumber == r.DocumentNumber));
    }

    [Fact]
    public void Unapprove_Returns_To_Same_Warehouse_And_Repeat_Is_Rejected()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);
        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 1000, 134), fx.Wfg2);
        Assert.True(svc.Approve(r.Id).Ok);
        Assert.Equal(1000, Bal(host, fx.Wfg2), 1);

        var u = svc.Unapprove(r.Id);
        Assert.True(u.Ok, u.Message);
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1); // عاد إلى الفرعي
        Assert.Equal(3000, Bal(host, fx.Wfg), 1);  // لا شيء ذهب إلى WFG
        using (var db = Db(host))
        {
            var rev = db.InventoryTransactions.Where(t => t.ReferenceDocNumber.StartsWith(r.DocumentNumber + "#REV")).ToList();
            Assert.Single(rev);
            Assert.Equal(fx.Wfg2, rev[0].WarehouseId);
            Assert.Equal(0, db.Lots.Single(l => l.Id == fx.LotId).DeliveredQtyKg, 1);
        }
        Assert.False(svc.Unapprove(r.Id).Ok); // غير معتمد الآن — لا عكس ثانٍ
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1);

        // إعادة الاعتماد بعد الإلغاء تعمل من المخزن نفسه (مرجع #R2) ولا تتضاعف
        Assert.True(svc.Approve(r.Id).Ok);
        Assert.Equal(1000, Bal(host, fx.Wfg2), 1);
        Assert.False(svc.Approve(r.Id).Ok);
        Assert.Equal(1000, Bal(host, fx.Wfg2), 1);
    }

    [Fact]
    public void Update_Draft_Changes_Warehouse_Without_Stock_Effect_But_Approved_Is_Locked()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);
        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 800, 107), fx.Wfg);
        var u = svc.Update(r.Id, fx.Customer, "23/08/2026", fx.OrderId, Items(fx, 900, 120), fx.Wfg2);
        Assert.True(u.Ok, u.Message);
        using (var db = Db(host))
        {
            var d = db.CustomerDeliveries.Include(x => x.Items).Single(x => x.Id == r.Id);
            Assert.Equal(fx.Wfg2, d.WarehouseId);
            Assert.Equal(900, d.Items.Single().QtyKg, 1);
            Assert.False(db.InventoryTransactions.Any(t => t.ReferenceDocType == ReferenceDocType.CustomerDelivery));
        }
        Assert.Equal(3000, Bal(host, fx.Wfg), 1);
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1);

        // تحديث من مستدعٍ قديم (بلا مخزن) يحافظ على مخزن السند ولا يعيده إلى الافتراضي
        Assert.True(svc.Update(r.Id, fx.Customer, "23/08/2026", fx.OrderId, Items(fx, 900, 120)).Ok);
        using (var db = Db(host)) Assert.Equal(fx.Wfg2, db.CustomerDeliveries.Single(x => x.Id == r.Id).WarehouseId);

        Assert.True(svc.Approve(r.Id).Ok);
        var locked = svc.Update(r.Id, fx.Customer, "23/08/2026", fx.OrderId, Items(fx, 900, 120), fx.Wfg);
        Assert.False(locked.Ok);
        using (var db = Db(host)) Assert.Equal(fx.Wfg2, db.CustomerDeliveries.Single(x => x.Id == r.Id).WarehouseId);
        Assert.Equal(1100, Bal(host, fx.Wfg2), 1);
    }

    [Fact]
    public void Legacy_Document_Without_Warehouse_Unapproves_To_Evidence_Warehouse_WFG()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);
        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 1200, 160), fx.Wfg);
        Assert.True(svc.Approve(r.Id).Ok);
        Assert.Equal(1800, Bal(host, fx.Wfg), 1);
        // محاكاة سند قديم: العمود فارغ بعد الترحيل (الحركات التاريخية على WFG تبقى كما هي)
        using (var db = Db(host)) { var d = db.CustomerDeliveries.Single(x => x.Id == r.Id); d.WarehouseId = null; db.SaveChanges(); }

        var u = svc.Unapprove(r.Id);
        Assert.True(u.Ok, u.Message);
        Assert.Equal(3000, Bal(host, fx.Wfg), 1);   // عاد إلى WFG حيث صُرف
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1);  // لا شيء نُسب للفرعي
        using var db2 = Db(host);
        Assert.Equal(fx.Wfg, db2.CustomerDeliveries.Single(x => x.Id == r.Id).WarehouseId); // وُثّق المخزن المستنتج
        Assert.Equal(fx.Wfg, db2.InventoryTransactions.Single(t => t.ReferenceDocNumber == r.DocumentNumber + "#REV1").WarehouseId);
    }

    [Fact]
    public void Legacy_Draft_Without_Warehouse_Approves_On_WFG_And_Records_It()
    {
        using var host = new TestHost(); host.LoginAsAdmin();
        var fx = Build(host);
        var svc = Svc<ICustomerDeliveryService>(host);
        var r = svc.Save(fx.Customer, "22/08/2026", fx.OrderId, Items(fx, 300, 40), fx.Wfg2);
        using (var db = Db(host)) { var d = db.CustomerDeliveries.Single(x => x.Id == r.Id); d.WarehouseId = null; db.SaveChanges(); }
        Assert.True(svc.Approve(r.Id).Ok);
        Assert.Equal(2700, Bal(host, fx.Wfg), 1);
        Assert.Equal(2000, Bal(host, fx.Wfg2), 1);
        using var db2 = Db(host);
        Assert.Equal(fx.Wfg, db2.CustomerDeliveries.Single(x => x.Id == r.Id).WarehouseId);
    }
}
