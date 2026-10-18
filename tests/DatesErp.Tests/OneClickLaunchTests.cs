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
        Assert.Contains("GetFolderPath('Desktop')", bat);
        Assert.Contains("MfgSystem.exe", bat);
        // رقم الإصدار في البوت = رقم الإصدار في المشروع
        var csproj = Read("src/DatesErp.Desktop/DatesErp.Desktop.csproj");
        var m = System.Text.RegularExpressions.Regex.Match(csproj, "<Version>([^<]+)</Version>");
        Assert.True(m.Success, "csproj بلا <Version>");
        Assert.Contains($"set \"VER={m.Groups[1].Value}\"", bat);
    }

    [Fact]
    public void From_Zero_Setup_Keeps_Safety_Rules()
    {
        string bat = Read("Installer/SETUP-FROM-ZERO.bat");
        Assert.True(bat.Contains("\r\n"), "SETUP-FROM-ZERO.bat يجب أن يكون CRLF");
        Assert.DoesNotContain("chcp", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", bat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[FAILED]", bat);
        Assert.Contains("MfgSystem", bat);
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
