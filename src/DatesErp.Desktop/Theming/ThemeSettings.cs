using System;
using System.Collections.Generic;
using System.Windows;

namespace DatesErp.Desktop.Theming
{
    /// <summary>
    /// إعدادات الثيم الشاملة - قابلة للتخصيص بالكامل من شاشة واحدة
    /// جميع القيم قابلة للتعديل مركزياً وتنعكس على النظام بالكامل
    /// </summary>
    public class ThemeSettings
    {
        // ══════════ معلومات الثيم ══════════
        public string Name { get; set; } = "الافتراضي";
        public string DisplayName { get; set; } = "الافتراضي";
        public string Mode { get; set; } = "Light"; // Light / Dark / Auto
        public bool IsBuiltIn { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        // ══════════ 1. الألوان ══════════
        public string PrimaryColor { get; set; } = "#14532D"; // اللون الرئيسي
        public string PrimaryDarkColor { get; set; } = "#0B3D1F";
        public string SecondaryColor { get; set; } = "#C9A227"; // اللون الثانوي
        public string AccentColor { get; set; } = "#C9A227";
        public string BackgroundColor { get; set; } = "#F1F5F9"; // لون الخلفيات
        public string WindowBackgroundColor { get; set; } = "#F1F5F9"; // خلفية النوافذ
        public string SurfaceColor { get; set; } = "#F8FAFC";
        public string TableBackgroundColor { get; set; } = "#FFFFFF"; // خلفية الجداول
        public string TableHeaderBackgroundColor { get; set; } = "#0A246A"; // رؤوس الجداول
        public string TableHeaderForegroundColor { get; set; } = "#FFFFFF";
        public string TextPrimaryColor { get; set; } = "#1E293B"; // النص الرئيسي
        public string TextSecondaryColor { get; set; } = "#64748B"; // النص الثانوي
        public string ButtonBackgroundColor { get; set; } = "#F8FAFC"; // لون الأزرار
        public string ButtonForegroundColor { get; set; } = "#1F2937";
        public string ButtonHoverColor { get; set; } = "#FEF9EC"; // Hover
        public string ButtonPressedColor { get; set; } = "#E2E8F0"; // Pressed
        public string PrimaryButtonBackgroundColor { get; set; } = "#0A246A";
        public string PrimaryButtonForegroundColor { get; set; } = "#FFFFFF";
        public string PrimaryButtonHoverColor { get; set; } = "#123A8F";
        public string BorderColor { get; set; } = "#94A3B8"; // الحدود
        public string LineColor { get; set; } = "#CBD5E1";

        // ألوان الحالات
        public string SuccessColor { get; set; } = "#15803D";
        public string SuccessLightColor { get; set; } = "#DCFCE7";
        public string WarningColor { get; set; } = "#B45309";
        public string WarningLightColor { get; set; } = "#FEF3C7";
        public string ErrorColor { get; set; } = "#B91C1C";
        public string ErrorLightColor { get; set; } = "#FEE2E2";
        public string InfoColor { get; set; } = "#0284C7";
        public string InfoLightColor { get; set; } = "#E0F2FE";

        // ألوان إضافية للجداول
        public string TableRowColor { get; set; } = "#FFFFFF";
        public string TableAlternateRowColor { get; set; } = "#F8FAFC";
        public string TableSelectedRowColor { get; set; } = "#FEF3C7";
        public string TableHoverRowColor { get; set; } = "#E8EEF7";
        public string TableSelectedCellColor { get; set; } = "#C9A227";

        // ══════════ 2. الخطوط ══════════
        public string MainFontFamily { get; set; } = "Segoe UI, Tahoma, Arial";
        public string HeadingFontFamily { get; set; } = "Segoe UI, Tahoma, Arial";
        public double BaseFontSize { get; set; } = 12.5;
        public double PageTitleFontSize { get; set; } = 18;
        public double SectionTitleFontSize { get; set; } = 15;
        public double TableFontSize { get; set; } = 12.5;
        public double TableHeaderFontSize { get; set; } = 12;
        public double ButtonFontSize { get; set; } = 12;
        public double FieldLabelFontSize { get; set; } = 12;
        public bool IsHeadingBold { get; set; } = true;
        public bool IsBodyBold { get; set; } = false;
        public bool IsButtonBold { get; set; } = true;
        public string HeadingFontWeight { get; set; } = "Bold"; // Bold / Normal / SemiBold
        public string BodyFontWeight { get; set; } = "Normal";

        // ══════════ 3. التسطير والحدود ══════════
        public bool ShowUnderline { get; set; } = false;
        public bool ShowTextBoxBorder { get; set; } = true;
        public bool ShowButtonBorder { get; set; } = true;
        public double BorderThickness { get; set; } = 1.2;
        public double TextBoxBorderThickness { get; set; } = 1.2;
        public double ButtonBorderThickness { get; set; } = 1.2;
        public double CornerRadius { get; set; } = 6;
        public double ButtonCornerRadius { get; set; } = 6;
        public double TextBoxCornerRadius { get; set; } = 4;
        public double CardCornerRadius { get; set; } = 8;
        public string InputShape { get; set; } = "Rounded"; // Flat / Rounded / Simple
        public double Padding { get; set; } = 6;
        public double FieldPadding { get; set; } = 6;
        public double ButtonPaddingH { get; set; } = 14;
        public double ButtonPaddingV { get; set; } = 6;

        // ══════════ 4. شكل الأزرار ══════════
        public string ButtonShape { get; set; } = "Rounded"; // Square / Rounded / Flat / Border
        public string ButtonStyle { get; set; } = "Default"; // Default / Flat / Border / Gradient
        public string ButtonTextColor { get; set; } = "#1F2937";
        public double ButtonHeight { get; set; } = 30;
        public double ButtonMinWidth { get; set; } = 92;
        public double ButtonIconSize { get; set; } = 14;
        public string ButtonIconPosition { get; set; } = "Left"; // Left / Right / Top / None
        public bool ButtonHasShadow { get; set; } = false;

        // ══════════ 5. شكل الجداول ══════════
        public double TableRowHeight { get; set; } = 32;
        public double TableHeaderHeight { get; set; } = 32;
        public double TableBorderThickness { get; set; } = 1;
        public bool TableShowGridLines { get; set; } = true;
        public bool TableShowColumnSeparators { get; set; } = true;
        public bool TableShowRowSeparators { get; set; } = true;
        public string TableTextAlignment { get; set; } = "Right"; // Right / Left / Center
        public string TableHeaderAlignment { get; set; } = "Center";
        public bool TableShowRowNumbers { get; set; } = false;
        public bool TableAlternateRows { get; set; } = true;
        public bool TableEnableHover { get; set; } = true;

        // ══════════ 6. شكل النوافذ والشاشات ══════════
        public string ScreenBackgroundColor { get; set; } = "#F1F5F9";
        public string PageHeaderBackgroundColor { get; set; } = "#FFFFFF";
        public string ChromeBackgroundColor { get; set; } = "#0A246A";
        public string SidebarBackgroundColor { get; set; } = "#0F172A";
        public double TitleBarHeight { get; set; } = 40;
        public double ContentPadding { get; set; } = 12;
        public string SidebarShape { get; set; } = "Default"; // Default / Rounded / Flat
        public double SidebarWidth { get; set; } = 260;
        public bool ShowSidebar { get; set; } = true;
        public string CardShape { get; set; } = "Rounded"; // Rounded / Square / Flat
        public bool CardHasShadow { get; set; } = true;
        public double ShadowDepth { get; set; } = 1;
        public double ShadowBlur { get; set; } = 8;
        public double ShadowOpacity { get; set; } = 0.12;
        public bool EnableShadow { get; set; } = true;

        // ══════════ 7. الوضع العام ══════════
        public bool IsDarkMode { get; set; } = false;
        public double OverallOpacity { get; set; } = 1.0;

        // ══════════ دوال مساعدة ══════════
        public ThemeSettings Clone()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this);
            return System.Text.Json.JsonSerializer.Deserialize<ThemeSettings>(json);
        }

        public static ThemeSettings CreateDefault()
        {
            return new ThemeSettings
            {
                Name = "default",
                DisplayName = "الافتراضي",
                Mode = "Light",
                IsBuiltIn = true,
                PrimaryColor = "#14532D",
                PrimaryDarkColor = "#0B3D1F",
                SecondaryColor = "#C9A227",
                AccentColor = "#C9A227",
                BackgroundColor = "#F1F5F9",
                WindowBackgroundColor = "#F1F5F9",
                SurfaceColor = "#F8FAFC",
                TableBackgroundColor = "#FFFFFF",
                TableHeaderBackgroundColor = "#0A246A",
                TableHeaderForegroundColor = "#FFFFFF",
                TextPrimaryColor = "#1E293B",
                TextSecondaryColor = "#64748B",
                ButtonBackgroundColor = "#F8FAFC",
                ButtonForegroundColor = "#1F2937",
                ButtonHoverColor = "#FEF9EC",
                ButtonPressedColor = "#E2E8F0",
                PrimaryButtonBackgroundColor = "#0A246A",
                PrimaryButtonForegroundColor = "#FFFFFF",
                PrimaryButtonHoverColor = "#123A8F",
                BorderColor = "#94A3B8",
                LineColor = "#CBD5E1",
                SuccessColor = "#15803D",
                WarningColor = "#B45309",
                ErrorColor = "#B91C1C",
                InfoColor = "#0284C7"
            };
        }

        public static ThemeSettings CreateOfficial()
        {
            var t = CreateDefault();
            t.Name = "official";
            t.DisplayName = "رسمي";
            t.PrimaryColor = "#0A246A";
            t.PrimaryDarkColor = "#061845";
            t.SecondaryColor = "#1E3A8A";
            t.AccentColor = "#C9A227";
            t.BackgroundColor = "#F8FAFC";
            t.WindowBackgroundColor = "#FFFFFF";
            t.TableHeaderBackgroundColor = "#0A246A";
            t.BorderColor = "#64748B";
            t.CornerRadius = 4;
            t.ButtonCornerRadius = 4;
            t.CardCornerRadius = 6;
            t.IsHeadingBold = true;
            t.HeadingFontWeight = "Bold";
            return t;
        }

        public static ThemeSettings CreateDark()
        {
            var t = CreateDefault();
            t.Name = "dark";
            t.DisplayName = "داكن";
            t.Mode = "Dark";
            t.IsDarkMode = true;
            t.PrimaryColor = "#1E3A5F";
            t.PrimaryDarkColor = "#0F172A";
            t.SecondaryColor = "#C9A227";
            t.AccentColor = "#F59E0B";
            t.BackgroundColor = "#0F172A";
            t.WindowBackgroundColor = "#1E293B";
            t.SurfaceColor = "#1E293B";
            t.TableBackgroundColor = "#1E293B";
            t.TableHeaderBackgroundColor = "#0F172A";
            t.TableHeaderForegroundColor = "#F1F5F9";
            t.TextPrimaryColor = "#F1F5F9";
            t.TextSecondaryColor = "#94A3B8";
            t.ButtonBackgroundColor = "#334155";
            t.ButtonForegroundColor = "#F1F5F9";
            t.ButtonHoverColor = "#475569";
            t.ButtonPressedColor = "#1E293B";
            t.PrimaryButtonBackgroundColor = "#1E40AF";
            t.PrimaryButtonForegroundColor = "#FFFFFF";
            t.BorderColor = "#475569";
            t.LineColor = "#334155";
            t.ScreenBackgroundColor = "#0F172A";
            t.PageHeaderBackgroundColor = "#1E293B";
            t.ChromeBackgroundColor = "#020617";
            t.SidebarBackgroundColor = "#020617";
            t.TableRowColor = "#1E293B";
            t.TableAlternateRowColor = "#0F172A";
            t.TableSelectedRowColor = "#1E3A5F";
            t.TableHoverRowColor = "#334155";
            return t;
        }

        public static ThemeSettings CreateLight()
        {
            var t = CreateDefault();
            t.Name = "light";
            t.DisplayName = "فاتح";
            t.Mode = "Light";
            t.IsDarkMode = false;
            t.PrimaryColor = "#2563EB";
            t.PrimaryDarkColor = "#1D4ED8";
            t.SecondaryColor = "#0EA5E9";
            t.AccentColor = "#06B6D4";
            t.BackgroundColor = "#F8FAFC";
            t.WindowBackgroundColor = "#FFFFFF";
            t.SurfaceColor = "#FFFFFF";
            t.TableBackgroundColor = "#FFFFFF";
            t.TableHeaderBackgroundColor = "#2563EB";
            t.BorderColor = "#E2E8F0";
            t.LineColor = "#F1F5F9";
            t.CornerRadius = 8;
            t.ButtonCornerRadius = 8;
            t.CardCornerRadius = 12;
            return t;
        }

        public static List<ThemeSettings> GetBuiltInThemes()
        {
            return new List<ThemeSettings>
            {
                CreateDefault(),
                CreateOfficial(),
                CreateDark(),
                CreateLight()
            };
        }
    }
}
