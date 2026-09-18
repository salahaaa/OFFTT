@echo off
chcp 65001 >nul
REM =====================================================================
REM  BUILD + PUBLISH 1.50.72 (ONE CLICK) - Windows
REM  يبني الحلين (DateERP.sln بما فيه audit/ + DatesErp.sln) ثم ينشر
REM  MfgSystem.exe حقيقي self-contained win-x64 في publish_1.50.72\
REM  ويتحقق من سلامة المخرجات ثم يجهز حزمة التحديث بضغطة واحدة.
REM  لا يلمس قاعدة البيانات ولا الإعدادات إطلاقاً (مخرجات build فقط).
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "VER=1.50.72"
set "OUT=publish_%VER%"
set "PKG=UPDATE_1.50.72_PACKAGE"

if not exist "src\DatesErp.Desktop\DatesErp.Desktop.csproj" (
  echo [FAILED] شُغِّل هذا الملف من جذر المستودع (حيث DatesErp.sln).
  exit /b 1
)
where dotnet >nul 2>nul || (
  echo [FAILED] .NET 8 SDK غير مثبت على هذا الجهاز.
  echo          ثبّته من https://dotnet.microsoft.com/download/dotnet/8.0 ثم أعد التشغيل.
  exit /b 1
)

echo ============================================================
echo   [1/5] فحص سلامة المصدر (VERIFY-SOURCE-1.50.72.bat)
echo ============================================================
if not exist "VERIFY-SOURCE-1.50.72.bat" (
  echo [FAILED] VERIFY-SOURCE-1.50.72.bat غير موجود — فك حزمة UPDATE_1.50.72_FIXED2 كاملة.
  exit /b 1
)
call "VERIFY-SOURCE-1.50.72.bat"
if errorlevel 1 (
  echo [FAILED] المصدر ليس كاملًا/حديثًا — راجع الفشل أعلاه قبل أي بناء.
  exit /b 1
)

echo ============================================================
echo   [2/5] بناء DateERP.sln (يتضمن audit/UnitRuleAudit) - Release
echo ============================================================
REM حذف وسيطات مشروع الواجهة القديمة (obj/bin) — يمنع بقاء كود XAML مولَّد قديم
if exist "src\DatesErp.Desktop\obj" rmdir /s /q "src\DatesErp.Desktop\obj"
if exist "src\DatesErp.Desktop\bin" rmdir /s /q "src\DatesErp.Desktop\bin"
dotnet build DateERP.sln -c Release --nologo -v minimal --no-incremental
if errorlevel 1 (
  echo [FAILED] بناء DateERP.sln فشل — أخطاء أعلاه. لا يوجد publish.
  exit /b 1
)

echo ============================================================
echo   [3/5] بناء DatesErp.sln - Release
echo ============================================================
dotnet build DatesErp.sln -c Release --nologo -v minimal --no-incremental
if errorlevel 1 (
  echo [FAILED] بناء DatesErp.sln فشل — أخطاء أعلاه. لا يوجد publish.
  exit /b 1
)

echo ============================================================
echo   [4/5] نشر MfgSystem (win-x64 self-contained) -> %OUT%\
echo ============================================================
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish src\DatesErp.Desktop\DatesErp.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%" --nologo
if errorlevel 1 (
  echo [FAILED] النشر فشل — أخطاء أعلاه.
  exit /b 1
)

echo ============================================================
echo   [5/5] فحص سلامة الحزمة + تجهيز تحديث بضغطة واحدة
echo ============================================================
if not exist "%OUT%\MfgSystem.exe" (
  echo [FAILED] MfgSystem.exe غير موجود في المخرجات — هذه ليست حزمة حقيقية.
  exit /b 1
)
if not exist "%OUT%\coreclr.dll" (
  echo [FAILED] coreclr.dll مفقود — النشر ليس self-contained (سيحتاج runtime على الجهاز).
  exit /b 1
)
set "EXESIZE=0"
for %%F in ("%OUT%\MfgSystem.exe") do set "EXESIZE=%%~zF"
if %EXESIZE% LSS 100000 (
  echo [FAILED] حجم MfgSystem.exe غير طبيعي (%EXESIZE% بايت) — ملف وهمي.
  exit /b 1
)
> "%OUT%\VERSION.txt" echo MfgSystem %VER% - self-contained win-x64
echo  Built: %DATE% %TIME% >> "%OUT%\VERSION.txt"
for %%F in ("%OUT%\MfgSystem.exe") do echo  Size: %%~zF bytes >> "%OUT%\VERSION.txt"
> "%OUT%\SHA256.txt" powershell -NoProfile -Command "(Get-FileHash '%OUT%\MfgSystem.exe' -Algorithm SHA256).Hash"

REM حزمة التحديث بضغطة واحدة (تُجهز محلياً — خارج Git عمداً)
if exist "%PKG%" rmdir /s /q "%PKG%"
mkdir "%PKG%"
xcopy /E /I /Q /Y "%OUT%" "%PKG%\publish_%VER%" >nul
copy /Y "UPDATE_1.50.72_ONECLICK\UPDATE-1.50.72-ONECLICK.bat" "%PKG%\" >nul
copy /Y "UPDATE_1.50.72_ONECLICK\README_AR.txt" "%PKG%\" >nul

echo.
echo ============================================================
echo   [OK] اكتمل البناء والنشر بنجاح:
echo   - البرنامج المنشور:  %OUT%\MfgSystem.exe
echo   - حزمة التحديث:      %PKG%\  (انسخها للهدف وشغّل UPDATE-1.50.72-ONECLICK.bat)
echo   - الإصدار الظاهر:    %VER% (شريط العنوان + معلومات النظام)
echo ============================================================
endlocal
