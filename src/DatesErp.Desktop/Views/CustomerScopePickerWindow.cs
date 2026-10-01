using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DatesErp.Core.Domain.Entities;

namespace DatesErp.Desktop.Views;

/// <summary>
/// نافذة اختيار عملاء نطاق خطة الإنتاج.
/// لا تُعرض قائمة العملاء داخل شاشة التخطيط نفسها؛ يختار مدير التخطيط عميلًا أو عدة
/// عملاء من نافذة مستقلة، ثم يعود إلى الشاشة بملخص واضح للاختيار.
/// </summary>
public sealed class CustomerScopePickerWindow : Window
{
    private sealed class CustomerRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public CustomerRow(Customer customer, bool isSelected)
        {
            Customer = customer;
            _isSelected = isSelected;
        }

        public Customer Customer { get; }
        public int Id => Customer.Id;
        public string CustomerCode => Customer.CustomerCode ?? "—";
        public string CustomerName => Customer.CustomerName ?? "—";
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private readonly List<CustomerRow> _allRows;
    private readonly ObservableCollection<CustomerRow> _visibleRows = new();
    private readonly DataGrid _grid = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _countText = new();

    /// <summary>العملاء الذين اعتمدهم المستخدم في النافذة.</summary>
    public List<Customer> SelectedCustomers { get; private set; } = new();

    public CustomerScopePickerWindow(IEnumerable<Customer> customers, IEnumerable<int> selectedCustomerIds = null)
    {
        var source = customers?.Where(c => c != null).GroupBy(c => c.Id).Select(g => g.First())
            .OrderBy(c => c.CustomerName).ToList() ?? new List<Customer>();
        var selected = selectedCustomerIds?.ToHashSet();
        // عدم تمرير اختيار يعني «كل العملاء النشطين» — وهو نطاق الخطة متعددة العملاء الافتراضي.
        bool selectAll = selected == null;
        _allRows = source.Select(c => new CustomerRow(c, selectAll || selected.Contains(c.Id))).ToList();
        foreach (var row in _allRows) row.PropertyChanged += Row_PropertyChanged;

        Title = "اختيار عملاء نطاق الخطة";
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 760;
        Height = 560;
        MinWidth = 620;
        MinHeight = 420;
        Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
        ShowInTaskbar = false;

        BuildContent();
        ApplyFilter();
        Closed += (_, _) =>
        {
            foreach (var row in _allRows) row.PropertyChanged -= Row_PropertyChanged;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var heading = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x28, 0x3C)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "👥 اختر عميلًا أو عدة عملاء لنطاق خطة الإنتاج",
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14
                    },
                    new TextBlock
                    {
                        Text = "يمكن ترك الاختيار كما هو لتخطيط جميع العملاء النشطين، أو إلغاء تحديد الكل ثم تحديد العملاء المطلوبين.",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE4, 0xF0)),
                        FontSize = 11.5,
                        Margin = new Thickness(0, 4, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var searchBar = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        searchBar.Children.Add(new TextBlock
        {
            Text = "🔍 بحث:",
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        _searchBox.MinWidth = 260;
        _searchBox.FontSize = 13;
        _searchBox.Padding = new Thickness(6, 4, 6, 4);
        _searchBox.ToolTip = "ابحث باسم العميل أو الكود";
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        searchBar.Children.Add(_searchBox);

        var selectAll = Button("تحديد الكل", "ErpButton");
        selectAll.Margin = new Thickness(8, 0, 0, 0);
        selectAll.Click += (_, _) => SetVisibleSelection(true);
        searchBar.Children.Add(selectAll);
        var clearAll = Button("إلغاء تحديد الكل", "ErpButton");
        clearAll.Margin = new Thickness(6, 0, 0, 0);
        clearAll.Click += (_, _) => SetVisibleSelection(false);
        searchBar.Children.Add(clearAll);
        DockPanel.SetDock(searchBar, Dock.Top);
        root.Children.Add(searchBar);

        _countText.FontSize = 11.5;
        _countText.Foreground = Brushes.DimGray;
        _countText.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(_countText, Dock.Top);
        root.Children.Add(_countText);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = false;
        _grid.CanUserAddRows = false;
        _grid.RowHeight = 31;
        _grid.FontSize = 13;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.Background = Brushes.White;
        _grid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xFB, 0xFA, 0xF5));
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "اختيار",
            Binding = new Binding(nameof(CustomerRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 75
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "كود العميل",
            Binding = new Binding(nameof(CustomerRow.CustomerCode)),
            IsReadOnly = true,
            Width = 150
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "اسم العميل",
            Binding = new Binding(nameof(CustomerRow.CustomerName)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.ItemsSource = _visibleRows;
        root.Children.Add(_grid);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var ok = Button("✅ اعتماد الاختيار", "ErpPrimaryButton");
        ok.IsDefault = true;
        ok.Margin = new Thickness(0, 0, 8, 0);
        ok.Click += (_, _) => ConfirmSelection();
        footer.Children.Add(ok);
        var cancel = Button("إلغاء", "ErpButton");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        Content = root;
    }

    private static Button Button(string text, string styleKey)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5) };
        if (Application.Current?.TryFindResource(styleKey) is Style style) button.Style = style;
        return button;
    }

    private void ApplyFilter()
    {
        string term = _searchBox.Text?.Trim() ?? "";
        _visibleRows.Clear();
        foreach (var row in _allRows.Where(r => string.IsNullOrWhiteSpace(term)
            || r.CustomerName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || r.CustomerCode.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            _visibleRows.Add(row);
        UpdateCount();
    }

    private void SetVisibleSelection(bool selected)
    {
        foreach (var row in _visibleRows) row.IsSelected = selected;
        UpdateCount();
    }

    private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomerRow.IsSelected)) UpdateCount();
    }

    private void UpdateCount()
    {
        int selected = _allRows.Count(r => r.IsSelected);
        _countText.Text = $"المحدد: {selected} من {_allRows.Count} عميل نشط — المعروض: {_visibleRows.Count}";
    }

    private void ConfirmSelection()
    {
        if (_allRows.Count == 0)
        {
            MessageBox.Show("لا يوجد عملاء نشطون للاختيار.", "عملاء نطاق الخطة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_allRows.All(r => !r.IsSelected))
        {
            MessageBox.Show("حدد عميلاً واحداً على الأقل، أو أغلق النافذة لاستخدام جميع العملاء النشطين تلقائياً.",
                "عملاء نطاق الخطة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedCustomers = _allRows.Where(r => r.IsSelected).Select(r => r.Customer).ToList();
        DialogResult = true;
        Close();
    }
}
