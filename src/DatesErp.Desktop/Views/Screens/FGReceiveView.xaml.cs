using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Desktop.Views.Screens;

public class FrLineUi : INotifyPropertyChanged
{
    public int ItemId { get; set; }         // معرف بند السند بعد الحفظ
    public int? DeliveryItemId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public int? PackagingTypeId { get; set; }
    public string PackagingName { get; set; }
    public string ProductName { get; set; }
    public string LotCode { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; }
    public double Remaining { get; set; }
    /// <summary>إجمالي كراتين بند أمر التسليم، للعرض والتدقيق.</summary>
    public int Packages { get; set; }
    /// <summary>كراتين هذا السند فعلياً؛ يطابق الوزن ولا يُقرَّب تراكمياً على نحو مستقل.</summary>
    public int ReceiptPackages { get; set; }
    private bool _included = true;
    public bool Included { get => _included; set { _included = value; OnChanged(nameof(Included)); } }
    private double _qty;
    public double Qty { get => _qty; set { _qty = value; OnChanged(nameof(Qty)); } }
    private double _receiveNow;
    public double ReceiveNow { get => _receiveNow; set { _receiveNow = value; OnChanged(nameof(ReceiveNow)); } }
    private double _received;
    public double Received { get => _received; set { _received = value; OnChanged(nameof(Received)); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// §B96 — أوامر استلام الإنتاج (إدارة المخازن):
/// سند من أمر تسليم إنتاج محرر (العميل من بند التسليم) ← إصدار ← استلام.
/// </summary>
public partial class FGReceiveView : UserControl
{
    private readonly ObservableCollection<FrLineUi> _lines = new();
    private List<object> _records_all = new();
    private List<int> _recordIds = new();
    private List<(int Id, string Label)> _picks = new();
    private List<(int OrderId, int QcId)> _eligible = new();
    private int _currentId, _currentDeliveryId, _currentOrderId;

    public FGReceiveView()
    {
        InitializeComponent();
        ItemsGrid.ItemsSource = _lines;
        Loaded += (_, _) => Load();
    }

    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("أوامر استلام الإنتاج");
        chrome.SetScreenCode("MRPINV1006");
        var tb = new Views.ErpToolbar()
            .WithNew((_, _) => NewForm(), "سند استلام جديد (F2)")
            .WithSave((_, _) => Save(), "حفظ السند كمسودة (F10)")
            .WithCustom("📤 إصدار السند", "ErpButton", (_, _) => Issue(), "إصدار السند للتنفيذ — لا يمس الأرصدة")
            .WithPrint((_, _) => Print(), "طباعة سند الاستلام المحفوظ (Ctrl+P)")
            .WithSearch((_, _) => { RefreshList(); RecSearchBox.Focus(); }, "بحث في سندات الاستلام (F9)")
            .WithUndo((_, _) => UndoSmart(), "تراجع: يعيد آخر نسخة محفوظة — لا يحذف أي سند")
            .WithNavigation((_, _) => Nav(0), (_, _) => Nav(-1), (_, _) => Nav(1), (_, _) => Nav(int.MaxValue))
            .WithList((_, _) => RefreshList(), "عرض كل سندات الاستلام")
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard"));
        chrome.SetToolbar(tb);
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }

    private void Load()
    {
        try
        {
            ModeBox.ItemsSource = new List<string> { "من أمر تسليم إنتاج محرر" };
            ModeBox.SelectedIndex = 0;
            DateBox.SelectedDate = DateTime.Now;
            NewForm();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Load"); }
    }

    private bool FromDelivery() => ModeBox.SelectedIndex == 0;

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            PickLabel.Text = "أمر تسليم الإنتاج المحرر:";
            _picks.Clear();
            _eligible.Clear();
            using var scope = AppContainer.NewScope();
            if (FromDelivery())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IProductionDeliveryService>();
                foreach (var c in svc.GetDeliveries("Issued")
                    .Where(c => c.SourceType == DeliverySources.FromActual && c.Lines.Any(l => l.RemainingQtyKg > 0.001)))
                    _picks.Add((c.Id, $"{c.DocumentNumber} — {c.SourceTypeAr} {c.SourceNumber} — متبقي {c.Lines.Sum(l => l.RemainingQtyKg):N1} كجم"));
            }
            PickBox.ItemsSource = _picks.Select(p => p.Label).ToList();
            PickBox.SelectedIndex = _picks.Count > 0 ? 0 : -1;
            _lines.Clear();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Mode"); }
    }

    private void Pick_Changed(object sender, SelectionChangedEventArgs e) => Download();

    private void Download_Click(object sender, RoutedEventArgs e) => Download();

    private void Download()
    {
        try
        {
            if (_currentId > 0) return;
            if (PickBox.SelectedIndex < 0 || PickBox.SelectedIndex >= _picks.Count) return;
            int id = _picks[PickBox.SelectedIndex].Id;
            _lines.Clear();
            using var scope = AppContainer.NewScope();
            if (FromDelivery())
            {
                _currentDeliveryId = id;
                var svc = scope.ServiceProvider.GetRequiredService<IProductionDeliveryService>();
                var card = svc.GetDelivery(id);
                if (card == null) return;
                _currentOrderId = card.Lines.FirstOrDefault(l => l.OrderId != null)?.OrderId ?? 0;
                foreach (var l in card.Lines.Where(l => l.RemainingQtyKg > 0.001))
                    _lines.Add(new FrLineUi
                    {
                        DeliveryItemId = l.Id,
                        ProductId = l.ProductId,
                        LotId = l.LotId,
                        PackagingTypeId = l.PackagingTypeId,
                        PackagingName = l.PackagingName ?? "غير محددة",
                        ProductName = l.ProductName,
                        LotCode = l.LotCode ?? "—",
                        CustomerId = l.CustomerId,
                        CustomerName = l.CustomerName ?? "—",
                        Remaining = Math.Round(l.RemainingQtyKg, 1),
                        Packages = l.PackageCount,
                        ReceiptPackages = l.PackageCount,
                        Qty = Math.Round(l.RemainingQtyKg, 1)
                    });
            }
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Download"); }
    }

    private void Save()
    {
        try
        {
            if (_currentId > 0) { AppContainer.Get<DialogService>().Error("السند محفوظ — السندات المحفوظة تُصدَر وتُستلَم (لا تعديل بعد الحفظ)."); return; }
            var invalid = _lines.Where(l => l.Included && (l.Qty < 0 || l.ReceiptPackages < 0 || l.ReceiveNow < 0)).ToList();
            if (invalid.Count > 0)
            {
                AppContainer.Get<DialogService>().Error("لا يمكن حفظ سند الاستلام — الكمية أو عدد الكراتين لا يمكن أن يكون سالباً.");
                return;
            }
            var selected = _lines.Where(l => l.Included && l.Qty > 0.001).ToList();
            if (selected.Count == 0) { AppContainer.Get<DialogService>().Error("ضمّن بنداً واحداً على الأقل بكمية أكبر من صفر."); return; }
            if (_currentOrderId == 0) { AppContainer.Get<DialogService>().Error("اختر المصدر أولاً."); return; }

            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IFinishedGoodsService>();
            int? qc = null;
            // المصدر الوحيد هنا أمر تسليم إنتاج محرر؛ لا يوجد مسار مباشر من أمر الإنتاج.
            // العبوة والكراتين جزء من هوية السطر، وكراتين السند الفعلية تُدخل صراحة
            // حتى لا يؤدي التقريب المستقل لكل استلام إلى انحراف تراكمي.
            var r = svc.SaveReceipt(_currentOrderId, qc,
                (DateBox.SelectedDate ?? DateTime.Now).ToString("dd/MM/yyyy"),
                selected.Select(l => new FinishedGoodsItemDto
                {
                    ProductId = l.ProductId,
                    LotId = l.LotId,
                    PackagingTypeId = l.PackagingTypeId,
                    PackageCount = l.ReceiptPackages,
                    NetWeightKg = l.Qty,
                    CustomerId = l.CustomerId,
                    DeliveryItemId = l.DeliveryItemId
                }).ToList(),
                _currentDeliveryId);
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message);
            RefreshList();
            OpenReceipt(r.Id);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Save"); }
    }

    private void Issue()
    {
        try
        {
            if (_currentId == 0) { AppContainer.Get<DialogService>().Error("احفظ السند أولاً ثم أصدره."); return; }
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IFinishedGoodsService>();
            var r = svc.Issue(_currentId);
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message + "\n(الإصدار لم يؤثر على الأرصدة — نفّذ الاستلام)");
            RefreshList();
            OpenReceipt(_currentId);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Issue"); }
    }

    private void Receive_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentId == 0) { AppContainer.Get<DialogService>().Error("افتح سند استلام مُصدراً."); return; }
            // الخريطة الجزئية صريحة: السطر الغائب = صفر، وليس «استلم المتبقي كله».
            // الاستلام الكامل عبر API القديم (null) لا يُنشأ من هذه الشاشة ولا يُستخدم
            // إلا بأمر صريح من المستدعي المتوافق مع المسار القديم.
            if (_lines.Any(l => l.ItemId > 0 && (l.ReceiveNow < 0 || l.ReceiptPackages < 0)))
            {
                AppContainer.Get<DialogService>().Error("لا يمكن تنفيذ الاستلام — الكمية أو عدد الكراتين لا يمكن أن يكون سالباً.");
                return;
            }
            var entered = _lines.Where(l => l.ItemId > 0 && l.ReceiveNow > 0.001).ToList();
            var map = entered.ToDictionary(l => l.ItemId, l => l.ReceiveNow);

            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IFinishedGoodsService>();
            var r = svc.Receive(_currentId, map);
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message);
            foreach (var l in _lines) l.ReceiveNow = 0;
            RefreshList();
            OpenReceipt(_currentId);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Receive"); }
    }

    private void Unapprove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentId == 0) { AppContainer.Get<DialogService>().Error("افتح سنداً أولاً."); return; }
            if (!AppContainer.Get<DialogService>().Confirm("سيُلغى سند الاستلام ويُعكس أثره المخزني إن وُجد. لا يمكن التراجع عن هذا الإجراء من نفس الشاشة. متابعة؟")) return;
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IFinishedGoodsService>();
            var r = svc.Unapprove(_currentId);
            if (!r.Ok) { AppContainer.Get<DialogService>().Error(r.Message); return; }
            AppContainer.Get<DialogService>().Info(r.Message);
            RefreshList();
            OpenReceipt(_currentId);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Unapprove"); }
    }

    private void RefreshList()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var receipts = db.FinishedGoodsReceipts.OrderByDescending(r => r.Id).Take(300).ToList();
            _recordIds = receipts.Select(r => r.Id).ToList();
            _records_all = receipts.Select(r => new
            {
                Id = r.Id,
                DocNo = r.DocumentNumber,
                ReceiptNo = r.ReceiptNumber ?? "—",
                OrderNo = db.ProductionOrders.Where(o => o.Id == r.OrderId).Select(o => o.DocumentNumber).FirstOrDefault(),
                DeliveryNo = r.DeliveryId != null
                    ? db.ProductionDeliveries.Where(d => d.Id == r.DeliveryId.Value).Select(d => d.DocumentNumber).FirstOrDefault() ?? "—"
                    : "تاريخي — بلا أمر تسليم",
                Total = db.FinishedGoodsReceiptItems.Where(i => i.ReceiptId == r.Id).Sum(i => i.NetWeightKg).ToString("N1"),
                StatusAr = Core.Common.DocStatuses.ToArabic(r.Status),
                ReceiptAr = r.ReceiptStatus == "Full" ? "مستلم بالكامل ✅" : r.ReceiptStatus == "Partial" ? "استلام جزئي 🟠" : "بانتظار الاستلام ⏳"
            }).ToList().Cast<object>().ToList();
            ScreenSearch.Apply(RecSearchBox, RecordsGrid, _records_all);
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.List"); }
    }

    private void Print()
    {
        if (_currentId <= 0)
        {
            AppContainer.Get<DialogService>().Error("احفظ سند الاستلام أولاً؛ الطباعة تستخدم النسخة المحفوظة فقط.");
            return;
        }
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var model = Printing.StoredPrintModels.FinishedReceipt(db, _currentId);
            new PrintPreviewWindow(PhasePrint.Build(model), $"{model.DocTitle} {model.DocNo}")
            { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FGReceive.Print"); }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void Records_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecordsGrid.SelectedItem == null) return;
        int id = (int)RecordsGrid.SelectedItem.GetType().GetProperty("Id").GetValue(RecordsGrid.SelectedItem);
        OpenReceipt(id);
    }

    private void OpenReceipt(int id)
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var r = db.FinishedGoodsReceipts.Include(x => x.Items).FirstOrDefault(x => x.Id == id);
            if (r == null) return;
            _currentId = r.Id;
            _currentOrderId = r.OrderId;
            _currentDeliveryId = r.DeliveryId ?? 0;
            DocNoBox.Text = r.DocumentNumber;
            _lines.Clear();
            foreach (var i in r.Items)
                _lines.Add(new FrLineUi
                {
                    ItemId = i.Id,
                    DeliveryItemId = i.DeliveryItemId,
                    ProductId = i.ProductId,
                    LotId = i.LotId,
                    PackagingTypeId = i.PackagingTypeId,
                    PackagingName = i.PackagingTypeId != null
                        ? db.PackagingTypes.Where(p => p.Id == i.PackagingTypeId.Value).Select(p => p.PackageNameAr).FirstOrDefault() ?? "غير محددة"
                        : "غير محددة",
                    ProductName = db.Products.Where(p => p.Id == i.ProductId).Select(p => p.ProductNameAr).FirstOrDefault() ?? "-",
                    LotCode = db.Lots.Where(l => l.Id == i.LotId).Select(l => l.LotCode).FirstOrDefault() ?? "—",
                    CustomerId = i.CustomerId,
                    CustomerName = db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.CustomerName).FirstOrDefault() ?? "—",
                    Remaining = Math.Max(0, Math.Round(i.NetWeightKg - i.ReceivedQtyKg, 1)),
                    Packages = i.PackageCount,
                    ReceiptPackages = i.PackageCount,
                    Included = false,
                    Qty = i.NetWeightKg,
                    Received = i.ReceivedQtyKg
                });
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "FR.Open"); }
    }

    private void NewForm()
    {
        _currentId = 0;
        _currentDeliveryId = 0;
        _currentOrderId = 0;
        try
        {
            using var _numScope = AppContainer.NewScope();
            var _num = _numScope.ServiceProvider.GetRequiredService<INumberingService>().Peek("FGR");
            DocNoBox.Text = _num;
        }
        catch { DocNoBox.Text = "(تلقائي عند الحفظ)"; }
        DateBox.SelectedDate = DateTime.Now;
        _lines.Clear();
        if (ModeBox.SelectedIndex < 0) ModeBox.SelectedIndex = 0;
        else Mode_Changed(null, null);
    }

    private void UndoSmart()
    {
        if (_currentId > 0) OpenReceipt(_currentId);
        else NewForm();
    }

    private void Nav(int move)
    {
        if (_recordIds.Count == 0) RefreshList();
        if (_recordIds.Count == 0) return;
        int i = _recordIds.IndexOf(_currentId);
        int n = move == 0 ? 0 : move == int.MaxValue ? _recordIds.Count - 1 : Math.Min(_recordIds.Count - 1, Math.Max(0, i + move));
        OpenReceipt(_recordIds[n]);
    }
}
