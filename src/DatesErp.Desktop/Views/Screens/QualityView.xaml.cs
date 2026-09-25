using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatesErp.Desktop.Views.Screens;

/// <summary>لون حالة المطابقة للمعايير.</summary>
public class MatchStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && s.Contains("غير مطابق") ? Brushes.Red : Brushes.Green;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>قاعدة صفوف قابلة للتحرير: تغيير الحقل ينعكس فوراً على المشتقات.</summary>
public abstract class EditableRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void Raise([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

public partial class QualityView : UserControl
{
    /// <summary>
    /// §v1.50.29: صف المصفوفة — بنفس شكل خطة الإنتاج: عميل × صنف × وزن × عبوة × المستلم للفحص،
    /// ثم عمود لكل صفة جودة (GradeQtys[typeId]) وعمود الإجمالي المحسوب. الصفات ليست أصنافاً.
    /// </summary>
    private class ItemRowUi : EditableRow
    {
        public int ProductId { get; set; }
        public int? OrderItemId { get; set; }
        public int? LotId { get; set; }
        public string LotCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string CartonWeight { get; set; } = "";
        public string PackageName { get; set; } = "";
        public double ReceivedCartons { get; set; }
        public List<AllowedResultType> Grades { get; set; } = new();
        public Dictionary<int, double> GradeQtys { get; } = new();
        public double Total => GradeQtys.Values.Sum();
        public bool IsInvalid => !double.IsFinite(ReceivedCartons) || !double.IsFinite(Total)
            || Math.Abs(Total - ReceivedCartons) > 0.001;
        public string ValidationError => IsInvalid
            ? $"مجموع الصفات {Total:N0} لا يساوي المستلم {ReceivedCartons:N0} أو يحتوي قيمة غير صالحة"
            : null;
        /// <summary>بعد تحرير خلية صفة: يُحدّث الإجمالي.</summary>
        public void CellEdited() { Raise("Item[]"); Raise(nameof(Total)); Raise(nameof(IsInvalid)); Raise(nameof(ValidationError)); }
    }

    /// <summary>صف معيار معتمد بقيمته المسجلة وحالة المطابقة.</summary>
    private class StandardUi : EditableRow
    {
        public int StandardId { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string UnitLabel { get; set; } = "%";
        public double Min { get; set; } = double.MinValue;
        public double Max { get; set; } = double.MaxValue;
        public string MinText => Min == double.MinValue ? "—" : Min.ToString("0.##");
        public string MaxText => Max == double.MaxValue ? "—" : Max.ToString("0.##");
        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                if (!Set(ref _value, value)) return;
                Raise(nameof(IsInvalid));
                Raise(nameof(ValidationError));
                Raise(nameof(StatusAr));
            }
        }
        public bool IsInvalid => !double.IsFinite(Value) || Value < Min || Value > Max;
        public string ValidationError => IsInvalid ? $"القيمة {Value} خارج [{MinText} - {MaxText}] أو غير صالحة" : null;
        public string StatusAr => !IsInvalid ? "مطابق ✓" : "خارج الحدود ✗";
    }

    private List<QualitySourceDto> _sources = new();
    private QualitySourceDto _current;
    private readonly ObservableCollection<ItemRowUi> _rows = new();
    private readonly ObservableCollection<StandardUi> _standards = new();
    private List<AllowedResultType> _gradeColumns = new();
    private bool _loading;
    private static bool QualityCan(string action)
        => DatesErp.Desktop.Views.PermissionGate.Can("quality", action);

    public QualityView() { InitializeComponent(); ResultsGrid.ItemsSource = _rows; CriteriaGrid.ItemsSource = _standards; Loaded += (_, _) => Load(); }
    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("فحص وتأكيد جودة التمور — المواصفة القياسية");
        chrome.SetScreenCode("MRPQC1002");
        chrome.SetToolbar(new Views.ErpToolbar()
            .WithPrint((_, _) => Print_Click(this, new RoutedEventArgs()), "طباعة محضر الفحص المحفوظ")
            .WithList((_, _) => Load(), "تحديث تسليمات الإنتاج")
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard")));
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }
    private T WithInsp<T>(Func<IInspectionService, T> action)
    {
        using var scope = AppContainer.NewScope();
        return action(scope.ServiceProvider.GetRequiredService<IInspectionService>());
    }

    /// <summary>تحميل المصادر: تسليمات الإنتاج المكتملة — القابلة للفحص أولاً.</summary>
    private void Load(int? keepOrderId = null)
    {
        try
        {
            ErrorLog.WriteInfo($"Quality.Load STEP=ENTER KeepOrderId={keepOrderId?.ToString() ?? "<null>"}");
            _sources = WithInsp(s => s.GetDeliverySources());
            ErrorLog.WriteInfo($"Quality.Load STEP=RETURN_SOURCES Count={_sources.Count}");
            SourceBox.ItemsSource = _sources;
            SourceBox.DisplayMemberPath = nameof(QualitySourceDto.Label);
            // فتح الجودة من تقرير/مهمة يجب أن يفتح الفحص المطلوب، لا أول مصدر
            // عشوائي في القائمة. هذا المعرف لا يغيّر عقدة الخدمة؛ هو حالة تنقل
            // مؤقتة تضعها MainWindow عند OpenDocument("quality", id).
            int? pendingCheckId = DatesErp.Desktop.Views.MainWindow.PendingCheckIdToOpen;
            DatesErp.Desktop.Views.MainWindow.PendingCheckIdToOpen = null;
            _loading = true;
            SourceBox.SelectedItem = _sources.FirstOrDefault(s2 => pendingCheckId.HasValue
                    && s2.CheckId == pendingCheckId.Value)
                ?? _sources.FirstOrDefault(s2 => s2.OrderId == keepOrderId && !s2.CheckApproved)
                ?? _sources.FirstOrDefault(s2 => !s2.CheckApproved)
                ?? _sources.FirstOrDefault(s2 => s2.OrderId == keepOrderId)
                ?? _sources.FirstOrDefault();
            _loading = false;
            if (_sources.Count == 0)
                StatusLabel.Text = "لا توجد تسليمات إنتاج مكتملة بعد — سجّل الفعلي في «تسليم الإنتاج» وسيأتي إلى هنا تلقائياً بأصنافه.";
            Source_Changed(SourceBox, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, new List<object>(), new List<object> { SourceBox.SelectedItem }));
        }
        catch (Exception ex)
        {
            WriteQualityExceptionTrace("Quality.Load", ex);
            DisableQualityActions();
            StatusLabel.Text = $"تعذر تحميل مصادر الفحص: {ex.Message} — تم تعطيل الأزرار حتى ينجح التحديث.";
        }
    }

    private void DisableQualityActions()
    {
        SaveButton.IsEnabled = false;
        ApproveButton.IsEnabled = false;
        PrintButton.IsEnabled = false;
        ResultsGrid.IsReadOnly = true;
        CriteriaGrid.IsReadOnly = true;
        DecisionPassed.IsEnabled = false;
        DecisionQuarantine.IsEnabled = false;
        DecisionRejected.IsEnabled = false;
    }

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid == null || _loading) return;
        _current = SourceBox.SelectedItem as QualitySourceDto;
        _rows.Clear(); _standards.Clear();
        // إعادة تحميل المصدر يجب ألا تضاعف شرائح الأصناف في كل تغيير أو تحديث.
        ItemsPanel.Children.Clear();
        ItemsPanel.Children.Add(new TextBlock
        {
            Text = "الأصناف المستلمة للفحص:", FontWeight = FontWeights.Bold, FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        });
        ErrorLog.WriteInfo($"Quality.Source_Changed STEP=SELECT OrderId={_current?.OrderId.ToString() ?? "<null>"} CheckId={_current?.CheckId?.ToString() ?? "<null>"}");
        CheckNoChip.Text = _current?.CheckId != null
            ? $"🧾 فحص {_current.CheckNumber} — {QualityCheckStatuses.ToArabic(_current.CheckStatus)}{(_current.CheckApproved ? " (معتمد — للقراءة)" : "")}"
            : "— لا فحص محفوظ بعد";
        bool canCreate = QualityCan("Create");
        bool canApprove = QualityCan("Approve");
        bool canPrint = QualityCan("Print");
        SaveButton.IsEnabled = _current != null && !_current.CheckApproved && canCreate;
        ApproveButton.IsEnabled = _current?.CheckId != null && !_current.CheckApproved && canApprove;
        PrintButton.IsEnabled = _current?.CheckId != null && canPrint;
        ResultsGrid.IsReadOnly = _current == null || _current.CheckApproved || !canCreate;
        CriteriaGrid.IsReadOnly = _current == null || _current.CheckApproved || !canCreate;
        DecisionPassed.IsEnabled = _current != null && !_current.CheckApproved && canCreate;
        DecisionQuarantine.IsEnabled = _current != null && !_current.CheckApproved && canCreate;
        DecisionRejected.IsEnabled = _current != null && !_current.CheckApproved && canCreate;
        StatusLabel.Text = _current == null
            ? (_sources.Count == 0
                ? "لا توجد تسليمات إنتاج مكتملة بعد — حدّث الشاشة بعد إقفال إنتاج فعلي."
                : "اختر تسليم إنتاج من الأعلى — تنزل أصنافه المنتَجة تلقائياً بكمياتها.")
            : $"مصدر الفحص: تسليم الإنتاج رقم {_current.OrderNumber} — إجمالي المنتَج {_current.TotalProducedCartons:N0} كرتون"
              + (_current.CheckApproved ? " — الفحص معتمد، النتائج للقراءة والطباعة." : " — النتائج تُدخل تحت مباشرة.");
        if (_current != null)
        {
            if (!canCreate && !_current.CheckApproved)
                StatusLabel.Text += " — الحفظ غير متاح: لا توجد صلاحية إنشاء/تعديل فحص الجودة لهذا المستخدم.";
            if (!canApprove && !_current.CheckApproved)
                StatusLabel.Text += " — الاعتماد غير متاح: لا توجد صلاحية اعتماد الجودة لهذا المستخدم.";
            if (!canPrint)
                StatusLabel.Text += " — الطباعة غير متاحة: لا توجد صلاحية الطباعة لهذا المستخدم.";
        }
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var savedResults = _current?.CheckId != null
                ? db.InspectionResults.AsNoTracking().Where(r2 => r2.CheckId == _current.CheckId).ToList()
                : new List<InspectionResult>();
            foreach (var item in _current?.Items ?? new List<QualitySourceItemDto>())
            {
                // §التدفق المعتمد: الأصناف المنتَجة كلها تنزل بكمياتها — بلا إعادة اختيار الصنف،
                // وكل عميل بصفه كما في خطة الإنتاج (عميل × صنف × وزن × عبوة).
                var chip = new Border { Background = new System.Windows.Media.SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF3EE")),
                    BorderBrush = new System.Windows.Media.SolidColorBrush((Color)ColorConverter.ConvertFromString("#CADCCF")),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(9,3,9,3), Margin = new Thickness(0,0,8,0) };
                chip.Child = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 12, Foreground = new System.Windows.Media.SolidColorBrush((Color)ColorConverter.ConvertFromString("#14532D")),
                    Text = $"{item.CustomerName ?? "غير محدد"} — {item.ProductName} — {item.ProducedCartons:N0}" };
                ItemsPanel.Children.Add(chip);
                var row = new ItemRowUi { ProductId = item.ProductId, OrderItemId = item.OrderItemId, LotId = item.LotId,
                    CustomerName = item.CustomerName ?? "غير محدد", ProductName = item.ProductName, LotCode = item.LotCode ?? "—",
                    CartonWeight = item.CartonWeightKg > 0 ? item.CartonWeightKg.ToString("0.##") : "—",
                    PackageName = string.IsNullOrWhiteSpace(item.PackageName) ? "—" : item.PackageName,
                    ReceivedCartons = item.ProducedCartons, Grades = item.AllowedGrades };
                var mine = savedResults.Where(r2 => r2.ProductId == item.ProductId && (item.LotId == null || r2.LotId == item.LotId)).ToList();
                if (mine.Count > 0)
                    foreach (var r2 in mine)
                        row.GradeQtys[r2.ResultTypeId] = (double)r2.Qty;
                else if (item.AllowedGrades.FirstOrDefault(g => g.ResultKind == InspectionResultType.KindAccepted) is { } acceptedGrade)
                    row.GradeQtys[acceptedGrade.ResultTypeId] = item.ProducedCartons;
                _rows.Add(row);
            }
            // المعايير المعتمدة: تظهر تلقائياً بقيمها المحفوظة أو الافتراضية
            var savedStd = _current?.CheckId != null
                ? db.QualityStandardRecords.AsNoTracking().Where(r2 => r2.CheckId == _current.CheckId).ToDictionary(r2 => r2.StandardId, r2 => r2.Value)
                : new Dictionary<int, double>();
            foreach (var st in db.QualityStandards.AsNoTracking().Where(s2 => s2.IsActive).OrderBy(s2 => s2.SortNo))
                _standards.Add(new StandardUi { StandardId = st.Id, Key = st.Code, Name = st.NameAr, UnitLabel = st.UnitLabel,
                    Min = st.MinValue ?? double.MinValue, Max = st.MaxValue ?? double.MaxValue,
                    Value = savedStd.TryGetValue(st.Id, out var v) ? v : st.DefaultValue });
            BuildGradeColumns();
            Recalc();
            var itemsWithoutGrades = _rows.Where(r2 => r2.Grades.Count == 0).ToList();
            if (itemsWithoutGrades.Count > 0)
            {
                SaveButton.IsEnabled = false;
                StatusLabel.Text += $" — ⛔ لا توجد صفات جودة (مقبول/مرفوض) معرفة لـ {itemsWithoutGrades.Count} بنداً؛ عرّفها أولاً من إعدادات الجودة.";
            }
            // §1.50.72 P4-3: تمييز القيم المعبأة مسبقاً (كل المنتَج «مقبول») حتى لا تُحفظ دون تدقيق
            else if (_current != null && _current.CheckId == null && _rows.Count > 0)
                StatusLabel.Text += " — ⚠ القيم معبأة مسبقاً (كل الكمية المستلمة «مقبول» افتراضياً)؛ راجعها وعدّل المرفوض قبل الحفظ.";
        }
        catch (Exception ex)
        {
            WriteQualityExceptionTrace("Quality.Source", ex);
            DisableQualityActions();
            StatusLabel.Text = $"تعذر تحميل تفاصيل مصدر الفحص: {ex.Message} — تم تعطيل الأزرار حتى ينجح التحديث.";
        }
    }

    /// <summary>§v1.50.29: أعمدة الشبكة = الثابتة (عميل/صنف/وزن/عبوة/المستلم/الدفعة) + عمود لكل صفة + الإجمالي.</summary>
    private void BuildGradeColumns()
    {
        _gradeColumns = _rows.SelectMany(r2 => r2.Grades).GroupBy(g => g.ResultTypeId).Select(g => g.First())
            .OrderByDescending(g => g.ResultKind == InspectionResultType.KindAccepted).ThenBy(g => g.ResultTypeId).ToList();
        ResultsGrid.Columns.Clear();

        var wrapStyle = new Style(typeof(TextBlock));
        wrapStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        wrapStyle.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 4, 6, 4)));
        wrapStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        wrapStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.5));

        var boldCenterStyle = new Style(typeof(TextBlock));
        boldCenterStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        boldCenterStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
        boldCenterStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        boldCenterStyle.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 4, 6, 4)));
        boldCenterStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.5));

        void AddCol(string header, string path, double width, Style style = null, bool ro = true, string format = null)
        {
            var binding = new Binding(path);
            if (!string.IsNullOrWhiteSpace(format)) binding.StringFormat = format;
            var col = new DataGridTextColumn { Header = header, Binding = binding, IsReadOnly = ro, Width = width };
            if (style != null) col.ElementStyle = style;
            ResultsGrid.Columns.Add(col);
        }

        AddCol("العميل", nameof(ItemRowUi.CustomerName), 220, wrapStyle);
        AddCol("الصنف", nameof(ItemRowUi.ProductName), 240, wrapStyle);
        AddCol("وزن الكرتون", nameof(ItemRowUi.CartonWeight), 100, boldCenterStyle);
        AddCol("العبوة", nameof(ItemRowUi.PackageName), 120, wrapStyle);
        AddCol("المستلم للفحص", nameof(ItemRowUi.ReceivedCartons), 120, boldCenterStyle, format: "{0:N0}");
        foreach (var g in _gradeColumns)
        {
            var col = new DataGridTextColumn
            {
                Header = g.NameAr,
                Binding = new Binding($"GradeQtys[{g.ResultTypeId}]") { UpdateSourceTrigger = PropertyChangedTrigger, Mode = BindingModeTwoWay },
                Width = 115,
                ElementStyle = boldCenterStyle
            };
            ResultsGrid.Columns.Add(col);
        }
        AddCol("الدفعة", nameof(ItemRowUi.LotCode), 120, wrapStyle);
        AddCol("الإجمالي", nameof(ItemRowUi.Total), 110, boldCenterStyle, format: "{0:N0}");
    }
    private const System.Windows.Data.UpdateSourceTrigger PropertyChangedTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged;
    private const System.Windows.Data.BindingMode BindingModeTwoWay = System.Windows.Data.BindingMode.TwoWay;

    private void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    /// <summary>بعد تحرير خلية صفة: تحديث الإجماليات ومعادلة الحفظ الحية.</summary>
    private void Results_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            (e.Row.Item as ItemRowUi)?.CellEdited();
            Dispatcher.BeginInvoke(new Action(Recalc));
        }
    }

    /// <summary>معادلة الحفظ الحية لكل صف: مجموع الصفات مقابل الكمية المستلمة، والنسب لكل صفة.</summary>
    private void Recalc()
    {
        foreach (var row in _rows) row.CellEdited();
        bool allOk = _rows.Count > 0 && _rows.All(IsRowBalanced);
        var lines = new List<string>();
        foreach (var row in _rows)
        {
            double sum = row.Total;
            bool ok = double.IsFinite(sum) && double.IsFinite(row.ReceivedCartons)
                && Math.Abs(sum - row.ReceivedCartons) <= 0.001;
            string pcts = string.Join(" · ", _gradeColumns
                .Where(g => row.GradeQtys.TryGetValue(g.ResultTypeId, out var q) && row.ReceivedCartons > 0 && q > 0)
                .Select(g => $"{g.NameAr} {row.GradeQtys[g.ResultTypeId] / row.ReceivedCartons * 100:N2}٪"));
            lines.Add($"{row.CustomerName}/{row.ProductName}: {sum:N0} / {row.ReceivedCartons:N0} {(ok ? "✓" : "✗")}"
                      + (pcts.Length > 0 ? $" ({pcts})" : ""));
        }
        var perGrade = _gradeColumns
            .Select(g => (g.NameAr, Q: _rows.Sum(r2 => r2.GradeQtys.TryGetValue(g.ResultTypeId, out var q) ? q : 0)))
            .Where(x => x.Q > 0).ToList();
        double tot = _rows.Sum(r2 => r2.Total);
        TotalsLabel.Text = "الإجمالي: "
            + (perGrade.Count > 0 ? string.Join(" · ", perGrade.Select(x => $"{x.NameAr} {x.Q:N0}")) + " — " : "")
            + $"{tot:N0} من {_current?.TotalProducedCartons ?? 0:N0}";
        EqLabel.Text = _rows.Count == 0
            ? "معادلة الحفظ: مجموع صفات كل صنف = كميته المستلمة للفحص."
            : (allOk ? "✓ مجموع الصفات يطابق الكمية المستلمة لكل صف — يُسمح بالحفظ.  "
                     : "✗ لا يُسمح بالحفظ — الصفات لا تطابق الكمية المستلمة:  ") + string.Join("  •  ", lines);
        EqLabel.Foreground = new System.Windows.Media.SolidColorBrush((Color)ColorConverter.ConvertFromString(allOk ? "#14532D" : "#B91C1C"));
    }
    private static bool IsRowBalanced(ItemRowUi row)
        => double.IsFinite(row.Total) && double.IsFinite(row.ReceivedCartons)
           && Math.Abs(row.Total - row.ReceivedCartons) <= 0.001;

    private void Grid_SingleClickEdit(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.IsReadOnly) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null and not DataGridCell) dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridCell { IsReadOnly: false, IsEditing: false } cell)
        {
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            grid.CurrentCell = new DataGridCellInfo(cell);
            grid.BeginEdit();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!QualityCan("Create"))
        {
            StatusLabel.Text = "⛔ لا يمكن الحفظ: لا توجد صلاحية إنشاء/تعديل فحص الجودة لهذا المستخدم.";
            return;
        }
        if (_current == null)
        {
            ErrorLog.WriteInfo("Quality.Save_Click STEP=EXIT Reason=NoSource");
            StatusLabel.Text = "⛔ لا يمكن الحفظ: اختر مصدر فحص من تسليمات الإنتاج أولاً.";
            return;
        }
        if (_current.CheckApproved)
        {
            ErrorLog.WriteInfo($"Quality.Save_Click STEP=EXIT Reason=AlreadyApproved CheckId={_current.CheckId}");
            StatusLabel.Text = "⛔ لا يمكن تعديل فحص معتمد — النتائج للقراءة والطباعة.";
            return;
        }
        try
        {
            ErrorLog.WriteInfo($"Quality.Save_Click STEP=ENTER OrderId={_current.OrderId} CheckId={_current.CheckId?.ToString() ?? "<new>"}");
            ResultsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ResultsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            CriteriaGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            CriteriaGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ErrorLog.WriteInfo($"Quality.Save_Click STEP=READ_INPUT_ROWS OrderId={_current.OrderId} Rows={_rows.Count} Standards={_standards.Count}");
            if (!_rows.All(IsRowBalanced))
            {
                StatusLabel.Text = "⛔ الحفظ مرفوض: " + EqLabel.Text;
                return;
            }
            if (_standards.Any(s2 => s2.IsInvalid))
            {
                StatusLabel.Text = "⛔ الحفظ مرفوض: توجد قيمة مخبرية خارج حدود المواصفة أو غير صالحة.";
                return;
            }
            // §1.50.72 P2-4: نتائج فيها مرفوضات ← القرار يجب أن يُحدد صراحةً —
            // كان الحفظ بدون اختيار أي زر قرار يسجل «مطابق» بصمت رغم المرفوضات.
            bool hasRejected = _rows.Any(r2 => r2.GradeQtys.Any(kv =>
                double.IsFinite(kv.Value) && kv.Value > 0
                && r2.Grades.Any(g => g.ResultTypeId == kv.Key && g.ResultKind == InspectionResultType.KindRejected)));
            if (hasRejected && DecisionRejected.IsChecked != true && DecisionQuarantine.IsChecked != true)
            {
                StatusLabel.Text = "⛔ هذا الفحص يحتوي كراتين مرفوضة — حدّد القرار صراحةً (مطابق / حجز / مرفوض) قبل الحفظ.";
                return;
            }
            ErrorLog.WriteInfo($"Quality.Save_Click STEP=BUILD_DTO OrderId={_current.OrderId}");
            var dto = new QualityDeliveryCheckDto
            {
                OrderId = _current.OrderId,
                CheckDate = DateTime.Now.ToString(DatesErp.Core.Common.UiFormat.DatePattern),
                Decision = DecisionRejected.IsChecked == true ? "Rejected" : DecisionQuarantine.IsChecked == true ? "Quarantine" : "Passed",
                InspectorNotes = null,
                Rows = _rows.SelectMany(r2 => r2.GradeQtys
                        .Where(kv => kv.Key > 0 && double.IsFinite(kv.Value) && kv.Value > 0)
                        .Select(kv => new QualityGradeRowDto { OrderItemId = r2.OrderItemId, ProductId = r2.ProductId, LotId = r2.LotId, ResultTypeId = kv.Key, Cartons = kv.Value }))
                    .ToList(),
                Standards = _standards.Select(s2 => new QualityStandardValueDto { StandardId = s2.StandardId, Value = s2.Value }).ToList(),
            };
            if (dto.Rows.Count == 0) { StatusLabel.Text = "⛔ أدخل كمية صفة واحدة على الأقل."; return; }
            ErrorLog.WriteInfo($"Quality.Save_Click STEP=CALL_SAVE_DELIVERY_CHECK OrderId={dto.OrderId} Rows={dto.Rows.Count}");
            var r = WithInsp(s => s.SaveDeliveryCheck(dto));
            ErrorLog.WriteInfo($"Quality.Save_Click STEP=RETURN_SAVE_DELIVERY_CHECK OrderId={dto.OrderId} Ok={r.Ok} ResultId={r.Id} Message={r.Message}");
            StatusLabel.Text = r.Ok ? "✅ " + r.Message : "⛔ " + r.Message;
            if (r.Ok) Load(_current.OrderId);
        }
        catch (Exception ex)
        {
            WriteQualityExceptionTrace("Quality.Save", ex);
            StatusLabel.Text = $"Quality.Save: {ex.GetType().FullName}: {ex.Message}";
        }
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (!QualityCan("Approve"))
        {
            StatusLabel.Text = "⛔ لا يمكن الاعتماد: لا توجد صلاحية اعتماد الجودة لهذا المستخدم.";
            return;
        }
        if (_current == null)
        {
            ErrorLog.WriteInfo("Quality.Approve_Click STEP=EXIT Reason=NoSource");
            StatusLabel.Text = "⛔ لا يمكن الاعتماد: اختر مصدر فحص من تسليمات الإنتاج أولاً.";
            return;
        }
        if (_current.CheckId == null)
        {
            ErrorLog.WriteInfo($"Quality.Approve_Click STEP=EXIT Reason=NoSavedCheck OrderId={_current.OrderId}");
            StatusLabel.Text = "⛔ لا يمكن الاعتماد: احفظ نتائج الفحص أولاً.";
            return;
        }
        if (_current.CheckApproved)
        {
            ErrorLog.WriteInfo($"Quality.Approve_Click STEP=EXIT Reason=AlreadyApproved CheckId={_current.CheckId}");
            StatusLabel.Text = "الفحص معتمد مسبقاً — النتائج للقراءة والطباعة.";
            return;
        }
        try
        {
            ErrorLog.WriteInfo($"Quality.Approve_Click STEP=ENTER OrderId={_current.OrderId} CheckId={_current.CheckId}");
            if (!AppContainer.Get<DialogService>().Confirm($"اعتماد فحص الجودة {_current.CheckNumber}؟ الاعتماد يُقفل النتائج."))
            {
                ErrorLog.WriteInfo($"Quality.Approve_Click STEP=EXIT Reason=UserCancelled CheckId={_current.CheckId}");
                StatusLabel.Text = "لم يتم اعتماد الفحص — أُلغي التأكيد.";
                return;
            }
            using var scope = AppContainer.NewScope();
            var qc = scope.ServiceProvider.GetRequiredService<IQualityService>();
            ErrorLog.WriteInfo($"Quality.Approve_Click STEP=CALL_APPROVE CheckId={_current.CheckId}");
            var r = qc.ApproveCheck(_current.CheckId.Value);
            ErrorLog.WriteInfo($"Quality.Approve_Click STEP=RETURN_APPROVE CheckId={_current.CheckId} Ok={r.Ok} Message={r.Message}");
            StatusLabel.Text = r.Ok ? "✅ " + r.Message : "⛔ " + r.Message;
            if (r.Ok) Load(_current.OrderId);
        }
        catch (Exception ex)
        {
            WriteQualityExceptionTrace("Quality.Approve", ex);
            StatusLabel.Text = $"Quality.Approve: {ex.GetType().FullName}: {ex.Message}";
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!QualityCan("Print"))
        {
            StatusLabel.Text = "⛔ لا يمكن الطباعة: لا توجد صلاحية طباعة محضر الجودة لهذا المستخدم.";
            return;
        }
        if (_current?.CheckId == null)
        {
            ErrorLog.WriteInfo("Quality.Print_Click STEP=EXIT Reason=NoSavedCheck");
            StatusLabel.Text = "الطباعة بعد حفظ الفحص — احفظ النتائج أولاً.";
            return;
        }
        try
        {
            ErrorLog.WriteInfo($"Quality.Print_Click STEP=ENTER CheckId={_current.CheckId}");
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var m = Printing.StoredPrintModels.Quality(db, _current.CheckId.Value);
            if (m == null)
            {
                StatusLabel.Text = "⛔ تعذر الطباعة: نموذج الفحص غير موجود.";
                return;
            }
            new PrintPreviewWindow(PhasePrint.Build(m), $"{m.DocTitle} {m.DocNo}") { Owner = Window.GetWindow(this) }.ShowDialog();
            ErrorLog.WriteInfo($"Quality.Print_Click STEP=SUCCESS CheckId={_current.CheckId}");
            StatusLabel.Text = "✅ تم فتح محضر الفحص للطباعة.";
        }
        catch (Exception ex)
        {
            WriteQualityExceptionTrace("Quality.Print", ex);
            StatusLabel.Text = $"Quality.Print: {ex.GetType().FullName}: {ex.Message}";
        }
    }

    private static void WriteQualityExceptionTrace(string source, Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur != null; cur = cur.InnerException)
            parts.Add($"{cur.GetType().FullName}: {cur.Message}\n{cur.StackTrace}");
        ErrorLog.WriteInfo($"{source} EXCEPTION\n{string.Join(Environment.NewLine + "--- INNER ---" + Environment.NewLine, parts)}");
        ErrorLog.Write(ex, source);
    }
}
