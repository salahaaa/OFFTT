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
        var rowVersionEntities = db.Model.GetEntityTypes()
            .Where(entityType => typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            .ToList();
        Assert.NotEmpty(rowVersionEntities);
        foreach (var entityType in rowVersionEntities)
        {
            var rowVersion = entityType.FindProperty(nameof(BaseEntity.RowVersion))!;
            Assert.False(rowVersion.IsNullable, $"{entityType.ClrType.Name}.RowVersion must remain required.");
            Assert.True(rowVersion.IsConcurrencyToken, $"{entityType.ClrType.Name}.RowVersion must remain a concurrency token.");
            Assert.Equal(ValueGenerated.Never, rowVersion.ValueGenerated);
        }
        foreach (var requiredType in new[]
                 {
                     typeof(AuditLog), typeof(PermissionAuditLog), typeof(UnitConversion), typeof(ByProduct),
                     typeof(InspectionResultType), typeof(SystemSetting), typeof(RoleResourcePermission)
                 })
            Assert.Contains(rowVersionEntities, entityType => entityType.ClrType == requiredType);

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
    public void Sqlite_DirectContext_AdditionsAndUpdatesGetRowVersionsWithoutInterceptor()
    {
        using var host = new TestHost();
        var options = new DbContextOptionsBuilder<DatesErpDbContext>()
            .UseSqlite(host.Connection)
            .Options;
        using var db = new DatesErpDbContext(options); // deliberately bypasses DI interceptors

        var unitConversion = new UnitConversion { FromUnitId = 1, ToUnitId = 2, Factor = 1.25m };
        var byProduct = new ByProduct { ByProductCode = "RV-BP", ByProductNameAr = "اختبار رمز الصف" };
        var resultType = new InspectionResultType
        {
            Code = "RV-RESULT", NameAr = "اختبار رمز الصف", ResultKind = InspectionResultType.KindAccepted,
            UnitLabel = "كجم", SortNo = 999
        };
        var setting = new SystemSetting
        {
            SettingKey = "Sqlite_RowVersion_Direct_" + Guid.NewGuid().ToString("N"),
            SettingValue = "before"
        };
        var rolePermission = new RoleResourcePermission { RoleId = 1, ResourceId = 1, OperationId = 1, IsAllowed = true };
        var permissionLog = new PermissionAuditLog
        {
            ChangedByName = "test", ResourceCode = "test", OperationCode = "Edit",
            OldValue = "before", NewValue = "after", ActionType = "grant"
        };
        var auditLog = new AuditLog
        {
            UserName = "test", ComputerName = "test", MachineName = "test", ScreenName = "test",
            ActionType = "Create", DocumentType = "Test", DocumentNumber = "1", OldValue = "", NewValue = "{}"
        };
        BaseEntity[] entities = { unitConversion, byProduct, resultType, setting, rolePermission, permissionLog, auditLog };

        db.AddRange(entities);
        db.SaveChanges();

        Assert.All(entities, entity => Assert.NotEmpty(entity.RowVersion));
        Assert.Equal(entities.Length,
            entities.Select(entity => Convert.ToHexString(entity.RowVersion)).Distinct().Count());
        var oldUnitVersion = unitConversion.RowVersion.ToArray();
        var oldByProductVersion = byProduct.RowVersion.ToArray();
        var oldResultVersion = resultType.RowVersion.ToArray();
        var oldSettingVersion = setting.RowVersion.ToArray();
        var oldRolePermissionVersion = rolePermission.RowVersion.ToArray();
        var oldPermissionVersion = permissionLog.RowVersion.ToArray();
        var oldAuditVersion = auditLog.RowVersion.ToArray();

        unitConversion.Factor = 2m;
        byProduct.IsActive = false;
        resultType.NameAr = "اختبار رمز الصف — تعديل";
        setting.SettingValue = "after";
        rolePermission.IsAllowed = false;
        permissionLog.ActionType = "revoke";
        auditLog.ActionType = "Edit";
        db.SaveChanges();

        Assert.False(oldUnitVersion.SequenceEqual(unitConversion.RowVersion));
        Assert.False(oldByProductVersion.SequenceEqual(byProduct.RowVersion));
        Assert.False(oldResultVersion.SequenceEqual(resultType.RowVersion));
        Assert.False(oldSettingVersion.SequenceEqual(setting.RowVersion));
        Assert.False(oldRolePermissionVersion.SequenceEqual(rolePermission.RowVersion));
        Assert.False(oldPermissionVersion.SequenceEqual(permissionLog.RowVersion));
        Assert.False(oldAuditVersion.SequenceEqual(auditLog.RowVersion));
    }

    [Fact]
    public async Task Sqlite_DirectContextAsyncSaveChanges_UpdatesRowVersion()
    {
        using var host = new TestHost();
        var options = new DbContextOptionsBuilder<DatesErpDbContext>()
            .UseSqlite(host.Connection)
            .Options;
        await using var db = new DatesErpDbContext(options);
        var setting = new SystemSetting
        {
            SettingKey = "Sqlite_RowVersion_Async_" + Guid.NewGuid().ToString("N"),
            SettingValue = "before"
        };

        db.SystemSettings.Add(setting);
        await db.SaveChangesAsync();
        var original = setting.RowVersion.ToArray();
        setting.SettingValue = "after";
        await db.SaveChangesAsync();

        Assert.NotEmpty(setting.RowVersion);
        Assert.False(original.SequenceEqual(setting.RowVersion));
    }

    [Fact]
    public void Sqlite_SchemaMigrator_AddsRequiredRowVersionToLegacyTable()
    {
        using var host = new TestHost();
        var db = host.Get<DatesErpDbContext>();
        db.Database.ExecuteSqlRaw("ALTER TABLE [SystemSettings] DROP COLUMN [RowVersion]");

        var changes = SchemaMigrator.Migrate(db);
        Assert.Contains(changes, change => change.Contains("SystemSettings.RowVersion", StringComparison.Ordinal));

        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT [notnull] FROM pragma_table_info('SystemSettings') WHERE name = 'RowVersion'";
        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));

        db.ChangeTracker.Clear();
        var seededSetting = db.SystemSettings.AsNoTracking().First();
        Assert.NotEmpty(seededSetting.RowVersion);
    }

    [Fact]
    public void SqlServer_RowVersion_Remains_Store_Generated()
    {
        var options = new DbContextOptionsBuilder<DatesErpDbContext>()
            .UseSqlServer("Server=localhost;Database=DateErpRowVersionModel;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new DatesErpDbContext(options);
        var rowVersionEntities = db.Model.GetEntityTypes()
            .Where(entityType => typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            .ToList();
        Assert.NotEmpty(rowVersionEntities);
        foreach (var entityType in rowVersionEntities)
        {
            var rowVersion = entityType.FindProperty(nameof(BaseEntity.RowVersion))!;
            Assert.False(rowVersion.IsNullable, $"{entityType.ClrType.Name}.RowVersion must remain required.");
            Assert.True(rowVersion.IsConcurrencyToken, $"{entityType.ClrType.Name}.RowVersion must remain a concurrency token.");
            Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        }
    }
}
