using System.Windows;
using System.Windows.Controls;

namespace DatesErp.Desktop.Views;

/// <summary>
/// حاوية موحّدة لمحتوى الشاشات.
/// شريط عنوان النظام، واسم الصفحة، وشريط المعلومات، والتذييل تُرسم مرة واحدة
/// في MainWindow؛ هذه الحاوية تحتفظ فقط بشريط أدوات النموذج ومحتواه.
/// </summary>
public partial class ErpChrome : UserControl
{
    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(ErpChrome),
            new PropertyMetadata("نظام إدارة وتصنيع التمور"));

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    /// <summary>يُستخدم للحفاظ على توافق الشاشات القديمة؛ زر الإغلاق يطلب الرجوع السابق.</summary>
    public event EventHandler CloseRequested;

    public ErpChrome()
    {
        InitializeComponent();
    }

    // هذه البيانات أصبحت معروضة في شريط MainWindow، وتبقى الدوال لأجل توافق
    // AttachChrome وGenericListView دون إعادة بناء كل شاشة على حدة.
    public void SetModule(string module) { }
    public void SetScreenCode(string code) { }
    public void SetCount(int count) { }

    /// <summary>شريط الأدوات الحالي — تستخدمه الاختصارات العامة (F2/F3/F5/F9/F10/Ctrl+P).</summary>
    public ErpToolbar CurrentToolbar { get; private set; }

    /// <summary>وحدة الصلاحيات لهذه الشاشة، وتُطبق على الشريط مرة واحدة.</summary>
    public string PermissionModule { get; private set; }

    public void SetPermissionModule(string module)
    {
        PermissionModule = module;
        CurrentToolbar?.ForModule(module);
    }

    public void SetToolbar(UIElement toolbar)
    {
        ToolbarArea.Content = toolbar;
        CurrentToolbar = toolbar as ErpToolbar;
        if (!string.IsNullOrWhiteSpace(PermissionModule)) CurrentToolbar?.ForModule(PermissionModule);
    }

    public void SetBody(UIElement body) => BodyArea.Content = body;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // زر الإغلاق داخل إطار النموذج يعيد المستخدم إلى الصفحة السابقة،
        // ولا يعيده قسراً إلى لوحة النظام.
        if (CloseRequested != null)
            CloseRequested(this, EventArgs.Empty);
        else
            (Window.GetWindow(this) as MainWindow)?.OpenPreviousScreen();
    }

    private void Min_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w != null) w.WindowState = WindowState.Minimized;
    }

    private void Max_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w == null) return;
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
