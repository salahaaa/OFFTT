@echo off
REM ═══════════════════════════════════════════════════════════════════════
REM  MfgSystem 1.50.50 — بناء حزمة التحديث الفوري (يُشغَّل على جهاز المطوّر)
REM  ينتج مجلد DateERP_1.50.43_Update بنفس شكل حزم التحديث المصغّرة:
REM  الملفات المجمّعة الأحد عشر + سكربتات التشغيل + SHA256.txt + اقرأني.
REM  يُشترط: .NET 8 SDK ومجلد مصدر محدَّث (git pull) بجانب هذا الملف.
REM ═══════════════════════════════════════════════════════════════════════
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "ROOT=%~dp0.."
set "VER=1.50.50"
set "SLN=%ROOT%\DateERP.sln"
set "BIN=%ROOT%\src\DatesErp.Desktop\bin\Release\net8.0-windows"
set "OUT=%ROOT%\MfgSystem_%VER%_Update"

echo.
echo ══════════════════════════════════════════════════════════
echo   DateERP %VER% — بناء حزمة التحديث الفوري
echo ══════════════════════════════════════════════════════════
echo.

REM ── 1) حارس SDK ──
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [خطأ] لم يُعثر على .NET 8 SDK.
    echo        ثبّته من: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

REM ── 2) بناء Release ──
echo [1/4] بناء الحل (Release)...
dotnet build "%SLN%" -c Release --nologo
if errorlevel 1 (
    echo [خطأ] فشل البناء — لا تُسلَّم حزمة من بناء فاشل.
    pause
    exit /b 1
)
if not exist "%BIN%\MfgSystem.exe" (
    echo [خطأ] نواتج البناء غير موجودة في:
    echo        %BIN%
    pause
    exit /b 1
)
echo        تم البناء.
echo.

REM ── 3) تجميع مجلد التحديث ──
echo [2/4] تجميع مجلد التحديث المصغّر...
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
for %%F in (MfgSystem.exe MfgSystem.dll MfgSystem.pdb MfgSystem.deps.json MfgSystem.runtimeconfig.json DatesErp.Core.dll DatesErp.Core.pdb DatesErp.Application.dll DatesErp.Application.pdb DatesErp.Infrastructure.dll DatesErp.Infrastructure.pdb) do (
    if exist "%BIN%\%%F" (copy /y "%BIN%\%%F" "%OUT%\" >nul) else (echo [تنبيه] الملف %%F غير موجود في النواتج.)
)
echo        تم نسخ الملفات المجمّعة.
echo.

REM ── 4) السكربتات والاقرأني والبصمة ──
echo [3/4] نسخ السكربتات والاقرأني وتوليد SHA256.txt...
copy /y "%~dp01-حدّث_وشغل.bat" "%OUT%\" >nul
copy /y "%~dp02-تنصيب.bat" "%OUT%\" >nul
copy /y "%~dp0اقرأني_للتحديث_المصغر.html" "%OUT%\" >nul
powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%OUT%' -File | Where-Object { @('.exe','.dll','.json') -contains $_.Extension } | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name } | Set-Content -Encoding ascii -LiteralPath '%OUT%\SHA256.txt'"
echo        تم.
echo.

echo ══════════════════════════════════════════════════════════
echo   اكتملت الحزمة:  %OUT%
echo   للتطبيق على جهازك بطريقتك المعتادة: اسحب مجلد التثبيت
echo   (الذي يحوي MfgSystem.exe) وأفلته فوق:
echo   Installer\نسخ_فوق_مجلد_التثبيت.bat
echo   وللأجهزة الأخرى: انسخ مجلد الحزمة كما هو.
echo ══════════════════════════════════════════════════════════
echo.
pause
endlocal
