using System.IO;
using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §v1.50.34 — طلب المستخدم: زر الحفظ في رأس شاشة أمر الإنتاج + الأزرار الكلاسيكية الناقصة
/// (إضافة/تعديل/بحث/حذف). انضباط التخطيط باقٍ: الكميات ثابتة من الخطة المعتمدة،
/// الحفظ من الرأس يخص ملاحظات الأمر، الإضافة إصدار من الخطة، والحذف للمسودة فقط.
/// </summary>
public class OrdersScreenToolbarTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DateERP.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Read(string rel) => File.ReadAllText(Path.Combine(RepoRoot(), rel));

    [Fact]
    public void Save_Button_Lives_At_The_Screen_Head_Above_The_Scrolling_Content()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/Screens/OrdersView.xaml");
        string cs = Read("src/DatesErp.Desktop/Views/Screens/OrdersWindows.cs");
        Assert.Contains("x:Name=\"SaveBtn\"", xaml);
        Assert.Contains("Click=\"Save_Click\"", xaml);
        int issue = xaml.IndexOf("IssueTodayBtn", StringComparison.Ordinal);
        int save = xaml.IndexOf("x:Name=\"SaveBtn\"", StringComparison.Ordinal);
        int grid = xaml.IndexOf("x:Name=\"TodayGrid\"", StringComparison.Ordinal);
        Assert.True(issue > 0 && save > issue && grid > save, "شريط الحفظ فوق جدول اليوم");
        Assert.Contains("UpdateOrderHeader(_orderId, notes: _notesBox.Text ?? \"\")", cs);
        Assert.Contains("ملاحظات الأمر", cs);
        Assert.DoesNotContain("head.Children.Add(BuildTopBar());", cs);
    }

    [Fact]
    public void All_Classic_Buttons_Are_Present_At_The_Head()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/Screens/OrdersView.xaml");
        string cs = Read("src/DatesErp.Desktop/Views/Screens/OrdersWindows.cs");
        Assert.Contains("➕ إضافة أمر من الخطة", xaml);
        Assert.Contains("✏️ تعديل", xaml);
        Assert.Contains("💾 حفظ", xaml);
        Assert.Contains("🔍 بحث", xaml);
        Assert.Contains("🗑 حذف المسودة", xaml);
        Assert.Contains("SearchOrders", cs);
        Assert.Contains("GetTodayPendingGroups", cs);
        Assert.Contains("IssueTodayGroup(", cs);
        Assert.Contains("ShowOrderInPlace", File.ReadAllText(Path.Combine(RepoRoot(), "src/DatesErp.Desktop/Views/Screens/OrdersView.xaml.cs")));
        Assert.DoesNotContain("x:Name=\"DocArea\"", File.ReadAllText(Path.Combine(RepoRoot(), "src/DatesErp.Desktop/Views/Screens/OrdersView.xaml")));
        Assert.Contains("DeleteOrder(_orderId)", cs);
    }

    [Fact]
    public void Planning_Discipline_Stays_Intact_Around_The_New_Buttons()
    {
        string svc = Read("src/DatesErp.Application/Services/ProductionOrderService.cs");
        // كميات البنود: القفل الأصلي باقٍ حرفياً (قرار معتمد بمجموعة القبول)
        Assert.Contains("أصناف أمر الإنتاج وكمياته ثابتة من الخطة المعتمدة", svc);
        // الجدولة (تاريخ/وردية/خط) مقفلة، والملاحظات وحدها تُحفظ
        Assert.Contains("تاريخ أمر الإنتاج وورديته وخطه من الخطة المعتمدة", svc);
        Assert.Contains("تم حفظ ملاحظات الأمر.", svc);
        // الحذف: مسودة فقط (الحارس الأصلي باقٍ)
        Assert.Contains("if (order.IsApproved) return OpResult.Fail(\"لا يمكن حذف أمر معتمد", svc);
    }

    [Fact]
    public void Add_Orders_Are_Issued_From_The_Approved_Plan_Only()
    {
        string today = Read("src/DatesErp.Application/Services/ProductionOrderService.Today.cs");
        // إصدار المجموعة الواحدة يمشي في مسار الإصدار اليومي نفسه بكل حراسه
        Assert.Contains("SaveTodayGroup(\"FromPlan\", planId, customerId, day.ToString(\"dd/MM/yyyy\"), shiftId, lineId, group.Select(e => FromPlan(e.Item)).ToList());", today);
        // حراسة الهوية والكمية الأصلية باقية في SaveTodayGroup
        Assert.Contains("هوية بند أمر الإنتاج وكميته يجب أن تطابق الخطة المعتمدة تماماً", today);
        Assert.Contains("أمر الإنتاج لا ينشأ يدوياً؛ يلزم مرجع خطة اليوم المعتمدة.", today);
    }
}
