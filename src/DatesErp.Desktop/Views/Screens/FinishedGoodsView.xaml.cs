using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DatesErp.Core.Common;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Desktop.Views.Screens;

/// <summary>صف إنتاج تام في شاشة التسليم — مصدره بند أمر الإنتاج بأرقامه المحسوبة من الخدمة.</summary>
public class FgLineUi : INotifyPropertyChanged
{
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int? PackagingTypeId { get; set; }
    public string PackSpec { get; set; }
    public double CartonWeightKg { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; }
    public int? LotId { get; set; }
    public string LotCode { get; set; }
    public double PlannedKg { get; set; }
    public double ProducedKg { get; set; }
    public int ProducedCartons { get; set; }
    public double DeliveredKg { get; set; }
    public double RemainingKg { get; set; }
    public double? BalanceAfter { get; set; }
    public string BalanceAfterText => BalanceAfter is double b ? b.ToString("N1") : "—";

    /// <summary>وزن الكرتون المعتمد للتحويل: وزن العبوة ← متوسط الفعلي (كجم/كرتون) ← 0 (إدخال بالكيلو).</summary>
    public double UnitKg => CartonWeightKg > 0 ? CartonWeightKg : ProducedCartons > 0 ? ProducedKg / ProducedCartons : 0;
    public int RemainingCartons => UnitKg > 0 ? (int)Math.Floor(RemainingKg / UnitKg + 0.0001) : 0;

    private int _cartons;
    public int DeliverCartons
    {
        get => _cartons;
        set { _cartons = Math.Max(0, value); OnChanged(nameof(DeliverCartons)); OnChanged(nameof(DeliverKg)); OnChanged(nameof(IsOver)); }
    }
    public double DeliverKg => UnitKg > 0 ? Math.Round(_cartons * UnitKg, 3) : _cartons;
    public bool IsOver => DeliverKg > RemainingKg + 0.001;

    private int? _warehouseId;
    public int? WarehouseId { get => _warehouseId; set { _warehouseId = value; OnChanged(nameof(WarehouseId)); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>صف مخرج ثانوي/مخلفات في السند.</summary>
public class FgByProductUi : INotifyPropertyChanged
{
    public int ByProductId { get; set; }
    public string Name { get; set; }
    public string KindAr { get; set; }
    public string Unit { get; set; }
    public bool PostsStock { get; set; }
    public string PostsStockAr => PostsStock ? "نعم" : "توثيق فقط";
    public double ProducedQty { get; set; }
    public double DeliveredQty { get; set; }
    public double RemainingQty { get; set; }
    private double _qty;
    public double DeliverQty { get => _qty; set { _qty = Math.Max(0, value); OnChanged(nameof(DeliverQty)); OnChanged(nameof(IsOver)); } }
    public bool IsOver => DeliverQty > RemainingQty + 0.0001;
    private int? _warehouseId;
    public int? WarehouseId { get => _warehouseId; set { _warehouseId = value; OnChanged(nameof(WarehouseId)); } }
    private string _notes;
    public string Notes { get => _notes; set { _notes = value; OnChanged(nameof(Notes)); } }
    public event PropertyChangedEventHandler PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// §تسليم الإنتاج للمخازن (MRPMPS1015) — الشاشة التشغيلية:
/// جديد ← اختيار أمر إنتاج له إنتاج فعلي (بحث) ← كميات لكل بند مع مخزن الإدخال ← حفظ مسودة (بلا أثر)
/// ← اعتماد (مستند + حركات مخزون في عملية واحدة) ← طباعة. التراجع عن معتمد = قيد عكسي موثق.
/// كل الحسابات (الفعلي/المسلَّم/المتبقي/السقوف) من <see cref="IFinishedGoodsService"/> — لا منطق مخزني هنا.
/// </summary>
public partial class FinishedGoodsView : UserControl
{
    /// <summary>فتح الشاشة على أمر إنتاج محدد (من شاشة تسجيل الفعلي أو الأوامر).</summary>
    public static int? PendingOrderIdToDeliver;

    private readonly ObservableCollection<FgLineUi> _lines = new();
    private readonly ObservableCollection<FgByProductUi> _byProducts = new();
    private List<FgWarehouseOption> _warehouses = new();
    private FgOrderDeliveryContext _ctx;
    private int _receiptId;              // 0 = سند جديد غير محفوظ
    private bool _editable = true;       // false = سند محفوظ معروض للقراءة (معتمد أو قبل «تعديل»)
    private bool _loading;
    private ErpToolbar _toolbar;

    public FinishedGoodsView()
    {
        InitializeComponent();
        ItemsGrid.ItemsSource = _lines;
        ByProductsGrid.ItemsSource = _byProducts;
        Loaded += (_, _) => FirstLoad();
    }

    private static IFinishedGoodsService Svc(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IFinishedGoodsService>();
    private static DialogService Dlg => AppContainer.Get<DialogService>();

    public void AttachChrome(ErpChrome chrome)
    {
        chrome.SetModule("تسليم الإنتاج للمخازن");
        chrome.SetScreenCode("MRPMPS1015");
        _toolbar = new ErpToolbar()
            .WithNew((_, _) => NewDoc(), "➕ جديد")
            .WithSave((_, _) => SaveDraft(), "💾 حفظ مسودة")
            .WithApprove((_, _) => Approve(), "🔒 اعتماد التسليم")
            .WithSearch((_, _) => SearchReceipts(), "بحث في سندات التسليم المحفوظة (F9)")
            .WithEdit((_, _) => EditDoc())
            .WithUndo((_, _) => Undo(), "↩ تراجع")
            .WithCustom("⛔ إلغاء المسودة", "ErpDangerButton", (_, _) => CancelDraft(), "إلغاء سند مسودة لم يُعتمد — لا يُحذف من السجل", "Cancel")
            .WithPrint((_, _) => Print())
            .WithRefresh((_, _) => Refresh())
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard"))
            .ForModule("finishedgoods");
        chrome.SetToolbar(_toolbar);
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }

    // ═══════════════ تحميل ═══════════════

    private void FirstLoad()
    {
        try
        {
            if (!PermissionGate.Can("finishedgoods", "View") && !PermissionGate.Can("production", "View"))
            {
                Dlg.Error("ليس لديك صلاحية عرض شاشة تسليم الإنتاج للمخازن.");
                (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
                return;
            }
            LoadWarehouses();
            if (MainWindow.PendingFGIdToOpen is int pending) { MainWindow.PendingFGIdToOpen = null; OpenReceipt(pending); return; }
            NewDoc();
            if (PendingOrderIdToDeliver is int orderId) { PendingOrderIdToDeliver = null; LoadOrder(orderId); }
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Load"); }
    }

    private void LoadWarehouses()
    {
        using var scope = AppContainer.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<DatesErp.Infrastructure.Persistence.DatesErpDbContext>();
        _warehouses = db.Warehouses.Where(w => w.IsActive)
            .OrderBy(w => w.WarehouseType == "Finished" || w.WarehouseCode == "WFG" ? 0 : w.WarehouseType == "General" ? 1 : 2)
            .ThenBy(w => w.IsDefault ? 0 : 1).ThenBy(w => w.Id)
            .Select(w => new FgWarehouseOption { Id = w.Id, Name = w.WarehouseNameAr + " (" + w.WarehouseCode + ")", IsDefault = w.IsDefault || w.WarehouseCode == "WFG" })
            .ToList();
        WarehouseCol.ItemsSource = _warehouses;
        BpWarehouseCol.ItemsSource = _warehouses;
        BulkWarehouseBox.ItemsSource = _warehouses;
    }

    /// <summary>سند جديد: رقم تلقائي للعرض (Peek) — يُثبَّت عند الحفظ.</summary>
    private void NewDoc()
    {
        _loading = true;
        try
        {
            _receiptId = 0; _ctx = null; _editable = true;
            _lines.Clear(); _byProducts.Clear();
            using var scope = AppContainer.NewScope();
            DocNoBox.Text = scope.ServiceProvider.GetRequiredService<INumberingService>().Peek("FGR");
            DateBox.SelectedDate = DateTime.Today;
            OrderNoBox.Text = ""; ReceiptNoBox.Text = ""; NotesBox.Text = "";
            BulkWarehouseBox.SelectedValue = _warehouses.FirstOrDefault(w => w.IsDefault)?.Id;
            FillSummary(null);
            SetStatus("جديد");
            HintText.Text = "اختر أمر إنتاج له إنتاج فعلي، أدخل كراتين التسليم ومخزن الإدخال لكل بند، ثم «حفظ مسودة» فـ«اعتماد».";
        }
        finally { _loading = false; ApplyMode(); }
    }

    private void Refresh()
    {
        try
        {
            LoadWarehouses();
            if (_receiptId > 0) OpenReceipt(_receiptId);
            else if (_ctx != null) LoadOrder(_ctx.OrderId, keepInput: true);
            else NewDoc();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Refresh"); }
    }

    // ═══════════════ اختيار أمر الإنتاج ═══════════════

    private void PickOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!_editable || _receiptId > 0)
        { Dlg.Info("لا يُغيَّر أمر الإنتاج لسند محفوظ — أنشئ سنداً جديداً من «جديد»."); return; }
        try
        {
            var win = new DocSearchWindow("أوامر الإنتاج القابلة للتسليم",
                new List<SearchFieldDef>
                {
                    new() { Key = "term", LabelAr = "رقم الأمر / العميل / الصنف" },
                    new() { Key = "all", LabelAr = "إظهار المكتملة أيضاً (نعم/لا)" }
                },
                cond =>
                {
                    bool onlyRemaining = !(cond.GetValueOrDefault("all") ?? "").Trim().StartsWith("ن");
                    using var scope = AppContainer.NewScope();
                    var rows = Svc(scope).SearchDeliverableOrders(cond.GetValueOrDefault("term"), onlyRemaining);
                    var res = new SearchResult { Columns = new List<string> { "رقم الأمر", "التاريخ", "العملاء", "الأصناف", "المخطط (كجم)", "الفعلي (كجم)", "المسلَّم (كجم)", "المتبقي (كجم)", "الحالة" } };
                    foreach (var r in rows)
                        res.Rows.Add((r.OrderId, new object[] { r.OrderNumber, r.OrderDate, r.Customers, r.Products, r.PlannedKg.ToString("N1"), r.ProducedKg.ToString("N1"), r.DeliveredKg.ToString("N1"), r.RemainingKg.ToString("N1"), r.StatusAr }));
                    return res;
                });
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() == true && win.SelectedId is int id) LoadOrder(id);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.PickOrder"); }
    }

    private void LoadOrder(int orderId, bool keepInput = false)
    {
        try
        {
            Dictionary<int, (int DeliverCartons, int? WarehouseId)> prevCartons = keepInput ? _lines.ToDictionary(l => l.OrderItemId, l => (l.DeliverCartons, l.WarehouseId)) : new();
            Dictionary<int, (double DeliverQty, int? WarehouseId, string Notes)> prevBp = keepInput ? _byProducts.ToDictionary(b => b.ByProductId, b => (b.DeliverQty, b.WarehouseId, b.Notes)) : new();
            using var scope = AppContainer.NewScope();
            _ctx = Svc(scope).GetOrderDeliveryContext(orderId);
            if (_ctx == null) { Dlg.Error("أمر الإنتاج غير موجود."); return; }
            if (!_ctx.HasActual && _ctx.ByProducts.Count == 0)
                Dlg.Info("هذا الأمر بلا إنتاج فعلي مسجَّل — سجِّل الفعلي من «تسجيل الإنتاج الفعلي (إقفال اليوم)» أولاً.");
            _loading = true;
            OrderNoBox.Text = _ctx.OrderNumber;
            FillSummary(_ctx);
            int? defWh = BulkWarehouseBox.SelectedValue as int? ?? _warehouses.FirstOrDefault(w => w.IsDefault)?.Id;
            _lines.Clear();
            foreach (var l in _ctx.Lines)
            {
                var ui = new FgLineUi
                {
                    OrderItemId = l.OrderItemId, ProductId = l.ProductId, ProductName = l.ProductName, PackagingTypeId = l.PackagingTypeId,
                    PackSpec = l.PackSpec, CartonWeightKg = l.CartonWeightKg, CustomerId = l.CustomerId, CustomerName = l.CustomerName,
                    LotId = l.LotId, LotCode = l.LotCode, PlannedKg = l.PlannedKg, ProducedKg = l.ProducedKg, ProducedCartons = l.ProducedCartons,
                    DeliveredKg = l.DeliveredKg, RemainingKg = l.RemainingKg, WarehouseId = defWh
                };
                if (prevCartons.TryGetValue(l.OrderItemId, out var p)) { ui.DeliverCartons = p.DeliverCartons; ui.WarehouseId = p.WarehouseId ?? defWh; }
                ui.PropertyChanged += (_, _) => UpdateTotals();
                _lines.Add(ui);
            }
            _byProducts.Clear();
            foreach (var b in _ctx.ByProducts)
            {
                var ui = new FgByProductUi
                {
                    ByProductId = b.ByProductId, Name = b.Name, KindAr = b.KindAr, Unit = b.Unit, PostsStock = b.ProductId != null,
                    ProducedQty = b.ProducedQty, DeliveredQty = b.DeliveredQty, RemainingQty = b.RemainingQty, WarehouseId = defWh
                };
                if (prevBp.TryGetValue(b.ByProductId, out var p)) { ui.DeliverQty = p.DeliverQty; ui.WarehouseId = p.WarehouseId ?? defWh; ui.Notes = p.Notes; }
                ui.PropertyChanged += (_, _) => UpdateTotals();
                _byProducts.Add(ui);
            }
            HintText.Text = _ctx.RemainingKg <= 0.001 && _ctx.HasActual
                ? "كل الإنتاج الفعلي لهذا الأمر مسلَّم للمخازن — لا متبقي."
                : "أدخل كراتين التسليم (≤ المتبقي) ومخزن الإدخال لكل بند. المخرجات الثانوية اختيارية.";
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.LoadOrder"); }
        finally { _loading = false; UpdateTotals(); ApplyMode(); }
    }

    private void FillSummary(FgOrderDeliveryContext c)
    {
        SumPlan.Text = "الخطة: " + (c?.PlanNumber ?? "—");
        SumDate.Text = "التاريخ: " + (c?.OrderDate ?? "—");
        SumShift.Text = "الوردية: " + (c?.Shift ?? "—");
        SumLine.Text = "الخط: " + (c?.Line ?? "—");
        SumCustomers.Text = "العملاء: " + (c?.Customers ?? "—");
        SumOrderStatus.Text = "حالة الأمر: " + (c?.StatusAr ?? "—");
        SumTotals.Text = c == null ? "الفعلي: — | المسلَّم: — | المتبقي: —"
            : $"الفعلي: {c.ProducedKg:N1} كجم | المسلَّم: {c.DeliveredKg:N1} | المتبقي: {c.RemainingKg:N1}";
    }

    // ═══════════════ إدخال الجدول ═══════════════

    private void AllRemaining_Click(object sender, RoutedEventArgs e)
    {
        if (!_editable) return;
        foreach (var l in _lines) l.DeliverCartons = l.RemainingCartons;
        foreach (var b in _byProducts) b.DeliverQty = b.RemainingQty;
        UpdateTotals();
    }

    private void ClearQty_Click(object sender, RoutedEventArgs e)
    {
        if (!_editable) return;
        foreach (var l in _lines) l.DeliverCartons = 0;
        foreach (var b in _byProducts) b.DeliverQty = 0;
        UpdateTotals();
    }

    private void BulkWarehouse_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !_editable || BulkWarehouseBox.SelectedValue is not int wh) return;
        foreach (var l in _lines) l.WarehouseId = wh;
        foreach (var b in _byProducts) b.WarehouseId = wh;
    }

    /// <summary>➕ صف مخرج ثانوي: من التعريفات التي لها متبقٍ مسجَّل ولم تُدرج بعد.</summary>
    private void AddByProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!_editable) { Dlg.Info("السند للقراءة فقط — اضغط «تعديل» إن كان مسودة."); return; }
        if (_ctx == null) { Dlg.Info("اختر أمر إنتاج أولاً."); return; }
        var candidates = _ctx.ByProducts.Where(b => b.RemainingQty > 0.0001 && _byProducts.All(x => x.ByProductId != b.ByProductId)).ToList();
        if (candidates.Count == 0)
        {
            Dlg.Info("لا توجد مخرجات ثانوية مسجَّلة لهذا الأمر لها متبقٍ غير مسلَّم.\nالمخرجات تُسجَّل في «تسجيل الإنتاج الفعلي (إقفال اليوم)» — لا تُخترع من هنا.");
            return;
        }
        var win = new DocSearchWindow("المخرجات الثانوية المسجَّلة على الأمر",
            new List<SearchFieldDef> { new() { Key = "name", LabelAr = "اسم المخرج" } },
            cond =>
            {
                string t = (cond.GetValueOrDefault("name") ?? "").Trim();
                var res = new SearchResult { Columns = new List<string> { "المخرج", "النوع", "الوحدة", "الفعلي", "سبق تسليمه", "المتبقي" } };
                foreach (var b in candidates.Where(b => t.Length == 0 || b.Name.Contains(t)))
                    res.Rows.Add((b.ByProductId, new object[] { b.Name, b.KindAr, b.Unit, b.ProducedQty.ToString("N2"), b.DeliveredQty.ToString("N2"), b.RemainingQty.ToString("N2") }));
                return res;
            });
        win.Owner = Window.GetWindow(this);
        if (win.ShowDialog() != true || win.SelectedId is not int id) return;
        var src = candidates.First(b => b.ByProductId == id);
        var ui = new FgByProductUi
        {
            ByProductId = src.ByProductId, Name = src.Name, KindAr = src.KindAr, Unit = src.Unit, PostsStock = src.ProductId != null,
            ProducedQty = src.ProducedQty, DeliveredQty = src.DeliveredQty, RemainingQty = src.RemainingQty,
            WarehouseId = BulkWarehouseBox.SelectedValue as int? ?? _warehouses.FirstOrDefault(w => w.IsDefault)?.Id
        };
        ui.PropertyChanged += (_, _) => UpdateTotals();
        _byProducts.Add(ui);
        UpdateTotals();
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!_editable) { e.Cancel = true; return; }
        Dispatcher.BeginInvoke(new Action(UpdateTotals));
    }

    private void UpdateTotals()
    {
        if (_loading) return;
        int cartons = _lines.Sum(l => l.DeliverCartons);
        double kg = _lines.Sum(l => l.DeliverKg);
        int count = _lines.Count(l => l.DeliverCartons > 0) + _byProducts.Count(b => b.DeliverQty > 0);
        var over = _lines.FirstOrDefault(l => l.IsOver);
        var overBp = _byProducts.FirstOrDefault(b => b.IsOver);
        DocTotals.Text = $"إجمالي التسليم: {cartons:N0} كرتون / {kg:N1} كجم — {count} بند"
            + (_byProducts.Any(b => b.DeliverQty > 0) ? $" + {_byProducts.Sum(b => b.DeliverQty):N2} مخرجات ثانوية" : "");
        if (over != null)
            HintText.Text = $"⛔ «{over.ProductName}» — {over.CustomerName}: كمية التسليم {over.DeliverKg:N1} كجم تتجاوز المتبقي {over.RemainingKg:N1} كجم.";
        else if (overBp != null)
            HintText.Text = $"⛔ المخرج «{overBp.Name}»: {overBp.DeliverQty:N2} يتجاوز المتبقي {overBp.RemainingQty:N2}.";
        else if (_ctx != null && _editable)
            HintText.Text = count == 0 ? "أدخل كمية تسليم لبند واحد على الأقل." : "جاهز للحفظ كمسودة — الأرصدة لا تتأثر إلا بالاعتماد.";
    }

    // ═══════════════ الحفظ / الاعتماد / الإلغاء ═══════════════

    private FgDeliveryDraftDto BuildDraft()
    {
        if (_ctx == null) throw new InvalidOperationException("اختر أمر إنتاج أولاً.");
        return new FgDeliveryDraftDto
        {
            OrderId = _ctx.OrderId,
            DeliveryDate = (DateBox.SelectedDate ?? DateTime.Today).ToString("dd/MM/yyyy"),
            Notes = NotesBox.Text,
            Items = _lines.Where(l => l.DeliverCartons > 0).Select(l => new FinishedGoodsItemDto
            {
                ProductId = l.ProductId, LotId = l.LotId, CustomerId = l.CustomerId, PackagingTypeId = l.PackagingTypeId,
                PackageCount = l.DeliverCartons, NetWeightKg = l.DeliverKg, WarehouseId = l.WarehouseId
            }).ToList(),
            ByProducts = _byProducts.Where(b => b.DeliverQty > 0).Select(b => new FgByProductInputDto
            { ByProductId = b.ByProductId, Qty = b.DeliverQty, WarehouseId = b.WarehouseId, Notes = b.Notes }).ToList()
        };
    }

    private bool ValidateInput()
    {
        if (_ctx == null) { Dlg.Error("اختر أمر إنتاج أولاً."); return false; }
        if (_lines.All(l => l.DeliverCartons <= 0) && _byProducts.All(b => b.DeliverQty <= 0)) { Dlg.Error("أدخل كمية تسليم لبند واحد على الأقل."); return false; }
        var over = _lines.FirstOrDefault(l => l.IsOver);
        if (over != null) { Dlg.Error($"كمية التسليم للصنف «{over.ProductName}» ({over.DeliverKg:N1} كجم) تتجاوز المتبقي ({over.RemainingKg:N1} كجم)."); return false; }
        var overBp = _byProducts.FirstOrDefault(b => b.IsOver);
        if (overBp != null) { Dlg.Error($"كمية المخرج «{overBp.Name}» تتجاوز المتبقي ({overBp.RemainingQty:N2})."); return false; }
        var noWh = _lines.FirstOrDefault(l => l.DeliverCartons > 0 && l.WarehouseId == null);
        if (noWh != null) { Dlg.Error($"حدد مخزن الإدخال للصنف «{noWh.ProductName}» — لا مخزن افتراضي إلزامي."); return false; }
        var noWhBp = _byProducts.FirstOrDefault(b => b.DeliverQty > 0 && b.WarehouseId == null);
        if (noWhBp != null) { Dlg.Error($"حدد مخزن الاستلام للمخرج «{noWhBp.Name}»."); return false; }
        return true;
    }

    /// <summary>💾 حفظ مسودة: إنشاء أو تحديث — لا أثر على الأرصدة. يعيد السند المحفوظ إلى الشاشة.</summary>
    private bool SaveDraft()
    {
        if (!_editable) { Dlg.Info("السند للقراءة فقط — اضغط «تعديل» إن كان مسودة، أو «جديد» لسند آخر."); return false; }
        if (!ValidateInput()) return false;
        try
        {
            using var scope = AppContainer.NewScope();
            var r = Svc(scope).SaveDraft(BuildDraft(), _receiptId > 0 ? _receiptId : null);
            if (!r.Ok) { Dlg.Error(r.Message); return false; }
            OpenReceipt(r.Id);
            Dlg.Info(r.Message);
            return true;
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.SaveDraft"); return false; }
    }

    /// <summary>🔒 اعتماد: يحفظ المسودة أولاً إن لزم، ثم يُنشئ الحركات المخزنية (مستند + حركات في عملية واحدة).</summary>
    private void Approve()
    {
        try
        {
            if (_editable)
            {
                if (!ValidateInput()) return;
                using var s0 = AppContainer.NewScope();
                var saved = Svc(s0).SaveDraft(BuildDraft(), _receiptId > 0 ? _receiptId : null);
                if (!saved.Ok) { Dlg.Error(saved.Message); return; }
                _receiptId = saved.Id;
            }
            if (_receiptId <= 0) { Dlg.Error("لا يوجد سند للاعتماد."); return; }
            using var scope = AppContainer.NewScope();
            var card = Svc(scope).GetReceipt(_receiptId);
            if (card == null) { Dlg.Error("السند غير موجود."); return; }
            if (card.IsApproved) { Dlg.Info($"السند {card.DocumentNumber} معتمد مسبقاً — لا يُعاد اعتماده."); OpenReceipt(_receiptId); return; }
            string lines = string.Join("\n", card.Lines.Select(l => $"• {l.ProductName} — {l.CustomerName}: {l.PackageCount:N0} كرتون / {l.NetWeightKg:N1} كجم → {l.WarehouseName}"));
            string bps = card.ByProducts.Count == 0 ? "" : "\n" + string.Join("\n", card.ByProducts.Select(b => $"• {b.Name}: {b.Qty:N2} {b.Unit} → {b.WarehouseName}" + (b.PostsStock ? "" : " (توثيق)")));
            if (!Dlg.Confirm($"اعتماد سند التسليم {card.DocumentNumber} لأمر {card.OrderNumber}؟\nسيُنشئ حركات إدخال فعلية في المخازن التالية:\n{lines}{bps}\n\nالتراجع لاحقاً يكون بقيد عكسي موثق فقط.", "اعتماد التسليم"))
            { OpenReceipt(_receiptId); return; }
            var r = Svc(scope).Approve(_receiptId);
            OpenReceipt(_receiptId);
            if (r.Ok) Dlg.Info(r.Message); else Dlg.Error(r.Message);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Approve"); }
    }

    /// <summary>✏️ تعديل: يفتح مسودة محفوظة للتحرير — المعتمد يُرفض بتوجيه إلى التراجع.</summary>
    private void EditDoc()
    {
        if (_receiptId <= 0) { Dlg.Info("السند الجديد قابل للتحرير مباشرة — لا حاجة لزر التعديل."); return; }
        using var scope = AppContainer.NewScope();
        var card = Svc(scope).GetReceipt(_receiptId);
        if (card == null) { Dlg.Error("السند غير موجود."); return; }
        if (card.Status == DocStatuses.Cancelled) { Dlg.Error("السند ملغى — لا يُعدَّل."); return; }
        if (card.IsApproved) { Dlg.Error("السند معتمد وله حركات مخزنية — استخدم «تراجع» (إلغاء الاعتماد بقيد عكسي) ثم عدِّل."); return; }
        _editable = true;
        LoadOrder(card.OrderId, keepInput: true);
        SetStatus("مسودة — قيد التعديل");
    }

    /// <summary>↩ تراجع: سند جديد = مسح الإدخال؛ مسودة = إعادة آخر نسخة محفوظة؛ معتمد = إلغاء الاعتماد بقيد عكسي (بتأكيد).</summary>
    private void Undo()
    {
        try
        {
            if (_receiptId <= 0) { if (_ctx == null || Dlg.Confirm("إلغاء الإدخالات غير المحفوظة وبدء سند جديد؟")) NewDoc(); return; }
            using var scope = AppContainer.NewScope();
            var card = Svc(scope).GetReceipt(_receiptId);
            if (card == null) { NewDoc(); return; }
            if (!card.IsApproved) { OpenReceipt(_receiptId); Dlg.Info("أُعيدت آخر نسخة محفوظة من المسودة."); return; }
            if (!PermissionGate.Can("finishedgoods", "Cancel")) { Dlg.Error("ليس لديك صلاحية إلغاء اعتماد سندات التسليم."); return; }
            if (!Dlg.Confirm($"إلغاء اعتماد السند {card.DocumentNumber}؟\nسيُسجَّل قيد عكسي لكل حركة إدخال (لا حذف من دفتر الحركات) ويعود السند مسودة قابلة للتعديل.", "إلغاء الاعتماد")) return;
            var r = Svc(scope).Unapprove(_receiptId);
            OpenReceipt(_receiptId);
            if (r.Ok) Dlg.Info(r.Message); else Dlg.Error(r.Message);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Undo"); }
    }

    private void CancelDraft()
    {
        try
        {
            if (_receiptId <= 0) { Dlg.Info("السند غير محفوظ بعد — استخدم «تراجع» لمسح الإدخال."); return; }
            if (!Dlg.Confirm($"إلغاء السند {DocNoBox.Text}؟ يبقى في السجل بحالة «ملغى» ولا يُحتسب من المسلَّم.", "إلغاء المسودة")) return;
            using var scope = AppContainer.NewScope();
            var r = Svc(scope).CancelDraft(_receiptId);
            if (r.Ok) { Dlg.Info(r.Message); OpenReceipt(_receiptId); } else Dlg.Error(r.Message);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.CancelDraft"); }
    }

    // ═══════════════ فتح سند محفوظ / بحث ═══════════════

    private void OpenReceipt(int receiptId)
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var card = Svc(scope).GetReceipt(receiptId);
            if (card == null) { Dlg.Error("السند غير موجود."); return; }
            _ctx = Svc(scope).GetOrderDeliveryContext(card.OrderId);
            _loading = true;
            _receiptId = card.Id;
            _editable = false;
            DocNoBox.Text = card.DocumentNumber;
            ReceiptNoBox.Text = card.ReceiptNumber ?? "";
            OrderNoBox.Text = card.OrderNumber;
            NotesBox.Text = card.Notes ?? "";
            DateBox.SelectedDate = UiFormat.TryParseDate(card.DeliveryDate, out var d) ? d : DateTime.Today;
            FillSummary(_ctx);
            _lines.Clear();
            foreach (var l in card.Lines)
            {
                var src = _ctx?.Lines.FirstOrDefault(x => x.ProductId == l.ProductId && x.LotId == l.LotId && x.CustomerId == l.CustomerId);
                // §عرض سند محفوظ: المتبقي المعروض يستثني هذا السند نفسه إن كان معتمداً (حتى تُقرأ الأرقام بلا لبس)
                double remaining = (src?.RemainingKg ?? 0) + (card.IsApproved ? l.ReceivedQtyKg : 0);
                var ui = new FgLineUi
                {
                    OrderItemId = src?.OrderItemId ?? 0, ProductId = l.ProductId, ProductName = l.ProductName, PackagingTypeId = l.PackagingTypeId,
                    PackSpec = l.PackSpec, CartonWeightKg = l.PackageCount > 0 ? l.NetWeightKg / l.PackageCount : src?.CartonWeightKg ?? 0,
                    CustomerId = l.CustomerId, CustomerName = l.CustomerName, LotId = l.LotId, LotCode = l.LotCode,
                    PlannedKg = src?.PlannedKg ?? 0, ProducedKg = src?.ProducedKg ?? 0, ProducedCartons = src?.ProducedCartons ?? 0,
                    DeliveredKg = (src?.DeliveredKg ?? 0) - (card.IsApproved ? l.ReceivedQtyKg : 0), RemainingKg = remaining,
                    WarehouseId = l.WarehouseId, BalanceAfter = l.BalanceAfter
                };
                ui.DeliverCartons = l.PackageCount;
                ui.PropertyChanged += (_, _) => UpdateTotals();
                _lines.Add(ui);
            }
            _byProducts.Clear();
            foreach (var b in card.ByProducts)
            {
                var src = _ctx?.ByProducts.FirstOrDefault(x => x.ByProductId == b.ByProductId);
                var ui = new FgByProductUi
                {
                    ByProductId = b.ByProductId, Name = b.Name, KindAr = b.KindAr, Unit = b.Unit, PostsStock = b.PostsStock,
                    ProducedQty = src?.ProducedQty ?? 0, DeliveredQty = (src?.DeliveredQty ?? 0) - b.ReceivedQty,
                    RemainingQty = (src?.RemainingQty ?? 0) + b.ReceivedQty, WarehouseId = b.WarehouseId, Notes = b.Notes
                };
                ui.DeliverQty = b.Qty;
                ui.PropertyChanged += (_, _) => UpdateTotals();
                _byProducts.Add(ui);
            }
            SetStatus(card.StatusAr);
            HintText.Text = card.IsApproved
                ? $"سند معتمد — اعتمده {card.ApprovedBy ?? "—"} في {card.ApprovedDate ?? "—"}. التعديل عبر «تراجع» (قيد عكسي) فقط."
                : card.Status == DocStatuses.Cancelled ? "سند ملغى — للعرض فقط."
                : "مسودة محفوظة بلا أثر على الأرصدة — «تعديل» للتحرير أو «اعتماد» لقيد الحركات.";
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Open"); }
        finally { _loading = false; UpdateTotals(); ApplyMode(); }
    }

    private void SearchReceipts()
    {
        try
        {
            var win = new DocSearchWindow("سندات تسليم الإنتاج للمخازن",
                new List<SearchFieldDef> { new() { Key = "term", LabelAr = "رقم السند / رقم الاستلام / أمر الإنتاج" } },
                cond =>
                {
                    using var scope = AppContainer.NewScope();
                    var cards = Svc(scope).SearchReceipts(cond.GetValueOrDefault("term"));
                    var res = new SearchResult { Columns = new List<string> { "رقم السند", "التاريخ", "أمر الإنتاج", "رقم الاستلام", "البنود", "الكراتين", "الكمية (كجم)", "المستلم (كجم)", "الحالة", "أنشأه" } };
                    foreach (var c in cards)
                        res.Rows.Add((c.Id, new object[] { c.DocumentNumber, c.DeliveryDate, c.OrderNumber, c.ReceiptNumber ?? "—", c.Lines.Count + (c.ByProducts.Count > 0 ? $" + {c.ByProducts.Count} ثانوي" : ""),
                            c.Lines.Sum(l => l.PackageCount).ToString("N0"), c.Lines.Sum(l => l.NetWeightKg).ToString("N1"), c.Lines.Sum(l => l.ReceivedQtyKg).ToString("N1"), c.StatusAr, c.CreatedBy ?? "—" }));
                    return res;
                });
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() == true && win.SelectedId is int id) OpenReceipt(id);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Search"); }
    }

    private void Print()
    {
        if (_receiptId <= 0) { Dlg.Error("احفظ السند أولاً — الطباعة من النسخة المحفوظة فقط."); return; }
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErp.Infrastructure.Persistence.DatesErpDbContext>();
            var m = Printing.StoredPrintModels.FinishedReceipt(db, _receiptId);
            new PrintPreviewWindow(PhasePrint.Build(m), $"{m.DocTitle} {m.DocNo}") { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "FG.Print"); }
    }

    // ═══════════════ حالة الشاشة ═══════════════

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>تمكين/تعطيل حسب الحالة — الأزرار المحجوبة بالصلاحية لا يُعاد تفعيلها (ErpToolbar يحرسها).</summary>
    private void ApplyMode()
    {
        bool hasOrder = _ctx != null;
        ItemsGrid.IsReadOnly = !_editable;
        ByProductsGrid.IsReadOnly = !_editable;
        NotesBox.IsReadOnly = !_editable;
        DateBox.IsEnabled = _editable;
        PickOrderBtn.IsEnabled = _editable && _receiptId <= 0;
        AllRemainingBtn.IsEnabled = _editable && hasOrder;
        ClearQtyBtn.IsEnabled = _editable && hasOrder;
        AddByProductBtn.IsEnabled = _editable && hasOrder;
        BulkWarehouseBox.IsEnabled = _editable;
        if (_toolbar == null) return;
        string st = StatusText.Text ?? "";
        bool approved = _receiptId > 0 && (st.Contains("معتمد") || st.Contains("مكتمل") || st.Contains("جزئي"));
        bool cancelled = StatusText.Text.Contains("ملغى");
        void Set(Button b, bool on) { if (b != null && !_toolbar.IsDenied(b)) b.IsEnabled = on; }
        Set(_toolbar.SaveBtn, _editable && hasOrder);
        Set(_toolbar.ApproveBtn, hasOrder && !approved && !cancelled);
        Set(_toolbar.EditBtn, _receiptId > 0 && !_editable && !approved && !cancelled);
        Set(_toolbar.UndoBtn, !cancelled);
        Set(_toolbar.PrintBtn, _receiptId > 0);
    }
}
