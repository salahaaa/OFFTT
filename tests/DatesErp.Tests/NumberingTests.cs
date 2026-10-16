using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// اختبارات إصلاح الترقيم — كانت أرقام المستندات تتكرر (خطأ UNIQUE) لأن خدمة الترقيم
/// كانت Singleton فلا تُحفظ زيادة التسلسل أبداً. الآن Scoped + حلقة ضمان عدم التكرار.
/// </summary>
public class NumberingTests
{
    [Fact]
    public void Next_ProducesUniqueSequentialNumbers_AndPersists()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();

        var n1 = numbering.Next("SHIP"); db.SaveChanges();
        var n2 = numbering.Next("SHIP"); db.SaveChanges();
        var n3 = numbering.Next("SHIP"); db.SaveChanges();

        Assert.NotEqual(n1, n2);
        Assert.NotEqual(n2, n3);
        Assert.NotEqual(n1, n3);
        // §1.50.53 — التسلسل النقي 1،2،3…
        Assert.True(int.TryParse(n1, out _), $"يجب أن يكون نقياً: {n1}");
        Assert.True(int.TryParse(n2, out _), $"يجب أن يكون نقياً: {n2}");
        Assert.Equal("1", n1);
        Assert.Equal("2", n2);
        Assert.Equal("3", n3);

        // التسلسل محفوظ — خدمة جديدة في نفس النطاق تقرأ القيمة المحدثة
        var seq = db.NumberingSchemes.First(s => s.SchemeCode == "SHIP").LastSequence;
        Assert.True(seq >= 3, $"التسلسل يجب أن يُحفظ (كان {seq})");
    }

    [Fact]
    public void Peek_Shows_Next_Without_Reserving_And_Next_Matches_Peek()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();

        var peek1 = numbering.Peek("PLAN");
        Assert.Equal("1", peek1);
        // Peek لا يحجز — استدعاء ثانٍ يعطي نفس الرقم
        var peek2 = numbering.Peek("PLAN");
        Assert.Equal(peek1, peek2);

        var next = numbering.Next("PLAN"); db.SaveChanges();
        Assert.Equal(peek1, next);

        var peek3 = numbering.Peek("PLAN");
        Assert.Equal("2", peek3);
    }

    [Fact]
    public void Next_SelfCorrects_When_Sequence_Is_Stale_Behind_Existing_Data()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();

        // محاكاة قاعدة غير متزامنة: مستندات نقية موجودة حتى 7 بينما المخطط ما زال عند 0
        var cust = db.Customers.First();
        for (int i = 1; i <= 7; i++)
        {
            db.Shipments.Add(new Shipment
            {
                DocumentNumber = i.ToString(),
                CustomerId = cust.Id,
                TotalWeightKg = 100
            });
        }
        db.SaveChanges();
        var scheme = db.NumberingSchemes.First(s => s.SchemeCode == "SHIP");
        scheme.LastSequence = 0; // تسلسل متأخر (الخلل القديم)
        db.SaveChanges();

        // الرقم التالي يجب ألا يكرر أي رقم موجود — يقفز فوقها تلقائياً
        var next = numbering.Next("SHIP");
        Assert.True(int.TryParse(next, out var seq), $"الرقم الجديد يجب أن يكون نقياً 1،2،3… حصل على {next}");
        Assert.True(seq >= 8, $"الرقم يجب أن يتجاوز الموجود (حصل على {next})");

        // ولا يصطدم عند الحفظ الفعلي
        db.Shipments.Add(new Shipment { DocumentNumber = next, CustomerId = cust.Id, TotalWeightKg = 50 });
        db.SaveChanges(); // يجب ألا يرمي UNIQUE
    }

    [Fact]
    public void ProductionDeliveryNumbering_SkipsExistingPureNumber()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();

        // أمر تسليم إنتاج قديم موجود قبل إنشاء مخطط PDL أو بعد فقدان تزامن العداد.
        db.ProductionDeliveries.Add(new ProductionDelivery
        {
            DocumentNumber = "1",
            SourceType = DeliverySources.FromActual,
            SourceId = 1,
            DeliveryDate = DateTime.Today,
            Status = DocStatuses.Draft,
            ReceiptStatus = "None"
        });
        db.SaveChanges();

        var next = numbering.Next("PDL");
        Assert.Equal("2", next);
        db.ProductionDeliveries.Add(new ProductionDelivery
        {
            DocumentNumber = next,
            SourceType = DeliverySources.FromActual,
            SourceId = 2,
            DeliveryDate = DateTime.Today,
            Status = DocStatuses.Draft,
            ReceiptStatus = "None"
        });
        db.SaveChanges();
    }

    [Fact]
    public void TreatmentNumbering_UsesRawTreatments_AndRepairsAStaleSequence()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();
        int productId = db.Products.OrderBy(p => p.Id).Select(p => p.Id).First();
        var lot = new Lot
        {
            LotCode = "NUM-TEST-LOT",
            ProductId = productId,
            InitialQtyKg = 10,
            InStockQtyKg = 10,
            TreatmentReadyQtyKg = 10
        };
        db.Lots.Add(lot);
        db.SaveChanges();

        string AddTreatment()
        {
            var number = numbering.Next("TRT");
            db.RawTreatments.Add(NewRawTreatment(number, lot.Id, productId));
            db.SaveChanges();
            return number;
        }

        Assert.Equal("1", AddTreatment());
        Assert.Equal("2", AddTreatment());

        // حتى لو فُقدت زيادة المخطط، RawTreatments هو مصدر الحقيقة ويمنع إعادة الاستخدام.
        var scheme = db.NumberingSchemes.Single(s => s.SchemeCode == "TRT");
        scheme.LastSequence = 0;
        db.SaveChanges();
        Assert.Equal("3", AddTreatment());

        var saved = db.RawTreatments.OrderBy(t => t.Id).Select(t => t.TreatmentNo).ToList();
        Assert.Equal(new[] { "1", "2", "3" }, saved);
        Assert.Equal(saved.Count, saved.Distinct().Count());
    }

    [Fact]
    public async Task TreatmentNumbering_StaysUniqueAcrossConcurrentSqliteContexts()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"DateERP-TreatmentNumbering-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 15,
            Pooling = false
        }.ToString();

        try
        {
            using (var firstHost = new TestHost(sqliteConnectionString: connectionString))
            using (var secondHost = new TestHost(sqliteConnectionString: connectionString))
            {
                var setupDb = firstHost.Get<DatesErpDbContext>();
                int productId = setupDb.Products.OrderBy(p => p.Id).Select(p => p.Id).First();
                var lot = new Lot
                {
                    LotCode = "NUM-CONCURRENT-LOT",
                    ProductId = productId,
                    InitialQtyKg = 10,
                    InStockQtyKg = 10,
                    TreatmentReadyQtyKg = 10
                };
                setupDb.Lots.Add(lot);
                setupDb.SaveChanges();

                using var startTogether = new Barrier(2);
                var first = Task.Run(() => ReserveTreatmentNumber(firstHost, startTogether, lot.Id, productId));
                var second = Task.Run(() => ReserveTreatmentNumber(secondHost, startTogether, lot.Id, productId));
                var numbers = await Task.WhenAll(first, second);

                Assert.Equal(2, numbers.Distinct().Count());
                Assert.Equal(new[] { "1", "2" }, numbers.OrderBy(number => int.Parse(number)).ToArray());

                using var checkScope = firstHost.Services.CreateScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
                var saved = checkDb.RawTreatments.Select(t => t.TreatmentNo).OrderBy(number => int.Parse(number)).ToList();
                Assert.Equal(new[] { "1", "2" }, saved);
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            {
                var file = databasePath + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }

    private static string ReserveTreatmentNumber(TestHost host, Barrier barrier, int lotId, int productId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("لم يبدأ سياقا SQLite معاً ضمن المهلة.");

        using var transaction = db.Database.BeginTransaction();
        var number = numbering.Next("TRT");
        db.RawTreatments.Add(NewRawTreatment(number, lotId, productId));
        db.SaveChanges();
        transaction.Commit();
        return number;
    }

    private static RawTreatment NewRawTreatment(string number, int lotId, int productId)
    {
        var started = DateTime.Now;
        return new RawTreatment
        {
            TreatmentNo = number,
            LotId = lotId,
            ProductId = productId,
            QtyKg = 1,
            PackageCount = 1,
            StartedAt = started,
            DurationHours = 1,
            ExpectedReadyAt = started.AddHours(1),
            Notes = "اختبار ترقيم المعالجة",
            Status = TreatmentStatuses.InProgress
        };
    }

    [Fact]
    public void Next_CreatesMissingScheme_SelfHealing()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var numbering = scope.ServiceProvider.GetRequiredService<INumberingService>();

        // مخطط غير موجود إطلاقاً — يُنشأ ذاتياً بدل الانهيار
        Assert.Null(db.NumberingSchemes.FirstOrDefault(s => s.SchemeCode == "NEWSCHEME"));
        var n1 = numbering.Next("NEWSCHEME"); db.SaveChanges();
        var n2 = numbering.Next("NEWSCHEME"); db.SaveChanges();
        Assert.NotEqual(n1, n2);
        Assert.NotNull(db.NumberingSchemes.FirstOrDefault(s => s.SchemeCode == "NEWSCHEME"));
    }

    [Fact]
    public void ApproveShipment_CreatesUniqueLotsAndTransactions_NoDuplicateErrors()
    {
        // سيناريو المستخدم حرفياً: حفظ سندات واعتمادها بلا أخطاء UNIQUE
        using var host = new TestHost();
        host.LoginAsAdmin();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var receiving = scope.ServiceProvider.GetRequiredService<IReceivingService>();
        var cust = db.Customers.First();

        for (int s = 0; s < 3; s++)
        {
            var prod = db.Products.First(p => p.GroupCode == "001");
            var save = receiving.SaveShipment(cust.Id, "2026-08-25", "2026-08-25",
                new List<ShipmentItemDto>
                {
                    new() { TreatmentRequired = false, ProductId = prod.Id, PackageCount = 10, UnitWeightKg = 20, QtyKg = 200 }
                });
            Assert.True(save.Ok, save.Message);
            var approve = receiving.ApproveShipment(save.Id);
            Assert.True(approve.Ok, approve.Message);
        }

        // كل أرقام المستندات والدفعات والحركات فريدة
        var docNos = db.Shipments.Select(x => x.DocumentNumber).ToList();
        Assert.Equal(docNos.Count, docNos.Distinct().Count());
        var lotCodes = db.Lots.Select(x => x.LotCode).ToList();
        Assert.Equal(lotCodes.Count, lotCodes.Distinct().Count());
        var txnNos = db.InventoryTransactions.Select(x => x.TxnNumber).ToList();
        Assert.Equal(txnNos.Count, txnNos.Distinct().Count());
    }
}
