# =====================================================================
#  VERIFY-SOURCE 1.50.73 — فحص على مستوى البايْت
#  1) كل ملف .xaml تحت src يجب أن يبدأ بـ BOM  (EF BB BF)
#     — بِلاد BOM يقرأ مُترجم XAML الملف كـ cp1256 فيُشفَّر النص العربي
#     (هذا هو تشخيص زر الحفظ المشفّر: ملف قديم بلا BOM على القرص).
#  2) علامات المحتوى الجديد 1.50.73 في الملفات الحرجة.
#  الملف بـ BOM عمدًا — PowerShell يقرأ بلا BOM كـ ANSI فتتلف العربية.
# =====================================================================
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$fails = 0

Write-Host '============================================================'
Write-Host '  1. فحص BOM لكل ملفات XAML  —  بايْت بايْت'
Write-Host '============================================================'
if (-not (Test-Path $src)) { Write-Host '[FAIL] لم يُعثر على src — شغّل الفحص من جذر UPDATE_WORK' -ForegroundColor Red; exit 1 }
$xaml = Get-ChildItem -Path $src -Recurse -Filter *.xaml -File
if ($xaml.Count -eq 0) { Write-Host '[FAIL] لا ملفات XAML تحت src' -ForegroundColor Red; exit 1 }
$nobom = 0
foreach ($f in $xaml) {
    $fs = [System.IO.File]::OpenRead($f.FullName)
    try {
        $b = New-Object byte[] 3
        $n = 0
        while ($n -lt 3) { $m = $fs.Read($b, $n, 3 - $n); if ($m -le 0) { break }; $n += $m }
    } finally { $fs.Close() }
    if ($n -lt 3 -or $b[0] -ne 0xEF -or $b[1] -ne 0xBB -or $b[2] -ne 0xBF) {
        $rel = $f.FullName.Substring($root.Length + 1)
        Write-Host ('[FAIL] بلا BOM:  ' + $rel) -ForegroundColor Red
        $nobom++; $fails++
    }
}
if ($nobom -eq 0) { Write-Host ('[OK]   ' + $xaml.Count + ' ملفات XAML — كلها ببداية BOM صحيحة') -ForegroundColor Green }

Write-Host ''
Write-Host '============================================================'
Write-Host '  2. علامات المحتوى الجديد 1.50.73'
Write-Host '============================================================'
function Test-Marker {
    param([string]$rel, [string]$needle, [string]$label)
    $p = Join-Path $root $rel
    if (-not (Test-Path $p)) { Write-Host ('[FAIL] الملف مفقود:  ' + $rel) -ForegroundColor Red; return 1 }
    $c = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
    if ($c.Contains($needle)) { Write-Host ('[OK]   ' + $rel + '  —  ' + $label) -ForegroundColor Green; return 0 }
    Write-Host ('[FAIL] ' + $rel + '  —  ' + $label) -ForegroundColor Red
    return 1
}
$fails += Test-Marker 'src\DatesErp.Desktop\Views\Screens\PlanningView.xaml' 'حفظ الخطة' 'زر الحفظ موجود'
$fails += Test-Marker 'src\DatesErp.Desktop\Views\Screens\PlanningView.xaml' 'BorderThickness="1.4"' 'الحدود المغمّقة مطبّقة'
$fails += Test-Marker 'src\DatesErp.Desktop\Views\ErpToolbar.cs' 'حفظ الخطة (F10)' 'تسمية حفظ شريط الأدوات'
$fails += Test-Marker 'src\DatesErp.Desktop\DatesErp.Desktop.csproj' '1.50.73' 'الإصدار 1.50.73'

Write-Host ''
if ($fails -gt 0) {
    Write-Host ('[STOP] ' + $fails + ' فحصًا فاشلًا — احذف الملفات الفاشلة من UPDATE_WORK') -ForegroundColor Red
    Write-Host '        ثم أعد فك ضغط حزمة UPDATE_1.50.73_FIXED5 فوق UPDATE_WORK وأعد التشغيل.' -ForegroundColor Red
    exit 1
}
Write-Host '[PASS] كل ملفات XAML ببداية BOM وكل العلامات حديثة — البناء يمكن أن يبدأ.' -ForegroundColor Green
exit 0
