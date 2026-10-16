using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DatesErp.Application.Services;
using DatesErp.Core.Domain.Entities;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Desktop.Views.Screens;

/// <summary>
/// شاشة احتياجات الخامات الفعلية. هذه ليست شاشة إدخال MPS؛ بل نقطة مراجعة واحدة
/// للرصيد والالتزامات والاحتياج المحسوب قبل فتح محرر الخطة.
/// </summary>
public partial class PlanningRequirementsView : UserControl
{
    private readonly ObservableCollection<RequirementRow> _rows = new();
    private bool _loading;

    public PlanningRequirementsView()
    {
        InitializeComponent();
        RequirementsGrid.ItemsSource = _rows;
        Loaded += (_, _) => LoadData();
    }

    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("planning");
        chrome.SetScreenCode("MRPMPS1001");
        chrome.SetPermissionModule("planning");
        var toolbar = new Views.ErpToolbar()
            .WithList((_, _) => LoadData())
            .WithCustom("إعداد خطة الإنتاج", "ErpPrimaryButton", OpenPlanEditor_Click);
        chrome.SetToolbar(toolbar);
        chrome.SetBody(this);
    }

    private void LoadData()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();

            var raw = db.Products.AsNoTracking().Where(p => p.IsActive && p.ItemType == "Raw")
                .OrderBy(p => p.ProductNameAr).ToList();
            var finished = db.Products.AsNoTracking().Where(p => p.IsActive && p.ItemType == "Finished")
                .OrderBy(p => p.ProductNameAr).ToList();
            var warehouses = db.Warehouses.AsNoTracking().Where(w => w.IsActive && (w.WarehouseType == "Raw" || w.WarehouseType == null))
                .OrderBy(w => w.WarehouseNameAr).ToList();
            var lotOptions = db.Lots.AsNoTracking().Where(l => l.InStockQtyKg > 0)
                .OrderByDescending(l => l.LotDate).ThenBy(l => l.LotCode).ToList();

            if (FinishedProductBox.Items.Count == 0)
            {
                FinishedProductBox.ItemsSource = new[] { new ProductOption { Id = 0, ProductNameAr = "كل المنتجات النهائية" } }
                    .Concat(finished.Select(p => new ProductOption { Id = p.Id, ProductNameAr = p.ProductNameAr })).ToList();
                FinishedProductBox.SelectedIndex = 0;
                WarehouseBox.ItemsSource = new[] { new NamedOption { Id = 0, Name = "كل المستودعات" } }
                    .Concat(warehouses.Select(w => new NamedOption { Id = w.Id, Name = w.WarehouseNameAr })).ToList();
                WarehouseBox.SelectedIndex = 0;
                LotBox.ItemsSource = new[] { new NamedOption { Id = 0, Name = "كل الدفعات" } }
                    .Concat(lotOptions.Select(l => new NamedOption { Id = l.Id, Name = l.LotCode })).ToList();
                LotBox.SelectedIndex = 0;
                FromDate.SelectedDate ??= DateTime.Today.AddDays(-30);
                ToDate.SelectedDate ??= DateTime.Today.AddDays(30);
            }

            var selectedFinishedId = (FinishedProductBox.SelectedItem as ProductOption)?.Id ?? 0;
            var selectedWarehouseId = (WarehouseBox.SelectedItem as NamedOption)?.Id ?? 0;
            var selectedLotId = (LotBox.SelectedItem as NamedOption)?.Id ?? 0;
            var from = FromDate.SelectedDate?.Date;
            var to = ToDate.SelectedDate?.Date.AddDays(1).AddTicks(-1);

            var stock = db.StockBalances.AsNoTracking()
                .Where(s => s.ProductId != null && s.QtyKg > 0
                    && (selectedWarehouseId == 0 || s.WarehouseId == selectedWarehouseId)
                    && (selectedLotId == 0 || s.LotId == selectedLotId))
                .ToList();
            var lots = db.Lots.AsNoTracking().Where(l => l.InStockQtyKg > 0
                && (selectedLotId == 0 || l.Id == selectedLotId)).ToList();
            var planItems = db.ProductionPlanItems.AsNoTracking()
                .Where(i => i.Status != "Cancelled" && !i.IsClosed
                    && (from == null || i.ScheduledDate == null || i.ScheduledDate >= from)
                    && (to == null || i.ScheduledDate == null || i.ScheduledDate <= to))
                .ToList();
            if (selectedFinishedId > 0) planItems = planItems.Where(i => i.ProductId == selectedFinishedId).ToList();
            if (selectedLotId > 0) planItems = planItems.Where(i => i.LotId == selectedLotId).ToList();

            var planIds = planItems.Select(i => i.Id).ToHashSet();
            var allocations = db.ProductionPlanItemAllocations.AsNoTracking()
                .Where(a => a.AllocatedQtyKg > 0 && a.Status != "Released" && a.Status != "Consumed")
                .Where(a => planIds.Contains(a.PlanItemId)).ToList();
            var productById = raw.ToDictionary(p => p.Id);
            var needByRaw = CalculateNeeds(db, planItems, productById);
            // الالتزام التشغيلي الآخر = أوامر مستقلة لم تُنشأ من بند خطة.
            // أوامر الخطة لا تُجمع هنا مرة أخرى حتى لا يحدث ازدواج بين الحجز والتشغيل.
            var independentOrderItems = db.ProductionOrderItems.AsNoTracking()
                .Where(i => i.PlanItemId == null && !i.IsClosed && i.Status != "Cancelled")
                .ToList();
            var otherByRaw = CalculateOrderNeeds(db, independentOrderItems, productById);
            var reservedByRaw = allocations.GroupBy(a => lots.FirstOrDefault(l => l.Id == a.LotId)?.ProductId ?? 0)
                .Where(g => g.Key > 0).ToDictionary(g => g.Key, g => g.Sum(x => x.AllocatedQtyKg));
            var actualByRaw = stock.GroupBy(s => s.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyKg));
            // قاعدة قديمة بلا StockBalance: لا نخفي الرصيد المعروف في الدفعات.
            foreach (var group in lots.GroupBy(l => l.ProductId))
                if (selectedWarehouseId == 0 && !actualByRaw.ContainsKey(group.Key))
                    actualByRaw[group.Key] = group.Sum(l => l.InStockQtyKg);
            var underByRaw = lots.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0, l.UnderTreatmentQtyKg)));

            _rows.Clear();
            foreach (var p in raw)
            {
                actualByRaw.TryGetValue(p.Id, out var actual);
                reservedByRaw.TryGetValue(p.Id, out var reserved);
                needByRaw.TryGetValue(p.Id, out var required);
                otherByRaw.TryGetValue(p.Id, out var other);
                underByRaw.TryGetValue(p.Id, out var under);
                var available = Math.Max(0, actual - reserved - under - other);
                var remaining = available - required;
                _rows.Add(new RequirementRow
                {
                    RawId = p.Id, RawName = p.ProductNameAr, Actual = actual, Reserved = reserved,
                    Other = other, UnderTreatment = under, Available = available, Required = required,
                    Remaining = remaining, IsShort = remaining < -0.001
                });
            }
            SummaryText.Text = $"{_rows.Count} خامة · المتاح { _rows.Sum(r => r.Available):N0} كجم · الاحتياج المحسوب {_rows.Sum(r => r.Required):N0} كجم"
                + (_rows.Any(r => r.IsShort) ? " · يوجد عجز يحتاج قراراً يدوياً من مدير التخطيط" : " · الأرصدة قابلة للتخصيص");
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Planning.Requirements.Load");
            SummaryText.Text = "تعذر تحديث الأرصدة؛ راجع سجل الأخطاء ثم أعد المحاولة.";
        }
        finally { _loading = false; }
    }

    private static Dictionary<int, double> CalculateOrderNeeds(DatesErpDbContext db, List<ProductionOrderItem> items,
        Dictionary<int, Product> rawProducts)
    {
        var result = new Dictionary<int, double>();
        var lotProduct = db.Lots.AsNoTracking().ToDictionary(l => l.Id, l => l.ProductId);
        foreach (var item in items)
        {
            var selectedRaw = item.LotId is int lotId && lotProduct.TryGetValue(lotId, out var lotRaw) ? lotRaw : (int?)null;
            var remainingKg = Math.Max(0, item.PlannedQtyKg - item.ProducedQtyKg);
            var calculation = RawRequirementEngine.Calculate(db, item.ProductId, remainingKg, selectedRaw, item.PackagingTypeId);
            if (!calculation.IsConfigured || calculation.RawProductId is not int rawId || !rawProducts.ContainsKey(rawId)) continue;
            result[rawId] = result.GetValueOrDefault(rawId) + (double)calculation.RawRequiredQtyKg;
        }
        return result;
    }

    private static Dictionary<int, double> CalculateNeeds(DatesErpDbContext db, List<ProductionPlanItem> items,
        Dictionary<int, Product> rawProducts)
    {
        var result = new Dictionary<int, double>();
        var lotProduct = db.Lots.AsNoTracking().ToDictionary(l => l.Id, l => l.ProductId);
        foreach (var item in items)
        {
            var selectedRaw = item.SelectedRawProductId;
            if (selectedRaw == null && item.LotId is int lotId && lotProduct.TryGetValue(lotId, out var lotRaw)) selectedRaw = lotRaw;
            var calculation = RawRequirementEngine.Calculate(db, item.ProductId, Math.Max(0, item.PlannedQtyKg - item.ProducedQtyKg), selectedRaw, item.PackagingTypeId);
            if (!calculation.IsConfigured || calculation.RawProductId is not int rawId || !rawProducts.ContainsKey(rawId)) continue;
            result[rawId] = result.GetValueOrDefault(rawId) + (double)calculation.RawRequiredQtyKg;
        }
        return result;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) LoadData(); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) LoadData(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

    private void OpenPlanEditor_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenScreen("planning-editor");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"DateERP_احتياجات_الخامات_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            var csv = new StringBuilder();
            csv.AppendLine("الخامة,المخزون الفعلي,مخصص للخطط,التزامات أخرى,تحت المعالجة,المتاح للتخطيط,الاحتياج,المتبقي,الحالة");
            foreach (var r in _rows)
                csv.AppendLine(string.Join(",", Csv(r.RawName), r.Actual, r.Reserved, r.Other, r.UnderTreatment,
                    r.Available, r.Required, r.Remaining, Csv(r.Status)));
            File.WriteAllText(file, csv.ToString(), new UTF8Encoding(true));
            new DialogService().Toast($"تم تصدير جدول الاحتياجات إلى: {file}");
        }
        catch (Exception ex) { new DialogService().HandleException(ex, "Planning.Requirements.Export"); }
    }

    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private sealed class ProductOption
    {
        public int Id { get; init; }
        public string ProductNameAr { get; init; }
    }
    private sealed class NamedOption
    {
        public int Id { get; init; }
        public string Name { get; init; }
    }
    private sealed class RequirementRow
    {
        public int RawId { get; init; }
        public string RawName { get; init; }
        public double Actual { get; init; }
        public double Reserved { get; init; }
        public double Other { get; init; }
        public double UnderTreatment { get; init; }
        public double Available { get; init; }
        public double Required { get; init; }
        public double Remaining { get; init; }
        public bool IsShort { get; init; }
        public string Status => IsShort ? "عجز" : "كافٍ";
        public Brush StatusBackground => IsShort ? new SolidColorBrush(Color.FromRgb(254, 226, 226)) : new SolidColorBrush(Color.FromRgb(220, 252, 231));
        public Brush StatusForeground => IsShort ? new SolidColorBrush(Color.FromRgb(185, 28, 28)) : new SolidColorBrush(Color.FromRgb(21, 128, 61));
    }
}
