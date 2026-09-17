using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Desktop.Services;

/// <summary>
/// §44 — النسخ الاحتياطي التلقائي: مرة واحدة كل يوم عند أول إقلاع بعد الدخول،
/// ونسخة إجبارية قبل أي ترحيل مخطط. لا يعتمد على انضباط المستخدم اليدوي.
/// </summary>
public static class AutoBackup
{
    public const string KeyLast = "LastAutoBackupDate";
    public const string KeyFolder = "BackupFolder";

    public static string DefaultFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DateERP", "Backups");

    // §1.50.67 FIX: مجلدات بديلة للنسخ الاحتياطي يمكن لخدمة SQL Server الكتابة فيها (خطأ Access is denied 5)
    private static readonly string[] FallbackFolders = new[]
    {
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "DateERP", "Backups"),
        @"C:\SQLBackups\DateERP",
        @"C:\Temp\DateERP\Backups",
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DateERP", "Backups"),
        DefaultFolder
    };

    private static string GetSqlServerDefaultBackupPath(DatesErpDbContext db)
    {
        try
        {
            // محاولة قراءة مسار النسخ الافتراضي من SQL Server
            var conn = db.Database.GetDbConnection();
            bool wasClosed = conn.State == System.Data.ConnectionState.Closed;
            if (wasClosed) conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SERVERPROPERTY('InstanceDefaultBackupPath') as p";
            var result = cmd.ExecuteScalar() as string;
            if (wasClosed) conn.Close();
            if (!string.IsNullOrWhiteSpace(result)) return result;
        }
        catch { }
        return null;
    }

    private static string ResolveWritableBackupFolder(DatesErpDbContext db, string preferred = null)
    {
        var candidates = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(preferred)) candidates.Add(preferred);
        string sqlDefault = GetSqlServerDefaultBackupPath(db);
        if (!string.IsNullOrWhiteSpace(sqlDefault)) candidates.Add(System.IO.Path.Combine(sqlDefault, "DateERP"));
        candidates.AddRange(FallbackFolders);

        foreach (var folder in candidates.Distinct())
        {
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                // اختبار كتابة سريعة
                string testFile = System.IO.Path.Combine(folder, $"_test_write_{Guid.NewGuid():N}.tmp");
                System.IO.File.WriteAllText(testFile, "test");
                System.IO.File.Delete(testFile);
                return folder;
            }
            catch { continue; }
        }
        return DefaultFolder;
    }

    /// <summary>يُستدعى مرة واحدة عند تحميل النافذة الرئيسية (بعد الدخول وتوفّر الجلسة).</summary>
    public static void RunDaily()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            if (!db.Database.IsSqlServer()) return;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            // §1.50.67 FIX EF1002: إضافة OrderBy لتجنب تحذير FirstOrDefault بدون ترتيب
            string last = db.SystemSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefault(s => s.SettingKey == KeyLast)?.SettingValue;
            if (last == today) return;

            string folderSetting = db.SystemSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefault(s => s.SettingKey == KeyFolder)?.SettingValue;
            string folder = string.IsNullOrWhiteSpace(folderSetting) ? ResolveWritableBackupFolder(db) : folderSetting;

            var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var res = backup.FullBackup(folder);
            if (res.Ok)
            {
                scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().Set(KeyLast, today);
                UiToast.Show("تم النسخ الاحتياطي اليومي تلقائياً والتحقق منه في:\n" + folder);
            }
            else
            {
                ErrorLog.WriteInfo("AutoBackup: تعذّر النسخ اليومي — " + res.Message);
                UiToast.Show("تعذّر النسخ الاحتياطي اليومي التلقائي.\n" + res.Message, "warn");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "AutoBackup.RunDaily");
        }
    }

    /// <summary>نسخة إجبارية قبل ترحيل المخطط؛ إن تعذّرت يُخيَّر المستخدم بالمخاطرة صراحة — مع مجلدات بديلة.</summary>
    public static bool EnsurePreMigration(DatesErpDbContext db)
    {
        string attemptedFile = null;
        try
        {
            if (!db.Database.IsSqlServer()) return true;
            string folder = ResolveWritableBackupFolder(db, DefaultFolder);
            System.IO.Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, $"DateERP_PreMigration_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
            attemptedFile = file;
            string dbName = db.Database.GetDbConnection().Database;
            // §1.50.67 FIX: استخدام مسار يمكن لخدمة SQL Server الكتابة فيه + معالجة Access is denied
            db.Database.ExecuteSqlRaw($"BACKUP DATABASE [{dbName}] TO DISK = N'{file}' WITH INIT, COMPRESSION, CHECKSUM");
            db.Database.ExecuteSqlRaw($"RESTORE VERIFYONLY FROM DISK = N'{file}'");
            ErrorLog.WriteInfo("AutoBackup: نسخة ما قبل الترحيل جاهزة — " + file);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, $"AutoBackup.PreMigration — file={attemptedFile}");
            // §1.50.67 FIX: رسالة أوضح مع اقتراح مجلد بديل
            string msg = ex.Message.Contains("Access is denied") || ex.Message.Contains("Operating system error 5")
                ? $"تعذّر إنشاء نسخة احتياطية في '{attemptedFile}' بسبب صلاحيات خدمة SQL Server (خطأ 5 Access is denied).\n" +
                  $"خدمة SQL Server تعمل بحساب لا يملك صلاحية الكتابة في مجلد المستندات.\n" +
                  $"جرّب: 1) شغّل SSMS كمسؤول وأعطِ صلاحية للمجلد، أو 2) استخدم مجلد C:\SQLBackups، أو 3) اضغط نعم للمتابعة بدون نسخة (مخاطرة).\n\nالتفاصيل: {ex.Message}"
                : $"تعذّر إنشاء نسخة احتياطية قبل ترحيل قاعدة البيانات:\n{ex.Message}";
            return System.Windows.MessageBox.Show(
                msg +
                "\n\nالترحيل بدون نسخة احتياطية مخاطرة بفقدان البيانات عند فشل الترحيل.\nهل تريد المتابعة رغم ذلك؟",
                "نسخ احتياطي قبل الترحيل", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
                == System.Windows.MessageBoxResult.Yes;
        }
    }

}
