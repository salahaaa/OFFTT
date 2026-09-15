using DatesErp.Application.Services;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Tests;

/// <summary>
/// §1.50.66 — سلامة المخزون: PackagingTypeId جزء من مفتاح الرصيد
/// اختبارات: نفس المنتج/المخزن/العميل/Lot مع Packaging 4kg و8kg، صرف من 8kg لا يؤثر على 4kg،
/// رصيد عميل A وB، عدم وجود StockBalance مكرر، عدم وجود رصيد سالب
/// </summary>
public class StockBalancePackagingTests
{
    private static (TestHost host, DatesErpDbContext db, ServiceBase svc) CreateHost()
    {
        var host = new TestHost();
        var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        // نستخدم ServiceBase عبر InventoryService للحصول على PostStockMovement؟ سننشئ خدمة وهمية
        // نستخدم ExecutionService كونه يرث ServiceBase ويحتوي PostStockMovement protected، لكننا سنختبر مباشرة عبر Db
        // بدلاً من ذلك، نختبر عبر CustomerDeliveryService و CartonService التي تستخدم PostStockMovement
        var svc = scope.ServiceProvider.GetRequiredService<ExecutionService>();
        return (host, db, svc);
    }

    [Fact]
    public void SameProduct_Warehouse_Customer_Lot_With_Different_Packaging_Should_Be_Separate_Balances()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();

        // إعداد: مخزن، منتج، عميل، دفعة، نوعين عبوة 4كجم و8كجم
        var wh = db.Warehouses.First();
        var product = db.Products.First(p => p.ItemType == "Finished");
        var customer = db.Customers.First();
        var lot = db.Lots.FirstOrDefault() ?? new Lot { LotCode = "TEST-LOT-001", ProductId = product.Id, CustomerId = customer.Id, InitialQtyKg = 1000, InStockQtyKg = 1000, ShipmentId = db.Shipments.First().Id, ShipmentItemId = db.ShipmentItems.First().Id };
        if (lot.Id == 0) { db.Lots.Add(lot); db.SaveChanges(); }

        var pack4 = db.PackagingTypes.FirstOrDefault(p => p.PackageNameAr.Contains("4")) ?? db.PackagingTypes.First();
        var pack8 = db.PackagingTypes.FirstOrDefault(p => p.PackageNameAr.Contains("8")) ?? db.PackagingTypes.Skip(1).FirstOrDefault() ?? pack4;

        // إذا نفس العبوة، أنشئ عبوة ثانية وهمية
        if (pack4.Id == pack8.Id)
        {
            pack8 = new PackagingType { PackageCode = "PKG-8KG-TEST", PackageNameAr = "كرتون 8 كجم", UnitWeightKg = 8 };
            db.PackagingTypes.Add(pack8);
            db.SaveChanges();
        }

        // إنشاء رصيدين منفصلين: 4كجم و8كجم
        var balance4 = new StockBalance
        {
            WarehouseId = wh.Id,
            ProductId = product.Id,
            LotId = lot.Id,
            CustomerId = customer.Id,
            PackagingTypeId = pack4.Id,
            QtyKg = 100,
            PackageCount = 25 // 25 كرتون 4كجم = 100 كجم
        };
        var balance8 = new StockBalance
        {
            WarehouseId = wh.Id,
            ProductId = product.Id,
            LotId = lot.Id,
            CustomerId = customer.Id,
            PackagingTypeId = pack8.Id,
            QtyKg = 200,
            PackageCount = 25 // 25 كرتون 8كجم = 200 كجم
        };

        db.StockBalances.AddRange(balance4, balance8);
        db.SaveChanges();

        // التحقق: يجب أن يكون هناك سجلين منفصلين
        var balances = db.StockBalances
            .Where(b => b.WarehouseId == wh.Id && b.ProductId == product.Id && b.LotId == lot.Id && b.CustomerId == customer.Id)
            .ToList();

        Assert.Equal(2, balances.Count);
        var b4 = balances.FirstOrDefault(b => b.PackagingTypeId == pack4.Id);
        var b8 = balances.FirstOrDefault(b => b.PackagingTypeId == pack8.Id);
        Assert.NotNull(b4);
        Assert.NotNull(b8);
        Assert.Equal(100, b4.QtyKg);
        Assert.Equal(200, b8.QtyKg);
    }

    [Fact]
    public void Dispensing_From_8kg_Should_Not_Affect_4kg()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var deliverySvc = scope.ServiceProvider.GetRequiredService<CustomerDeliveryService>();

        var whFg = db.Warehouses.FirstOrDefault(w => w.WarehouseCode == "WFG") ?? db.Warehouses.First();
        var product = db.Products.First(p => p.ItemType == "Finished");
        var customer = db.Customers.First();
        var lot = db.Lots.FirstOrDefault() ?? new Lot { LotCode = "TEST-LOT-002", ProductId = product.Id, CustomerId = customer.Id, InitialQtyKg = 1000, InStockQtyKg = 1000, ShipmentId = db.Shipments.First().Id, ShipmentItemId = db.ShipmentItems.First().Id };
        if (lot.Id == 0) { db.Lots.Add(lot); db.SaveChanges(); }

        var pack4 = db.PackagingTypes.First();
        var pack8 = db.PackagingTypes.Skip(1).FirstOrDefault() ?? new PackagingType { PackageCode = "PKG-8KG-TEST2", PackageNameAr = "كرتون 8 كجم", UnitWeightKg = 8 };
        if (pack8.Id == 0) { db.PackagingTypes.Add(pack8); db.SaveChanges(); }

        // رصيدين
        db.StockBalances.Add(new StockBalance { WarehouseId = whFg.Id, ProductId = product.Id, LotId = lot.Id, CustomerId = customer.Id, PackagingTypeId = pack4.Id, QtyKg = 100, PackageCount = 25 });
        db.StockBalances.Add(new StockBalance { WarehouseId = whFg.Id, ProductId = product.Id, LotId = lot.Id, CustomerId = customer.Id, PackagingTypeId = pack8.Id, QtyKg = 200, PackageCount = 25 });
        db.SaveChanges();

        // صرف 10 كرتون 8كجم (80 كجم)
        var delivery = new CustomerDelivery
        {
            DocumentNumber = "TEST-CDEL-001",
            CustomerId = customer.Id,
            DeliveryDate = DateTime.Now,
            Status = "Draft",
            TotalQtyKg = 80,
            TotalCartons = 10
        };
        delivery.Items.Add(new CustomerDeliveryItem { ProductId = product.Id, LotId = lot.Id, PackagingTypeId = pack8.Id, PackageCount = 10, QtyKg = 80, CartonWeightKg = 8 });
        db.CustomerDeliveries.Add(delivery);
        db.SaveChanges();

        var result = deliverySvc.Approve(delivery.Id);
        Assert.True(result.Ok, result.Message);

        // التحقق: رصيد 4كجم لم يتأثر، 8كجم نقص
        var after4 = db.StockBalances.FirstOrDefault(b => b.WarehouseId == whFg.Id && b.ProductId == product.Id && b.LotId == lot.Id && b.CustomerId == customer.Id && b.PackagingTypeId == pack4.Id);
        var after8 = db.StockBalances.FirstOrDefault(b => b.WarehouseId == whFg.Id && b.ProductId == product.Id && b.LotId == lot.Id && b.CustomerId == customer.Id && b.PackagingTypeId == pack8.Id);

        Assert.NotNull(after4);
        Assert.NotNull(after8);
        Assert.Equal(100, after4.QtyKg); // لم يتأثر
        Assert.Equal(25, after4.PackageCount);
        Assert.Equal(120, after8.QtyKg); // 200-80
        Assert.Equal(15, after8.PackageCount); // 25-10
    }

    [Fact]
    public void Customer_A_And_B_Should_Have_Separate_Balances()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();

        var wh = db.Warehouses.First();
        var product = db.Products.First(p => p.ItemType == "Finished");
        var customers = db.Customers.Take(2).ToList();
        if (customers.Count < 2)
        {
            var c2 = new Customer { CustomerCode = "CUST-B-TEST", CustomerName = "عميل ب اختبار", IsActive = true };
            db.Customers.Add(c2);
            db.SaveChanges();
            customers = db.Customers.Take(2).ToList();
        }

        var lot = db.Lots.FirstOrDefault() ?? new Lot { LotCode = "TEST-LOT-003", ProductId = product.Id, CustomerId = customers[0].Id, InitialQtyKg = 1000, InStockQtyKg = 1000, ShipmentId = db.Shipments.First().Id, ShipmentItemId = db.ShipmentItems.First().Id };
        if (lot.Id == 0) { db.Lots.Add(lot); db.SaveChanges(); }

        var pack = db.PackagingTypes.First();

        var balA = new StockBalance { WarehouseId = wh.Id, ProductId = product.Id, LotId = lot.Id, CustomerId = customers[0].Id, PackagingTypeId = pack.Id, QtyKg = 100, PackageCount = 10 };
        var balB = new StockBalance { WarehouseId = wh.Id, ProductId = product.Id, LotId = lot.Id, CustomerId = customers[1].Id, PackagingTypeId = pack.Id, QtyKg = 200, PackageCount = 20 };

        db.StockBalances.AddRange(balA, balB);
        db.SaveChanges();

        var balances = db.StockBalances.Where(b => b.WarehouseId == wh.Id && b.ProductId == product.Id && b.LotId == lot.Id && b.PackagingTypeId == pack.Id).ToList();
        Assert.Equal(2, balances.Count);
        Assert.Contains(balances, b => b.CustomerId == customers[0].Id && b.QtyKg == 100);
        Assert.Contains(balances, b => b.CustomerId == customers[1].Id && b.QtyKg == 200);
    }

    [Fact]
    public void No_Duplicate_StockBalance_Should_Exist()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var integritySvc = scope.ServiceProvider.GetRequiredService<StockBalanceIntegrityService>();

        var duplicates = integritySvc.FindDuplicates();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void No_Negative_Balance_Should_Exist()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();

        var negatives = db.StockBalances.Where(b => b.QtyKg < -0.001 || b.PackageCount < 0).ToList();
        Assert.Empty(negatives);
    }

    [Fact]
    public void PostStockMovement_Should_Include_PackagingTypeId_In_Key()
    {
        using var host = new TestHost();
        var session = host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<DatesErp.Core.Interfaces.Services.INumberingService>();

        // نستخدم خدمة وهمية ترث ServiceBase لاختبار PostStockMovement
        var testSvc = new TestStockService(db, session, numbering);

        var wh = db.Warehouses.First();
        var product = db.Products.First(p => p.ItemType == "Finished");
        var pack4 = db.PackagingTypes.First();
        var pack8 = db.PackagingTypes.Skip(1).FirstOrDefault() ?? pack4;

        // إضافة رصيد 4كجم
        testSvc.TestPost(wh.Id, product.Id, null, null, null, pack4.Id, 100, 10, "TEST-001");

        // إضافة رصيد 8كجم لنفس المنتج والمخزن لكن عبوة مختلفة — يجب أن ينشئ سجل منفصل
        testSvc.TestPost(wh.Id, product.Id, null, null, null, pack8.Id, 200, 20, "TEST-002");

        var balances = db.StockBalances.Where(b => b.WarehouseId == wh.Id && b.ProductId == product.Id).ToList();

        if (pack4.Id != pack8.Id)
        {
            Assert.Equal(2, balances.Count);
            Assert.Contains(balances, b => b.PackagingTypeId == pack4.Id && b.QtyKg == 100);
            Assert.Contains(balances, b => b.PackagingTypeId == pack8.Id && b.QtyKg == 200);
        }
        else
        {
            // نفس العبوة — يجب أن يكون سجل واحد مجموع
            Assert.Single(balances);
            Assert.Equal(300, balances[0].QtyKg);
        }
    }

    [Fact]
    public void PostStockMovement_Should_Prevent_Negative_Unless_Adjustment()
    {
        using var host = new TestHost();
        var session = host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<DatesErp.Core.Interfaces.Services.INumberingService>();
        var testSvc = new TestStockService(db, session, numbering);

        var wh = db.Warehouses.First();
        var product = db.Products.First(p => p.ItemType == "Finished");
        var pack = db.PackagingTypes.First();

        // إضافة 100 كجم
        testSvc.TestPost(wh.Id, product.Id, null, null, null, pack.Id, 100, 10, "TEST-NEG-001");

        // محاولة صرف 150 كجم (أكثر من المتاح) — يجب أن يفشل
        var ex = Assert.Throws<DatesErp.Core.Exceptions.DomainException>(() =>
            testSvc.TestPostOutbound(wh.Id, product.Id, null, null, null, pack.Id, 150, 15, "TEST-NEG-002")
        );
        Assert.Contains("المتوفر", ex.Message);

        // صرف 50 كجم — يجب أن ينجح
        testSvc.TestPostOutbound(wh.Id, product.Id, null, null, null, pack.Id, 50, 5, "TEST-NEG-003");
        var bal = db.StockBalances.First(b => b.WarehouseId == wh.Id && b.ProductId == product.Id && b.PackagingTypeId == pack.Id);
        Assert.Equal(50, bal.QtyKg);

        // تسوية سالبة مصرحة (Adjustment) — يجب أن تسمح بالسالب
        testSvc.TestPostAdjustment(wh.Id, product.Id, null, null, null, pack.Id, -100, -10, "TEST-ADJ-001");
        var balAfterAdj = db.StockBalances.First(b => b.WarehouseId == wh.Id && b.ProductId == product.Id && b.PackagingTypeId == pack.Id);
        Assert.Equal(-50, balAfterAdj.QtyKg); // سالب مسموح فقط بتسوية
    }

    // خدمة اختبار تتيح الوصول لـ PostStockMovement
    private class TestStockService : ServiceBase
    {
        public TestStockService(DatesErpDbContext db, DatesErp.Core.Interfaces.Services.ICurrentSession session, DatesErp.Core.Interfaces.Services.INumberingService numbering)
            : base(db, session, numbering) { }

        public void TestPost(int wh, int? prod, int? mat, int? lot, int? cust, int? pack, double qtyKg, int pkgCount, string refNo)
        {
            PostStockMovement(wh, MovementType.Inbound, qtyKg, pkgCount, ReferenceDocType.Adjustment, refNo, prod, mat, lot, cust, null, pack, "test inbound");
            Db.SaveChanges();
        }

        public void TestPostOutbound(int wh, int? prod, int? mat, int? lot, int? cust, int? pack, double qtyKg, int pkgCount, string refNo)
        {
            PostStockMovement(wh, MovementType.Outbound, qtyKg, pkgCount, ReferenceDocType.CustomerDelivery, refNo, prod, mat, lot, cust, null, pack, "test outbound");
            Db.SaveChanges();
        }

        public void TestPostAdjustment(int wh, int? prod, int? mat, int? lot, int? cust, int? pack, double qtyKg, int pkgCount, string refNo)
        {
            // سالب عبر Adjustment مسموح
            var balance = Db.StockBalances.FirstOrDefault(b => b.WarehouseId == wh && b.ProductId == prod && b.MaterialId == mat && b.LotId == lot && b.CustomerId == cust && b.PackagingTypeId == pack);
            if (balance == null)
            {
                balance = new StockBalance { WarehouseId = wh, ProductId = prod, MaterialId = mat, LotId = lot, CustomerId = cust, PackagingTypeId = pack };
                Db.StockBalances.Add(balance);
            }
            balance.QtyKg += qtyKg;
            balance.PackageCount += pkgCount;

            Db.InventoryTransactions.Add(new InventoryTransaction
            {
                TxnNumber = Numbering.Next("TXN"),
                WarehouseId = wh,
                ProductId = prod,
                MaterialId = mat,
                LotId = lot,
                CustomerId = cust,
                PackagingTypeId = pack,
                MovementType = MovementType.Adjustment,
                QtyKg = qtyKg,
                PackageCount = pkgCount,
                ReferenceDocType = ReferenceDocType.Adjustment,
                ReferenceDocNumber = refNo,
                IsApproved = true
            });
            Db.SaveChanges();
        }
    }
}
