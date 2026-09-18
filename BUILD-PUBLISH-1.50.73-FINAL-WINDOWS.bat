@echo off
chcp 65001 >nul
REM =====================================================================
REM  BUILD + PUBLISH 1.50.73 FINAL (ONE CLICK) - Windows
REM  يبني الحلين Release ثم ينشر MfgSystem.exe إلى مجلد جديد
REM  publish_1.50.73_FINAL\ فقط — لا يمس publish_FINAL ولا publish_1.50.73.
REM  بعد النشر: فحص آلي لسمات الإصدار في exe نفسها  (1.50.73 FINAL):
REM    FileVersion = 1.50.73.0  و  ProductVersion = 1.50.73
REM  ثم يشغّل النسخة الجديدة للتأكد من فتحها.
REM  هذا الملف بلا BOM عمدًا — cmd يفسر BOM كحروف ويكسر أول أمر.
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "VER=1.50.73"
set "OUT=publish_1.50.73_FINAL"

if not exist "src\DatesErp.Desktop\DatesErp.Desktop.csproj" (
  echo [FAILED] شُغِّل هذا الملف من جذر المستودع UPDATE_WORK.
  exit /b 1
)
where dotnet >nul 2>nul || (
  echo [FAILED] .NET 8 SDK غير مثبت على هذا الجهاز.
  exit /b 1
)

echo ============================================================
echo   [1/6] فحص سلامة المصدر  -  VERIFY-SOURCE-1.50.73.bat
echo ============================================================
call "VERIFY-SOURCE-1.50.73.bat"
if errorlevel 1 (
  echo [FAILED] المصدر ليس كاملًا/حديثًا — راجع الفشل أعلاه قبل أي بناء.
  exit /b 1
)

echo ============================================================
echo   [2/6] بناء DateERP.sln - Release
echo ============================================================
REM حذف وسيطات مشروع الواجهة القديمة  -  يمنع بقاء سمات قديمة مولَّدة
if exist "src\DatesErp.Desktop\obj" rmdir /s /q "src\DatesErp.Desktop\obj"
if exist "src\DatesErp.Desktop\bin" rmdir /s /q "src\DatesErp.Desktop\bin"
dotnet build DateERP.sln -c Release --nologo -v minimal --no-incremental
if errorlevel 1 (
  echo [FAILED] بناء DateERP.sln فشل — أخطاء أعلاه. لا يوجد publish.
  exit /b 1
)

echo ============================================================
echo   [3/6] بناء DatesErp.sln - Release
echo ============================================================
dotnet build DatesErp.sln -c Release --nologo -v minimal --no-incremental
if errorlevel 1 (
  echo [FAILED] بناء DatesErp.sln فشل — أخطاء أعلاه. لا يوجد publish.
  exit /b 1
)

echo ============================================================
echo   [4/6] نشر MfgSystem  -  win-x64 self-contained  -  %OUT%\
echo   (مجلد جديد — لا يُلمس publish_FINAL ولا publish_1.50.73)
echo ============================================================
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish src\DatesErp.Desktop\DatesErp.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%" --nologo
if errorlevel 1 (
  echo [FAILED] النشر فشل — أخطاء أعلاه.
  exit /b 1
)

echo ============================================================
echo   [5/6] فحص آلي لسمات الإصدار في MfgSystem.exe
echo ============================================================
if not exist "%OUT%\MfgSystem.exe" (
  echo [FAILED] MfgSystem.exe غير موجود في المخرجات.
  exit /b 1
)
if not exist "%OUT%\coreclr.dll" (
  echo [FAILED] coreclr.dll مفقود — النشر ليس self-contained.
  exit /b 1
)
set "EXESIZE=0"
for %%F in ("%OUT%\MfgSystem.exe") do set "EXESIZE=%%~zF"
if %EXESIZE% LSS 100000 (
  echo [FAILED] حجم MfgSystem.exe غير طبيعي - ملف وهمي.
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0CHECK-EXE-VERSION-1.50.73.ps1" "%OUT%\MfgSystem.exe"
if errorlevel 1 (
  echo ============================================================
  echo [FAILED] سمات الإصدار في exe ليست 1.50.73 — انظر السطور أعلاه.
  echo          هذا يعني أن ملف csproj على القرص قديم رغم الفحص، أو أن
  echo          البناء استخدم obj قديمًا. الحل: احذف src\DatesErp.Desktop\obj
  echo          وbin من UPDATE_WORK وأعد التشغيل.
  echo ============================================================
  exit /b 1
)

echo ============================================================
echo   [6/6] تشغيل النسخة الجديدة للتأكد من فتحها
echo ============================================================
> "%OUT%\VERSION.txt" echo MfgSystem %VER% FINAL - self-contained win-x64
echo  FileVersion: 1.50.73.0 / ProductVersion: 1.50.73 >> "%OUT%\VERSION.txt"
echo  Built: %DATE% %TIME% >> "%OUT%\VERSION.txt"
> "%OUT%\SHA256.txt" powershell -NoProfile -Command "(Get-FileHash '%OUT%\MfgSystem.exe' -Algorithm SHA256).Hash"
start "" "%OUT%\MfgSystem.exe"
echo.
echo ============================================================
echo   [OK] اكتمل:
echo   - النسخة المنشورة:   %OUT%\MfgSystem.exe
echo   - سمات الإصدار:      FileVersion 1.50.73.0 / ProductVersion 1.50.73
echo   - النسخة الجديدة شُغِّلت الآن — تأكد من:
echo       1) فتح النافذة الرئيسية بنجاح.
echo       2) أسفل النافذة:  1.50.73
echo       3) شاشة المعلومات:  إصدار التطبيق 1.50.73
echo ============================================================
endlocal
