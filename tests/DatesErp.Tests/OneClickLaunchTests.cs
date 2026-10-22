using System.IO;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// اختبارات حزمة 1.50.72: مثبت المصدر، مدخل التوافق، وعزل هوية قاعدة البيانات المحلية.
/// أول تشغيل يبني/ينشئ قاعدة SQLite المحلية دون استيراد إعدادات DateERP القديمة.
/// </summary>
public class OneClickLaunchTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DateERP.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Read(string rel) => File.ReadAllText(Path.Combine(RepoRoot(), rel));

    private static System.Text.Json.JsonElement SeededConfig(string batText)
    {
        var lines = batText.Split('\n')
            .Where(l => l.StartsWith(">>\"%DATADIR%") || l.StartsWith(">\"%DATADIR%"))
            .Select(l => l.Substring(l.IndexOf(" echo ", StringComparison.Ordinal) + 6).TrimEnd('\r'))
            .ToList();
        return System.Text.Json.JsonDocument.Parse(string.Join('\n', lines)).RootElement;
    }

    /// <summary>§تنظيف المستودع: بقي مسار تحديث واحد — UPDATE-AND-RUN.bat في الجذر — بدل سكربتات التنصيب القديمة المحذوفة.</summary>
    [Fact]
    public void One_Click_Updater_Is_Ascii_Crlf_Safe_And_Matches_Project_Version()
    {
        string bat = Read("UPDATE-AND-RUN.bat");
        Assert.True(bat.Contains("\r\n"), "UPDATE-AND-RUN.bat يجب أن يكون CRLF");
        Assert.All(bat, ch => Assert.True(ch < 128, "UPDATE-AND-RUN.bat يجب أن يكون ASCII خالصاً (بلا تغيير صفحة ترميز)"));
        Assert.DoesNotContain("chcp", bat, StringComparison.OrdinalIgnoreCase);
        // لا مساس بقاعدة البيانات أو بيانات المستخدم
        Assert.DoesNotContain("DROP DATABASE", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlcmd", bat, StringComparison.OrdinalIgnoreCase);
        // نسخة احتياطية قبل النسخ + تقارير OK/FAILED + توقف عند الفشل + اختصار سطح المكتب
        Assert.Contains("_backup_before_", bat);
        Assert.Contains("[FAILED]", bat);
        Assert.Contains("goto :fail_end", bat);
        Assert.DoesNotContain("taskkill", bat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("goto :legacy_target", bat);
        Assert.Contains("GetFolderPath('Desktop')", bat);
        Assert.Contains("MfgSystem.exe", bat);
        // رقم الإصدار في البوت = رقم الإصدار في المشروع
        var csproj = Read("src/DatesErp.Desktop/DatesErp.Desktop.csproj");
        var m = System.Text.RegularExpressions.Regex.Match(csproj, "<Version>([^<]+)</Version>");
        Assert.True(m.Success, "csproj بلا <Version>");
        Assert.Contains($"set \"VER={m.Groups[1].Value}\"", bat);
    }

    [Fact]
    public void From_Zero_Compatibility_Entry_Delegates_To_Root_Installer()
    {
        string bat = Read("Installer/SETUP-FROM-ZERO.bat");
        Assert.True(bat.Contains("\r\n"), "SETUP-FROM-ZERO.bat يجب أن يكون CRLF");
        Assert.DoesNotContain("chcp", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", bat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[FAILED]", bat);
        Assert.Contains("INSTALL-AND-RUN.bat", bat);
        Assert.Contains("call \"%INSTALLER%\"", bat);
    }

    [Fact]
    public void Install_And_Run_Builds_The_Current_Version_Without_Overwriting_Old_Folders()
    {
        string bat = Read("INSTALL-AND-RUN.bat");
        Assert.True(bat.Contains("\r\n"), "INSTALL-AND-RUN.bat يجب أن يكون CRLF");
        Assert.All(bat, ch => Assert.True(ch < 128, "INSTALL-AND-RUN.bat يجب أن يكون ASCII خالصاً"));
        Assert.DoesNotContain("chcp", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", bat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verb RunAs", bat);
        Assert.Contains("--self-contained true", bat);
        Assert.Contains("Application_%VER%", bat);
        Assert.Contains("mfgsystem_local.db", bat);
        Assert.Contains("MfgSystem %VER%.lnk", bat);
        Assert.Contains("BUILD SUCCESS", bat);
        Assert.Contains("INSTALL SUCCESS", bat);
        Assert.Contains("[FAILED]", bat);
        Assert.Contains("goto :fail", bat);
        Assert.Contains("goto :legacy_root", bat); // المسارات القديمة المحمية تُرفض
    }

    [Fact]
    public void Local_Profile_And_Boot_Logs_Use_MfgSystem_Without_Legacy_Login_Migration()
    {
        string cfg = Read("src/DatesErp.Infrastructure/Connection/AppConfig.cs");
        Assert.Contains("LocalApplicationData", cfg);
        Assert.Contains("\"MfgSystem\"", cfg);
        string bootTrace = Read("src/DatesErp.Desktop/Services/BootTrace.cs");
        Assert.Contains("AppConfig.ConfigDirectory", bootTrace);
        Assert.Contains("\"logs\"", bootTrace);
        string login = Read("src/DatesErp.Desktop/Views/LoginWindow.xaml.cs");
        Assert.Contains("AppConfig.ConfigDirectory", login);
        Assert.DoesNotContain("LegacyRememberPath", login);
    }

    [Fact]
    public void Optional_SQL_Setup_Creates_Only_New_MfgSystem_Database_And_Backup_Job()
    {
        string databaseSetup = Read("Installer/server/إعداد_قاعدة_البيانات.sql");
        Assert.Contains("DB_ID(N'MfgSystemDB') IS NOT NULL", databaseSetup);
        Assert.Contains("CREATE DATABASE [MfgSystemDB]", databaseSetup);
        Assert.DoesNotContain("DROP DATABASE", databaseSetup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DB_ID(N'DateFactory')", databaseSetup);
        Assert.DoesNotContain("USE [DateFactory]", databaseSetup);

        string backupSetup = Read("Installer/server/إعداد_النسخ_الاحتياطي.sql");
        Assert.Contains("MfgSystem_DailyBackup", backupSetup);
        Assert.Contains("IF EXISTS", backupSetup);
        Assert.Contains("RESTORE VERIFYONLY", backupSetup);
        Assert.Contains("DATEPART(SECOND", backupSetup);
        Assert.DoesNotContain("WITH INIT", backupSetup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_delete_job", backupSetup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void First_Launch_Creates_Database_And_Tables_Automatically()
    {
        string boot = Read("src/DatesErp.Desktop/Services/Bootstrapper.cs");
        Assert.Contains("db.Database.EnsureCreated();", boot);
        Assert.Contains("DbSeeder.Seed(db);", boot);
        Assert.Contains("SchemaMigrator.Migrate(db)", boot);
        Assert.Contains("bool autoInit = true;", boot);
        // مجلد البيانات المنفصل في الكود نفسه — لا يلتقي مع أي نسخة أخرى
        string cfgCode = Read("src/DatesErp.Infrastructure/Connection/AppConfig.cs");
        Assert.Contains("MfgSystem", cfgCode);
        string di = Read("src/DatesErp.Infrastructure/DependencyInjection.cs");
        Assert.Contains("cfg.BuildSqlServerConnectionString()", di);
    }
}
