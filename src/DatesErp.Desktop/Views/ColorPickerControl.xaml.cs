using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DatesErp.Desktop.Views
{
    public partial class ColorPickerControl : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register("SelectedColor", typeof(string), typeof(ColorPickerControl),
                new PropertyMetadata("#14532D", OnSelectedColorChanged));

        public string SelectedColor
        {
            get => (string)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public event EventHandler<string> ColorChanged;

        private static readonly string[] PresetColors = new[]
        {
            "#14532D", "#0A246A", "#C9A227", "#B91C1C", "#15803D", "#0284C7",
            "#7C3AED", "#DB2777", "#059669", "#D97706", "#1F2937", "#FFFFFF",
            "#0B3D1F", "#061845", "#FEF3C7", "#FEE2E2", "#DCFCE7", "#E0F2FE",
            "#F8FAFC", "#F1F5F9", "#E2E8F0", "#CBD5E1", "#94A3B8", "#64748B",
            "#1E293B", "#0F172A", "#2563EB", "#7C2D12", "#BE123C", "#0F766E",
            "#4338CA", "#6D28D9", "#065F46", "#92400E", "#1E40AF", "#000000"
        };

        public ColorPickerControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildPresetColors();
            UpdatePreview();
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerControl ctrl)
            {
                ctrl.UpdatePreview();
            }
        }

        private void BuildPresetColors()
        {
            PresetColorsPanel.Children.Clear();
            foreach (var hex in PresetColors)
            {
                var border = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(ParseColor(hex)),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = hex
                };
                border.MouseLeftButtonUp += (s, args) =>
                {
                    SelectedColor = hex;
                    HexBox.Text = hex;
                    CustomHexBox.Text = hex;
                    UpdatePreview();
                    ColorChanged?.Invoke(this, hex);
                    ColorPopup.IsOpen = false;
                };
                PresetColorsPanel.Children.Add(border);
            }
        }

        private void UpdatePreview()
        {
            try
            {
                var color = ParseColor(SelectedColor);
                ColorPreview.Background = new SolidColorBrush(color);
                if (HexBox.Text != SelectedColor)
                    HexBox.Text = SelectedColor;
                if (CustomHexBox != null && CustomHexBox.Text != SelectedColor)
                    CustomHexBox.Text = SelectedColor;
            }
            catch { }
        }

        private void ColorPreview_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ColorPopup.IsOpen = true;
        }

        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = !ColorPopup.IsOpen;
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var text = HexBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                if (!text.StartsWith("#")) text = "#" + text;
                var color = ParseColor(text);
                SelectedColor = text;
                ColorPreview.Background = new SolidColorBrush(color);
                ColorChanged?.Invoke(this, text);
            }
            catch { }
        }

        private void ApplyCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hex = CustomHexBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(hex)) return;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                var color = ParseColor(hex);
                SelectedColor = hex;
                HexBox.Text = hex;
                ColorPreview.Background = new SolidColorBrush(color);
                ColorChanged?.Invoke(this, hex);
                ColorPopup.IsOpen = false;
            }
            catch
            {
                MessageBox.Show("لون غير صالح. استخدم صيغة Hex مثل #14532D", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return Colors.Black;
                hex = hex.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return Colors.Black;
            }
        }
    }
}
