using System.IO;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §التشغيل بضغطة واحدة + الهوية المنفصلة + الحل التلقائي للاتصال (v1.50.23):
/// • «1-حدّث_وشغل.bat» يعمل من مجلد النظام نفسه: بلا نسخ ملفات وبلا أي نسخ احتياطية،
///   مع حارس يمنع النقر من الحزمة المصغّرة غير المدموجة.
/// • إعداد الاتصال يُكتب تلقائياً في مجلد بيانات خاص «MfgSystem»: ‎.\SQLEXPRESS01
///   / قاعدة «MfgSystemDB» منفصلة عن أي نسخة أخرى / مصادقة Windows.
/// • أول تشغيل ينشئ القاعدة والجداول تلقائياً (EnsureCreated + DbSeeder + SchemaMigrator).
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

    [Fact]
    public void Installer_Seeds_The_Separate_Identity_With_Valid_Json()
    {
        string bat = Read("Installer/2-تنصيب.bat");
        // ملفات bat بنهايات CRLF
        Assert.True(bat.Contains("\r\n"), "2-تنصيب.bat يجب أن يكون CRLF");
        // الهوية المنفصلة: مجلد بيانات وقاعدة بأسماء لا تلتقي مع أي نسخة أخرى
        Assert.Contains("MfgSystem", bat);
        Assert.DoesNotContain("config.backup", bat);
        Assert.DoesNotContain("mfgsystem_local", bat);
        // JSON الفعلي الذي سيكتبه السكربت
        var cfg = SeededConfig(bat);
        Assert.Equal(".\\SQLEXPRESS01", cfg.GetProperty("Server").GetString());
        Assert.Equal("MfgSystemDB", cfg.GetProperty("Database").GetString());
        Assert.Equal("Windows", cfg.GetProperty("AuthMode").GetString());
        Assert.True(cfg.GetProperty("TrustServerCertificate").GetBoolean());
        Assert.Equal("1.50.66", cfg.GetProperty("AppVersion").GetString());
        // الاختصار
        Assert.Contains("GetFolderPath('Desktop')", Read("Installer/إنشاء_اختصار.ps1"));
    }

    [Fact]
    public void OneClick_Upgrader_Copies_Nothing_And_Backs_Up_Nothing()
    {
        string bat = Read("Installer/1-حدّث_وشغل.bat");
        Assert.True(bat.Contains("\r\n"), "1-حدّث_وشغل.bat يجب أن يكون CRLF");
        // بلا نسخ ملفات وبلا نسخ احتياطية — الملفات في مكانها أصلاً (نقرة = تهيئة + تشغيل)
        Assert.DoesNotContain("copy /y", bat);
        Assert.DoesNotContain("xcopy", bat);
        Assert.DoesNotContain("قبل_التحديث", bat);
        // حارس الدمج: إن نُقر من الحزمة المصغّرة غير المدموجة تظهر رسالة بدل الفشل
        Assert.Contains("coreclr.dll", bat);
        // الهوية المنفصلة في كل السكربت
        Assert.Contains("MfgSystem", bat);
        var cfg = SeededConfig(bat);
        Assert.Equal("MfgSystemDB", cfg.GetProperty("Database").GetString());
        Assert.Equal("1.50.66", cfg.GetProperty("AppVersion").GetString());
        // التشغيل المباشر للملف التنفيذي من مجلده (لا BAT ولا PS في التشغيل اليومي)
        Assert.Contains("start \"\" \"%SRC%MfgSystem.exe\"", bat);
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
