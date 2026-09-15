using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Session;

namespace DatesErp.Desktop.Views.Screens;

/// <summary>§v1.50.36 — شاشة واحدة: بنود اليوم + مستند الأمر أسفلها. لا تبديل شاشات.</summary>
public partial class OrdersView : UserControl
{
    private TodayProductionDto _sheet;
    private readonly Func<TodayProductionDto> _load;
    private readonly Func<OpResult> _issue;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly bool _isolated;
    private OrderDocumentPanel _panel;
    private int? _shownOrderId;
    private bool _syncing;

    public OrdersView() : this(null, null) { }

    public OrdersView(Func<TodayProductionDto> load, Func<OpResult> issue)
    {
        InitializeComponent();
        _isolated = load != null;
        _load = load ?? (() => { using var scope = AppContainer.NewScope(); return scope.ServiceProvider.GetRequiredService<IProductionOrderService>().GetTodayProduction(); });
        _issue = issue ?? (() => { using var scope = AppContainer.NewScope(); return scope.ServiceProvider.GetRequiredService<IProductionOrderService>().IssueTodayOrders(); });
        _timer.Tick += (_, _) => RefreshToday();
        Loaded += (_, _) =>
        {
            RefreshToday(); _timer.Start();
            if (!_isolated && MainWindow.PendingOrderIdToOpen is int id)
            {
                MainWindow.PendingOrderIdToOpen = null;
                ShowOrderInPlace(id);
            }
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("أمر الإنتاج — شاشة واحدة: اليوم + المستند");
        chrome.SetScreenCode("MRPMPS1007");
        chrome.SetToolbar(new Views.ErpToolbar()
            .WithNew((_, _) => AddFromPlan_Click(null, null), "➕ إضافة أمر من الخطة")
            .WithEdit((_, _) => Edit_Click(null, null))
            .WithSave((_, _) => Save_Click(null, null), "💾 حفظ")
            .WithSearch((_, _) => Search_Click(null, null), "بحث عن أمر إنتاج")
            .WithDelete((_, _) => Delete_Click(null, null))
            .WithRefresh((_, _) => RefreshToday())
            .WithPrint((_, _) => Print_Click(null, null))
            .WithCustom("📤 إصدار أوامر اليوم", "ErpApproveButton", (_, _) => IssueToday_Click(null, null), "إصدار كل بنود اليوم المعتمدة")
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard")));
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }

    private void RefreshToday()
    {
        try
        {
            int? selected = (TodayGrid.SelectedItem as TodayProductionRowDto)?.PlanItemId;
            var next = _load();
            // §1.50.61 — إصدار جماعي: تهيئة EditableCartons و IsSelected
            foreach (var r in next.Rows)
            {
                if (r.EditableCartons <= 0) r.EditableCartons = r.PlannedCartons;
                // Preserve selection across refresh if same item was selected before
            }
            _sheet = next;
            DayLabel.Text = $"اليوم: {next.Day:dd/MM/yyyy} — تاريخ العمل من الخادم";
            var open = next.Rows.Where(r => !r.DayClosed).ToList();
            DayHint.Text = open.Count == 0 && next.Rows.Count > 0
                ? "أُقفل يوم جميع بنود اليوم ✓ — بقاياهم في «تسليم الإنتاج» كسجلات تم التسجيل وفي التقارير."
                : next.Message;
            _syncing = true;
            TodayGrid.ItemsSource = open;
            TodayGrid.SelectedItem = open.FirstOrDefault(r => r.PlanItemId == selected)
                ?? open.FirstOrDefault(r => r.OrderId == _shownOrderId);
            _syncing = false;
            DayChip.Text = $"📅 اليوم: {next.Day:dd/MM/yyyy}";
            var closed = next.Rows.Count(r => r.DayClosed);
            ItemsChip.Text = $"📦 بنود اليوم المتبقية: {open.Count}";
            IssuedChip.Text = $"🗂 أوامر صادرة: {next.Rows.Count(r => r.OrderId != null)}";
            PendingChip.Text = $"⏳ بانتظار الإصدار: {open.Count(r => r.IsPending)}";
            TotCartonsBox.Text = $"كراتين اليوم المتبقية: {open.Sum(r => r.PlannedCartons):N0}";
            TotQtyBox.Text = $"الوزن المتبقي: {open.Sum(r => r.PlannedKg):N1} كجم";
            TotCustsBox.Text = $"عملاء: {open.Select(r => r.CustomerId).Distinct().Count()}";
            TotLinesBox.Text = $"خطوط: {open.Select(r => r.LineId).Distinct().Count()}";
            DayClosedChip.Text = $"🔒 أُقفل يومه: {closed}";
            IssueTodayBtn.IsEnabled = next.CanIssue && (_isolated || AppContainer.Get<SessionContext>().Can("production", "Create"));
            if (TodayGrid.SelectedItem is TodayProductionRowDto row && row.OrderId is int oid)
                ShowOrderInPlace(oid);
        }
        catch (Exception ex)
        {
            _sheet = null; TodayGrid.ItemsSource = null;
            IssueTodayBtn.IsEnabled = false;
            ItemsChip.Text = "📦 بنود اليوم: 0"; IssuedChip.Text = "🗂 أوامر صادرة: 0";
            PendingChip.Text = "⏳ بانتظار الإصدار: 0";
            TotCartonsBox.Text = "كراتين اليوم: 0"; TotQtyBox.Text = "الوزن: 0 كجم";
            TotCustsBox.Text = "عملاء: 0"; TotLinesBox.Text = "خطوط: 0";
            ClearDetails();
            DayHint.Text = "تعذر تحميل خطة اليوم المعتمدة؛ لا إصدار من بيانات قديمة. حدّث الشاشة بعد معالجة الاتصال/الصلاحية.";
            if (!_isolated) AppContainer.Get<DialogService>().HandleException(ex, "Orders.Today");
            else throw;
        }
    }

    private void IssueToday_Click(object sender, RoutedEventArgs e)
    {
        if (_sheet?.CanIssue != true) return;
        if (!_isolated && !AppContainer.Get<DialogService>().Confirm("إصدار أوامر جميع بنود اليوم المعتمدة كما هي؟\nلا تغيير للأصناف أو الكميات، ولا صرف مخزون بمجرد الإصدار.")) return;
        IssueTodayBtn.IsEnabled = false;
        try
        {
            var result = _issue();
            if (!_isolated)
            {
                if (result.Ok) AppContainer.Get<DialogService>().Info(result.Message);
                else AppContainer.Get<DialogService>().Error(result.Message);
            }
            RefreshToday();
            if (!result.Ok) DayHint.Text = result.Message;
        }
        catch (Exception ex)
        {
            RefreshToday();
            if (!_isolated) AppContainer.Get<DialogService>().HandleException(ex, "Orders.IssueToday");
            else throw;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_sheet?.Rows == null) return;
        bool allSelected = _sheet.Rows.Where(r => r.IsPending).All(r => r.IsSelected);
        foreach (var r in _sheet.Rows.Where(r => r.IsPending)) r.IsSelected = !allSelected;
    }

    private void IssueSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_sheet?.Rows == null) return;
        var sel = _sheet.Rows.Where(r => r.IsPending && r.IsSelected).ToList();
        if (sel.Count == 0) { AppContainer.Get<DialogService>().Info("حدد بنداً واحداً على الأقل من الجدول (✓)."); return; }
        if (sel.Any(r => r.EditableCartons <= 0)) { AppContainer.Get<DialogService>().Error("بعض البنود المحددة كميتها ≤ 0 — صححها قبل الإصدار (الخلفية حمراء 7-ز)."); return; }
        if (!_isolated && !AppContainer.Get<DialogService>().Confirm($"إصدار {sel.Count} بنداً محدداً فقط؟")) return;
        try
        {
            int issued = 0;
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IProductionOrderService>();
            // §1.50.61 — إصدار جماعي حسب المجموعات المميزة
            var groups = sel.GroupBy(r => new { r.PlanId, r.CustomerId, r.ShiftId, r.LineId }).ToList();
            foreach (var g in groups)
            {
                var res = svc.IssueTodayGroup(g.Key.PlanId, g.Key.CustomerId, g.Key.ShiftId, g.Key.LineId);
                if (!res.Ok) { AppContainer.Get<DialogService>().Error(res.Message); return; }
                issued++;
            }
            AppContainer.Get<DialogService>().Info($"تم إصدار {issued} أمر من البنود المحددة ({sel.Count} بنداً).");
            RefreshToday();
        }
        catch (Exception ex)
        {
            if (!_isolated) AppContainer.Get<DialogService>().HandleException(ex, "Orders.IssueSelected");
            else throw;
        }
    }

    /// <summary>§v1.50.36 — يعرض المستند أسفل الجدول دون إخفاء قائمة اليوم.</summary>
    private void ShowOrderInPlace(int id)
    {
        if (_isolated) return;
        if (_shownOrderId == id && _panel != null) return;
        DetailsHost.Content = _panel = new OrderDocumentPanel(id)
        {
            Navigator = ShowOrderInPlace,
            Changed = RefreshToday,
            ReturnToList = ClearDetails
        };
        _shownOrderId = id;
        DetailsHint.Visibility = Visibility.Collapsed;
    }

    private void ClearDetails()
    {
        _panel = null; _shownOrderId = null;
        DetailsHost.Content = null;
        DetailsHint.Visibility = Visibility.Visible;
    }

    private void AddFromPlan_Click(object sender, RoutedEventArgs e)
        => OrderDocumentPanel.PromptAddFromPlan(Window.GetWindow(this), ShowOrderInPlace, RefreshToday);
    private void Search_Click(object sender, RoutedEventArgs e)
        => OrderDocumentPanel.PromptSearch(Window.GetWindow(this), ShowOrderInPlace);
    private void Save_Click(object sender, RoutedEventArgs e)
    { if (_panel == null) { AppContainer.Get<DialogService>().Info("اختر أمراً من الجدول أولاً."); return; } _panel.InvokeSave(); }
    private void Edit_Click(object sender, RoutedEventArgs e)
    { if (_panel == null) { AppContainer.Get<DialogService>().Info("اختر أمراً من الجدول أولاً."); return; } _panel.InvokeEdit(); }
    private void Delete_Click(object sender, RoutedEventArgs e)
    { if (_panel == null) { AppContainer.Get<DialogService>().Info("اختر أمراً من الجدول أولاً."); return; } _panel.InvokeDelete(); }
    private void Print_Click(object sender, RoutedEventArgs e)
    { if (_panel == null) { AppContainer.Get<DialogService>().Info("اختر أمراً من الجدول أولاً."); return; } _panel.InvokePrint(); }

    private void TodayGrid_DoubleClick(object sender, MouseButtonEventArgs e) => TodayGrid_SelectionChanged(sender, null);
    private void TodayGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if ((TodayGrid.SelectedItem as TodayProductionRowDto)?.OrderId is int id) ShowOrderInPlace(id);
        else if ((TodayGrid.SelectedItem as TodayProductionRowDto)?.IsPending == true)
            DetailsHint.Text = "هذا البند بانتظار الإصدار — اضغط «📤 إصدار أوامر اليوم» أو «➕ إضافة أمر من الخطة».";
    }
}
