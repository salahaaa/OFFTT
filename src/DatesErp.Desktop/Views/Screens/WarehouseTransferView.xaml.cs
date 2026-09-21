using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Desktop.Views.Screens;

public class TransferRow : System.ComponentModel.INotifyPropertyChanged
{
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = "— اختر الصنف —";
    public int? LotId { get; set; }
    public string LotCode { get; set; } = "—";
    public int? CustomerId { get; set; }
    public int? PackagingTypeId { get; set; }
    public double AvailableQty { get; set; }
    private double _qtyKg;
    public double QtyKg { get => _qtyKg; set { _qtyKg = value; OnChanged(nameof(QtyKg)); } }
    private int _packageCount;
    public int PackageCount { get => _packageCount; set { _packageCount = value; OnChanged(nameof(PackageCount)); } }
    public string Notes { get; set; }
    public bool IsEmpty => ProductId == null;
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

public partial class WarehouseTransferView : UserControl
{
    private List<object> _transfers_all = new();
    private readonly ObservableCollection<StockByWarehouseDto> _balances = new();
    private readonly ObservableCollection<TransferRow> _items = new();
    private List<int> _transferIds = new();
    private int _currentId;
    private bool _locked;
    private Views.ErpToolbar _toolbar;

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
            .WithNew((_, _) => NewForm(), "سند تحويل جديد (F2)")
            .WithSave((_, _) => Save(), "حفظ التحويل (F10)")
            .WithSearch((_, _) => { RefreshList(); SearchBox.Focus(); }, "بحث (F9)")
            .WithUndo((_, _) => UndoSmart(), "تراجع")
            .WithApprove((_, _) => Approve(), "🔒 اعتماد وتنفيذ التحويل")
            .WithUnapprove((_, _) => Unapprove(), "إلغاء وعكس")
            .WithPrint((_, _) => Print(), "طباعة")
            .WithExcel((_, _) => Export())
            .WithNavigation((_, _) => Nav(0), (_, _) => Nav(-1), (_, _) => Nav(1), (_, _) => Nav(int.MaxValue))
            .WithList((_, _) => RefreshList(), "عرض الكل")
            .WithDelete((_, _) => DeleteDraft(), "حذف المسودة")
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard"));
        chrome.SetToolbar(_toolbar);
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }

    private void Load()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var warehouses = db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.IsDefault ? 0 : 1).ThenBy(w => w.Id).ToList();
            SourceWhBox.ItemsSource = warehouses;
            DestWhBox.ItemsSource = warehouses;
            var def = warehouses.FirstOrDefault(w => w.IsDefault) ?? warehouses.FirstOrDefault();
            if (def != null) SourceWhBox.SelectedValue = def.Id;
            if (warehouses.Count > 1) DestWhBox.SelectedValue = warehouses[1].Id;
            NewForm();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Load"); }
    }

    private void RefreshList()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var list = svc.GetTransfers();
            _transferIds = list.Select(t => t.Id).ToList();
            _transfers_all = list.Select(t => new { Id = t.Id, DocNo = t.DocumentNumber, SourceWh = t.SourceWarehouseName, DestWh = t.DestinationWarehouseName, Date = t.TransferDate, Qty = t.TotalQtyKg, StatusAr = t.StatusAr }).ToList().Cast<object>().ToList();
            ScreenSearch.Apply(SearchBox, TransfersGrid, _transfers_all);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.List"); }
    }

    private void UndoSmart()
    {
        if (_currentId > 0) OpenTransfer(_currentId);
        else NewForm();
    }

    private void SourceWh_Changed(object sender, SelectionChangedEventArgs e)
    {
        _balances.Clear();
        if (SourceWhBox.SelectedValue is not int whId) return;
        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var balances = svc.GetStockByWarehouse(null, whId, null);
            foreach (var b in balances) _balances.Add(b);
            BalanceChip.Text = $"الرصيد في {SourceWhBox.Text}: {balances.Sum(x => x.QtyKg):N1} كجم / {balances.Count} أصناف";
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Balance"); }
    }

    private void Balance_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_locked) return;
        if (BalanceGrid.SelectedItem is StockByWarehouseDto b)
        {
            _items.Add(new TransferRow
            {
                ProductId = b.ProductId ?? b.MaterialId,
                ProductName = b.ProductName ?? b.MaterialName ?? "—",
                LotId = b.LotId,
                LotCode = b.LotCode ?? "—",
                CustomerId = b.CustomerId,
                PackagingTypeId = b.PackagingTypeId,
                AvailableQty = b.QtyKg,
                QtyKg = b.QtyKg,
                PackageCount = b.PackageCount
            });
            EnsureEmptyRow();
        }
    }

    private void AddFromBalance_Click(object sender, RoutedEventArgs e) => Balance_DoubleClick(sender, null);

    private void TransferAll_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        _items.Clear();
        foreach (var b in _balances)
        {
            _items.Add(new TransferRow
            {
                ProductId = b.ProductId ?? b.MaterialId,
                ProductName = b.ProductName ?? b.MaterialName ?? "—",
                LotId = b.LotId,
                LotCode = b.LotCode ?? "—",
                CustomerId = b.CustomerId,
                PackagingTypeId = b.PackagingTypeId,
                AvailableQty = b.QtyKg,
                QtyKg = b.QtyKg,
                PackageCount = b.PackageCount
            });
        }
        EnsureEmptyRow();
    }

    private void Save()
    {
        try
        {
            if (_locked) { AppContainer.Get<DialogService>().Error("السند مقفل (معتمد)."); return; }
            if (SourceWhBox.SelectedValue is not int srcId) { AppContainer.Get<DialogService>().Error("اختر المخزن المصدر."); return; }
            if (DestWhBox.SelectedValue is not int dstId) { AppContainer.Get<DialogService>().Error("اختر المخزن المستلم."); return; }
            if (srcId == dstId) { AppContainer.Get<DialogService>().Error("المصدر والوجهة لا يمكن أن يكونا نفس المخزن."); return; }
            var valid = _items.Where(r => !r.IsEmpty && r.QtyKg > 0).ToList();
            if (valid.Count == 0) { AppContainer.Get<DialogService>().Error("أدخل بنداً واحداً على الأقل."); return; }

            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var dto = valid.Select(i => new WarehouseTransferItemDto
            {
                ProductId = i.ProductId,
                LotId = i.LotId,
                CustomerId = i.CustomerId,
                PackagingTypeId = i.PackagingTypeId,
                QtyKg = i.QtyKg,
                PackageCount = i.PackageCount,
                Notes = i.Notes
            }).ToList();

            OpResult r = _currentId > 0 && !_locked
                ? null // تحديث غير مدعوم حالياً — نحذف وننشئ
                : svc.SaveTransfer((DateBox.SelectedDate ?? DateTime.Now).ToString("dd/MM/yyyy"), srcId, dstId, dto, NotesBox.Text);

            // إذا كان تعديل مسودة: احذف القديم ثم أنشئ جديد
            if (_currentId > 0 && !_locked)
            {
                var del = svc.DeleteDraft(_currentId);
                r = svc.SaveTransfer((DateBox.SelectedDate ?? DateTime.Now).ToString("dd/MM/yyyy"), srcId, dstId, dto, NotesBox.Text);
            }

            if (r == null || !r.Ok) { AppContainer.Get<DialogService>().Error(r?.Message ?? "فشل الحفظ."); return; }
            _currentId = r.Id;
            DocNoBox.Text = r.DocumentNumber;
            AppContainer.Get<DialogService>().Info(r.Message + "\nالسند باقٍ أمامك — اعتمده لتنفيذ النقل الفعلي بين المخزنين.");
            RefreshList();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Save"); }
    }

    private void Approve()
    {
        try
        {
            if (_currentId == 0) { AppContainer.Get<DialogService>().Error("احفظ السند أولاً."); return; }
            if (!AppContainer.Get<DialogService>().Confirm("الاعتماد سينقل الكميات فعلياً من المخزن المصدر إلى المستلم. متابعة؟")) return;
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var r = svc.ApproveTransfer(_currentId);
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message);
            SetLocked(true);
            RefreshList();
            SourceWh_Changed(null, null);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Approve"); }
    }

    private void Unapprove()
    {
        try
        {
            if (_currentId == 0) return;
            if (!AppContainer.Get<DialogService>().Confirm("إلغاء التحويل سيعيد الكميات للمصدر. متابعة؟")) return;
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var r = svc.CancelTransfer(_currentId, "إلغاء من الشاشة");
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message);
            SetLocked(false);
            RefreshList();
            SourceWh_Changed(null, null);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Unapprove"); }
    }

    private void DeleteDraft()
    {
        if (_currentId == 0) { AppContainer.Get<DialogService>().Error("لا يوجد سند محفوظ."); return; }
        if (_locked) { AppContainer.Get<DialogService>().Error("السند معتمد — ألغِ الاعتماد أولاً."); return; }
        if (!AppContainer.Get<DialogService>().Confirm($"حذف التحويل {DocNoBox.Text}؟")) return;
        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var r = svc.DeleteDraft(_currentId);
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message);
            NewForm();
            RefreshList();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Delete"); }
    }

    private void NewForm()
    {
        _currentId = 0;
        _items.Clear();
        try
        {
            using var s = AppContainer.NewScope();
            var num = s.ServiceProvider.GetRequiredService<INumberingService>().Peek("WHT");
            DocNoBox.Text = num;
        }
        catch { DocNoBox.Text = "(تلقائي عند الحفظ)"; }
        DateBox.SelectedDate = DateTime.Now;
        NotesBox.Text = "";
        SetLocked(false);
        AddEmptyRow();
    }

    private void AddEmptyRow() => _items.Add(new TransferRow());
    private void EnsureEmptyRow()
    {
        if (_locked) return;
        if (_items.Count == 0 || !_items.Last().IsEmpty) AddEmptyRow();
    }
    private void AddEmptyRow_Click(object sender, RoutedEventArgs e) { if (!_locked) AddEmptyRow(); }
    private void RemoveRow_Click(object sender, RoutedEventArgs e) { if (ItemsGrid.SelectedItem is TransferRow r) { _items.Remove(r); EnsureEmptyRow(); } }
    private void RemoveItem_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is TransferRow r) { _items.Remove(r); EnsureEmptyRow(); } }

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        if (sender is Button btn && btn.Tag is TransferRow row)
        {
            // فتح نافذة اختيار صنف من الرصيد
            var picker = new Views.FinishedBatchPickerWindow(null) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true && picker.SelectedBatch != null)
            {
                row.ProductId = picker.SelectedBatch.ProductId;
                row.ProductName = picker.SelectedBatch.ProductName;
                row.LotId = picker.SelectedBatch.LotId;
                row.LotCode = picker.SelectedBatch.LotCode;
                row.AvailableQty = picker.SelectedBatch.Qty;
                row.QtyKg = picker.SelectedBatch.Qty;
                row.PackageCount = picker.SelectedBatch.Packages;
                EnsureEmptyRow();
                ItemsGrid.Items.Refresh();
            }
        }
    }

    private void SetLocked(bool locked)
    {
        _locked = locked;
        if (_toolbar != null)
        {
            if (_toolbar.SaveBtn != null) _toolbar.SaveBtn.IsEnabled = !locked;
            if (_toolbar.ApproveBtn != null) _toolbar.ApproveBtn.IsEnabled = !locked;
            if (_toolbar.UnapproveBtn != null) _toolbar.UnapproveBtn.IsEnabled = locked;
        }
    }

    private void Nav(int dir)
    {
        if (_transferIds.Count == 0) return;
        int idx = _transferIds.IndexOf(_currentId);
        idx = dir switch { 0 => 0, int.MaxValue => _transferIds.Count - 1, _ => Math.Clamp(idx + dir, 0, _transferIds.Count - 1) };
        OpenTransfer(_transferIds[idx]);
    }

    private void OpenTransfer(int id)
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWarehouseTransferService>();
            var t = svc.GetTransfer(id);
            if (t == null) return;
            _currentId = t.Id;
            DocNoBox.Text = t.DocumentNumber;
            DateBox.SelectedDate = DateTime.TryParse(t.TransferDate, out var d) ? d : DateTime.Now;
            SourceWhBox.SelectedValue = t.SourceWarehouseId;
            DestWhBox.SelectedValue = t.DestinationWarehouseId;
            NotesBox.Text = t.Notes;
            _items.Clear();
            foreach (var it in t.Items)
            {
                _items.Add(new TransferRow
                {
                    ProductId = it.ProductId,
                    ProductName = it.ProductId != null ? scope.ServiceProvider.GetRequiredService<DatesErpDbContext>().Products.Where(p => p.Id == it.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"#{it.ProductId}" : "—",
                    LotId = it.LotId,
                    LotCode = it.LotId != null ? scope.ServiceProvider.GetRequiredService<DatesErpDbContext>().Lots.Where(l => l.Id == it.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "—" : "—",
                    CustomerId = it.CustomerId,
                    PackagingTypeId = it.PackagingTypeId,
                    QtyKg = it.QtyKg,
                    PackageCount = it.PackageCount,
                    Notes = it.Notes
                });
            }
            SetLocked(t.Status == "Approved");
            if (t.Status != "Approved") EnsureEmptyRow();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Open"); }
    }

    private void Print()
    {
        if (_currentId <= 0) { AppContainer.Get<DialogService>().Error("احفظ السند أولاً."); return; }
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var t = db.WarehouseTransfers.Include(x => x.Items).FirstOrDefault(x => x.Id == _currentId);
            if (t == null) return;
            var m = Printing.StoredPrintModels.WarehouseTransfer(db, t.Id);
            new PrintPreviewWindow(PhasePrint.Build(m), $"{m.DocTitle} {m.DocNo}") { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "WTransfer.Print"); }
    }

    private void Export()
    {
        var report = new ReportResult
        {
            TitleAr = $"تحويل مخزني {DocNoBox.Text}",
            Columns = new List<string> { "الصنف", "الدفعة", "الكمية (كجم)", "العبوات" },
            Rows = _items.Select(i => new object[] { i.ProductName, i.LotCode, i.QtyKg, i.PackageCount }).ToList()
        };
        AppContainer.Get<ExportPrintService>().ExportExcel(report);
    }

    private void TransfersGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TransfersGrid.SelectedItem?.GetType().GetProperty("Id")?.GetValue(TransfersGrid.SelectedItem) is int id)
            OpenTransfer(id);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ScreenSearch.Apply(SearchBox, TransfersGrid, _transfers_all);
}
