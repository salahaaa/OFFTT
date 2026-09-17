@echo off
chcp 65001 >nul
REM =====================================================================
REM  VERIFY-SOURCE 1.50.72 — فحص سلامة المصدر قبل البناء
REM  يتحقق أن الملفات الحرجة في UPDATE_WORK هي النسخ الحديثة (بالمحتوى،
REM  لا بالطابع الزمني). إن وُجد ملف قديم (لم يحوّله فك الضغط) يُسمَّى
REM  صراحةً ويُوقف البناء قبل إهدار دورة.
REM  شغّله قبل أي بناء:  VERIFY-SOURCE-1.50.72.bat
REM =====================================================================
setlocal
cd /d "%~dp0"
echo ============================================================
echo   فحص سلامة المصدر 1.50.72 (قبل البناء)
echo ============================================================
set FAILS=0

set "F1=src\DatesErp.Desktop\Views\Screens\ProductBOMView.xaml"
findstr /m "QtyLabel" "%F1%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F1% — نسخة قديمة (بدون x:Name="QtyLabel") & set /a FAILS+=1) else (echo [OK]   %F1%)

set "F2=src\DatesErp.Application\Services\PlanningService.cs"
findstr /m "FIX 1.50.70" "%F2%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F2% — نسخة قديمة (بدون إصلاح الحفظ §1.50.70) & set /a FAILS+=1) else (echo [OK]   %F2%)

set "F3=src\DatesErp.Desktop\Services\AutoBackup.cs"
findstr /m "\\SQLBackups" "%F3%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F3% — نسخة قديمة (escape غير مرمّم) & set /a FAILS+=1) else (echo [OK]   %F3%)

set "F4=src\DatesErp.Desktop\Views\Screens\ItemsView.xaml.cs"
findstr /m "الصنف.\nهذا" "%F4%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F4% — نسخة قديمة (سلسلة متعددة الأسطر غير مرمّمة) & set /a FAILS+=1) else (echo [OK]   %F4%)

set "F5=src\DatesErp.Desktop\DatesErp.Desktop.csproj"
findstr /m "1.50.72" "%F5%" >nul 2>nul
if errorlevel 1 (echo [FAIL] %F5% — الإصدار ليس 1.50.72 & set /a FAILS+=1) else (echo [OK]   %F5%)

echo.
if %FAILS% gtr 0 (
  echo ============================================================
  echo [STOP] %FAILS% من الملفات حازت نسختها القديمة — البناء سيفشل.
  echo        الحل: احذف الملف(الأسطر) الفاشل(ة) أعلاه من UPDATE_WORK ثم
  echo        أعد فك ضغط حزمة UPDATE_1.50.72_FIXED2 على UPDATE_WORK
  echo        (أو افك الملف المعني فقط من الحزمة فوق مكانه).
  echo        أعد تشغيل هذا الملف بعد ذلك حتى تظهر [OK] للجميع.
  echo ============================================================
  exit /b 1
)
echo [PASS] كل الملفات الحرجة نسختها الحديثة — البناء يمكن أن يبدأ.
echo.
exit /b 0
