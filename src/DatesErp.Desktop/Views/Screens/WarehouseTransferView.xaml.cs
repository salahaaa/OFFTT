using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Desktop.Views.Screens;

/// <summary>سطر بند تحويل في الشاشة — يحمل مفتاح الرصيد الكامل (صنف/مادة + دفعة + عميل + عبوة) والمتاح في المصدر.</summary>
public class TransferRow : INotifyPropertyChanged
{
    public int? ProductId { get; set; }
    public int? MaterialId { get; set; }
    public string ProductName { get; set; } = "— اختر الصنف من رصيد المخزن المصدر —";
    public int? LotId { get; set; }
    public string LotCode { get; set; } = "—";
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "—";
    public int? PackagingTypeId { get; set; }
    public string PackagingName { get; set; } = "—";
    public double AvailableQty { get; set; }
    public int AvailablePackages { get; set; }
    private double _qtyKg;
    public double QtyKg { get => _qtyKg; set { _qtyKg = value; OnChanged(nameof(QtyKg)); } }
    private int _packageCount;
    public int PackageCount { get => _packageCount; set { _packageCount = value; OnChanged(nameof(PackageCount)); } }
    public string Notes { get; set; }
    public bool IsEmpty => ProductId == null && MaterialId == null;
    public string BalanceKey => $"{ProductId}|{MaterialId}|{LotId}|{CustomerId}|{PackagingTypeId}";
    public event PropertyChangedEventHandler PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public static TransferRow From(StockByWarehouseDto b) => new()
    {
        ProductId = b.ProductId,
        MaterialId = b.MaterialId,
        ProductName = b.ProductName ?? b.MaterialName ?? "—",
        LotId = b.LotId,
        LotCode = b.LotCode ?? "—",
        CustomerId = b.CustomerId,
        CustomerName = b.CustomerName ?? "—",
        PackagingTypeId = b.PackagingTypeId,
        PackagingName = b.PackagingName ?? "—",
        AvailableQty = b.QtyKg,
        AvailablePackages = b.PackageCount,
        QtyKg = b.QtyKg,
        PackageCount = b.PackageCount
    };
}

/// <summary>
/// §MRPWH1001 — تحويل مخزني بين المخازن. كل العمليات عبر IWarehouseTransferService؛
/// الشاشة لا تكتب في قاعدة البيانات مباشرة. المسودة بلا أثر؛ الاعتماد ينفّذ النقل؛ الإلغاء يعكسه.
/// </summary>
public partial class WarehouseTransferView : UserControl
{
    private List<object> _transfersAll = new();
    private readonly ObservableCollection<StockByWarehouseDto> _balances = new();
    private readonly ObservableCollection<TransferRow> _items = new();
    private List<int> _transferIds = new();
    private int _currentId;
    private bool _locked;
    private string _currentStatus = "";
    private Views.ErpToolbar _toolbar;
    private DialogService Dlg => AppContainer.Get<DialogService>();

    public WarehouseTransferView()
    {
        InitializeComponent();
        BalanceGrid.ItemsSource = _balances;
        ItemsGrid.ItemsSource = _items;
        Loaded += (_, _) => Load();
    }

    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("المخازن — تحويل مخزني");
        chrome.SetScreenCode("MRPWH1001");
        _toolbar = new Views.ErpToolbar()
            .WithNew((_, _) => NewForm(), "➕ سند تحويل جديد")
            .WithSave((_, _) => Save(), "💾 حفظ المسودة")
            .WithSearch((_, _) => OpenSearch(), "بحث في سندات التحويل")
            .WithUndo((_, _) => UndoSmart(), "↩ تراجع")
            .WithApprove((_, _) => Approve(), "🔒 اعتماد وتنفيذ النقل")
            .WithUnapprove((_, _) => Cancel(), "إلغاء السند (عكس الحركات إن كان معتمداً)")
            .WithDelete((_, _) => DeleteDraft())
            .WithPrint((_, _) => Print(), "🖨️ طباعة")
            .WithExcel((_, _) => Export())
            .WithNavigation((_, _) => Nav(0), (_, _) => Nav(-1), (_, _) => Nav(1), (_, _) => Nav(int.MaxValue))
            .WithList((_, _) => { RefreshList(); SearchBox.Focus(); }, "📋 القائمة")
            .WithRefresh((_, _) => { RefreshList(); RefreshBalances(); if (_currentId > 0) OpenTransfer(_currentId); })
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard"));
        chrome.SetToolbar(_toolbar);
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }

    private T WithSvc<T>(Func<IWarehouseTransferService, T> action)
    {
        using var scope = AppContainer.NewScope();
        return action(scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>());
    }

    // ═══════════════════ تحميل ═══════════════════

    private void Load()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var warehouses = db.Warehouses.AsNoTracking().Where(w => w.IsActive)
                .OrderBy(w => w.IsDefault ? 0 : 1).ThenBy(w => w.Id).ToList();
            SourceWhBox.ItemsSource = warehouses;
            DestWhBox.ItemsSource = warehouses;
            if (warehouses.Count < 2)
                Dlg.Error("التحويل المخزني يحتاج مخزنين فعّالين على الأقل — عرّف المخازن من شاشة الإعدادات أولاً.");
            NewForm();
            RefreshList();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Load"); }
    }

    private void RefreshList()
    {
        try
        {
            var list = WithSvc(s => s.GetTransfers());
            _transferIds = list.Select(t => t.Id).OrderBy(x => x).ToList();
            _transfersAll = list.Select(t => new
            {
                t.Id,
                DocNo = t.DocumentNumber,
                SourceWh = t.SourceWarehouseName,
                DestWh = t.DestinationWarehouseName,
                Date = t.TransferDate,
                Qty = t.TotalQtyKg,
                t.StatusAr
            }).Cast<object>().ToList();
            ScreenSearch.Apply(SearchBox, TransfersGrid, _transfersAll);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.List"); }
    }

    private void RefreshBalances()
    {
        _balances.Clear();
        if (SourceWhBox.SelectedValue is not int whId) { BalanceChip.Text = "الرصيد: —"; return; }
        try
        {
            var balances = WithSvc(s => s.GetStockByWarehouse(null, whId, null));
            foreach (var b in balances) _balances.Add(b);
            BalanceChip.Text = balances.Count == 0
                ? $"لا يوجد رصيد في «{SourceWhBox.Text}» — لا شيء يُحوَّل منه."
                : $"الرصيد في «{SourceWhBox.Text}»: {balances.Sum(x => x.QtyKg):N1} كجم / {balances.Sum(x => x.PackageCount):N0} عبوة / {balances.Count} سطر";
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Balance"); }
    }

    private void SourceWh_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshBalances();
        // تغيير المصدر على مسودة قيد الإدخال: البنود القديمة تخص مخزناً آخر — تُفرَّغ (لا تُحفظ بنود لا رصيد لها)
        if (!_locked && sender != null && _items.Any(r => !r.IsEmpty))
        {
            _items.Clear();
            AddEmptyRow();
        }
    }

    // ═══════════════════ البنود ═══════════════════

    private bool AddRowFromBalance(StockByWarehouseDto b, bool silent = false)
    {
        if (_locked) return false;
        var row = TransferRow.From(b);
        var dup = _items.FirstOrDefault(r => !r.IsEmpty && r.BalanceKey == row.BalanceKey);
        if (dup != null)
        {
            if (!silent) Dlg.Error($"«{row.ProductName}» بنفس الدفعة/العميل/العبوة موجود بالفعل في البنود — عدّل كميته بدل تكراره.");
            return false;
        }
        var empty = _items.FirstOrDefault(r => r.IsEmpty);
        int idx = empty != null ? _items.IndexOf(empty) : _items.Count;
        if (empty != null) _items.Remove(empty);
        _items.Insert(idx, row);
        EnsureEmptyRow();
        return true;
    }

    private void Balance_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (BalanceGrid.SelectedItem is StockByWarehouseDto b) AddRowFromBalance(b);
    }

    private void AddFromBalance_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) { Dlg.Error("السند مقفل — أنشئ سنداً جديداً."); return; }
        if (BalanceGrid.SelectedItem is not StockByWarehouseDto b) { Dlg.Error("اختر سطر رصيد أولاً من جدول «الرصيد في المخزن المصدر»."); return; }
        AddRowFromBalance(b);
    }

    private void TransferAll_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) { Dlg.Error("السند مقفل — أنشئ سنداً جديداً."); return; }
        if (_balances.Count == 0) { Dlg.Error("لا يوجد رصيد في المخزن المصدر."); return; }
        if (!Dlg.Confirm($"إضافة كل أرصدة «{SourceWhBox.Text}» ({_balances.Count} سطر) كبنود تحويل بكامل الكمية؟")) return;
        _items.Clear();
        foreach (var b in _balances) AddRowFromBalance(b, silent: true);
        EnsureEmptyRow();
    }

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        if (sender is not Button btn || btn.Tag is not TransferRow row) return;
        if (SourceWhBox.SelectedValue is not int) { Dlg.Error("اختر المخزن المصدر أولاً."); return; }
        if (_balances.Count == 0) { Dlg.Error("لا يوجد رصيد في المخزن المصدر لاختيار صنف منه."); return; }

        // اختيار من رصيد المصدر فقط — لا يُحوَّل ما ليس موجوداً فيه
        var snapshot = _balances.ToList();
        var win = new DocSearchWindow("أرصدة المخزن المصدر — " + SourceWhBox.Text,
            new List<SearchFieldDef> { new() { Key = "q", LabelAr = "الصنف / الدفعة / العميل" } },
            f =>
            {
                string q = (f.TryGetValue("q", out var v) ? v : "")?.Trim() ?? "";
                var res = new SearchResult { Columns = new List<string> { "الصنف/المادة", "الدفعة", "العميل", "العبوة", "الرصيد (كجم)", "العبوات" } };
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var b = snapshot[i];
                    string name = b.ProductName ?? b.MaterialName ?? "—";
                    if (q.Length > 0 && !(name.Contains(q) || (b.LotCode ?? "").Contains(q) || (b.CustomerName ?? "").Contains(q))) continue;
                    res.Rows.Add((i, new object[] { name, b.LotCode ?? "—", b.CustomerName ?? "—", b.PackagingName ?? "—", b.QtyKg, b.PackageCount }));
                }
                return res;
            })
        { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() != true || win.SelectedId is not int picked || picked < 0 || picked >= snapshot.Count) return;

        var b2 = snapshot[picked];
        var newRow = TransferRow.From(b2);
        if (_items.Any(r => r != row && !r.IsEmpty && r.BalanceKey == newRow.BalanceKey))
        { Dlg.Error($"«{newRow.ProductName}» بنفس الدفعة/العميل/العبوة موجود بالفعل في بند آخر."); return; }
        int idx = _items.IndexOf(row);
        _items.RemoveAt(idx);
        _items.Insert(idx, newRow);
        EnsureEmptyRow();
    }

    private void AddEmptyRow() => _items.Add(new TransferRow());
    private void EnsureEmptyRow()
    {
        if (_locked) return;
        if (_items.Count == 0 || !_items.Last().IsEmpty) AddEmptyRow();
    }
    private void AddEmptyRow_Click(object sender, RoutedEventArgs e) { if (!_locked) EnsureEmptyRow(); }
    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        if (ItemsGrid.SelectedItem is TransferRow r) { _items.Remove(r); EnsureEmptyRow(); }
        else Dlg.Error("اختر سطراً من بنود التحويل أولاً.");
    }
    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        if (sender is Button b && b.Tag is TransferRow r) { _items.Remove(r); EnsureEmptyRow(); }
    }

    /// <summary>تحقق الواجهة قبل الخدمة (الخدمة تعيد التحقق نفسه بشكل ملزم).</summary>
    private List<WarehouseTransferItemDto> ValidateItems(out string error)
    {
        error = null;
        ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var rows = _items.Where(r => !r.IsEmpty).ToList();
        if (rows.Count == 0) { error = "أدخل بنداً واحداً على الأقل (نقر مزدوج على سطر رصيد أو زر «إضافة من الرصيد»)."; return null; }
        int n = 0;
        foreach (var r in rows)
        {
            n++;
            if (r.QtyKg <= 0) { error = $"البند {n} «{r.ProductName}»: الكمية يجب أن تكون أكبر من صفر."; return null; }
            if (r.QtyKg > r.AvailableQty + 0.0005) { error = $"البند {n} «{r.ProductName}»: الكمية {r.QtyKg:N1} أكبر من المتاح {r.AvailableQty:N1} كجم."; return null; }
            if (r.PackageCount < 0) { error = $"البند {n}: عدد العبوات لا يكون سالباً."; return null; }
            if (r.PackageCount > r.AvailablePackages) { error = $"البند {n} «{r.ProductName}»: العبوات {r.PackageCount:N0} أكبر من المتاح {r.AvailablePackages:N0}."; return null; }
        }
        var dupKey = rows.GroupBy(r => r.BalanceKey).FirstOrDefault(g => g.Count() > 1);
        if (dupKey != null) { error = $"«{dupKey.First().ProductName}» مكرر بنفس الدفعة/العميل/العبوة — ادمجه في بند واحد."; return null; }
        return rows.Select(i => new WarehouseTransferItemDto
        {
            ProductId = i.ProductId,
            MaterialId = i.MaterialId,
            LotId = i.LotId,
            CustomerId = i.CustomerId,
            PackagingTypeId = i.PackagingTypeId,
            QtyKg = i.QtyKg,
            PackageCount = i.PackageCount,
            Notes = i.Notes
        }).ToList();
    }

    // ═══════════════════ العمليات ═══════════════════

    private void Save()
    {
        try
        {
            if (_locked) { Dlg.Error("السند مقفل (معتمد/ملغي) — أنشئ سنداً جديداً."); return; }
            if (SourceWhBox.SelectedValue is not int srcId) { Dlg.Error("اختر المخزن المصدر."); return; }
            if (DestWhBox.SelectedValue is not int dstId) { Dlg.Error("اختر المخزن المستلم."); return; }
            if (srcId == dstId) { Dlg.Error("المصدر والمستلم لا يمكن أن يكونا نفس المخزن."); return; }
            var dto = ValidateItems(out var err);
            if (dto == null) { Dlg.Error(err); return; }

            string date = (DateBox.SelectedDate ?? DateTime.Now).ToString("dd/MM/yyyy");
            var r = _currentId > 0
                ? WithSvc(s => s.UpdateTransfer(_currentId, date, srcId, dstId, dto, NotesBox.Text))
                : WithSvc(s => s.SaveTransfer(date, srcId, dstId, dto, NotesBox.Text));
            if (!r.Ok) { Dlg.Error(r.Message); return; }
            _currentId = r.Id;
            DocNoBox.Text = r.DocumentNumber;
            _currentStatus = "Draft";
            Dlg.Info(r.Message + "\n\nالسند باقٍ أمامك — اضغط «اعتماد وتنفيذ النقل» لتنفيذ الحركة فعلياً بين المخزنين.");
            RefreshList();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Save"); }
    }

    private void Approve()
    {
        try
        {
            if (_currentId == 0) { Dlg.Error("احفظ المسودة أولاً ثم اعتمدها."); return; }
            if (_locked) { Dlg.Error("السند معتمد/ملغي بالفعل."); return; }
            if (!Dlg.Confirm($"اعتماد السند {DocNoBox.Text} سينقل الكميات فعلياً من «{SourceWhBox.Text}» إلى «{DestWhBox.Text}» ويُنشئ حركات مخزون.\nمتابعة؟")) return;

            // رصيد قبل/بعد للمصدر (للعرض فقط — الحكم عند الخدمة)
            double before = _balances.Sum(x => x.QtyKg);
            var r = WithSvc(s => s.ApproveTransfer(_currentId));
            if (!r.Ok) { Dlg.Error(r.Message); return; }
            RefreshBalances();
            double after = _balances.Sum(x => x.QtyKg);
            Dlg.Info(r.Message + $"\n\nرصيد «{SourceWhBox.Text}»: قبل {before:N1} كجم ← بعد {after:N1} كجم.");
            OpenTransfer(_currentId);
            RefreshList();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Approve"); }
    }

    private void Cancel()
    {
        try
        {
            if (_currentId == 0) { Dlg.Error("لا يوجد سند محفوظ لإلغائه."); return; }
            if (_currentStatus == "Cancelled") { Dlg.Error("السند ملغي بالفعل."); return; }
            string msg = _currentStatus == "Approved"
                ? $"إلغاء السند المعتمد {DocNoBox.Text} سيعكس حركاته ويعيد الكميات من «{DestWhBox.Text}» إلى «{SourceWhBox.Text}».\nيفشل إن كانت الكمية قد صُرفت من المخزن المستلم. متابعة؟"
                : $"إلغاء مسودة التحويل {DocNoBox.Text}؟ (لا أثر مخزني)";
            if (!Dlg.Confirm(msg)) return;
            var r = WithSvc(s => s.CancelTransfer(_currentId, "إلغاء من شاشة التحويل المخزني"));
            if (!r.Ok) { Dlg.Error(r.Message); return; }
            Dlg.Info(r.Message);
            OpenTransfer(_currentId);
            RefreshBalances();
            RefreshList();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Cancel"); }
    }

    private void DeleteDraft()
    {
        try
        {
            if (_currentId == 0) { Dlg.Error("لا يوجد سند محفوظ."); return; }
            if (_currentStatus == "Approved") { Dlg.Error("السند معتمد — لا يُحذف. استخدم «إلغاء السند» لعكس الحركات."); return; }
            if (!Dlg.Confirm($"حذف مسودة التحويل {DocNoBox.Text} نهائياً؟")) return;
            var r = WithSvc(s => s.DeleteDraft(_currentId));
            if (!r.Ok) { Dlg.Error(r.Message); return; }
            Dlg.Info(r.Message);
            NewForm();
            RefreshList();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Delete"); }
    }

    private void UndoSmart()
    {
        if (_currentId > 0) OpenTransfer(_currentId);
        else NewForm();
    }

    private void NewForm()
    {
        _currentId = 0;
        _currentStatus = "";
        _items.Clear();
        try
        {
            using var s = AppContainer.NewScope();
            DocNoBox.Text = s.ServiceProvider.GetRequiredService<INumberingService>().Peek("WHT");
        }
        catch { DocNoBox.Text = "(تلقائي عند الحفظ)"; }
        DateBox.SelectedDate = DateTime.Now;
        NotesBox.Text = "";
        if (SourceWhBox.Items.Count > 0 && SourceWhBox.SelectedValue == null) SourceWhBox.SelectedIndex = 0;
        if (DestWhBox.Items.Count > 1 && (DestWhBox.SelectedValue == null || Equals(DestWhBox.SelectedValue, SourceWhBox.SelectedValue)))
            DestWhBox.SelectedIndex = DestWhBox.SelectedIndex == 1 ? 0 : 1;
        SetLocked(false);
        RefreshBalances();
        AddEmptyRow();
    }

    private void SetLocked(bool locked)
    {
        _locked = locked;
        SourceWhBox.IsEnabled = !locked;
        DestWhBox.IsEnabled = !locked;
        DateBox.IsEnabled = !locked;
        NotesBox.IsReadOnly = locked;
        ItemsGrid.IsReadOnly = locked;
        if (_toolbar == null) return;
        bool approved = _currentStatus == "Approved";
        bool cancelled = _currentStatus == "Cancelled";
        if (_toolbar.SaveBtn != null) _toolbar.SaveBtn.IsEnabled = !locked;
        if (_toolbar.ApproveBtn != null) _toolbar.ApproveBtn.IsEnabled = !locked && _currentId > 0;
        if (_toolbar.UnapproveBtn != null) _toolbar.UnapproveBtn.IsEnabled = _currentId > 0 && !cancelled;
        if (_toolbar.DeleteBtn != null) _toolbar.DeleteBtn.IsEnabled = _currentId > 0 && !approved;
        if (_toolbar.PrintBtn != null) _toolbar.PrintBtn.IsEnabled = _currentId > 0;
    }

    // ═══════════════════ تنقل / فتح / بحث ═══════════════════

    private void Nav(int dir)
    {
        if (_transferIds.Count == 0) RefreshList();
        if (_transferIds.Count == 0) { Dlg.Error("لا توجد سندات تحويل محفوظة."); return; }
        int idx = _transferIds.IndexOf(_currentId);
        if (idx < 0) idx = dir >= 0 ? -1 : _transferIds.Count;
        idx = dir switch
        {
            0 => 0,
            int.MaxValue => _transferIds.Count - 1,
            _ => Math.Clamp(idx + dir, 0, _transferIds.Count - 1)
        };
        OpenTransfer(_transferIds[idx]);
    }

    private void OpenSearch()
    {
        var win = new DocSearchWindow("سندات التحويل المخزني",
            new List<SearchFieldDef>
            {
                new() { Key = "no", LabelAr = "رقم السند" },
                new() { Key = "wh", LabelAr = "المخزن (مصدر أو مستلم)" },
                new() { Key = "st", LabelAr = "الحالة", Kind = "combo", Options = new[] { "مسودة", "معتمد", "ملغي" } }
            },
            f =>
            {
                var all = WithSvc(s => s.GetTransfers());
                string no = f.GetValueOrDefault("no")?.Trim() ?? "";
                string wh = f.GetValueOrDefault("wh")?.Trim() ?? "";
                string st = f.GetValueOrDefault("st")?.Trim() ?? "";
                var res = new SearchResult { Columns = new List<string> { "الرقم", "التاريخ", "من مخزن", "إلى مخزن", "الإجمالي (كجم)", "الحالة" } };
                foreach (var t in all)
                {
                    if (no.Length > 0 && !(t.DocumentNumber ?? "").Contains(no)) continue;
                    if (wh.Length > 0 && !((t.SourceWarehouseName ?? "").Contains(wh) || (t.DestinationWarehouseName ?? "").Contains(wh))) continue;
                    if (st.Length > 0 && t.StatusAr != st) continue;
                    res.Rows.Add((t.Id, new object[] { t.DocumentNumber, t.TransferDate, t.SourceWarehouseName, t.DestinationWarehouseName, t.TotalQtyKg, t.StatusAr }));
                }
                return res;
            })
        { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true && win.SelectedId is int id) OpenTransfer(id);
    }

    private void OpenTransfer(int id)
    {
        try
        {
            var t = WithSvc(s => s.GetTransfer(id));
            if (t == null) { Dlg.Error("سند التحويل غير موجود (ربما حُذف)."); RefreshList(); return; }
            _currentId = t.Id;
            _currentStatus = t.Status;
            DocNoBox.Text = t.DocumentNumber;
            DateBox.SelectedDate = Core.Common.UiFormat.TryParseDate(t.TransferDate, out var d) ? d : DateTime.Now;
            _locked = true; // منع تفريغ البنود أثناء ضبط المصدر
            SourceWhBox.SelectedValue = t.SourceWarehouseId;
            DestWhBox.SelectedValue = t.DestinationWarehouseId;
            NotesBox.Text = t.Notes;
            _items.Clear();
            foreach (var it in t.Items)
            {
                var bal = _balances.FirstOrDefault(b => b.ProductId == it.ProductId && b.MaterialId == it.MaterialId
                    && b.LotId == it.LotId && b.CustomerId == it.CustomerId && b.PackagingTypeId == it.PackagingTypeId);
                _items.Add(new TransferRow
                {
                    ProductId = it.ProductId,
                    MaterialId = it.MaterialId,
                    ProductName = it.ItemName ?? "—",
                    LotId = it.LotId,
                    LotCode = it.LotCode ?? "—",
                    CustomerId = it.CustomerId,
                    CustomerName = it.CustomerName ?? "—",
                    PackagingTypeId = it.PackagingTypeId,
                    PackagingName = it.PackagingName ?? "—",
                    AvailableQty = bal?.QtyKg ?? 0,
                    AvailablePackages = bal?.PackageCount ?? 0,
                    QtyKg = it.QtyKg,
                    PackageCount = it.PackageCount,
                    Notes = it.Notes
                });
            }
            SetLocked(t.Status != "Draft");
            if (!_locked) EnsureEmptyRow();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Open"); }
    }

    private void Print()
    {
        if (_currentId <= 0) { Dlg.Error("احفظ السند أولاً."); return; }
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var m = Printing.StoredPrintModels.WarehouseTransfer(db, _currentId);
            new PrintPreviewWindow(PhasePrint.Build(m), $"{m.DocTitle} {m.DocNo}") { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Print"); }
    }

    private void Export()
    {
        try
        {
            var rows = _items.Where(i => !i.IsEmpty).ToList();
            if (rows.Count == 0) { Dlg.Error("لا توجد بنود للتصدير."); return; }
            var report = new ReportResult
            {
                TitleAr = $"تحويل مخزني {DocNoBox.Text} — من {SourceWhBox.Text} إلى {DestWhBox.Text}",
                Columns = new List<string> { "الصنف/المادة", "الدفعة", "العميل", "العبوة", "الكمية (كجم)", "العبوات", "ملاحظات" },
                Rows = rows.Select(i => new object[] { i.ProductName, i.LotCode, i.CustomerName, i.PackagingName, i.QtyKg, i.PackageCount, i.Notes ?? "" }).ToList()
            };
            AppContainer.Get<ExportPrintService>().ExportExcel(report);
        }
        catch (Exception ex) { Dlg.HandleException(ex, "WTransfer.Export"); }
    }

    private void TransfersGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TransfersGrid.SelectedItem?.GetType().GetProperty("Id")?.GetValue(TransfersGrid.SelectedItem) is int id)
            OpenTransfer(id);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ScreenSearch.Apply(SearchBox, TransfersGrid, _transfersAll);
}
