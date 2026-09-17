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

    /// <summary>يُستدعى مرة واحدة عند تحميل النافذة الرئيسية (بعد الدخول وتوفّر الجلسة).</summary>
    public static void RunDaily()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            if (!db.Database.IsSqlServer()) return; // الوضع المحلي المدمج يُنسخ بنسخ ملفه
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string last = db.SystemSettings.AsNoTracking().FirstOrDefault(s => s.SettingKey == KeyLast)?.SettingValue;
            if (last == today) return;

            string folder = db.SystemSettings.AsNoTracking().FirstOrDefault(s => s.SettingKey == KeyFolder)?.SettingValue;
            if (string.IsNullOrWhiteSpace(folder)) folder = DefaultFolder;

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

    /// <summary>نسخة إجبارية قبل ترحيل المخطط؛ إن تعذّرت يُخيَّر المستخدم بالمخاطرة صراحة.</summary>
    public static bool EnsurePreMigration(DatesErpDbContext db)
    {
        try
        {
            if (!db.Database.IsSqlServer()) return true;
            System.IO.Directory.CreateDirectory(DefaultFolder);
            string file = System.IO.Path.Combine(DefaultFolder, $"DateERP_PreMigration_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
            string dbName = db.Database.GetDbConnection().Database;
            db.Database.ExecuteSqlRaw($"BACKUP DATABASE [{dbName}] TO DISK = N'{file}' WITH INIT, COMPRESSION, CHECKSUM");
            db.Database.ExecuteSqlRaw($"RESTORE VERIFYONLY FROM DISK = N'{file}'");
            ErrorLog.WriteInfo("AutoBackup: نسخة ما قبل الترحيل جاهزة — " + file);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "AutoBackup.PreMigration");
            return System.Windows.MessageBox.Show(
                "تعذّر إنشاء نسخة احتياطية قبل ترحيل قاعدة البيانات:\n" + ex.Message +
                "\n\nالترحيل بدون نسخة احتياطية مخاطرة بفقدان البيانات عند فشل الترحيل.\nهل تريد المتابعة رغم ذلك؟",
                "نسخ احتياطي قبل الترحيل", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
                == System.Windows.MessageBoxResult.Yes;
        }
    }

}
