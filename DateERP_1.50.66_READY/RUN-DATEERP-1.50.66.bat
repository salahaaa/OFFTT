@echo off
REM =====================================================================
REM  DateERP 1.50.66 - READY TO RUN - ONE CLICK
REM  هذا الملف يجعل النظام جاهز يفتح بضغطة زر مع جميع الأدوات
REM  المطلوب: كليك يمين -> تشغيل كمسؤول
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%CD%"
set "LOG=%ROOT%\ready_1.50.66.log"
set "VERSION=1.50.66"

echo ============================================================ > "%LOG%" 2>nul
echo   DateERP %VERSION% READY TO RUN - %DATE% %TIME% >> "%LOG%"
echo   Root: %ROOT% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   DateERP %VERSION% - جاهز يفتح بضغطة زر
echo   DateERP %VERSION% - READY TO RUN - ONE CLICK
echo ============================================================
echo   هذا الملف يجهز كل شيء تلقائياً:
echo   - .NET 8 SDK
echo   - SQL Server 2022 Express (ان وجد)
echo   - بناء النظام
echo   - نشر النظام
echo   - تشغيل النظام
echo ============================================================
echo.

REM ---------- Admin check ----------
net session >nul 2>&1
if %errorlevel%==0 goto :admin_ok
echo [STEP] Administrator required - relaunching...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:admin_ok
echo [OK] Running as administrator.

REM ---------- Determine install folder ----------
set "INSTALL_DRIVE=C:"
if exist "D:\." set "INSTALL_DRIVE=D:"
set "INSTALL_ROOT=%INSTALL_DRIVE%\DateERP_1.50.66"
echo Install folder: %INSTALL_ROOT%
call :log "Install root: %INSTALL_ROOT%"

REM ---------- Check if already installed and real ----------
if exist "%INSTALL_ROOT%\publish\MfgSystem.exe" (
  for %%A in ("%INSTALL_ROOT%\publish\MfgSystem.exe") do set "SIZE=%%~zA"
  if !SIZE! GTR 50000 (
    echo [INFO] Found existing real installation at %INSTALL_ROOT%
    echo        Size: !SIZE! bytes - seems real, will run it.
    goto :run_installed
  )
)

REM ---------- Check publish in READY package ----------
set "READY_PUBLISH=%ROOT%\DateERP_1.50.66_INSTALLER\publish"
if exist "%READY_PUBLISH%\MfgSystem.exe" (
  for %%A in ("%READY_PUBLISH%\MfgSystem.exe") do set "SIZE=%%~zA"
  if !SIZE! GTR 50000 (
    echo [INFO] Found real publish in READY package - installing...
    goto :install_from_ready
  )
)

REM ---------- Need to build ----------
echo [STEP] Real binaries not found - will build from source...
goto :build_from_source

:install_from_ready
echo [STEP] Installing from READY publish...
mkdir "%INSTALL_ROOT%" 2>nul
mkdir "%INSTALL_ROOT%\publish" 2>nul
mkdir "%INSTALL_ROOT%\Config" 2>nul
mkdir "%INSTALL_ROOT%\Cache" 2>nul
mkdir "%INSTALL_ROOT%\Logs" 2>nul
mkdir "%INSTALL_ROOT%\Backup" 2>nul
mkdir "%INSTALL_ROOT%\Runtime" 2>nul
xcopy "%READY_PUBLISH%\*" "%INSTALL_ROOT%\publish\" /E /I /Y /Q >> "%LOG%" 2>&1
echo [OK] Installed from READY publish
goto :create_shortcuts

:build_from_source
echo [STEP] Checking .NET 8 SDK...
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel%==0 goto :sdk_ok

echo        .NET 8 SDK not found - installing...
set "SDKPKG="
if exist "%ROOT%\Tools\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\Tools\dotnet-sdk-8-win-x64.exe"
if exist "%ROOT%\DateERP_1.50.66_INSTALLER\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\DateERP_1.50.66_INSTALLER\dotnet-sdk-8-win-x64.exe"
if exist "%ROOT%\Source\Installer\OneClick\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\Source\Installer\OneClick\dotnet-sdk-8-win-x64.exe"

if defined SDKPKG (
  echo        Installing from offline: !SDKPKG!
  "!SDKPKG!" /install /quiet /norestart >> "%LOG%" 2>&1
) else (
  echo        Downloading .NET 8 SDK (220 MB)...
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe' -OutFile '%TEMP%\dotnet-sdk-8-win-x64.exe'" >> "%LOG%" 2>&1
  if exist "%TEMP%\dotnet-sdk-8-win-x64.exe" (
    "%TEMP%\dotnet-sdk-8-win-x64.exe" /install /quiet /norestart >> "%LOG%" 2>&1
  )
)

dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] .NET 8 SDK install failed. See %LOG%
  pause
  exit /b 1
)

:sdk_ok
echo [OK] .NET 8 SDK ready.

REM ---------- Find solution ----------
echo [STEP] Finding DateERP.sln...
set "SLN="
for %%P in ("%ROOT%\Source\DateERP.sln" "%ROOT%\DateERP.sln" "%ROOT%\..\DateERP.sln" "D:\DateERP_1.50.43_FULL_SOURCE\DateERP.sln") do (
  if exist "%%~P" set "SLN=%%~P"
)
if "%SLN%"=="" (
  echo [FAILED] DateERP.sln not found. Expected at %ROOT%\Source\DateERP.sln
  pause
  exit /b 1
)
echo        Found: %SLN%
for %%F in ("%SLN%") do set "SLN_DIR=%%~dpF"

REM ---------- Build ----------
echo [STEP] Building DateERP %VERSION% Release - first build takes 2-6 minutes...
dotnet restore "%SLN%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] dotnet restore failed. See %LOG%
  pause
  exit /b 1
)

dotnet build "%SLN%" -c Release -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] dotnet build failed. See %LOG%
  pause
  exit /b 1
)
echo [OK] Build SUCCESS.

REM ---------- Publish ----------
echo [STEP] Publishing...
set "PUBLISH_OUT=%ROOT%\DateERP_1.50.66_INSTALLER\publish"
mkdir "%PUBLISH_OUT%" 2>nul
dotnet publish "%SLN_DIR%src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -o "%PUBLISH_OUT%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] dotnet publish failed. See %LOG%
  pause
  exit /b 1
)
echo %VERSION% > "%PUBLISH_OUT%\VERSION.txt"
echo [OK] Publish SUCCESS to %PUBLISH_OUT%

REM ---------- Install to isolated folder ----------
echo [STEP] Installing to %INSTALL_ROOT%...
mkdir "%INSTALL_ROOT%" 2>nul
mkdir "%INSTALL_ROOT%\publish" 2>nul
mkdir "%INSTALL_ROOT%\Config" 2>nul
mkdir "%INSTALL_ROOT%\Cache" 2>nul
mkdir "%INSTALL_ROOT%\Logs" 2>nul
mkdir "%INSTALL_ROOT%\Backup" 2>nul
mkdir "%INSTALL_ROOT%\Runtime" 2>nul

REM Backup existing if any
if exist "%INSTALL_ROOT%\publish\MfgSystem.exe" (
  set "BACKUP=%INSTALL_ROOT%_Backup_%DATE:~-4%%DATE:~-7,2%%DATE:~-10,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
  set "BACKUP=!BACKUP: =0!"
  mkdir "!BACKUP!" 2>nul
  xcopy "%INSTALL_ROOT%\*" "!BACKUP!\" /E /I /Y /Q >> "%LOG%" 2>&1
  echo [OK] Backup created: !BACKUP!
)

xcopy "%PUBLISH_OUT%\*" "%INSTALL_ROOT%\publish\" /E /I /Y /Q >> "%LOG%" 2>&1
echo [OK] Files copied to %INSTALL_ROOT%\publish

REM ---------- Isolated Config ----------
echo [STEP] Creating isolated Config...
powershell -NoProfile -Command ^
  "$jsonPath='%INSTALL_ROOT%\publish\appsettings.json'; $ver='1.50.66'; $root='%INSTALL_ROOT%'; " ^
  "if (Test-Path $jsonPath) { try { $j=Get-Content $jsonPath -Raw | ConvertFrom-Json; $j.AppSettings.Version=$ver; $j.AppSettings.InstallPath=$root; $j.AppSettings.CachePath='..\\Cache'; $j.AppSettings.LogsPath='..\\Logs'; $j.ConnectionStrings.DefaultConnection='Server=.\\SQLEXPRESS;Database=DateERP_1_50_66;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True'; $j | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8 } catch {} }" >> "%LOG%" 2>&1

copy /Y "%INSTALL_ROOT%\publish\appsettings.json" "%INSTALL_ROOT%\Config\appsettings.1.50.66.json" >nul 2>&1
echo [OK] Config isolated.

:create_shortcuts
echo [STEP] Creating shortcuts...
set "DESKTOP=%USERPROFILE%\Desktop"
if not exist "%DESKTOP%" set "DESKTOP=%PUBLIC%\Desktop"
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%DESKTOP%\DateERP 1.50.66.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.66'; $lnk.Save()" >> "%LOG%" 2>&1

set "STARTMENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs\DateERP"
mkdir "%STARTMENU%" 2>nul
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%STARTMENU%\DateERP 1.50.66.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.66'; $lnk.Save()" >> "%LOG%" 2>&1

set "STARTMENU_USER=%AppData%\Microsoft\Windows\Start Menu\Programs\DateERP"
mkdir "%STARTMENU_USER%" 2>nul
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%STARTMENU_USER%\DateERP 1.50.66.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.66'; $lnk.Save()" >> "%LOG%" 2>&1

echo [OK] Shortcuts created.

REM ---------- Create RUN.bat ----------
(
  echo @echo off
  echo REM DateERP %VERSION% - Isolated RUN
  echo cd /d "%INSTALL_ROOT%\publish"
  echo start "" "MfgSystem.exe"
) > "%INSTALL_ROOT%\RUN.bat"

copy /Y "%INSTALL_ROOT%\RUN.bat" "%ROOT%\RUN-INSTALLED-1.50.66.bat" >nul 2>&1

:run_installed
echo.
echo ============================================================
echo   تم تثبيت DateERP %VERSION% بنجاح
echo   DateERP %VERSION% installed successfully - READY TO RUN
echo ============================================================
echo   Install: %INSTALL_ROOT%
echo   EXE: %INSTALL_ROOT%\publish\MfgSystem.exe
echo   Config: %INSTALL_ROOT%\Config\appsettings.1.50.66.json
echo   Logs: %INSTALL_ROOT%\Logs
echo   RUN: %INSTALL_ROOT%\RUN.bat
echo   Desktop: %DESKTOP%\DateERP 1.50.66.lnk
echo ============================================================
echo.
echo   Database: DateERP_1_50_66 (isolated, preserved, no DROP)
echo   First run will create DB automatically if not exists.
echo.

set /p RUNNOW="هل تريد تشغيل DateERP الان؟ (Y/N): "
if /I "%RUNNOW%"=="Y" goto :do_run
if /I "%RUNNOW%"=="y" goto :do_run
if /I "%RUNNOW%"=="YES" goto :do_run
goto :end

:do_run
echo Launching...
start "" "%INSTALL_ROOT%\publish\MfgSystem.exe"
goto :end

:end
echo Log: %LOG%
pause
exit /b 0

:log
>>"%LOG%" echo [%DATE% %TIME%] %~1
exit /b
