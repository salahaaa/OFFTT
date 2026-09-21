using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DatesErp.Desktop.Services;
using DatesErp.Desktop.Theming;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace DatesErp.Desktop.Views.Screens
{
    public partial class ThemeCustomizationView : UserControl
    {
        private ThemeSettings _workingTheme;
        private ThemeSettings _originalTheme;
        private bool _isLoading = false;
        private ErpToolbar _toolbar;
        private List<ThemeSettings> _allThemes;

        public ThemeCustomizationView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public void AttachChrome(ErpChrome chrome)
        {
            chrome.SetModule("settings");
            chrome.SetScreenCode("MRPSYS1004");
            _toolbar = new ErpToolbar()
                .WithNew((_, _) => NewTheme_Click(null, null), "ثيم جديد")
                .WithSave((_, _) => Save_Click(null, null), "حفظ الثيم (F10)")
                .WithUndo((_, _) => Cancel_Click(null, null), "إلغاء")
                .WithSearch((_, _) => { }, "بحث")
                .WithPrint((_, _) => Export_Click(null, null))
                .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard"));
            chrome.SetToolbar(_toolbar);
            chrome.SetBody(this);
            chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _isLoading = true;

                // تحقق الصلاحيات
                if (!PermissionGate.Can("settings", "Edit"))
                {
                    AppContainer.Get<DialogService>().Error("ليس لديك صلاحية إدارة إعدادات النظام والمظهر.");
                    (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
                    return;
                }

                // تحميل الثيم الحالي
                _originalTheme = ThemeManager.Current.Clone();
                _workingTheme = ThemeManager.Current.Clone();

                // تحميل الثيمات المحفوظة
                LoadThemesList();

                // تهيئة عناصر التحكم
                InitializeControls();

                // ربط البيانات
                BindThemeToControls(_workingTheme);

                // معاينة الجدول
                PreviewGrid.ItemsSource = new[]
                {
                    new { No = 1, Name = "صنف تجريبي أول", Qty = "150 كجم" },
                    new { No = 2, Name = "صنف تجريبي ثاني", Qty = "200 كجم" },
                    new { No = 3, Name = "صنف تجريبي ثالث", Qty = "75 كجم" }
                };

                UpdateStatus();
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "ThemeCustomization.Load");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void LoadThemesList()
        {
            try
            {
                _allThemes = ThemeManager.GetSavedThemes();
                ThemeSelector.ItemsSource = _allThemes;
                ThemeSelector.DisplayMemberPath = "DisplayName";
                ThemeSelector.SelectedValuePath = "Name";
                ThemeSelector.SelectedItem = _allThemes.FirstOrDefault(t => t.Name == _workingTheme.Name) ?? _allThemes.FirstOrDefault();

                SavedThemesList.ItemsSource = _allThemes;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeCustomization.LoadThemesList");
                _allThemes = ThemeSettings.GetBuiltInThemes();
            }
        }

        private void InitializeControls()
        {
            // الخطوط
            var fonts = ThemeManager.GetAvailableFontFamilies();
            ComboMainFont.ItemsSource = fonts;
            ComboHeadingFont.ItemsSource = fonts;

            // أوزان الخطوط
            ComboHeadingWeight.SelectedIndex = 1; // Bold

            // أشكال
            ComboInputShape.SelectedIndex = 0;
            ComboButtonShape.SelectedIndex = 0;
            ComboButtonStyle.SelectedIndex = 0;
            ComboIconPos.SelectedIndex = 0;
            ComboTextAlign.SelectedIndex = 0;
            ComboCardShape.SelectedIndex = 0;

            CurrentThemeName.Text = _workingTheme.DisplayName;
            ThemeNameBox.Text = _workingTheme.DisplayName;
        }

        private void BindThemeToControls(ThemeSettings t)
        {
            _isLoading = true;
            try
            {
                // ألوان
                PickPrimary.SelectedColor = t.PrimaryColor;
                PickPrimaryDark.SelectedColor = t.PrimaryDarkColor;
                PickSecondary.SelectedColor = t.SecondaryColor;
                PickAccent.SelectedColor = t.AccentColor;
                PickBackground.SelectedColor = t.BackgroundColor;
                PickWindowBg.SelectedColor = t.WindowBackgroundColor;
                PickTableBg.SelectedColor = t.TableBackgroundColor;
                PickTableHeader.SelectedColor = t.TableHeaderBackgroundColor;
                PickTextPrimary.SelectedColor = t.TextPrimaryColor;
                PickTextSecondary.SelectedColor = t.TextSecondaryColor;
                PickBtnBg.SelectedColor = t.ButtonBackgroundColor;
                PickBtnFg.SelectedColor = t.ButtonForegroundColor;
                PickBtnHover.SelectedColor = t.ButtonHoverColor;
                PickBtnPressed.SelectedColor = t.ButtonPressedColor;
                PickBorder.SelectedColor = t.BorderColor;
                PickSuccess.SelectedColor = t.SuccessColor;
                PickWarning.SelectedColor = t.WarningColor;
                PickError.SelectedColor = t.ErrorColor;
                PickInfo.SelectedColor = t.InfoColor;
                PickTableRow.SelectedColor = t.TableRowColor;
                PickTableAltRow.SelectedColor = t.TableAlternateRowColor;
                PickTableSelected.SelectedColor = t.TableSelectedRowColor;
                PickTableHover.SelectedColor = t.TableHoverRowColor;
                PickTableCellSelected.SelectedColor = t.TableSelectedCellColor;
                PickScreenBg.SelectedColor = t.ScreenBackgroundColor;
                PickPageHeaderBg.SelectedColor = t.PageHeaderBackgroundColor;
                PickChromeBg.SelectedColor = t.ChromeBackgroundColor;
                PickSidebarBg.SelectedColor = t.SidebarBackgroundColor;

                // خطوط
                ComboMainFont.SelectedItem = t.MainFontFamily;
                ComboHeadingFont.SelectedItem = t.HeadingFontFamily;
                SliderBaseFont.Value = t.BaseFontSize;
                SliderPageTitle.Value = t.PageTitleFontSize;
                SliderSectionTitle.Value = t.SectionTitleFontSize;
                SliderTableFont.Value = t.TableFontSize;
                SliderButtonFont.Value = t.ButtonFontSize;
                SliderFieldLabel.Value = t.FieldLabelFontSize;

                CheckHeadingBold.IsChecked = t.IsHeadingBold;
                CheckBodyBold.IsChecked = t.IsBodyBold;
                CheckButtonBold.IsChecked = t.IsButtonBold;

                // حدود
                CheckShowTextBoxBorder.IsChecked = t.ShowTextBoxBorder;
                CheckShowButtonBorder.IsChecked = t.ShowButtonBorder;
                CheckShowUnderline.IsChecked = t.ShowUnderline;
                SliderBorderThickness.Value = t.BorderThickness;
                SliderTextBoxBorder.Value = t.TextBoxBorderThickness;
                SliderButtonBorder.Value = t.ButtonBorderThickness;
                SliderTableBorder.Value = t.TableBorderThickness;
                SliderCornerRadius.Value = t.CornerRadius;
                SliderBtnCorner.Value = t.ButtonCornerRadius;
                SliderTextBoxCorner.Value = t.TextBoxCornerRadius;
                SliderCardCorner.Value = t.CardCornerRadius;
                SliderFieldPadding.Value = t.FieldPadding;
                SliderContentPadding.Value = t.ContentPadding;
                SliderContentPad.Value = t.ContentPadding;

                // أزرار
                SliderButtonHeight.Value = t.ButtonHeight;
                SliderButtonMinWidth.Value = t.ButtonMinWidth;
                SliderIconSize.Value = t.ButtonIconSize;
                CheckButtonShadow.IsChecked = t.ButtonHasShadow;

                // جداول
                SliderRowHeight.Value = t.TableRowHeight;
                SliderHeaderHeight.Value = t.TableHeaderHeight;
                CheckShowGridLines.IsChecked = t.TableShowGridLines;
                CheckShowColSep.IsChecked = t.TableShowColumnSeparators;
                CheckShowRowSep.IsChecked = t.TableShowRowSeparators;
                CheckAltRows.IsChecked = t.TableAlternateRows;
                CheckEnableHover.IsChecked = t.TableEnableHover;
                CheckShowRowNumbers.IsChecked = t.TableShowRowNumbers;

                // نوافذ
                SliderTitleBar.Value = t.TitleBarHeight;
                SliderSidebarWidth.Value = t.SidebarWidth;
                CheckShowSidebar.IsChecked = t.ShowSidebar;
                SliderShadowDepth.Value = t.ShadowDepth;
                SliderShadowBlur.Value = t.ShadowBlur;
                SliderShadowOpacity.Value = t.ShadowOpacity;
                SliderOpacity.Value = t.OverallOpacity;
                CheckEnableShadow.IsChecked = t.EnableShadow;
                CheckCardShadow.IsChecked = t.CardHasShadow;

                // عام
                RadioLight.IsChecked = t.Mode == "Light" && !t.IsDarkMode;
                RadioDark.IsChecked = t.Mode == "Dark" || t.IsDarkMode;
                RadioAuto.IsChecked = t.Mode == "Auto";
                CheckIsDark.IsChecked = t.IsDarkMode;

                // Labels
                UpdateSliderLabels();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void UpdateSliderLabels()
        {
            LblBaseFont.Text = SliderBaseFont.Value.ToString("0.#");
            LblPageTitle.Text = SliderPageTitle.Value.ToString("0");
            LblSectionTitle.Text = SliderSectionTitle.Value.ToString("0");
            LblTableFont.Text = SliderTableFont.Value.ToString("0.#");
            LblButtonFont.Text = SliderButtonFont.Value.ToString("0.#");
            LblFieldLabel.Text = SliderFieldLabel.Value.ToString("0.#");
            LblBorderThickness.Text = SliderBorderThickness.Value.ToString("0.0");
            LblTextBoxBorder.Text = SliderTextBoxBorder.Value.ToString("0.0");
            LblButtonBorder.Text = SliderButtonBorder.Value.ToString("0.0");
            LblTableBorder.Text = SliderTableBorder.Value.ToString("0.0");
            LblCornerRadius.Text = SliderCornerRadius.Value.ToString("0");
            LblBtnCorner.Text = SliderBtnCorner.Value.ToString("0");
            LblTextBoxCorner.Text = SliderTextBoxCorner.Value.ToString("0");
            LblCardCorner.Text = SliderCardCorner.Value.ToString("0");
            LblFieldPadding.Text = SliderFieldPadding.Value.ToString("0");
            LblContentPadding.Text = SliderContentPadding.Value.ToString("0");
            LblButtonHeight.Text = SliderButtonHeight.Value.ToString("0");
            LblButtonMinWidth.Text = SliderButtonMinWidth.Value.ToString("0");
            LblIconSize.Text = SliderIconSize.Value.ToString("0");
            LblRowHeight.Text = SliderRowHeight.Value.ToString("0");
            LblHeaderHeight.Text = SliderHeaderHeight.Value.ToString("0");
            LblTitleBar.Text = SliderTitleBar.Value.ToString("0");
            LblSidebarWidth.Text = SliderSidebarWidth.Value.ToString("0");
            LblContentPad.Text = SliderContentPad.Value.ToString("0");
            LblShadowDepth.Text = SliderShadowDepth.Value.ToString("0.0");
            LblShadowBlur.Text = SliderShadowBlur.Value.ToString("0");
            LblShadowOpacity.Text = SliderShadowOpacity.Value.ToString("0.00");
            LblOpacity.Text = SliderOpacity.Value.ToString("0.00");
        }

        private ThemeSettings BuildThemeFromControls()
        {
            var t = _workingTheme.Clone();

            // ألوان
            t.PrimaryColor = PickPrimary.SelectedColor;
            t.PrimaryDarkColor = PickPrimaryDark.SelectedColor;
            t.SecondaryColor = PickSecondary.SelectedColor;
            t.AccentColor = PickAccent.SelectedColor;
            t.BackgroundColor = PickBackground.SelectedColor;
            t.WindowBackgroundColor = PickWindowBg.SelectedColor;
            t.TableBackgroundColor = PickTableBg.SelectedColor;
            t.TableHeaderBackgroundColor = PickTableHeader.SelectedColor;
            t.TextPrimaryColor = PickTextPrimary.SelectedColor;
            t.TextSecondaryColor = PickTextSecondary.SelectedColor;
            t.ButtonBackgroundColor = PickBtnBg.SelectedColor;
            t.ButtonForegroundColor = PickBtnFg.SelectedColor;
            t.ButtonHoverColor = PickBtnHover.SelectedColor;
            t.ButtonPressedColor = PickBtnPressed.SelectedColor;
            t.BorderColor = PickBorder.SelectedColor;
            t.SuccessColor = PickSuccess.SelectedColor;
            t.WarningColor = PickWarning.SelectedColor;
            t.ErrorColor = PickError.SelectedColor;
            t.InfoColor = PickInfo.SelectedColor;
            t.TableRowColor = PickTableRow.SelectedColor;
            t.TableAlternateRowColor = PickTableAltRow.SelectedColor;
            t.TableSelectedRowColor = PickTableSelected.SelectedColor;
            t.TableHoverRowColor = PickTableHover.SelectedColor;
            t.TableSelectedCellColor = PickTableCellSelected.SelectedColor;
            t.ScreenBackgroundColor = PickScreenBg.SelectedColor;
            t.PageHeaderBackgroundColor = PickPageHeaderBg.SelectedColor;
            t.ChromeBackgroundColor = PickChromeBg.SelectedColor;
            t.SidebarBackgroundColor = PickSidebarBg.SelectedColor;

            // تحديث مشتقات الألوان
            t.SurfaceColor = t.TableBackgroundColor;
            t.LineColor = t.BorderColor;
            t.PrimaryButtonBackgroundColor = t.PrimaryColor;
            t.TableHeaderForegroundColor = "#FFFFFF";

            // خطوط
            t.MainFontFamily = ComboMainFont.SelectedItem?.ToString() ?? t.MainFontFamily;
            t.HeadingFontFamily = ComboHeadingFont.SelectedItem?.ToString() ?? t.HeadingFontFamily;
            t.BaseFontSize = SliderBaseFont.Value;
            t.PageTitleFontSize = SliderPageTitle.Value;
            t.SectionTitleFontSize = SliderSectionTitle.Value;
            t.TableFontSize = SliderTableFont.Value;
            t.ButtonFontSize = SliderButtonFont.Value;
            t.FieldLabelFontSize = SliderFieldLabel.Value;
            t.IsHeadingBold = CheckHeadingBold.IsChecked == true;
            t.IsBodyBold = CheckBodyBold.IsChecked == true;
            t.IsButtonBold = CheckButtonBold.IsChecked == true;
            t.HeadingFontWeight = t.IsHeadingBold ? "Bold" : "Normal";
            t.BodyFontWeight = t.IsBodyBold ? "Bold" : "Normal";

            // حدود
            t.ShowTextBoxBorder = CheckShowTextBoxBorder.IsChecked == true;
            t.ShowButtonBorder = CheckShowButtonBorder.IsChecked == true;
            t.ShowUnderline = CheckShowUnderline.IsChecked == true;
            t.BorderThickness = SliderBorderThickness.Value;
            t.TextBoxBorderThickness = SliderTextBoxBorder.Value;
            t.ButtonBorderThickness = SliderButtonBorder.Value;
            t.TableBorderThickness = SliderTableBorder.Value;
            t.CornerRadius = SliderCornerRadius.Value;
            t.ButtonCornerRadius = SliderBtnCorner.Value;
            t.TextBoxCornerRadius = SliderTextBoxCorner.Value;
            t.CardCornerRadius = SliderCardCorner.Value;
            t.FieldPadding = SliderFieldPadding.Value;
            t.ContentPadding = SliderContentPadding.Value;

            // أزرار
            t.ButtonHeight = SliderButtonHeight.Value;
            t.ButtonMinWidth = SliderButtonMinWidth.Value;
            t.ButtonIconSize = SliderIconSize.Value;
            t.ButtonHasShadow = CheckButtonShadow.IsChecked == true;

            // جداول
            t.TableRowHeight = SliderRowHeight.Value;
            t.TableHeaderHeight = SliderHeaderHeight.Value;
            t.TableShowGridLines = CheckShowGridLines.IsChecked == true;
            t.TableShowColumnSeparators = CheckShowColSep.IsChecked == true;
            t.TableShowRowSeparators = CheckShowRowSep.IsChecked == true;
            t.TableAlternateRows = CheckAltRows.IsChecked == true;
            t.TableEnableHover = CheckEnableHover.IsChecked == true;
            t.TableShowRowNumbers = CheckShowRowNumbers.IsChecked == true;

            // نوافذ
            t.TitleBarHeight = SliderTitleBar.Value;
            t.SidebarWidth = SliderSidebarWidth.Value;
            t.ShowSidebar = CheckShowSidebar.IsChecked == true;
            t.ShadowDepth = SliderShadowDepth.Value;
            t.ShadowBlur = SliderShadowBlur.Value;
            t.ShadowOpacity = SliderShadowOpacity.Value;
            t.OverallOpacity = SliderOpacity.Value;
            t.EnableShadow = CheckEnableShadow.IsChecked == true;
            t.CardHasShadow = CheckCardShadow.IsChecked == true;

            // عام
            if (RadioLight.IsChecked == true) { t.Mode = "Light"; t.IsDarkMode = false; }
            else if (RadioDark.IsChecked == true) { t.Mode = "Dark"; t.IsDarkMode = true; }
            else if (RadioAuto.IsChecked == true) { t.Mode = "Auto"; }
            t.IsDarkMode = CheckIsDark.IsChecked == true || t.Mode == "Dark";

            // اسم
            t.DisplayName = ThemeNameBox.Text?.Trim() ?? t.DisplayName;
            if (string.IsNullOrWhiteSpace(t.Name) || t.IsBuiltIn)
            {
                t.Name = $"custom_{DateTime.Now:yyyyMMdd_HHmmss}";
                t.IsBuiltIn = false;
            }

            return t;
        }

        private void ApplyLivePreview()
        {
            if (_isLoading) return;
            try
            {
                var theme = BuildThemeFromControls();
                _workingTheme = theme;
                ThemeManager.ApplyTheme(theme, false); // معاينة فقط بدون حفظ
                CurrentThemeName.Text = theme.DisplayName;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeCustomization.ApplyLivePreview");
            }
        }

        // ══════════ أحداث التحكم ══════════
        private void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cat)
            {
                // إخفاء الكل
                PanelColors.Visibility = Visibility.Collapsed;
                PanelFonts.Visibility = Visibility.Collapsed;
                PanelBorders.Visibility = Visibility.Collapsed;
                PanelButtons.Visibility = Visibility.Collapsed;
                PanelTables.Visibility = Visibility.Collapsed;
                PanelWindows.Visibility = Visibility.Collapsed;
                PanelGeneral.Visibility = Visibility.Collapsed;

                // إعادة تعيين ستايل الأزرار
                CatColors.Style = (Style)FindResource("CategoryButton");
                CatFonts.Style = (Style)FindResource("CategoryButton");
                CatBorders.Style = (Style)FindResource("CategoryButton");
                CatButtons.Style = (Style)FindResource("CategoryButton");
                CatTables.Style = (Style)FindResource("CategoryButton");
                CatWindows.Style = (Style)FindResource("CategoryButton");
                CatGeneral.Style = (Style)FindResource("CategoryButton");

                // إظهار المحدد
                switch (cat)
                {
                    case "Colors": PanelColors.Visibility = Visibility.Visible; CatColors.Style = (Style)FindResource("CategoryButtonActive"); break;
                    case "Fonts": PanelFonts.Visibility = Visibility.Visible; CatFonts.Style = (Style)FindResource("CategoryButtonActive"); break;
                    case "Borders": PanelBorders.Visibility = Visibility.Visible; CatBorders.Style = (Style)FindResource("CategoryButtonActive"); break;
                    case "Buttons": PanelButtons.Visibility = Visibility.Visible; CatButtons.Style = (Style)FindResource("CategoryButtonActive"); break;
                    case "Tables": PanelTables.Visibility = Visibility.Visible; CatTables.Style = (Style)FindResource("CategoryButtonActive"); break;
                    case "Windows": PanelWindows.Visibility = Visibility.Visible; CatWindows.Style = (Style)FindResource("CategoryButtonActive"); break;
                    case "General": PanelGeneral.Visibility = Visibility.Visible; CatGeneral.Style = (Style)FindResource("CategoryButtonActive"); break;
                }

                StatusText.Text = $"الفئة الحالية: {btn.Content}";
            }
        }

        private void Color_Changed(object sender, string color)
        {
            if (_isLoading) return;
            ApplyLivePreview();
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            UpdateSliderLabels();
            ApplyLivePreview();
        }

        private void Font_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            ApplyLivePreview();
        }

        private void Combo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            ApplyLivePreview();
        }

        private void Check_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplyLivePreview();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplyLivePreview();
        }

        private void ThemeSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (ThemeSelector.SelectedItem is ThemeSettings theme)
            {
                _isLoading = true;
                _workingTheme = theme.Clone();
                BindThemeToControls(_workingTheme);
                ThemeManager.ApplyTheme(_workingTheme, false);
                CurrentThemeName.Text = _workingTheme.DisplayName;
                ThemeNameBox.Text = _workingTheme.DisplayName;
                _isLoading = false;
                StatusText.Text = $"تم تحميل ثيم: {theme.DisplayName}";
            }
        }

        private void SavedThemesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (SavedThemesList.SelectedItem is ThemeSettings theme)
            {
                ThemeSelector.SelectedItem = theme;
            }
        }

        private void PresetTheme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string preset)
            {
                ThemeSettings theme = preset switch
                {
                    "default" => ThemeSettings.CreateDefault(),
                    "official" => ThemeSettings.CreateOfficial(),
                    "dark" => ThemeSettings.CreateDark(),
                    "light" => ThemeSettings.CreateLight(),
                    _ => ThemeSettings.CreateDefault()
                };

                _isLoading = true;
                _workingTheme = theme;
                BindThemeToControls(theme);
                ThemeManager.ApplyTheme(theme, false);
                CurrentThemeName.Text = theme.DisplayName;
                ThemeNameBox.Text = theme.DisplayName;
                _isLoading = false;
                StatusText.Text = $"تم تطبيق الثيم الجاهز: {theme.DisplayName} - اضغط حفظ لتثبيته";
            }
        }

        // ══════════ أزرار الإجراءات ══════════
        private void NewTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newTheme = ThemeSettings.CreateDefault();
                newTheme.Name = $"custom_{DateTime.Now:yyyyMMdd_HHmmss}";
                newTheme.DisplayName = $"مخصص {DateTime.Now:HH:mm}";
                newTheme.IsBuiltIn = false;

                _isLoading = true;
                _workingTheme = newTheme;
                BindThemeToControls(newTheme);
                ThemeManager.ApplyTheme(newTheme, false);
                ThemeNameBox.Text = newTheme.DisplayName;
                CurrentThemeName.Text = newTheme.DisplayName;
                _isLoading = false;

                StatusText.Text = "تم إنشاء ثيم جديد - خصصه ثم احفظه";
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.New");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var theme = BuildThemeFromControls();
                theme.Name = _workingTheme.Name; // احتفظ بالاسم الداخلي
                if (theme.IsBuiltIn)
                {
                    theme.Name = $"custom_{DateTime.Now:yyyyMMdd_HHmmss}";
                    theme.IsBuiltIn = false;
                }

                ThemeManager.SaveTheme(theme, true);
                _originalTheme = theme.Clone();
                _workingTheme = theme.Clone();

                LoadThemesList();
                LastSavedText.Text = DateTime.Now.ToString("HH:mm:ss");
                StatusText.Text = $"تم حفظ الثيم {theme.DisplayName} بنجاح - سيتم تحميله تلقائياً عند التشغيل";
                AppContainer.Get<DialogService>().Info($"تم حفظ ثيم '{theme.DisplayName}' بنجاح.\nسيتم تطبيقه تلقائياً عند تشغيل النظام.");
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.Save");
            }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = ThemeNameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    AppContainer.Get<DialogService>().Error("أدخل اسم الثيم أولاً.");
                    return;
                }

                var theme = BuildThemeFromControls();
                theme.DisplayName = name;
                theme.Name = $"custom_{name.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}";
                theme.IsBuiltIn = false;

                ThemeManager.SaveTheme(theme, true);
                _workingTheme = theme.Clone();
                _originalTheme = theme.Clone();

                LoadThemesList();
                CurrentThemeName.Text = theme.DisplayName;
                LastSavedText.Text = DateTime.Now.ToString("HH:mm:ss");
                StatusText.Text = $"تم حفظ الثيم باسم {name}";
                AppContainer.Get<DialogService>().Info($"تم حفظ الثيم '{name}' بنجاح.");
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.SaveAs");
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var theme = BuildThemeFromControls();
                ThemeManager.ApplyTheme(theme, false);
                _workingTheme = theme;
                CurrentThemeName.Text = theme.DisplayName;
                StatusText.Text = $"تم تطبيق الثيم {theme.DisplayName} (معاينة) - اضغط حفظ للتثبيت الدائم";
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.Apply");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_originalTheme != null)
                {
                    ThemeManager.ApplyTheme(_originalTheme, false);
                    _workingTheme = _originalTheme.Clone();
                    BindThemeToControls(_workingTheme);
                    StatusText.Text = "تم إلغاء التغييرات والعودة للثيم الأصلي";
                }
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.Cancel");
            }
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!AppContainer.Get<DialogService>().Confirm("هل تريد استعادة الإعدادات الافتراضية؟\nسيتم فقدان جميع التخصيصات الحالية.")) return;

                var def = ThemeSettings.CreateDefault();
                _isLoading = true;
                _workingTheme = def;
                BindThemeToControls(def);
                ThemeManager.ApplyTheme(def, true);
                CurrentThemeName.Text = def.DisplayName;
                ThemeNameBox.Text = def.DisplayName;
                _isLoading = false;

                StatusText.Text = "تمت استعادة الإعدادات الافتراضية";
                AppContainer.Get<DialogService>().Info("تمت استعادة الثيم الافتراضي بنجاح.");
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.RestoreDefaults");
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var theme = BuildThemeFromControls();
                var dlg = new SaveFileDialog
                {
                    Filter = "Theme JSON (*.json)|*.json|All Files (*.*)|*.*",
                    FileName = $"{theme.DisplayName}.json",
                    Title = "تصدير الثيم"
                };
                if (dlg.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(theme, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                    AppContainer.Get<DialogService>().Info($"تم تصدير الثيم إلى:\n{dlg.FileName}");
                }
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.Export");
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "Theme JSON (*.json)|*.json|All Files (*.*)|*.*",
                    Title = "استيراد ثيم"
                };
                if (dlg.ShowDialog() == true)
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var theme = JsonSerializer.Deserialize<ThemeSettings>(json);
                    if (theme != null)
                    {
                        theme.IsBuiltIn = false;
                        if (string.IsNullOrWhiteSpace(theme.Name))
                            theme.Name = $"imported_{DateTime.Now:yyyyMMdd_HHmmss}";

                        _isLoading = true;
                        _workingTheme = theme;
                        BindThemeToControls(theme);
                        ThemeManager.ApplyTheme(theme, false);
                        ThemeNameBox.Text = theme.DisplayName;
                        CurrentThemeName.Text = theme.DisplayName;
                        _isLoading = false;

                        StatusText.Text = $"تم استيراد ثيم {theme.DisplayName} - اضغط حفظ لتثبيته";
                    }
                }
            }
            catch (Exception ex)
            {
                AppContainer.Get<DialogService>().HandleException(ex, "Theme.Import");
            }
        }

        private void UpdateStatus()
        {
            LastSavedText.Text = _originalTheme?.ModifiedAt.ToString("dd/MM/yyyy HH:mm") ?? "—";
        }
    }
}
