using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Desktop.Views.Screens;

/// <summary>
/// شاشة مستقلة لإقفال يوم الإنتاج وتسجيل الفعلي قبل إنشاء/تحرير أمر التسليم.
/// لا تنشئ استلاماً مخزنياً ولا حركة مخزون.
/// </summary>
public partial class ProductionActualCloseView : UserControl
{
    public static int? PendingOrderId { get; set; }
    public ObservableCollection<ActualByProductDefinitionDto> ByProductDefinitions { get; } = new();

    private readonly ObservableCollection<ActualProductionRow> _items = new();
    private readonly ObservableCollection<ActualSecondaryRow> _secondary = new();
    private List<ActualByProductDefinitionDto> _activeDefinitions = new();
    private bool _saving;

    private static bool CanProduction(string action)
        => DatesErp.Desktop.Views.PermissionGate.Can("production", action);

    public ProductionActualCloseView()
    {
        InitializeComponent();
        ItemsGrid.ItemsSource = _items;
        SecondaryGrid.ItemsSource = _secondary;
        Loaded += (_, _) => LoadOrders(PendingOrderId);
    }

    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("إقفال الإنتاج وتسجيل الفعلي");
        chrome.SetScreenCode("MRPMPS1022");
        chrome.SetToolbar(new Views.ErpToolbar()
            .WithList((_, _) => LoadOrders((OrderBox.SelectedItem as ActualDeliveryOrderDto)?.OrderId), "تحديث أوامر الإنتاج")
            .WithSave((_, _) => Save_Click(null, null), "حفظ وإقفال الإنتاج")
            .WithPrint((_, _) => Print(), "طباعة ملخص التنفيذ المحفوظ")
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenPreviousScreen()));
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenPreviousScreen();
    }

    public static void OpenForOrder(int orderId, Window source)
    {
        PendingOrderId = orderId;
        var main = System.Windows.Application.Current.MainWindow as MainWindow;
        if (source != null && source != main) source.Close();
        main?.OpenScreen("actualclose");
    }

    private T WithService<T>(Func<IProductionDeliveryService, T> action)
    {
        using var scope = AppContainer.NewScope();
        return action(scope.ServiceProvider.GetRequiredService<IProductionDeliveryService>());
    }

    private void LoadOrders(int? selected)
    {
        try
        {
            _activeDefinitions = WithService(s => s.GetActualByProducts());
            ByProductDefinitions.Clear();
            foreach (var definition in _activeDefinitions) ByProductDefinitions.Add(definition);

            var orders = WithService(s => s.GetActualDeliveryOrders())
                .Where(o => o.CanRecord || o.Recorded || o.OrderId == selected)
                .ToList();
            OrderBox.ItemsSource = orders;
            OrderBox.SelectedItem = orders.FirstOrDefault(o => o.OrderId == selected)
                ?? orders.FirstOrDefault(o => o.CanRecord)
                ?? orders.FirstOrDefault();
            PendingOrderId = null;
            if (orders.Count == 0)
                StatusLabel.Text = "لا توجد أوامر معتمدة قابلة لتسجيل الفعلي. افتح الأمر من شاشة أوامر الإنتاج بعد بدء التنفيذ.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "تعذر تحميل أوامر الإنتاج: " + ex.Message;
            AppContainer.Get<DialogService>().HandleException(ex, "ActualClose.Load");
        }
    }

    private void Order_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsGrid == null) return;
        var order = OrderBox.SelectedItem as ActualDeliveryOrderDto;
        _items.Clear();
        _secondary.Clear();
        RawBox.Text = "";
        DowntimeBox.Text = "0";
        ReasonBox.Text = "";
        NotesBox.Text = "";
        ByProductDefinitions.Clear();
        foreach (var definition in _activeDefinitions) ByProductDefinitions.Add(definition);

        bool editable = order?.CanRecord == true && CanProduction("Create") && PermissionGate.Can("execution", "Edit");
        ItemsGrid.IsReadOnly = !editable;
        MeasurementsPanel.IsEnabled = editable;
        ByProductsPanel.IsEnabled = editable;

        CustChip.Text = $"العملاء: {order?.Customer ?? "—"}";
        PlanChip.Text = $"الخطة: {order?.PlanNumber ?? "—"}";
        ShiftChip.Text = $"الوردية: {order?.Shift ?? "—"}";
        DateChip.Text = $"تاريخ الإنتاج: {GetProductionDate(order) ?? "—"}";
        StatusLabel.Text = order?.Status ?? "اختر أمراً من الأعلى.";

        if (order == null) return;
        foreach (var line in order.Items) _items.Add(new ActualProductionRow(line, order.Recorded));

        if (order.Recorded)
        {
            RawBox.Text = order.ConsumedRawKg.ToString(CultureInfo.CurrentCulture);
            DowntimeBox.Text = order.DowntimeHours.ToString(CultureInfo.CurrentCulture);
            ReasonBox.Text = order.DowntimeReason ?? "";
            NotesBox.Text = order.Notes ?? "";
            foreach (var byProduct in order.ByProducts)
            {
                _secondary.Add(new ActualSecondaryRow
                {
                    Definition = ByProductDefinitions.FirstOrDefault(x => x.Id == byProduct.ByProductId)
                        ?? new ActualByProductDefinitionDto { Id = byProduct.ByProductId, Name = $"مخرج محفوظ #{byProduct.ByProductId}", Unit = "راجع التعريف" },
                    Quantity = byProduct.QtyKg.ToString(CultureInfo.CurrentCulture)
                });
            }
            StatusLabel.Text += $" — الفحص: {order.QualityNumber ?? "بانتظار الفحص"}";
        }
        else
        {
            _secondary.Add(new ActualSecondaryRow());
            if (!editable)
                StatusLabel.Text += " — التسجيل غير متاح للصلاحية الحالية أو لحالة الأمر.";
        }
    }

    private static string GetProductionDate(ActualDeliveryOrderDto order)
        => order?.ProductionDate;

    private void AddByProduct_Click(object sender, RoutedEventArgs e)
        => _secondary.Add(new ActualSecondaryRow());

    private void RemoveByProduct_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ActualSecondaryRow row)
            _secondary.Remove(row);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_saving) return;
            if (OrderBox.SelectedItem is not ActualDeliveryOrderDto order)
            {
                StatusLabel.Text = "اختر أمر الإنتاج أولاً.";
                return;
            }
            if (!order.CanRecord)
            {
                StatusLabel.Text = "لا يمكن تسجيل الفعلي: " + (order.Status ?? "حالة الأمر لا تسمح بالإقفال.");
                return;
            }
            if (!CanProduction("Create") || !PermissionGate.Can("execution", "Edit"))
            {
                StatusLabel.Text = "لا يمكن الحفظ: يلزم صلاحية إنشاء الإنتاج وتعديل التنفيذ.";
                return;
            }

            ItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            SecondaryGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            SecondaryGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (_items.Count == 0 || _items.Any(i => !i.TryQuantity(out _)))
                throw new ArgumentException("أدخل الفعلي لكل بند، والصفر يُدخل صراحةً.");
            if (!ActualProductionRow.TryNonnegative(RawBox.Text, out var raw) || raw <= 0)
                throw new ArgumentException("أدخل الخام المستهلك فعلياً بالكيلو.");
            if (!ActualProductionRow.TryNonnegative(DowntimeBox.Text, out var hours))
                throw new ArgumentException("أدخل ساعات توقف صحيحة غير سالبة.");

            var input = new ActualProductionDto
            {
                OrderId = order.OrderId,
                ConsumedRawKg = raw,
                DowntimeHours = hours,
                DowntimeReason = ReasonBox.Text,
                Notes = NotesBox.Text
            };
            foreach (var row in _items)
            {
                row.TryQuantity(out var quantity);
                input.Items.Add(new ActualProductionItemDto { OrderItemId = row.Source.OrderItemId, ActualCartons = quantity });
            }
            foreach (var row in _secondary)
            {
                if (row.Definition == null && string.IsNullOrWhiteSpace(row.Quantity)) continue;
                if (row.Definition == null || !ActualProductionRow.TryNonnegative(row.Quantity, out var quantity) || quantity <= 0)
                    throw new ArgumentException("اختر المخرج الثانوي وأدخل كميته الموجبة، أو احذف السطر غير المستخدم.");
                input.ByProducts.Add(new ByProductQtyDto { ByProductId = row.Definition.Id, QtyKg = quantity });
            }

            _saving = true;
            var result = WithService(s => s.SaveActualProduction(input));
            if (!result.Ok)
            {
                StatusLabel.Text = result.Message;
                return;
            }
            MessageBox.Show(result.Message + "\n\nالخطوة التالية: افتح «أوامر تسليم الإنتاج» لإنشاء المسودة واختيار المخزن. لا توجد حركة مخزون حتى الاستلام المخزني.",
                "تم إقفال الإنتاج وتسجيل الفعلي", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusLabel.Text = result.Message;
            LoadOrders(order.OrderId);
        }
        catch (ArgumentException ex)
        {
            StatusLabel.Text = ex.Message;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "ActualClose.Save: " + ex.Message;
            AppContainer.Get<DialogService>().HandleException(ex, "ActualClose.Save");
        }
        finally
        {
            _saving = false;
        }
    }

    private void Print()
    {
        var order = OrderBox.SelectedItem as ActualDeliveryOrderDto;
        if (order == null) { AppContainer.Get<DialogService>().Info("اختر أمراً أولاً."); return; }
        AppContainer.Get<DialogService>().Info("يمكن طباعة التنفيذ المحفوظ من شاشة أمر الإنتاج أو التقارير بعد الحفظ.");
    }
}
