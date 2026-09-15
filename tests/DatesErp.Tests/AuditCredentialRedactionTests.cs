using DatesErp.Application.Services;
using DatesErp.Core.Domain.Entities;
using DatesErp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §B110 — سجل التدقيق لا يحوي أي أثر لحقول الاعتماد (بصمة كلمة المرور والملح)،
/// لا في اللقطات الجديدة ولا في السجلات التاريخية بعد الترحيل.
/// قبل الإصلاح: كل تعديل مستخدم كان يكتب PasswordHash/PasswordSalt نصاً في AuditLog.
/// </summary>
public class AuditCredentialRedactionTests
{
    [Fact]
    public void PasswordReset_AuditTrail_DoesNotContainHashOrSalt()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();

        int userId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var master = scope.ServiceProvider.GetRequiredService<MasterDataService>();
            var user = db.Users.First(u => u.UserName == "warehouse");
            userId = user.Id;

            var r = master.ResetUserPassword(user.Id, "NewPass@345");
            Assert.True(r.Ok, r.Message);
        }

        using (var verify = host.Services.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var userRows = db.AuditLogs.Where(a => a.DocumentType == nameof(AppUser)).ToList();
            Assert.NotEmpty(userRows); // التعديل دُقّق فعلاً

            foreach (var row in userRows)
            {
                var combined = (row.OldValue ?? "") + (row.NewValue ?? "");
                Assert.DoesNotContain("PasswordHash", combined);
                Assert.DoesNotContain("PasswordSalt", combined);
            }
            // اسم المستخدم وبقيته غير السرية تبقى للتدقيق — الحجب يخص الاعتماد فقط
            Assert.Contains(userRows, r => r.ActionType == "Edit");
        }
    }

    [Fact]
    public void UserEdit_OtherFields_StillAuditedWithValues()
    {
        using var host = new TestHost();
        host.LoginAsAdmin();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
        var user = db.Users.First(u => u.UserName == "quality");
        user.FullName = "مسؤول الجودة الأول";
        db.SaveChanges();

        var row = db.AuditLogs.AsEnumerable()
            .Where(a => a.DocumentType == nameof(AppUser) && a.ActionType == "Edit")
            .OrderByDescending(a => a.Id).First();
        Assert.Contains("مسؤول الجودة الأول", row.NewValue);
        Assert.DoesNotContain("PasswordHash", row.NewValue);
    }

    [Fact]
    public void LegacyAuditRows_WithEmbeddedHashes_AreScrubbedByReferenceUpgrade()
    {
        using var host = new TestHost();
        int legacyRowId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            // محاكاة سجل تاريخي كُتب قبل إصلاح B110 (ببصمة وملح داخل اللقطة)
            var legacy = new AuditLog
            {
                UserName = "system",
                ActionDate = DateTime.Now,
                ActionType = "Edit",
                DocumentType = nameof(AppUser),
                OldValue = "{\"UserName\":\"admin\",\"PasswordHash\":\"b64hash==\",\"PasswordSalt\":\"b64salt==\"}",
                NewValue = "{\"UserName\":\"admin\",\"PasswordHash\":\"b64hash2==\",\"PasswordSalt\":\"b64salt2==\",\"IsActive\":true}"
            };
            db.AuditLogs.Add(legacy);
            db.SaveChanges();
            legacyRowId = legacy.Id;
        }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var changes = DbSeeder.UpgradeReferenceData(db);
            Assert.Contains(changes, c => c.Contains("مسح حقول الاعتماد"));

            var row = db.AuditLogs.First(a => a.Id == legacyRowId);
            // البصمات والملح زالت…
            Assert.DoesNotContain("PasswordHash", row.OldValue);
            Assert.DoesNotContain("PasswordSalt", row.OldValue);
            Assert.DoesNotContain("b64hash2", row.NewValue);
            // …وبقية الحقول بقيت سليمة
            Assert.Contains("admin", row.OldValue);
            Assert.Contains("IsActive", row.NewValue);

            // التكرار آمن: علامة الترقية تمنع إعادة التنفيذ
            var second = DbSeeder.UpgradeReferenceData(db);
            Assert.DoesNotContain(second, c => c.Contains("مسح حقول الاعتماد"));
        }
    }
}
