# نظام إدارة وتخصيص الثيم (Theme & UI Appearance Manager)

## نظرة عامة
نظام مركزي لتخصيص مظهر النظام بالكامل من شاشة واحدة بدون تعديل XAML أو الكود.
جميع الشاشات تستخدم Resources مركزية يتم تحديثها عبر `ThemeManager`.

## المكونات

### 1. نموذج الثيم `ThemeSettings.cs`
`src/DatesErp.Desktop/Theming/ThemeSettings.cs`
- يحتوي على جميع خصائص التخصيص:
  - **الألوان**: Primary, Secondary, Background, Window, Table, Text, Button, Border, Success/Warning/Error/Info
  - **الخطوط**: MainFont, HeadingFont, أحجام (Base, PageTitle, SectionTitle, Table, Button, FieldLabel), Bold
  - **الحدود**: إظهار/إخفاء, سمك, CornerRadius, InputShape, Padding
  - **الأزرار**: شكل (Square/Rounded/Flat/Border), نص, ارتفاع, أيقونة, موضع, ظل
  - **الجداول**: ألوان الصفوف, ارتفاع, حدود, فصل أعمدة, محاذاة, ترقيم
  - **النوافذ**: خلفيات, ارتفاع شريط العنوان, عرض القائمة, بطاقات, ظلال
  - **الوضع العام**: Light/Dark/Auto, ثيمات جاهزة

- يوفر ثيمات مدمجة:
  - `CreateDefault()` - الافتراضي (Onyx Classic)
  - `CreateOfficial()` - رسمي كحلي
  - `CreateDark()` - داكن
  - `CreateLight()` - فاتح

### 2. مدير الثيم `ThemeManager.cs`
`src/DatesErp.Desktop/Theming/ThemeManager.cs`
- **مركزي**: جميع الشاشات تستخدمه
- **التحميل**: 
  - من قاعدة البيانات `SystemSettings` (مفتاح `UITheme_Current`)
  - احتياط من ملف محلي `%LocalAppData%\DateERP\Themes\current_theme.json`
  - افتراضي إن لم يوجد
- **الحفظ**: 
  - في قاعدة البيانات + ملف محلي
  - قائمة الثيمات `UITheme_List`
  - JSON مع WriteIndented
- **التطبيق**:
  - `ApplyTheme(theme, save)` يحدث `Application.Current.Resources`
  - يحدث جميع الألوان `Color` و `Brush`
  - يحدث الخطوط `FontFamily` و الأحجام `Double`
  - يحدث الزوايا `CornerRadius` و السماكات و المسافات
  - يطلق حدث `ThemeChanged`
- **الدوال**:
  - `Initialize()` - عند الإقلاع
  - `GetSavedThemes()` - قائمة الثيمات
  - `SaveTheme()`, `DeleteTheme()`, `RestoreDefaults()`
  - `ParseColor()`, `GetAvailableFontFamilies()`

### 3. الثيم الأساسي `DateErpTheme.xaml`
`src/DatesErp.Desktop/Themes/DateErpTheme.xaml`
- تم تحويله ليستخدم `DynamicResource` بدل `StaticResource` لجميع الألوان والخطوط
- يحتوي على جميع المفاتيح القابلة للتخصيص مع قيم افتراضية
- الأنماط (Styles) تستخدم الموارد الديناميكية:
  - `Window`, `DataGrid`, `TextBox`, `ComboBox`, `DatePicker`, `PasswordBox`, `CheckBox`, `TextBlock`, `ListBox`
  - `ErpButton`, `ErpPrimaryButton`, `ErpApproveButton`, `ErpDangerButton`
  - `Card`, `WhiteCard`, `NavItem`, `FieldLabel`, `SectionTitle`, `PageTitle`

### 4. منتقي الألوان `ColorPickerControl`
`src/DatesErp.Desktop/Views/ColorPickerControl.xaml`
- معاينة اللون + حقل Hex + زر 🎨
- Popup مع 32 لون جاهز + إدخال مخصص
- حدث `ColorChanged`

### 5. شاشة التخصيص `ThemeCustomizationView`
`src/DatesErp.Desktop/Views/Screens/ThemeCustomizationView.xaml`
- **الصلاحيات**: فقط `settings / Edit`
- **التقسيم**:
  - شريط علوي: اسم الثيم + اختيار ثيم + جديد/حفظ/تطبيق/إلغاء/افتراضي
  - يسار (200px): فئات (ألوان, خطوط, حدود, أزرار, جداول, نوافذ, عام) + قائمة الثيمات المحفوظة
  - وسط: إعدادات الفئة المختارة مع Sliders, CheckBoxes, ComboBoxes, ColorPickers
  - يمين (380px): معاينة مباشرة (عنوان, TextBox, ComboBox, أزرار, جدول, بطاقة, رسائل حالات, نصوص)
  - سفلي: حالة + آخر حفظ

- **المعاينة المباشرة**: كل تغيير يطبق فوراً عبر `ThemeManager.ApplyTheme(..., false)` بدون حفظ
- **الأزرار**:
  - `تطبيق` - معاينة على النظام الحالي بدون حفظ دائم
  - `حفظ` - حفظ وتثبيت دائم (قاعدة بيانات + ملف)
  - `إلغاء` - عودة للأصلي
  - `افتراضي` - استعادة الافتراضي
  - `حفظ باسم` - إنشاء ثيم جديد
  - `تصدير/استيراد` - JSON

### 6. التكامل مع الإقلاع `Bootstrapper.cs`
- بعد `AppContainer.Build()` يتم `ThemeManager.Initialize()` (تحميل من ملف محلي)
- بعد ترحيل قاعدة البيانات يتم إعادة التحميل من قاعدة البيانات (أحدث)

### 7. كتالوج الشاشات `ScreenCatalog.cs` + `ScreenFactory.cs`
- إضافة شاشة `theme` في مجموعة `إدارة النظام`
- كود `theme`, وحدة `settings`, أيقونة 🎨, كود شاشة `MRPSYS1004`

## التخزين
- **قاعدة البيانات**: جدول `SystemSettings`
  - `UITheme_Current` - JSON للثيم الحالي
  - `UITheme_List` - JSON لقائمة الثيمات المخصصة
- **ملف محلي**: 
  - `%LocalAppData%\DateERP\Themes\current_theme.json`
  - `%LocalAppData%\DateERP\Themes\themes_list.json`
- يعمل بدون قاعدة بيانات (وضع محلي)

## الصلاحيات
- شاشة الثيم تتطلب `settings / Edit`
- فقط مدير النظام يراها
- بوابة مركزية `PermissionGate.Can("settings", "Edit")`

## الالتزام بالمتطلبات
- ✅ لا تغيير لمنطق الأعمال أو المخزون
- ✅ لا تكرار Style داخل كل شاشة - مركزي فقط
- ✅ لا حذف تصميم أو وظيفة
- ✅ جميع القيم قابلة للتعديل مركزياً
- ✅ أسماء واضحة
- ✅ RTL متوافق
- ✅ لا يكسر الشاشات (أحجام محدودة بحدود آمنة)
- ✅ زر استعادة افتراضي + تطبيق + حفظ + إلغاء
- ✅ معاينة مباشرة قبل الحفظ
- ✅ يطبق على النظام بالكامل بدون تعديل كل شاشة
- ✅ حفظ في إعدادات النظام/قاعدة البيانات
- ✅ تحميل تلقائي عند التشغيل
- ✅ دعم Light/Dark/Auto + ثيمات متعددة

## كيفية الاستخدام
1. افتح `إدارة النظام > تخصيص النظام والمظهر`
2. اختر فئة من اليسار
3. عدل الألوان عبر منتقي الألوان (Hex أو جاهز)
4. عدل الأحجام عبر Sliders
5. شاهد المعاينة في اليمين تتغير فوراً
6. اضغط `تطبيق` لتجربة على النظام كامل
7. اضغط `حفظ` لتثبيت دائم
8. أنشئ ثيم جديد عبر `حفظ باسم` أو `جديد`

## الاختبار
- تم اختبار المنطق: الألوان والخطوط والأحجام تنعكس عبر DynamicResource
- جميع الشاشات تستخدم نفس الموارد المركزية
- لا حاجة لتعديل كل شاشة يدوياً
- يعمل في الوضع الفاتح والداكن
- RTL محفوظ

## ملاحظات تقنية
- يستخدم `System.Text.Json` للتسلسل
- `SolidColorBrush` مجمدة (Freeze) للأداء
- `Application.Current.Dispatcher.Invoke` لضمان التطبيق في UI Thread
- معالجة أخطاء شاملة مع `ErrorLog`
- لا يؤثر على أداء الإقلاع (تحميل ملف JSON صغير)
