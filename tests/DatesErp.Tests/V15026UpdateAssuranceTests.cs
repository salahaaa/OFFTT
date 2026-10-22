using System.IO;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §v1.50.26 — ضمان الوصول: الشاشة صفحة واحدة ثابتة، النقرة الواحدة تحرّر،
/// السكربت يثبّت نفسه من مجلد التحديث المتداخل، وختم العنوان = رقم التسليم.
/// </summary>
public class V15026UpdateAssuranceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DateERP.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
    private static string Read(string rel) => File.ReadAllText(Path.Combine(RepoRoot(), rel));

    [Fact]
    public void Delivery_Screen_Is_One_Fixed_Page_Without_Vertical_Scroll()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/Screens/ProductionDeliveryView.xaml");
        Assert.DoesNotContain("<ScrollViewer", xaml);           // §بلا تمرير عمودي للصفحة
        Assert.Contains("Height=\"*\"", xaml);                  // جدول البنود يأخذ المساحة المتبقية
        // قسم المخرجات الثانوية مضغوط محدود الارتفاع مهما حدث
        int i = xaml.IndexOf("x:Name=\"SecondaryGrid\"");
        Assert.True(i > 0);
        string grid = xaml.Substring(i, 320);
        Assert.Contains("MaxHeight=\"96\"", grid);
        Assert.Contains("MinHeight=\"66\"", grid);
    }

    [Fact]
    public void Single_Click_Begins_Editing_On_Both_Grids()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/Screens/ProductionDeliveryView.xaml");
        Assert.Equal(2, xaml.Split("PreviewMouseLeftButtonDown=\"Grid_SingleClickEdit\"").Length - 1);
        string cs = Read("src/DatesErp.Desktop/Views/Screens/ProductionDeliveryView.xaml.cs");
        Assert.Contains("grid.CurrentCell = new DataGridCellInfo(cell);", cs);
        Assert.Contains("grid.BeginEdit();", cs);
    }

    [Fact]
    public void Stale_Go_To_Orders_Screen_Message_Is_Gone()
    {
        string svc = Read("src/DatesErp.Application/Services/ProductionDeliveryService.Actual.cs");
        Assert.DoesNotContain("بدء تنفيذه من شاشة أمر الإنتاج", svc);
        Assert.Contains("اختر الأمر الصحيح من القائمة", svc);
    }

    [Fact]
    public void Current_One_Click_Installer_Publishes_Isolated_MfgSystem()
    {
        string bat = Read("INSTALL-AND-RUN.bat");
        Assert.Contains("Application_%VER%", bat);
        Assert.Contains("MfgSystem.exe", bat);
        Assert.Contains("mfgsystem_local.db", bat);
        Assert.Contains("Start-Process", bat);                  // طلب UAC دون تغيير صفحة الترميز
        Assert.Contains("[FAILED]", bat);
        Assert.Contains("goto :fail", bat);
        Assert.DoesNotContain("DROP DATABASE", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlcmd", bat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Title_Bar_Stamp_Equals_The_Delivered_Version()
    {
        // ختم العنوان كان ثابتاً على 2026-09-09 عبر إصدارات متتالية — صار رقم التسليم نفسه.
        string stamp = Read("src/DatesErp.Desktop/Services/BuildInfo.cs");
        Assert.Contains("\"1.50.72\"", stamp);
        Assert.DoesNotContain("ActualDeliveryDraft2", stamp);
    }
}
