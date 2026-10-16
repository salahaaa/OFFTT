using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// SQLite has no database-generated rowversion column. These tests ensure that
/// application-generated concurrency tokens cover AuditLog and ordinary BaseEntity
/// writes, while SQL Server keeps its native store-generated rowversion contract.
/// </summary>
public sealed class SqliteRowVersionTests
{
    [Fact]
    public void Sqlite_Seeded_AuditLogs_Have_Application_Generated_RowVersions()
    {
        using var host = new TestHost();
        var db = host.Get<DatesErpDbContext>();
        var rowVersion = db.Model.FindEntityType(typeof(AuditLog))!
            .FindProperty(nameof(BaseEntity.RowVersion))!;

        Assert.False(rowVersion.IsNullable);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, rowVersion.ValueGenerated);

        var auditRows = db.AuditLogs.AsNoTracking().ToList();
        Assert.NotEmpty(auditRows);
        Assert.All(auditRows, row => Assert.NotEmpty(row.RowVersion));
        Assert.Equal(auditRows.Count,
            auditRows.Select(row => Convert.ToHexString(row.RowVersion)).Distinct().Count());
    }

    [Fact]
    public void Sqlite_Generates_RowVersion_For_BaseEntity_Insert_And_Update()
    {
        using var host = new TestHost();
        var db = host.Get<DatesErpDbContext>();
        var setting = new SystemSetting { SettingKey = "Sqlite_RowVersion_Probe", SettingValue = "before" };

        db.SystemSettings.Add(setting);
        db.SaveChanges();
        Assert.NotEmpty(setting.RowVersion);
        var original = setting.RowVersion.ToArray();

        setting.SettingValue = "after";
        db.SaveChanges();

        Assert.NotEmpty(setting.RowVersion);
        Assert.False(original.SequenceEqual(setting.RowVersion));
    }

    [Fact]
    public void Sqlite_Stale_RowVersion_Still_Rejects_Concurrent_Update()
    {
        using var host = new TestHost();
        using var scope1 = host.Services.CreateScope();
        using var scope2 = host.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        int customerId = db1.Customers.OrderBy(customer => customer.Id).Select(customer => customer.Id).First();
        var first = db1.Customers.Single(customer => customer.Id == customerId);
        var stale = db2.Customers.Single(customer => customer.Id == customerId);

        first.Phone = "701000001";
        db1.SaveChanges();
        stale.Phone = "701000002";

        Assert.Throws<DbUpdateConcurrencyException>(() => db2.SaveChanges());
    }

    [Fact]
    public void SqlServer_RowVersion_Remains_Store_Generated()
    {
        var options = new DbContextOptionsBuilder<DatesErpDbContext>()
            .UseSqlServer("Server=localhost;Database=DateErpRowVersionModel;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new DatesErpDbContext(options);
        var rowVersion = db.Model.FindEntityType(typeof(AuditLog))!
            .FindProperty(nameof(BaseEntity.RowVersion))!;

        Assert.False(rowVersion.IsNullable);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
    }
}
