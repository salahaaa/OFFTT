using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Desktop.Theming
{
    /// <summary>
    /// مدير الثيم المركزي - يطبق الثيم على النظام بالكامل بدون تعديل كل شاشة
    /// جميع عناصر النظام تستخدم Resources مركزية يتم تحديثها هنا
    /// </summary>
    public static class ThemeManager
    {
        private static ThemeSettings _current;
        private static readonly string LocalThemePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DateERP", "Themes", "current_theme.json");

        private static readonly string LocalThemesListPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DateERP", "Themes", "themes_list.json");

        public static ThemeSettings Current => _current ?? ThemeSettings.CreateDefault();
        public static event EventHandler<ThemeSettings> ThemeChanged;

        // ══════════ تحميل الثيم ══════════
        public static void Initialize()
        {
            try
            {
                BootTrace.Step("ThemeManager: بدء تحميل الثيم...");
                var loaded = LoadFromDatabase() ?? LoadFromLocalFile() ?? ThemeSettings.CreateDefault();
                ApplyTheme(loaded, false);
                BootTrace.Step($"ThemeManager: تم تحميل ثيم {loaded.DisplayName} ({loaded.Mode})");
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.Initialize");
                _current = ThemeSettings.CreateDefault();
                ApplyTheme(_current, false);
            }
        }

        private static ThemeSettings LoadFromDatabase()
        {
            try
            {
                using var scope = AppContainer.NewScope();
                var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
                var setting = db.SystemSettings.AsNoTracking()
                    .FirstOrDefault(s => s.SettingKey == "UITheme_Current");
                if (setting != null && !string.IsNullOrWhiteSpace(setting.SettingValue))
                {
                    return JsonSerializer.Deserialize<ThemeSettings>(setting.SettingValue);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.LoadFromDatabase");
            }
            return null;
        }

        private static ThemeSettings LoadFromLocalFile()
        {
            try
            {
                if (File.Exists(LocalThemePath))
                {
                    var json = File.ReadAllText(LocalThemePath);
                    if (!string.IsNullOrWhiteSpace(json))
                        return JsonSerializer.Deserialize<ThemeSettings>(json);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.LoadFromLocalFile");
            }
            return null;
        }

        // ══════════ حفظ الثيم ══════════
        public static void SaveCurrentTheme()
        {
            if (_current == null) return;
            SaveTheme(_current, true);
        }

        public static void SaveTheme(ThemeSettings theme, bool setAsCurrent = true)
        {
            try
            {
                theme.ModifiedAt = DateTime.Now;
                if (setAsCurrent)
                {
                    _current = theme;
                }

                // حفظ في ملف محلي (للعمل بدون قاعدة بيانات)
                SaveToLocalFile(theme);

                // حفظ في قاعدة البيانات
                SaveToDatabase(theme);

                // حفظ في قائمة الثيمات المحفوظة
                if (!theme.IsBuiltIn)
                {
                    SaveToThemesList(theme);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.SaveTheme");
            }
        }

        private static void SaveToLocalFile(ThemeSettings theme)
        {
            try
            {
                var dir = Path.GetDirectoryName(LocalThemePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(theme, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LocalThemePath, json);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.SaveToLocalFile");
            }
        }

        private static void SaveToDatabase(ThemeSettings theme)
        {
            try
            {
                using var scope = AppContainer.NewScope();
                var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
                var setting = db.SystemSettings.FirstOrDefault(s => s.SettingKey == "UITheme_Current");
                var json = JsonSerializer.Serialize(theme);
                if (setting == null)
                {
                    db.SystemSettings.Add(new DatesErp.Core.Domain.Entities.SystemSetting
                    {
                        SettingKey = "UITheme_Current",
                        SettingValue = json,
                        Category = "UI",
                        Description = "الثيم الحالي للنظام"
                    });
                }
                else
                {
                    setting.SettingValue = json;
                }
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.SaveToDatabase");
            }
        }

        private static void SaveToThemesList(ThemeSettings theme)
        {
            try
            {
                var list = GetSavedThemes();
                var existing = list.FirstOrDefault(t => t.Name == theme.Name);
                if (existing != null)
                {
                    list.Remove(existing);
                }
                list.Add(theme);

                var dir = Path.GetDirectoryName(LocalThemesListPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LocalThemesListPath, json);

                // حفظ في قاعدة البيانات أيضاً
                using var scope = AppContainer.NewScope();
                var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
                var setting = db.SystemSettings.FirstOrDefault(s => s.SettingKey == "UITheme_List");
                if (setting == null)
                {
                    db.SystemSettings.Add(new DatesErp.Core.Domain.Entities.SystemSetting
                    {
                        SettingKey = "UITheme_List",
                        SettingValue = json,
                        Category = "UI",
                        Description = "قائمة الثيمات المحفوظة"
                    });
                }
                else
                {
                    setting.SettingValue = json;
                }
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.SaveToThemesList");
            }
        }

        public static List<ThemeSettings> GetSavedThemes()
        {
            var result = new List<ThemeSettings>();
            try
            {
                // من قاعدة البيانات
                using var scope = AppContainer.NewScope();
                var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
                var setting = db.SystemSettings.AsNoTracking()
                    .FirstOrDefault(s => s.SettingKey == "UITheme_List");
                if (setting != null && !string.IsNullOrWhiteSpace(setting.SettingValue))
                {
                    var list = JsonSerializer.Deserialize<List<ThemeSettings>>(setting.SettingValue);
                    if (list != null) result.AddRange(list);
                }
            }
            catch { }

            try
            {
                // من الملف المحلي كاحتياط
                if (File.Exists(LocalThemesListPath) && result.Count == 0)
                {
                    var json = File.ReadAllText(LocalThemesListPath);
                    var list = JsonSerializer.Deserialize<List<ThemeSettings>>(json);
                    if (list != null) result.AddRange(list);
                }
            }
            catch { }

            // دمج مع الثيمات المدمجة
            var builtIn = ThemeSettings.GetBuiltInThemes();
            foreach (var bt in builtIn)
            {
                if (!result.Any(r => r.Name == bt.Name))
                    result.Insert(0, bt);
            }

            return result;
        }

        public static void DeleteTheme(string themeName)
        {
            try
            {
                var builtInNames = ThemeSettings.GetBuiltInThemes().Select(t => t.Name).ToHashSet();
                if (builtInNames.Contains(themeName)) return; // لا يمكن حذف المدمجة

                var list = GetSavedThemes().Where(t => t.Name != themeName).ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });

                var dir = Path.GetDirectoryName(LocalThemesListPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LocalThemesListPath, json);

                using var scope = AppContainer.NewScope();
                var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
                var setting = db.SystemSettings.FirstOrDefault(s => s.SettingKey == "UITheme_List");
                if (setting != null)
                {
                    setting.SettingValue = json;
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.DeleteTheme");
            }
        }

        // ══════════ تطبيق الثيم على النظام بالكامل ══════════
        public static void ApplyTheme(ThemeSettings theme, bool save = true)
        {
            if (theme == null) theme = ThemeSettings.CreateDefault();
            _current = theme;

            try
            {
                if (Application.Current == null) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ApplyColors(theme);
                    ApplyFonts(theme);
                    ApplySizesAndShapes(theme);
                    ApplyTableStyles(theme);
                    ApplyButtonStyles(theme);
                    ApplyCardAndWindowStyles(theme);
                });

                ThemeChanged?.Invoke(null, theme);

                if (save)
                {
                    SaveTheme(theme, true);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.ApplyTheme");
            }
        }

        private static void ApplyColors(ThemeSettings t)
        {
            var res = Application.Current.Resources;

            // ألوان أساسية
            SetColor(res, "PrimaryColor", t.PrimaryColor);
            SetColor(res, "PrimaryDarkColor", t.PrimaryDarkColor);
            SetColor(res, "SecondaryColor", t.SecondaryColor);
            SetColor(res, "AccentColor", t.AccentColor);
            SetColor(res, "LightBgColor", t.BackgroundColor);
            SetColor(res, "WindowBgColor", t.WindowBackgroundColor);
            SetColor(res, "SurfaceColor", t.SurfaceColor);
            SetColor(res, "ErpSurfaceColor", t.SurfaceColor);
            SetColor(res, "BackgroundColor", t.BackgroundColor);
            SetColor(res, "ScreenBackgroundColor", t.ScreenBackgroundColor);
            SetColor(res, "PageHeaderBackgroundColor", t.PageHeaderBackgroundColor);
            SetColor(res, "ChromeBackgroundColor", t.ChromeBackgroundColor);
            SetColor(res, "SidebarBackgroundColor", t.SidebarBackgroundColor);

            // نصوص
            SetColor(res, "TextPrimaryColor", t.TextPrimaryColor);
            SetColor(res, "TextSecondaryColor", t.TextSecondaryColor);
            SetColor(res, "ErpInkColor", t.TextPrimaryColor);
            SetColor(res, "ErpMutedColor", t.TextSecondaryColor);
            SetColor(res, "DarkTitleColor", t.TextPrimaryColor);
            SetColor(res, "MutedTextColor", t.TextSecondaryColor);
            SetColor(res, "LightMutedColor", t.TextSecondaryColor);

            // أزرار
            SetColor(res, "ButtonBackgroundColor", t.ButtonBackgroundColor);
            SetColor(res, "ButtonForegroundColor", t.ButtonForegroundColor);
            SetColor(res, "ButtonHoverColor", t.ButtonHoverColor);
            SetColor(res, "ButtonPressedColor", t.ButtonPressedColor);
            SetColor(res, "PrimaryButtonBackgroundColor", t.PrimaryButtonBackgroundColor);
            SetColor(res, "PrimaryButtonForegroundColor", t.PrimaryButtonForegroundColor);
            SetColor(res, "PrimaryButtonHoverColor", t.PrimaryButtonHoverColor);
            SetColor(res, "NavyColor", t.PrimaryButtonBackgroundColor);

            // حدود
            SetColor(res, "BorderColor", t.BorderColor);
            SetColor(res, "ErpLineColor", t.LineColor);
            SetColor(res, "LineColor", t.LineColor);

            // حالات
            SetColor(res, "SuccessColor", t.SuccessColor);
            SetColor(res, "ErpOkColor", t.SuccessColor);
            SetColor(res, "SuccessLightColor", t.SuccessLightColor);
            SetColor(res, "WarningColor", t.WarningColor);
            SetColor(res, "ErpWarnColor", t.WarningColor);
            SetColor(res, "WarningLightColor", t.WarningLightColor);
            SetColor(res, "DangerColor", t.ErrorColor);
            SetColor(res, "ErpDangerColor", t.ErrorColor);
            SetColor(res, "ErrorColor", t.ErrorColor);
            SetColor(res, "ErrorLightColor", t.ErrorLightColor);
            SetColor(res, "InfoColor", t.InfoColor);
            SetColor(res, "ErpInfoColor", t.InfoColor);
            SetColor(res, "InfoLightColor", t.InfoLightColor);
            SetColor(res, "InvalidRowColor", t.ErrorLightColor);
            SetColor(res, "AuxWarningLightColor", t.WarningLightColor);

            // جداول
            SetColor(res, "TableBackgroundColor", t.TableBackgroundColor);
            SetColor(res, "TableHeaderBackgroundColor", t.TableHeaderBackgroundColor);
            SetColor(res, "TableHeaderForegroundColor", t.TableHeaderForegroundColor);
            SetColor(res, "TableRowColor", t.TableRowColor);
            SetColor(res, "TableAlternateRowColor", t.TableAlternateRowColor);
            SetColor(res, "TableSelectedRowColor", t.TableSelectedRowColor);
            SetColor(res, "TableHoverRowColor", t.TableHoverRowColor);
            SetColor(res, "TableSelectedCellColor", t.TableSelectedCellColor);

            // تحديث الـ Brushes
            UpdateBrushes(res, t);
        }

        private static void UpdateBrushes(ResourceDictionary res, ThemeSettings t)
        {
            SetBrush(res, "PrimaryBrush", t.PrimaryColor);
            SetBrush(res, "PrimaryDarkBrush", t.PrimaryDarkColor);
            SetBrush(res, "SecondaryBrush", t.SecondaryColor);
            SetBrush(res, "AccentBrush", t.AccentColor);
            SetBrush(res, "LightBgBrush", t.BackgroundColor);
            SetBrush(res, "WindowBgBrush", t.WindowBackgroundColor);
            SetBrush(res, "SurfaceBrush", t.SurfaceColor);
            SetBrush(res, "ErpSurfaceBrush", t.SurfaceColor);
            SetBrush(res, "BackgroundBrush", t.BackgroundColor);
            SetBrush(res, "ScreenBackgroundBrush", t.ScreenBackgroundColor);
            SetBrush(res, "PageHeaderBackgroundBrush", t.PageHeaderBackgroundColor);
            SetBrush(res, "ChromeBackgroundBrush", t.ChromeBackgroundColor);
            SetBrush(res, "SidebarBackgroundBrush", t.SidebarBackgroundColor);

            SetBrush(res, "TextPrimaryBrush", t.TextPrimaryColor);
            SetBrush(res, "TextSecondaryBrush", t.TextSecondaryColor);
            SetBrush(res, "ErpInkBrush", t.TextPrimaryColor);
            SetBrush(res, "ErpMutedBrush", t.TextSecondaryColor);
            SetBrush(res, "DarkTitleBrush", t.TextPrimaryColor);
            SetBrush(res, "MutedTextBrush", t.TextSecondaryColor);
            SetBrush(res, "LightMutedBrush", t.TextSecondaryColor);

            SetBrush(res, "ButtonBackgroundBrush", t.ButtonBackgroundColor);
            SetBrush(res, "ButtonForegroundBrush", t.ButtonForegroundColor);
            SetBrush(res, "ButtonHoverBrush", t.ButtonHoverColor);
            SetBrush(res, "ButtonPressedBrush", t.ButtonPressedColor);
            SetBrush(res, "PrimaryButtonBackgroundBrush", t.PrimaryButtonBackgroundColor);
            SetBrush(res, "PrimaryButtonForegroundBrush", t.PrimaryButtonForegroundColor);
            SetBrush(res, "PrimaryButtonHoverBrush", t.PrimaryButtonHoverColor);
            SetBrush(res, "NavyBrush", t.PrimaryButtonBackgroundColor);

            SetBrush(res, "BorderBrushStd", t.BorderColor);
            SetBrush(res, "ErpLineBrush", t.LineColor);
            SetBrush(res, "LineBrush", t.LineColor);
            SetBrush(res, "BorderBrush", t.BorderColor);

            SetBrush(res, "SuccessBrush", t.SuccessColor);
            SetBrush(res, "ErpOkBrush", t.SuccessColor);
            SetBrush(res, "SuccessLightBrush", t.SuccessLightColor);
            SetBrush(res, "WarningBrush", t.WarningColor);
            SetBrush(res, "ErpWarnBrush", t.WarningColor);
            SetBrush(res, "WarningLightBrush", t.WarningLightColor);
            SetBrush(res, "DangerBrush", t.ErrorColor);
            SetBrush(res, "ErpDangerBrush", t.ErrorColor);
            SetBrush(res, "ErrorBrush", t.ErrorColor);
            SetBrush(res, "ErrorLightBrush", t.ErrorLightColor);
            SetBrush(res, "InfoBrush", t.InfoColor);
            SetBrush(res, "ErpInfoBrush", t.InfoColor);
            SetBrush(res, "InfoLightBrush", t.InfoLightColor);
            SetBrush(res, "InvalidRowBrush", t.ErrorLightColor);
            SetBrush(res, "AuxWarningLightBrush", t.WarningLightColor);

            SetBrush(res, "TableBackgroundBrush", t.TableBackgroundColor);
            SetBrush(res, "TableHeaderBackgroundBrush", t.TableHeaderBackgroundColor);
            SetBrush(res, "TableHeaderForegroundBrush", t.TableHeaderForegroundColor);
            SetBrush(res, "TableRowBrush", t.TableRowColor);
            SetBrush(res, "TableAlternateRowBrush", t.TableAlternateRowColor);
            SetBrush(res, "TableSelectedRowBrush", t.TableSelectedRowColor);
            SetBrush(res, "TableHoverRowBrush", t.TableHoverRowColor);
            SetBrush(res, "TableSelectedCellBrush", t.TableSelectedCellColor);
            SetBrush(res, "GrayTextBrush", t.TextSecondaryColor);
        }

        private static void ApplyFonts(ThemeSettings t)
        {
            var res = Application.Current.Resources;

            try
            {
                res["BodyFont"] = new FontFamily(t.MainFontFamily);
                res["HeadFont"] = new FontFamily(t.HeadingFontFamily);
                res["MainFontFamily"] = new FontFamily(t.MainFontFamily);
                res["HeadingFontFamily"] = new FontFamily(t.HeadingFontFamily);

                res["UiFontSize"] = t.BaseFontSize;
                res["BaseFontSize"] = t.BaseFontSize;
                res["PageTitleFontSize"] = t.PageTitleFontSize;
                res["SectionTitleFontSize"] = t.SectionTitleFontSize;
                res["TableFontSize"] = t.TableFontSize;
                res["TableHeaderFontSize"] = t.TableHeaderFontSize;
                res["ButtonFontSize"] = t.ButtonFontSize;
                res["FieldLabelFontSize"] = t.FieldLabelFontSize;

                res["HeadingFontWeight"] = ParseFontWeight(t.HeadingFontWeight);
                res["BodyFontWeight"] = ParseFontWeight(t.BodyFontWeight);
                res["IsHeadingBold"] = t.IsHeadingBold;
                res["IsBodyBold"] = t.IsBodyBold;
                res["IsButtonBold"] = t.IsButtonBold;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ThemeManager.ApplyFonts");
            }
        }

        private static void ApplySizesAndShapes(ThemeSettings t)
        {
            var res = Application.Current.Resources;

            res["BorderThicknessValue"] = t.BorderThickness;
            res["TextBoxBorderThicknessValue"] = t.TextBoxBorderThickness;
            res["ButtonBorderThicknessValue"] = t.ButtonBorderThickness;
            res["TableBorderThicknessValue"] = t.TableBorderThickness;

            res["CornerRadiusValue"] = new CornerRadius(t.CornerRadius);
            res["ButtonCornerRadiusValue"] = new CornerRadius(t.ButtonCornerRadius);
            res["TextBoxCornerRadiusValue"] = new CornerRadius(t.TextBoxCornerRadius);
            res["CardCornerRadiusValue"] = new CornerRadius(t.CardCornerRadius);

            res["CornerRadiusDouble"] = t.CornerRadius;
            res["ButtonCornerRadiusDouble"] = t.ButtonCornerRadius;
            res["TextBoxCornerRadiusDouble"] = t.TextBoxCornerRadius;
            res["CardCornerRadiusDouble"] = t.CardCornerRadius;

            res["ButtonHeight"] = t.ButtonHeight;
            res["ButtonMinWidth"] = t.ButtonMinWidth;
            res["ButtonIconSize"] = t.ButtonIconSize;
            res["TitleBarHeight"] = t.TitleBarHeight;
            res["ContentPaddingValue"] = t.ContentPadding;
            res["SidebarWidthValue"] = t.SidebarWidth;
            res["ShadowDepthValue"] = t.ShadowDepth;
            res["ShadowBlurValue"] = t.ShadowBlur;
            res["ShadowOpacityValue"] = t.ShadowOpacity;

            res["FieldPaddingValue"] = new Thickness(t.FieldPadding);
            res["ButtonPaddingValue"] = new Thickness(t.ButtonPaddingH, t.ButtonPaddingV, t.ButtonPaddingH, t.ButtonPaddingV);
            res["ContentPaddingThickness"] = new Thickness(t.ContentPadding);

            // Booleans
            res["ShowUnderline"] = t.ShowUnderline;
            res["ShowTextBoxBorder"] = t.ShowTextBoxBorder;
            res["ShowButtonBorder"] = t.ShowButtonBorder;
            res["ShowSidebar"] = t.ShowSidebar;
            res["EnableShadow"] = t.EnableShadow;
            res["CardHasShadow"] = t.CardHasShadow;
            res["ButtonHasShadow"] = t.ButtonHasShadow;
            res["IsDarkMode"] = t.IsDarkMode;

            // Shapes
            res["InputShape"] = t.InputShape;
            res["ButtonShape"] = t.ButtonShape;
            res["ButtonStyle"] = t.ButtonStyle;
            res["CardShape"] = t.CardShape;
            res["SidebarShape"] = t.SidebarShape;
        }

        private static void ApplyTableStyles(ThemeSettings t)
        {
            var res = Application.Current.Resources;
            res["TableRowHeight"] = t.TableRowHeight;
            res["TableHeaderHeight"] = t.TableHeaderHeight;
            res["TableShowGridLines"] = t.TableShowGridLines;
            res["TableShowColumnSeparators"] = t.TableShowColumnSeparators;
            res["TableShowRowSeparators"] = t.TableShowRowSeparators;
            res["TableTextAlignment"] = t.TableTextAlignment;
            res["TableHeaderAlignment"] = t.TableHeaderAlignment;
            res["TableShowRowNumbers"] = t.TableShowRowNumbers;
            res["TableAlternateRows"] = t.TableAlternateRows;
            res["TableEnableHover"] = t.TableEnableHover;
        }

        private static void ApplyButtonStyles(ThemeSettings t)
        {
            var res = Application.Current.Resources;
            res["ButtonTextColor"] = ParseColor(t.ButtonTextColor);
            res["ButtonTextBrush"] = new SolidColorBrush(ParseColor(t.ButtonTextColor));
            res["ButtonIconPosition"] = t.ButtonIconPosition;
        }

        private static void ApplyCardAndWindowStyles(ThemeSettings t)
        {
            var res = Application.Current.Resources;
            res["OverallOpacity"] = t.OverallOpacity;
        }

        // ══════════ أدوات مساعدة ══════════
        private static void SetColor(ResourceDictionary res, string key, string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return;
                var color = ParseColor(hex);
                if (res.Contains(key))
                    res[key] = color;
                else
                    res.Add(key, color);
            }
            catch { }
        }

        private static void SetBrush(ResourceDictionary res, string key, string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return;
                var color = ParseColor(hex);
                var brush = new SolidColorBrush(color);
                // تجميد الفرشاة للأداء
                if (brush.CanFreeze) brush.Freeze();
                if (res.Contains(key))
                    res[key] = brush;
                else
                    res.Add(key, brush);
            }
            catch { }
        }

        public static Color ParseColor(string hex)
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

        private static FontWeight ParseFontWeight(string weight)
        {
            return weight?.ToLower() switch
            {
                "bold" => FontWeights.Bold,
                "semibold" => FontWeights.SemiBold,
                "light" => FontWeights.Light,
                "extrabold" => FontWeights.ExtraBold,
                "black" => FontWeights.Black,
                "medium" => FontWeights.Medium,
                _ => FontWeights.Normal
            };
        }

        public static void RestoreDefaults()
        {
            var def = ThemeSettings.CreateDefault();
            ApplyTheme(def, true);
        }

        public static List<string> GetAvailableFontFamilies()
        {
            return new List<string>
            {
                "Segoe UI, Tahoma, Arial",
                "Tahoma, Segoe UI, Arial",
                "Arial, Segoe UI, Tahoma",
                "Cairo, Segoe UI, Tahoma",
                "Tajawal, Segoe UI, Tahoma",
                "Amiri, Times New Roman, serif",
                "Segoe UI",
                "Tahoma",
                "Arial",
                "Calibri",
                "Verdana"
            };
        }
    }
}
