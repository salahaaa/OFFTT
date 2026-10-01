using Xunit;

namespace DatesErp.Tests;

/// <summary>
/// §B89 — اختبارات بنيوية لواجهات ملء الشاشة والفلترة الاحترافية (فحص مصدر فقط —
/// مشروع الاختبارات لا يرجع إلى Desktop).
///   1. دخول أي وحدة يُخفي القائمة الجانبية + زر عودة للوحة.
///   2. القائمة العامة: فلتر الحقل/القيمة + عدّاد حي + تدقيق بالنقر المزدوج.
///   3. الثيم: تركيز الحقول + إبراز الصف + قالب خطأ التحقق.
/// </summary>
public class UiB89Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DateERP.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string rel)
        => File.ReadAllText(Path.Combine(RepoRoot(), rel));

    [Fact]
    public void Fullscreen_Nav_Hides_On_Module_Screens()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/MainWindow.xaml");
        string cs = Read("src/DatesErp.Desktop/Views/MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"SideBar\"", xaml);
        Assert.Contains("x:Name=\"NavColumn\"", xaml);
        Assert.Contains("SetNavVisible", cs);
        Assert.Contains("GridLength(visible ? 250 : 0)", cs);
        // القرار في OpenScreen: اللوحة وحدها تُظهر القائمة
        Assert.Contains("SetNavVisible(code == \"dashboard\"", cs);
    }

    [Fact]
    public void Back_Button_Uses_Previous_Navigation_Entry()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/MainWindow.xaml");
        string cs = Read("src/DatesErp.Desktop/Views/MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"BackButton\"", xaml);
        Assert.Contains("Click=\"BackButton_Click\"", xaml);
        Assert.Contains("OpenPreviousScreen", cs);
        Assert.Contains("_navigationHistory", cs);
        Assert.DoesNotContain("BackToDashBtn", xaml);
    }

    [Fact]
    public void Shared_Chrome_Has_One_Global_Title_And_No_Duplicate_Metadata_Bars()
    {
        string chrome = Read("src/DatesErp.Desktop/Views/ErpChrome.xaml");
        string main = Read("src/DatesErp.Desktop/Views/MainWindow.xaml");
        Assert.Contains("WindowStyle=\"None\"", main);
        Assert.Contains("x:Name=\"TopPageTitle\"", main);
        Assert.Contains("x:Name=\"BackButton\"", main);
        Assert.DoesNotContain("ModuleText", chrome);
        Assert.DoesNotContain("ScreenCodeText", chrome);
        Assert.DoesNotContain("AuditText", chrome);
        Assert.DoesNotContain("UserText", chrome);
        Assert.Contains("ToolbarArea", chrome);
        Assert.Contains("BodyArea", chrome);
    }

    [Fact]
    public void GenericList_Has_Professional_Filters()
    {
        string xaml = Read("src/DatesErp.Desktop/Views/Screens/GenericListView.xaml");
        string cs = Read("src/DatesErp.Desktop/Views/Screens/GenericListView.xaml.cs");
        Assert.Contains("x:Name=\"FieldBox\"", xaml);
        Assert.Contains("x:Name=\"ValueBox\"", xaml);
        Assert.Contains("x:Name=\"CountText\"", xaml);
        Assert.Contains("PopulateFilterBoxes", cs);
        Assert.Contains("PopulateValueBox", cs);
        Assert.Contains("SelectedDisplayColumn", cs);
    }

    [Fact]
    public void GenericList_DoubleClick_Drills_Into_Readonly_Lists()
    {
        string cs = Read("src/DatesErp.Desktop/Views/Screens/GenericListView.xaml.cs");
        Assert.Contains("RecordDetailsDialog", cs);
        Assert.Contains("class RecordDetailsDialog", cs);
        // لم يعد النقر المزدوج يتجاهل القوائم القرائية
        Assert.DoesNotContain("if (_crud == null) return;", cs);
    }

    [Fact]
    public void Theme_Has_Focus_Hover_And_Validation_Polish()
    {
        string theme = Read("src/DatesErp.Desktop/Themes/DateErpTheme.xaml");
        Assert.Contains("Validation.ErrorTemplate", theme);
        Assert.Contains("AdornedElementPlaceholder", theme);
        Assert.Contains("IsFocused", theme);
        Assert.Contains("IsMouseOver", theme);
    }
}
