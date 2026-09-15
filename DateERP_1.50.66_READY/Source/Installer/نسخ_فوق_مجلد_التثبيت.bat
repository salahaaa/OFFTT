@echo off
REM ═══════════════════════════════════════════════════════════════════════
REM  DateERP 1.50.43 — نسخ التحديث فوق مجلد التثبيت (طريقتك المعتادة)
REM  اسحب مجلد التثبيت (الذي يحوي MfgSystem.exe) وأفلته فوق هذا الملف،
REM  أو انقره نقراً مزدوجاً وأدخل المسار يدوياً.
REM  ينسخ الملفات المجمّعة الأحد عشر فوق القديمة بعد حفظ نسخة احتياطية منها.
REM ═══════════════════════════════════════════════════════════════════════
chcp 65001 >nul
setlocal EnableDelayedExpansion
set "VER=1.50.43"
set "SRC=%~dp0..\MfgSystem_%VER%_Update"

if not exist "%SRC%\MfgSystem.exe" (
    echo [خطأ] مجلد حزمة التحديث غير موجود:
    echo        %SRC%
    echo        شغّل أولاً: بناء_التحديث_الفوري.bat
    pause
    exit /b 1
)

set "TGT=%~1"
if "%TGT%"=="" set /p "TGT=أدخل مسار مجلد التثبيت (الذي يحوي MfgSystem.exe): "
set "TGT=%TGT:"=%"
if not exist "%TGT%\MfgSystem.exe" (
    echo [خطأ] المجلد التالي لا يحوي MfgSystem.exe:
    echo        %TGT%
    pause
    exit /b 1
)

echo.
echo ══════════════════════════════════════════════════════════
echo   نسخ تحديث DateERP %VER% فوق مجلد التثبيت
echo   الهدف: %TGT%
echo ══════════════════════════════════════════════════════════
echo.

set "BAK=%TGT%\نسخة_احتياطية_قبل_%VER%"
if not exist "%BAK%" mkdir "%BAK%"

for %%F in (MfgSystem.exe MfgSystem.dll MfgSystem.pdb MfgSystem.deps.json MfgSystem.runtimeconfig.json DatesErp.Core.dll DatesErp.Core.pdb DatesErp.Application.dll DatesErp.Application.pdb DatesErp.Infrastructure.dll DatesErp.Infrastructure.pdb) do (
    if exist "%TGT%\%%F" copy /y "%TGT%\%%F" "%BAK%\" >nul
    copy /y "%SRC%\%%F" "%TGT%\" >nul
    echo    تم: %%F
)

echo.
echo ══════════════════════════════════════════════════════════
echo   اكتمل النسخ. النسخة القديمة محفوظة في:
echo   %BAK%
echo   شغّل النظام الآن للتحقق من الإصدار %VER%.
echo ══════════════════════════════════════════════════════════
echo.
pause
endlocal
