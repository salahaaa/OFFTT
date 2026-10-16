using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Desktop.Views;

/// <summary>
/// نافذة اختيار دفعة خام للتخطيط — جميع الدفعات المتاحة معروضة مع فلاتر المخزن والمعالجة، والقرار يدوي.
/// </summary>
public partial class LotPickerWindow : Window
{
    private List<AvailableLotDto> _all = new();
    private List<int> _currentPlanLotIds = new();
    private int? _customerId;
    private int? _excludePlanId;
    private DateTime? _availabilityDate;
    private readonly HashSet<int> _allowedCustomerIds;
    private bool _filtersReady;
    public AvailableLotDto SelectedLot { get; private set; }

    private sealed class FilterOption
    {
        public int Id { get; init; }
        public string Name { get; init; }
    }

    public LotPickerWindow(List<int> currentPlanLotIds = null, int? customerId = null,
        int? excludePlanId = null, DateTime? availabilityDate = null,
        IEnumerable<int> allowedCustomerIds = null)
    {
        InitializeComponent();
        _currentPlanLotIds = currentPlanLotIds ?? new List<int>();
        _customerId = customerId;
        _excludePlanId = excludePlanId;
        _availabilityDate = availabilityDate;
        _allowedCustomerIds = allowedCustomerIds?.Where(id => id > 0).ToHashSet() ?? new HashSet<int>();
        Loaded += (_, _) =>
        {
            LoadLots(_customerId, _excludePlanId, _availabilityDate);
            LotsGrid.Focus();
            if (LotsGrid.Items.Count > 0) LotsGrid.SelectedIndex = 0;
        };
    }

    private void LoadLots(int? customerId, int? excludePlanId, DateTime? availabilityDate)
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPlanningService>();
            _all = svc.GetAvailableLots(customerId, availabilityDate, excludePlanId);
            if (customerId == null && _allowedCustomerIds.Count > 0)
                _all = _all.Where(l => l.CustomerId is int owner && _allowedCustomerIds.Contains(owner)).ToList();
            BuildFilters();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            AppContainer.Get<DialogService>().HandleException(ex, "LotPicker.Load");
        }
    }

    private void BuildFilters()
    {
        ProductFilterBox.ItemsSource = new[] { new FilterOption { Id = 0, Name = "كل الأصناف الخام" } }
            .Concat(_all.GroupBy(x => x.ProductId).Select(g => new FilterOption
            {
                Id = g.Key,
                Name = g.First().ProductName ?? $"#{g.Key}"
            }).OrderBy(x => x.Name)).ToList();
        ProductFilterBox.SelectedIndex = 0;
        WarehouseFilterBox.ItemsSource = new[] { new FilterOption { Id = 0, Name = "كل المخازن" } }
            .Concat(_all.Where(x => x.WarehouseId != null).GroupBy(x => x.WarehouseId!.Value).Select(g => new FilterOption
            {
                Id = g.Key,
                Name = g.First().WarehouseName ?? $"مخزن {g.Key}"
            }).OrderBy(x => x.Name)).ToList();
        WarehouseFilterBox.SelectedIndex = 0;
        TreatmentFilterBox.SelectedIndex = 0;
        _filtersReady = true;
    }

    private void ApplyFilter()
    {
        if (!_filtersReady) return;
        var term = SearchBox.Text?.Trim().ToLower() ?? "";
        int productId = (ProductFilterBox.SelectedItem as FilterOption)?.Id ?? 0;
        int warehouseId = (WarehouseFilterBox.SelectedItem as FilterOption)?.Id ?? 0;
        string treatment = (TreatmentFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var filtered = _all.Where(r =>
                (productId == 0 || r.ProductId == productId)
                && (warehouseId == 0 || r.WarehouseId == warehouseId)
                && (treatment == "all"
                    || (treatment == "under" && r.UnderTreatmentKg > 0.001)
                    || (treatment == "ready" && r.UnderTreatmentKg <= 0.001 && r.AvailableForDateKg > 0.001))
                && (string.IsNullOrWhiteSpace(term)
                    || (r.CustomerName != null && r.CustomerName.ToLower().Contains(term))
                    || (r.LotCode != null && r.LotCode.ToLower().Contains(term))
                    || (r.ProductName != null && r.ProductName.ToLower().Contains(term))
                    || (r.ShipmentNo != null && r.ShipmentNo.ToLower().Contains(term))))
            .ToList();

        // إثراء للعرض
        var display = filtered.Select(r => new
        {
            r.LotId,
            r.LotCode,
            r.CustomerName,
            r.ProductName,
            r.ShipmentNo,
            r.WarehouseId,
            r.WarehouseName,
            r.ArrivalDate,
            r.InitialQtyKg,
            r.ReservedQtyKg,
            r.RemainingKg,
            r.AvailableForDateKg,
            r.UnderTreatmentKg,
            r.TreatmentReadyDate,
            r.ReceiptUnit,
            TreatmentStatus = r.RequiresTreatment
                ? (r.UnderTreatmentKg > 0
                    ? $"تحت المعالجة — {r.UnderTreatmentKg:N1} كجم حتى {r.TreatmentReadyDate:dd/MM/yyyy}"
                    : "معالجة مكتملة ✓")
                : "لا يحتاج",
            DaysInStock = r.ArrivalDate != null ? (DateTime.Now.Date - r.ArrivalDate.Value.Date).Days : 0,
            Entity = r
        }).ToList();

        LotsGrid.ItemsSource = display;
        CountText.Text = $"النتائج: {display.Count} من {_all.Count} دفعة متاحة — اختر يدوياً حسب قرار التخطيط";

        FifoHint.Text = display.Count == 0
            ? "لا توجد دفعات تطابق الفلاتر الحالية."
            : "حدد الصف المناسب يدوياً ثم اضغط اختيار. لا يعتمد النظام FIFO أو توزيعاً تلقائياً كقرار نهائي.";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _filtersReady = false;
        LoadLots(_customerId, _excludePlanId, _availabilityDate);
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ApplyFilter();
        LotsGrid.Focus();
    }

    private void LotsGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LotsGrid.SelectedItem != null)
            TrySelectCurrent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TrySelectCurrent();

    private void TrySelectCurrent()
    {
        if (LotsGrid.SelectedItem == null)
        {
            MessageBox.Show("اختر دفعة من القائمة أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        dynamic sel = LotsGrid.SelectedItem;
        AvailableLotDto lot = sel.Entity as AvailableLotDto;
        if (lot == null) return;

        // اختيار الدفعة قرار يدوي لمدير التخطيط. لا نستخدم FIFO أو توزيعاً
        // تلقائياً كحارس نهائي، ونحتفظ فقط ببيانات الرصيد والمعالجة المعروضة.
        SelectedLot = lot;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
