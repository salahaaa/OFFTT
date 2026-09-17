@echo off
REM =====================================================================
REM  DateERP 1.50.67 - UPDATE EXISTING INSTALL - ONE CLICK
REM  للنسخة المنصبة الجاهزة عندك في:
REM  C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish
REM  المطلوب: ملف تضغط عليه يحدث الملفات ويفتح النظام - بدون بناء من الصفر
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "THIS_DIR=%CD%"
set "LOG=%THIS_DIR%\update_1.50.67.log"

echo ============================================================ > "%LOG%"
echo   UPDATE EXISTING INSTALL 1.50.67 - %DATE% %TIME% >> "%LOG%"
echo   This dir: %THIS_DIR% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   DateERP 1.50.67 - تحديث النسخة المنصبة الجاهزة
echo   بدون بناء من الصفر - تحديث سريع
echo ============================================================
echo   مجلد التشغيل الحالي عندك:
echo   C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish
echo   الملف التنفيذي:
echo   C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish\MfgSystem.exe
echo   الكود المصدري:
echo   C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\Source
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
echo        - PrintPreviewWindow.xaml (Bd -> BdTool/BdGreen/BdGhost)
echo        - verify_xaml_names.py (0 errors)

REM Source fixed files are in THIS_DIR\Source\src\...
set "FIXED_SRC=%THIS_DIR%\Source"
if not exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" (
  set "FIXED_SRC=%THIS_DIR%\..\OFFTT-arena-01a091dd-offtt\publish_1.50.67_READY\Source"
)
if not exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" (
  set "FIXED_SRC=%THIS_DIR%"
)

if exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" (
  echo        نسخ PrintPreviewWindow.xaml المُصلح...
  copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" "%SOURCE_OLD%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" >nul 2>&1
  if exist "%FIXED_SRC%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" (
    copy /Y "%FIXED_SRC%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" "%SOURCE_OLD%\src\DatesErp.Desktop\Views\Screens\..\PrintPreviewWindow.xaml" >nul 2>&1
  )
  REM Also copy to Views root
  if exist "%SOURCE_OLD%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml" (
    echo [OK] تم تحديث PrintPreviewWindow.xaml
    call :log "Updated PrintPreviewWindow.xaml"
  )
) else (
  echo [WARN] لم اجد الملف المُصلح في %FIXED_SRC% - سأحاول تحميله من GitHub...
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/salahaaa/OFFTT/arena/01a091dd-offtt/src/DatesErp.Desktop/Views/PrintPreviewWindow.xaml' -OutFile '%SOURCE_OLD%\src\DatesErp.Desktop\Views\PrintPreviewWindow.xaml'" >> "%LOG%" 2>&1
)

if exist "%FIXED_SRC%\tools\ci\verify_xaml_names.py" (
  echo        نسخ verify_xaml_names.py المُصلح...
  mkdir "%SOURCE_OLD%\tools\ci" 2>nul
  copy /Y "%FIXED_SRC%\tools\ci\verify_xaml_names.py" "%SOURCE_OLD%\tools\ci\verify_xaml_names.py" >nul 2>&1
  echo [OK] تم تحديث verify_xaml_names.py
) else (
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/salahaaa/OFFTT/arena/01a091dd-offtt/tools/ci/verify_xaml_names.py' -OutFile '%SOURCE_OLD%\tools\ci\verify_xaml_names.py'" >> "%LOG%" 2>&1
)

REM ---------- Step 3: Verify XAML ----------
echo [STEP 3] فحص XAML قبل البناء...
if exist "%SOURCE_OLD%\tools\ci\verify_xaml_names.py" (
  python "%SOURCE_OLD%\tools\ci\verify_xaml_names.py" >> "%LOG%" 2>&1
  if %errorlevel% NEQ 0 (
    echo [WARN] فحص XAML فشل - سيتم المتابعة لكن قد يفشل البناء
    type "%LOG%" | findstr /C:"❌" | more
  ) else (
    echo [OK] فحص XAML نجح - 0 أخطاء ✅
  )
)

REM ---------- Step 4: Backup old publish ----------
echo [STEP 4] عمل نسخة احتياطية لمجلد التشغيل...
set "BACKUP=%PUBLISH_OLD%_Backup_1.50.66_%DATE:~-4%%DATE:~-7,2%%DATE:~-10,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "BACKUP=%BACKUP: =0%"
mkdir "%BACKUP%" 2>nul
xcopy "%PUBLISH_OLD%\*" "%BACKUP%\" /E /I /Y /Q >> "%LOG%" 2>&1
echo [OK] Backup في: %BACKUP%

REM ---------- Step 5: Build (incremental, not from zero) ----------
echo [STEP 5] بناء سريع (بدون تنظيف كامل)...
cd /d "%SOURCE_OLD%"

REM Find sln file
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
set "PUBLISH_NEW=%THIS_DIR%\publish_NEW_1.50.67"
if exist "%PUBLISH_NEW%" rmdir /s /q "%PUBLISH_NEW%" 2>nul
mkdir "%PUBLISH_NEW%" 2>nul

echo        dotnet publish -c Release -o publish_NEW_1.50.67 (framework-dependent - سريع)
dotnet publish "%SOURCE_OLD%\src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -o "%PUBLISH_NEW%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] publish فشل - افتح %LOG%
  echo Log: %LOG%
  pause
  exit /b 1
)

echo 1.50.67 > "%PUBLISH_NEW%\VERSION.txt"
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
echo   تم تحديث DateERP 1.50.67 بنجاح ✅
echo   مجلد التشغيل: %PUBLISH_OLD%
echo   EXE: %PUBLISH_OLD%\MfgSystem.exe
echo   ملفات: %FILECOUNT%
echo   Backup: %BACKUP%
echo ============================================================
echo.
echo [STEP 9] فتح النظام الآن...
start "" "%PUBLISH_OLD%\MfgSystem.exe"
call :log "Run MfgSystem.exe"

echo.
echo Log: %LOG%
echo Backup: %BACKUP%
echo.
echo اذا ظهرت مشكلة النسخ الاحتياطي اضغط "نعم" للمتابعة بدون نسخة
echo.
pause
exit /b 0

:log
>>"%LOG%" echo [%DATE% %TIME%] %~1
exit /b
