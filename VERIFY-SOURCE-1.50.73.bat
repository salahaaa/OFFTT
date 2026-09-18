@echo off
chcp 65001 >nul
REM =====================================================================
REM  VERIFY-SOURCE 1.50.73 — فحص سلامة المصدر قبل البناء
REM  الجزء الأول: علامات محتوى بالمحتوى لا بالطابع الزمني.
REM  الجزء الثاني: فحص BOM بايْت بايْت لكل XAML عبر
REM  VERIFY-SOURCE-1.50.73.ps1 — يكشف أي ملف قديم فقد BOM،
REM  وهو السبب المباشر لنص مشفّر في الأزرار مثل زر الحفظ.
REM  هذا الملف بلا BOM عمدًا — cmd يفسر BOM كحروف ويكسر أول أمر.
REM =====================================================================
setlocal
cd /d "%~dp0"
echo ============================================================
echo   فحص سلامة المصدر 1.50.73 - قبل البناء
echo ============================================================
set FAILS=0

set "F1=src\DatesErp.Desktop\Views\Screens\PlanningView.xaml"
findstr /m "حفظ الخطة" "%F1%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F1% - نسخة قديمة بلا زر الحفظ & set /a FAILS+=1) else (echo [OK]   %F1%)

set "F2=src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs"
findstr /m "حفظ الخطة" "%F2%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F2% - نسخة قديمة بلا تسمية الحفظ & set /a FAILS+=1) else (echo [OK]   %F2%)

set "F3=src\DatesErp.Desktop\DatesErp.Desktop.csproj"
findstr /m "1.50.73" "%F3%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F3% - الإصدار ليس 1.50.73 & set /a FAILS+=1) else (echo [OK]   %F3%)

set "F4=src\DatesErp.Desktop\DatesErp.Desktop.csproj"
findstr /m "AssemblyFileVersion>1.50.73" "%F4%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F4% - نسخة قديمة بلا سمات الإصدار الصريحة & set /a FAILS+=1) else (echo [OK]   %F4%)

set "F5=src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs"
findstr /m "1.50.73" "%F5%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F5% - نسخة قديمة: زر اعتماد شريط الأدوات لم يُحذف بعد & set /a FAILS+=1) else (echo [OK]   %F5%)

set "F6=src\DatesErp.Desktop\Views\Screens\PlanningView.xaml"
findstr /m "5B7595" "%F6%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F6% - نسخة قديمة: الحدود المغمّقة غير مطبقة & set /a FAILS+=1) else (echo [OK]   %F6%)

echo.
if %FAILS% gtr 0 (
  echo ============================================================
  echo [STOP] ملفات على نسخ قديمة - البناء سيفشل.
  echo        الحل: احذف الملفات الفاشلة أعلاه من UPDATE_WORK ثم
  echo        أعد فك ضغط حزمة UPDATE_1.50.73_FIXED5 فوق UPDATE_WORK
  echo        وأعد تشغيل هذا الفحص حتى تظهر OK للجميع.
  echo ============================================================
  exit /b 1
)

echo [OK]   علامات المحتوى — مستمرة على فحص BOM لكل XAML ...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0VERIFY-SOURCE-1.50.73.ps1"
if errorlevel 1 (
  echo ============================================================
  echo [STOP] فحص BOM فشل — لا تبدأ البناء قبل المعالجة.
  echo ============================================================
  exit /b 1
)
echo.
echo [PASS] كل الفحوص ناجحة — البناء يمكن أن يبدأ.
echo.
exit /b 0
