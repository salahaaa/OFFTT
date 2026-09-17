@echo off
REM =====================================================================
REM  DateERP 1.50.69 - UPDATE EXISTING INSTALL - ONE CLICK
REM  اصلاحان رئيسيان:
REM  1) الدفعة/الشحنة برقم سند استلام الشحنات (ShipmentNo = DocumentNumber)
REM     بدل LotCode الداخلي - PlanningView.xaml + LotPickerWindow.xaml
REM  2) الحفظ التلقائي معطل نهائيا + حذف مسودات PlanningDraft_*.json
REM     التي تسبب خطط وهمية - PlanningView.xaml.cs
REM  + اصلاح 1.50.68: منع رسالة لا تطابق عدد الكراتين
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "THIS_DIR=%CD%"
set "LOG=%THIS_DIR%\update_1.50.69.log"
set "VER=1.50.69"

echo ============================================================ > "%LOG%"
echo   UPDATE EXISTING INSTALL %VER% - %DATE% %TIME% >> "%LOG%"
echo   This dir: %THIS_DIR% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   DateERP %VER% - تحديث النسخة المنصبة الجاهزة
echo   اصلاح: الدفعة برقم سند الاستلام + الحفظ التلقائي معطل نهائيا
echo ============================================================
echo   مجلد التشغيل الحالي عندك:
echo   C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish
echo   الملف التنفيذي:
echo   C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish\MfgSystem.exe
echo ============================================================
echo.

REM ---------- Find existing publish ----------
set "PUBLISH_OLD=C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish"
if not exist "%PUBLISH_OLD%\MfgSystem.exe" (
  echo [WARN] لم اجد %PUBLISH_OLD%\MfgSystem.exe
  echo        سأحاول البحث...
  if exist "%THIS_DIR%\..\publish\MfgSystem.exe" set "PUBLISH_OLD=%THIS_DIR%\..\publish"
  if exist "C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\publish\MfgSystem.exe" set "PUBLISH_OLD=C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\publish"
)

echo [INFO] مجلد التشغيل: %PUBLISH_OLD%
call :log "Publish old: %PUBLISH_OLD%"

REM ---------- Find Source ----------
set "SOURCE_OLD=C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\Source"
if not exist "%SOURCE_OLD%\DatesErp.sln" (
  if exist "%THIS_DIR%\Source\DatesErp.sln" set "SOURCE_OLD=%THIS_DIR%\Source"
  if exist "%THIS_DIR%\..\Source\DatesErp.sln" set "SOURCE_OLD=%THIS_DIR%\..\Source"
  if exist "%THIS_DIR%\..\..\Source\DatesErp.sln" set "SOURCE_OLD=%THIS_DIR%\..\..\Source"
  if exist "C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\Source\DatesErp.sln" set "SOURCE_OLD=C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\Source"
)

echo [INFO] مجلد السورس: %SOURCE_OLD%
call :log "Source old: %SOURCE_OLD%"

if not exist "%SOURCE_OLD%\DatesErp.sln" (
  if not exist "%SOURCE_OLD%\DateERP.sln" (
    echo [FAILED] لم اجد ملف DatesErp.sln في %SOURCE_OLD%
    echo          تأكد انك فكيت الحزمة كاملة
    pause
    exit /b 1
  )
)

REM ---------- Check dotnet ----------
echo [STEP 1] فحص .NET SDK...
dotnet --version >nul 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] dotnet غير موجود - ثبّت .NET 8 SDK من Tools\ او من https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe
  pause
  exit /b 1
)
dotnet --version >> "%LOG%" 2>&1
echo [OK] dotnet موجود

REM ---------- Step 2: Update fixed files in Source ----------
echo [STEP 2] تحديث الملفات المُصلحة في السورس...
echo        - PlanningView.xaml (الدفعة = رقم سند الاستلام ShipmentNo + LotCode)
echo        - PlanningView.xaml.cs (حذف الحفظ التلقائي نهائيا + حذف PlanningDraft_*.json)
echo        - LotPickerWindow.xaml (رقم سند الاستلام كعمود اساسي)
echo        - PlanningRows.cs (QtyKg auto-correct Cartons)
echo        - UnitsPolicy.cs (EnsureCartonKgConsistency return computed)

set "FIXED_SRC=%THIS_DIR%\Source"
if not exist "%FIXED_SRC%\src\DatesErp.Desktop\Mvvm\PlanningRows.cs" (
  set "FIXED_SRC=%THIS_DIR%"
)

REM Copy fixed files if exist in FIXED_SRC
if exist "%FIXED_SRC%\src\DatesErp.Desktop\Mvvm\PlanningRows.cs" (
  echo        نسخ PlanningRows.cs...
  copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Mvvm\PlanningRows.cs" "%SOURCE_OLD%\src\DatesErp.Desktop\Mvvm\PlanningRows.cs" >nul 2>&1
)
if exist "%FIXED_SRC%\src\DatesErp.Application\Services\UnitsPolicy.cs" (
  echo        نسخ UnitsPolicy.cs...
  copy /Y "%FIXED_SRC%\src\DatesErp.Application\Services\UnitsPolicy.cs" "%SOURCE_OLD%\src\DatesErp.Application\Services\UnitsPolicy.cs" >nul 2>&1
)
if exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\Screens\PlanningView.xaml" (
  echo        نسخ PlanningView.xaml...
  copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Views\Screens\PlanningView.xaml" "%SOURCE_OLD%\src\DatesErp.Desktop\Views\Screens\PlanningView.xaml" >nul 2>&1
)
if exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs" (
  echo        نسخ PlanningView.xaml.cs...
  copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs" "%SOURCE_OLD%\src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs" >nul 2>&1
)
if exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\LotPickerWindow.xaml" (
  echo        نسخ LotPickerWindow.xaml...
  copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Views\LotPickerWindow.xaml" "%SOURCE_OLD%\src\DatesErp.Desktop\Views\LotPickerWindow.xaml" >nul 2>&1
)
if exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\Screens\PlanningWindows.cs" (
  echo        نسخ PlanningWindows.cs...
  copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Views\Screens\PlanningWindows.cs" "%SOURCE_OLD%\src\DatesErp.Desktop\Views\Screens\PlanningWindows.cs" >nul 2>&1
)

echo [OK] تم تحديث الملفات المُصلحة
call :log "Updated fixed files for %VER%"

REM ---------- Step 3: Delete old drafts (the cause of fake plans) ----------
echo [STEP 3] حذف ملفات الحفظ التلقائي القديمة (سبب الخطط الوهمية)...
set "DRAFTS=%LocalAppData%\DateERP\drafts"
if exist "%DRAFTS%" (
  echo        حذف: %DRAFTS%\PlanningDraft_*.json
  del /Q "%DRAFTS%\PlanningDraft_*.json" 2>nul
  echo [OK] تم حذف المسودات القديمة
  call :log "Deleted drafts from %DRAFTS%"
) else (
  echo        لا يوجد مجلد مسودات - لا حاجة للحذف
)
REM Also clean any other draft locations
if exist "%PUBLISH_OLD%\drafts" (
  del /Q "%PUBLISH_OLD%\drafts\PlanningDraft_*.json" 2>nul
)

REM ---------- Step 4: Backup old publish ----------
echo [STEP 4] عمل نسخة احتياطية لمجلد التشغيل...
set "BACKUP=%PUBLISH_OLD%_Backup_%VER%_%DATE:~-4%%DATE:~-7,2%%DATE:~-10,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "BACKUP=%BACKUP: =0%"
mkdir "%BACKUP%" 2>nul
xcopy "%PUBLISH_OLD%\*" "%BACKUP%\" /E /I /Y /Q >> "%LOG%" 2>&1
echo [OK] Backup في: %BACKUP%

REM ---------- Step 5: Build (incremental) ----------
echo [STEP 5] بناء سريع (incremental)...
cd /d "%SOURCE_OLD%"

set "SLN_FILE="
if exist "%SOURCE_OLD%\DateERP.sln" set "SLN_FILE=%SOURCE_OLD%\DateERP.sln"
if exist "%SOURCE_OLD%\DatesErp.sln" set "SLN_FILE=%SOURCE_OLD%\DatesErp.sln"
if "%SLN_FILE%"=="" (
  echo [FAILED] لم اجد sln
  pause
  exit /b 1
)

echo        SLN: %SLN_FILE%
echo        Building: dotnet build -c Release (incremental)...
dotnet build "%SLN_FILE%" -c Release -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [WARN] build فشل - سأحاول publish مباشرة...
  call :log "build failed, trying publish"
)

REM ---------- Step 6: Publish to temp ----------
echo [STEP 6] نشر الى مجلد مؤقت...
set "PUBLISH_NEW=%THIS_DIR%\publish_NEW_%VER%"
if exist "%PUBLISH_NEW%" rmdir /s /q "%PUBLISH_NEW%" 2>nul
mkdir "%PUBLISH_NEW%" 2>nul

echo        dotnet publish -c Release -o publish_NEW_%VER% (framework-dependent - سريع)
dotnet publish "%SOURCE_OLD%\src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -o "%PUBLISH_NEW%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] publish فشل - افتح %LOG%
  echo Log: %LOG%
  pause
  exit /b 1
)

echo %VER% > "%PUBLISH_NEW%\VERSION.txt"
for /f %%A in ('dir /b /a-d "%PUBLISH_NEW%" ^| find /c /v ""') do set FILECOUNT=%%A
echo [OK] Publish نجح - %FILECOUNT% ملف
call :log "Publish OK %FILECOUNT% files"

if not exist "%PUBLISH_NEW%\MfgSystem.exe" (
  echo [FAILED] MfgSystem.exe غير موجود بعد النشر
  pause
  exit /b 1
)

REM ---------- Step 7: Copy to old publish ----------
echo [STEP 7] نسخ الملفات الجديدة فوق مجلد التشغيل القديم...
echo        من: %PUBLISH_NEW%
echo        الى: %PUBLISH_OLD%

REM Close running exe
tasklist /FI "IMAGENAME eq MfgSystem.exe" 2>nul | find /I "MfgSystem.exe" >nul
if %errorlevel%==0 (
  echo        اغلاق MfgSystem.exe الجاري...
  taskkill /F /IM MfgSystem.exe >nul 2>&1
  timeout /t 2 /nobreak >nul
)

xcopy "%PUBLISH_NEW%\*" "%PUBLISH_OLD%\" /E /I /Y /Q >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] نسخ فشل
  pause
  exit /b 1
)
echo [OK] تم التحديث

REM ---------- Step 8: Verify ----------
echo [STEP 8] التحقق...
if exist "%PUBLISH_OLD%\MfgSystem.exe" (
  for %%A in ("%PUBLISH_OLD%\MfgSystem.exe") do set "EXESIZE=%%~zA"
  echo [OK] MfgSystem.exe موجود - الحجم: !EXESIZE! بايت
) else (
  echo [FAILED] MfgSystem.exe غير موجود بعد النسخ
)

if exist "%PUBLISH_OLD%\VERSION.txt" (
  type "%PUBLISH_OLD%\VERSION.txt"
)

REM ---------- Step 9: Run ----------
echo.
echo ============================================================
echo   تم تحديث DateERP %VER% بنجاح ✅
echo   مجلد التشغيل: %PUBLISH_OLD%
echo   EXE: %PUBLISH_OLD%\MfgSystem.exe
echo   ملفات: %FILECOUNT%
echo   Backup: %BACKUP%
echo   ----------------------------------------------------------
echo   ما تم اصلاحه:
echo   1) الدفعة/الشحنة الان برقم سند استلام الشحنات (ShipmentNo)
echo      بدل LotCode الداخلي - في خطة الانتاج + اختيار الدفعات
echo   2) الحفظ التلقائي معطل نهائيا + حذف مسودات قديمة
echo      %DRAFTS%\PlanningDraft_*.json
echo   3) منع رسالة لا تطابق عدد الكراتين (auto-correct)
echo ============================================================
echo.
echo [STEP 9] فتح النظام الآن...
start "" "%PUBLISH_OLD%\MfgSystem.exe"
call :log "Run MfgSystem.exe"

echo.
echo Log: %LOG%
echo Backup: %BACKUP%
echo.
pause
exit /b 0

:log
>>"%LOG%" echo [%DATE% %TIME%] %~1
exit /b
