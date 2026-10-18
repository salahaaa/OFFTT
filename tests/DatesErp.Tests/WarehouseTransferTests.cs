using DatesErp.Application.Services;
using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §تحويل مخزني (WHT) — خطة قبول:
///  1) المسودة لا تمس المخزون. 2) الاعتماد يخصم من المصدر ويضيف للمستلم بنفس مفتاح الرصيد
///  ويُنشئ حركتين لكل بند بمرجع السند. 3) الكمية فوق المتاح مرفوضة (حفظاً واعتماداً).
///  4) الاعتماد المكرر مرفوض بلا حركات إضافية. 5) فشل بند واحد يُلغي كل الحركات (ذرية).
///  6) الإلغاء يعكس الحركات ويعيد الرصيد. 7) لا حذف/تعديل لمعتمد. 8) نفس المخزن مرفوض.
/// </summary>
public class WarehouseTransferTests
{
    private static WarehouseTransferService Svc(TestHost host)
        => new(host.Services.CreateScope().ServiceProvider.GetRequiredService<DatesErpDbContext>(),
               host.Services.GetRequiredService<ICurrentSession>(),
               host.Services.CreateScope().ServiceProvider.GetRequiredService<INumberingService>());

    private static DatesErpDbContext Db(TestHost host)
        => new(new DbContextOptionsBuilder<DatesErpDbContext>().UseSqlite(host.Connection).Options);

    private sealed record Ctx(int Src, int Dst, int ProductId, int? LotId, int? CustomerId, int? PackagingTypeId);

    /// <summary>رصيد ابتدائي حقيقي في جدول الرصيد (المصدر الذي يقرأه PostStockMovement) — 100 كجم / 20 عبوة في مخزن التام.</summary>
    private static Ctx Seed(TestHost host, double qty = 100, int pkgs = 20)
    {
        using var db = Db(host);
        int src = db.Warehouses.First(w => w.WarehouseCode == "WFG").Id;
        int dst = db.Warehouses.First(w => w.WarehouseCode == "WRM").Id;
        var product = db.Products.OrderBy(p => p.Id).First();
        int? lot = db.Lots.OrderBy(l => l.Id).Select(l => (int?)l.Id).FirstOrDefault();
        int? cust = db.Customers.OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefault();
        int? pkg = db.PackagingTypes.OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefault();
        db.StockBalances.Add(new StockBalance
        {
            WarehouseId = src, ProductId = product.Id, LotId = lot, CustomerId = cust, PackagingTypeId = pkg,
            QtyKg = qty, PackageCount = pkgs
        });
        db.SaveChanges();
        return new Ctx(src, dst, product.Id, lot, cust, pkg);
    }

    private static double Bal(TestHost host, int wh, Ctx c)
    {
        using var db = Db(host);
        return db.StockBalances.Where(b => b.WarehouseId == wh && b.ProductId == c.ProductId && b.LotId == c.LotId
            && b.CustomerId == c.CustomerId && b.PackagingTypeId == c.PackagingTypeId).Select(b => b.QtyKg).FirstOrDefault();
    }

    private static int Pkgs(TestHost host, int wh, Ctx c)
    {
        using var db = Db(host);
        return db.StockBalances.Where(b => b.WarehouseId == wh && b.ProductId == c.ProductId && b.LotId == c.LotId
            && b.CustomerId == c.CustomerId && b.PackagingTypeId == c.PackagingTypeId).Select(b => b.PackageCount).FirstOrDefault();
    }

    private static WarehouseTransferItemDto Item(Ctx c, double qty, int pkgs) => new()
    {
        ProductId = c.ProductId, LotId = c.LotId, CustomerId = c.CustomerId, PackagingTypeId = c.PackagingTypeId,
        QtyKg = qty, PackageCount = pkgs
    };

    private static int TxnCount(TestHost host, string docNo)
    {
        using var db = Db(host);
        return db.InventoryTransactions.Count(t => t.ReferenceDocType == ReferenceDocType.WarehouseTransfer && t.ReferenceDocNumber == docNo);
    }

    [Fact]
    public void Draft_Does_Not_Touch_Stock_And_Gets_Number()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 40, 8) }, "اختبار");
        Assert.True(r.Ok, r.Message);
        Assert.False(string.IsNullOrWhiteSpace(r.DocumentNumber));
        Assert.Equal(100, Bal(host, c.Src, c));
        Assert.Equal(0, Bal(host, c.Dst, c));
        Assert.Equal(0, TxnCount(host, r.DocumentNumber));
        using var db = Db(host);
        var doc = db.WarehouseTransfers.Include(t => t.Items).Single(t => t.Id == r.Id);
        Assert.Equal(DocStatuses.Draft, doc.Status);
        Assert.Single(doc.Items);
    }

    [Fact]
    public void Approve_Moves_Qty_And_Packages_Between_Warehouses_With_Two_Txns_Per_Item()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 40, 8) }, null);
        Assert.True(r.Ok, r.Message);
        var a = Svc(host).ApproveTransfer(r.Id);
        Assert.True(a.Ok, a.Message);

        Assert.Equal(60, Bal(host, c.Src, c), 3);
        Assert.Equal(40, Bal(host, c.Dst, c), 3);
        Assert.Equal(12, Pkgs(host, c.Src, c));
        Assert.Equal(8, Pkgs(host, c.Dst, c));
        Assert.Equal(2, TxnCount(host, r.DocumentNumber));
        using var db = Db(host);
        var txns = db.InventoryTransactions.Where(t => t.ReferenceDocNumber == r.DocumentNumber).ToList();
        Assert.Contains(txns, t => t.WarehouseId == c.Src && t.MovementType == MovementType.Outbound && t.QtyKg == -40);
        Assert.Contains(txns, t => t.WarehouseId == c.Dst && t.MovementType == MovementType.Inbound && t.QtyKg == 40);
        Assert.All(txns, t => Assert.Equal(c.LotId, t.LotId)); // الدفعة تبقى مرتبطة بالمصدر
        var doc = db.WarehouseTransfers.Single(t => t.Id == r.Id);
        Assert.Equal(DocStatuses.Approved, doc.Status);
        Assert.True(doc.IsApproved && doc.IsPosted);
    }

    [Fact]
    public void Over_Available_Is_Rejected_On_Save_And_On_Approve()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var over = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 150, 1) }, null);
        Assert.False(over.Ok);
        Assert.Contains("أكبر من المتاح", over.Message);

        // مسودة صحيحة ثم يُستهلك الرصيد قبل الاعتماد → الاعتماد يفشل ولا تتحرك أي كمية
        var ok = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 80, 10) }, null);
        Assert.True(ok.Ok, ok.Message);
        using (var db = Db(host))
        {
            var b = db.StockBalances.First(x => x.WarehouseId == c.Src && x.ProductId == c.ProductId);
            b.QtyKg = 50; db.SaveChanges();
        }
        var a = Svc(host).ApproveTransfer(ok.Id);
        Assert.False(a.Ok);
        Assert.Equal(50, Bal(host, c.Src, c), 3);
        Assert.Equal(0, Bal(host, c.Dst, c), 3);
        Assert.Equal(0, TxnCount(host, ok.DocumentNumber));
    }

    [Fact]
    public void Negative_Or_Zero_Qty_And_Same_Warehouse_Are_Rejected()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        Assert.False(Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 0, 0) }, null).Ok);
        Assert.False(Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, -5, 0) }, null).Ok);
        Assert.False(Svc(host).SaveTransfer("01/10/2026", c.Src, c.Src, new() { Item(c, 5, 1) }, null).Ok);
        Assert.False(Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new(), null).Ok);
        // تكرار نفس مفتاح الرصيد في بندين
        Assert.False(Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 5, 1), Item(c, 5, 1) }, null).Ok);
    }

    [Fact]
    public void Double_Approve_Is_Rejected_Without_Extra_Movements()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 30, 6) }, null);
        Assert.True(Svc(host).ApproveTransfer(r.Id).Ok);
        var again = Svc(host).ApproveTransfer(r.Id);
        Assert.False(again.Ok);
        Assert.Equal(70, Bal(host, c.Src, c), 3);
        Assert.Equal(30, Bal(host, c.Dst, c), 3);
        Assert.Equal(2, TxnCount(host, r.DocumentNumber));
    }

    [Fact]
    public void Approve_Is_Atomic_When_Second_Item_Fails()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        // بند ثانٍ لصنف آخر بلا رصيد — يُدخل مباشرة كمسودة في القاعدة لتجاوز تحقق الحفظ ومحاكاة تغيّر الرصيد بعد الحفظ
        int otherProduct;
        string docNo;
        int docId;
        using (var db = Db(host))
        {
            otherProduct = db.Products.OrderBy(p => p.Id).Skip(1).Select(p => p.Id).First();
            var doc = new WarehouseTransfer
            {
                DocumentNumber = "WHT-ATOMIC-TEST", TransferDate = DateTime.Now, SourceWarehouseId = c.Src, DestinationWarehouseId = c.Dst,
                Status = DocStatuses.Draft
            };
            doc.Items.Add(new WarehouseTransferItem { ProductId = c.ProductId, LotId = c.LotId, CustomerId = c.CustomerId, PackagingTypeId = c.PackagingTypeId, QtyKg = 20, PackageCount = 2 });
            doc.Items.Add(new WarehouseTransferItem { ProductId = otherProduct, QtyKg = 5, PackageCount = 1 });
            db.WarehouseTransfers.Add(doc);
            db.SaveChanges();
            docNo = doc.DocumentNumber; docId = doc.Id;
        }
        var a = Svc(host).ApproveTransfer(docId);
        Assert.False(a.Ok);
        Assert.Equal(100, Bal(host, c.Src, c), 3);   // لم يُخصم البند الأول رغم صحته
        Assert.Equal(0, Bal(host, c.Dst, c), 3);
        Assert.Equal(0, TxnCount(host, docNo));
        using var db2 = Db(host);
        Assert.Equal(DocStatuses.Draft, db2.WarehouseTransfers.Single(t => t.Id == docId).Status);
    }

    [Fact]
    public void Cancel_Approved_Reverses_Movements_And_Restores_Balances()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 25, 5) }, null);
        Assert.True(Svc(host).ApproveTransfer(r.Id).Ok);
        var x = Svc(host).CancelTransfer(r.Id, "خطأ في الاختيار");
        Assert.True(x.Ok, x.Message);
        Assert.Equal(100, Bal(host, c.Src, c), 3);
        Assert.Equal(0, Bal(host, c.Dst, c), 3);
        Assert.Equal(20, Pkgs(host, c.Src, c));
        Assert.Equal(2, TxnCount(host, r.DocumentNumber + "#REV"));
        using var db = Db(host);
        var doc = db.WarehouseTransfers.Single(t => t.Id == r.Id);
        Assert.Equal(DocStatuses.Cancelled, doc.Status);
        Assert.Equal("خطأ في الاختيار", doc.CancelReason);
        // إلغاء ثانٍ مرفوض ولا حركات إضافية
        Assert.False(Svc(host).CancelTransfer(r.Id, "x").Ok);
        Assert.Equal(2, TxnCount(host, r.DocumentNumber + "#REV"));
    }

    [Fact]
    public void Cancel_Approved_Fails_If_Destination_Already_Consumed()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 25, 5) }, null);
        Assert.True(Svc(host).ApproveTransfer(r.Id).Ok);
        using (var db = Db(host))
        {
            var b = db.StockBalances.First(x => x.WarehouseId == c.Dst && x.ProductId == c.ProductId);
            b.QtyKg = 10; db.SaveChanges(); // صُرف 15 من المستلم
        }
        var x = Svc(host).CancelTransfer(r.Id, "محاولة عكس");
        Assert.False(x.Ok);
        Assert.Equal(75, Bal(host, c.Src, c), 3);  // لا تغيير جزئي
        Assert.Equal(10, Bal(host, c.Dst, c), 3);
        using var db2 = Db(host);
        Assert.Equal(DocStatuses.Approved, db2.WarehouseTransfers.Single(t => t.Id == r.Id).Status);
    }

    [Fact]
    public void Approved_Cannot_Be_Edited_Or_Deleted_But_Draft_Can()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 10, 2) }, null);
        var u = Svc(host).UpdateTransfer(r.Id, "02/10/2026", c.Src, c.Dst, new() { Item(c, 15, 3) }, "معدل");
        Assert.True(u.Ok, u.Message);
        Assert.Equal(r.DocumentNumber, u.DocumentNumber); // نفس الرقم
        var got = Svc(host).GetTransfer(r.Id);
        Assert.Equal(15, got.Items.Single().QtyKg, 3);
        Assert.Equal("معدل", got.Notes);

        Assert.True(Svc(host).ApproveTransfer(r.Id).Ok);
        Assert.False(Svc(host).UpdateTransfer(r.Id, "02/10/2026", c.Src, c.Dst, new() { Item(c, 5, 1) }, null).Ok);
        Assert.False(Svc(host).DeleteDraft(r.Id).Ok);

        var d = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 5, 1) }, null);
        Assert.True(Svc(host).DeleteDraft(d.Id).Ok);
        using var db = Db(host);
        Assert.False(db.WarehouseTransfers.Any(t => t.Id == d.Id));
        Assert.False(db.WarehouseTransferItems.Any(i => i.TransferId == d.Id));
    }

    [Fact]
    public void Stock_By_Warehouse_Reflects_Real_Balances_And_Lists_Show_Names()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var before = Svc(host).GetStockByWarehouse(null, c.Src, null);
        Assert.Contains(before, b => b.ProductId == c.ProductId && b.QtyKg == 100 && !string.IsNullOrEmpty(b.ProductName) && !string.IsNullOrEmpty(b.WarehouseName));
        Assert.Empty(Svc(host).GetStockByWarehouse(c.ProductId, c.Dst, null));

        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 40, 8) }, null);
        Assert.True(Svc(host).ApproveTransfer(r.Id).Ok);
        Assert.Contains(Svc(host).GetStockByWarehouse(c.ProductId, c.Dst, null), b => b.QtyKg == 40 && b.LotId == c.LotId);

        var list = Svc(host).GetTransfers();
        var row = list.Single(t => t.Id == r.Id);
        Assert.Equal("معتمد", row.StatusAr);
        Assert.Equal(40, row.TotalQtyKg, 3);
        Assert.False(string.IsNullOrEmpty(row.SourceWarehouseName));
        Assert.False(string.IsNullOrEmpty(row.DestinationWarehouseName));
    }

    [Fact]
    public void Requires_Inventory_Permissions()
    {
        using var host = new TestHost();
        var c = Seed(host);
        // بلا تسجيل دخول → رفض صلاحية (لا استثناء مبتلع ولا كتابة)
        Assert.ThrowsAny<Exception>(() => Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 1, 1) }, null));
        using var db = Db(host);
        Assert.Empty(db.WarehouseTransfers);
    }

    /// <summary>الطباعة تقرأ من السند المحفوظ فقط، وتعرض المصدر/المستلم/الحالة/البنود، ولا تغيّر المخزون ولا حالة السند.</summary>
    [Fact]
    public void Print_Model_Reads_Saved_Document_And_Does_Not_Touch_Stock_Or_Status()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        var c = Seed(host);
        var r = Svc(host).SaveTransfer("01/10/2026", c.Src, c.Dst, new() { Item(c, 40, 8) }, "ملاحظة اختبار");
        Assert.True(r.Ok, r.Message);

        string srcName, dstName;
        using (var db0 = Db(host))
        {
            srcName = db0.Warehouses.Single(w => w.Id == c.Src).WarehouseNameAr;
            dstName = db0.Warehouses.Single(w => w.Id == c.Dst).WarehouseNameAr;
        }

        // مسودة: بلا حركات — نموذج الطباعة يقول ذلك صراحة
        using (var db = Db(host))
        {
            var m = DatesErp.Desktop.Printing.StoredPrintModels.WarehouseTransfer(db, r.Id);
            Assert.Contains(m.Info, x => x.Label == "من مخزن" && x.Value == srcName);
            Assert.Contains(m.Info, x => x.Label == "إلى مخزن" && x.Value == dstName);
            Assert.Contains(m.Info, x => x.Label == "الحالة" && x.Value == DocStatuses.ToArabic(DocStatuses.Draft));
            Assert.Single(m.Rows);
            Assert.Equal(40m, m.Rows[0][5]);
            Assert.Equal(8, m.Rows[0][6]);
            Assert.Empty(m.SecondRows);
            Assert.Contains("لم تُنفَّذ", m.SecondTitle);
        }
        Assert.Equal(100, Bal(host, c.Src, c), 3);
        Assert.Equal(0, TxnCount(host, r.DocumentNumber));

        // معتمد: الحركات الفعلية تظهر في القسم الثاني والطباعة لا تُنشئ حركات إضافية
        Assert.True(Svc(host).ApproveTransfer(r.Id).Ok);
        using (var db = Db(host))
        {
            var m = DatesErp.Desktop.Printing.StoredPrintModels.WarehouseTransfer(db, r.Id);
            Assert.Contains(m.Info, x => x.Label == "الحالة" && x.Value == DocStatuses.ToArabic(DocStatuses.Approved));
            Assert.Equal(2, m.SecondRows.Count);
            Assert.Contains(m.SecondRows, row => (string)row[2] == srcName && (string)row[3] == "صادر");
            Assert.Contains(m.SecondRows, row => (string)row[2] == dstName && (string)row[3] == "وارد");
        }
        Assert.Equal(60, Bal(host, c.Src, c), 3);
        Assert.Equal(40, Bal(host, c.Dst, c), 3);
        Assert.Equal(2, TxnCount(host, r.DocumentNumber));
        using var db2 = Db(host);
        Assert.Equal(DocStatuses.Approved, db2.WarehouseTransfers.Single(t => t.Id == r.Id).Status);
    }
}
