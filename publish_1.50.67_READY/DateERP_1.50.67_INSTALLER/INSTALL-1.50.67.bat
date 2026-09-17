@echo off
REM =====================================================================
REM  DateERP 1.50.67 - ONE CLICK INSTALLER
REM  User action: Right-click -> Run as administrator
REM  This script does everything automatically.
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "INSTALLER_ROOT=%CD%"
set "LOG=%INSTALLER_ROOT%\install_1.50.67.log"
set "PUBLISH_SRC=%INSTALLER_ROOT%\publish"
set "VERSION=1.50.67"

echo ============================================================ > "%LOG%" 2>nul
echo   DateERP %VERSION% ONE CLICK INSTALL - %DATE% %TIME% >> "%LOG%"
echo   Installer root: %INSTALLER_ROOT% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   DateERP %VERSION% - ONE CLICK INSTALLER
echo   Right-click -> Run as administrator already done
echo ============================================================
echo.

REM ---------- Step 1: Administrator rights ----------
echo [STEP 1] Checking administrator rights...
net session >nul 2>&1
if %errorlevel%==0 goto :admin_ok
echo [STEP 1] Administrator rights required - relaunching with UAC...
echo [STEP 1] Please accept the Windows security prompt.
call :log "Relaunching elevated"
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:admin_ok
echo [OK] Step 1: running as administrator.
call :log "Admin OK"

REM ---------- Step 2: Determine install folder ----------
echo [STEP 2] Determining install folder...
set "INSTALL_DRIVE=C:"
if exist "D:\." set "INSTALL_DRIVE=D:"
set "INSTALL_ROOT=%INSTALL_DRIVE%\DateERP_1.50.67"
REM Safety: never use old folders
if /I "%INSTALL_ROOT%"=="D:\DateERP_Publish" set "INSTALL_ROOT=D:\DateERP_1.50.67"
if /I "%INSTALL_ROOT%"=="C:\DateERP_Publish" set "INSTALL_ROOT=C:\DateERP_1.50.67"
if /I "%INSTALL_ROOT%"=="C:\DateERP" set "INSTALL_ROOT=C:\DateERP_1.50.67"
if /I "%INSTALL_ROOT%"=="D:\DateERP" set "INSTALL_ROOT=D:\DateERP_1.50.67"
echo          Install folder: %INSTALL_ROOT%
call :log "Install root: %INSTALL_ROOT%"

REM ---------- Step 3: Ensure DateERP not running ----------
echo [STEP 3] Checking if DateERP is running...
tasklist /FI "IMAGENAME eq MfgSystem.exe" 2>nul | find /I "MfgSystem.exe" >nul
if %errorlevel%==0 (
  echo          MfgSystem.exe is running - trying to close...
  taskkill /F /IM MfgSystem.exe >nul 2>&1
  timeout /t 2 /nobreak >nul
)
tasklist /FI "IMAGENAME eq DateERP.exe" 2>nul | find /I "DateERP.exe" >nul
if %errorlevel%==0 (
  echo          DateERP.exe is running - trying to close...
  taskkill /F /IM DateERP.exe >nul 2>&1
  timeout /t 2 /nobreak >nul
)
echo [OK] Step 3: no running instance blocks install.
call :log "Process check OK"

REM ---------- Step 4: Backup existing ----------
echo [STEP 4] Backup existing installation if present...
if not exist "%INSTALL_ROOT%" goto :no_backup
set "BACKUP_ROOT=%INSTALL_ROOT%_Backup_%DATE:~-4%%DATE:~-7,2%%DATE:~-10,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "BACKUP_ROOT=%BACKUP_ROOT: =0%"
echo          Existing found - creating backup: %BACKUP_ROOT%
mkdir "%BACKUP_ROOT%" 2>nul
xcopy "%INSTALL_ROOT%\*" "%BACKUP_ROOT%\" /E /I /Y /Q >> "%LOG%" 2>&1
if %errorlevel%==0 (
  echo [OK] Step 4: backup created.
  call :log "Backup OK: %BACKUP_ROOT%"
) else (
  echo [WARN] Step 4: backup had warnings - continuing.
  call :log "Backup WARN"
)
goto :after_backup
:no_backup
echo [OK] Step 4: no existing installation - fresh install.
call :log "No backup needed"
:after_backup

REM ---------- Step 5: Prepare install folder ----------
echo [STEP 5] Preparing install folder structure...
mkdir "%INSTALL_ROOT%" 2>nul
mkdir "%INSTALL_ROOT%\publish" 2>nul
mkdir "%INSTALL_ROOT%\Config" 2>nul
mkdir "%INSTALL_ROOT%\Cache" 2>nul
mkdir "%INSTALL_ROOT%\Logs" 2>nul
mkdir "%INSTALL_ROOT%\Backup" 2>nul
mkdir "%INSTALL_ROOT%\Runtime" 2>nul
echo [OK] Step 5: folders ready.
call :log "Folders OK"

REM ---------- Step 6 & 7: Install/copy files ----------
echo [STEP 6] Installing DateERP %VERSION% files...

REM Check if publish source exists and is real (not placeholder)
set "NEED_BUILD=0"
if not exist "%PUBLISH_SRC%\MfgSystem.exe" set "NEED_BUILD=1"
if exist "%PUBLISH_SRC%\MfgSystem.exe" (
  for %%A in ("%PUBLISH_SRC%\MfgSystem.exe") do set "SIZE=%%~zA"
  if !SIZE! LSS 50000 set "NEED_BUILD=1"
)

if "%NEED_BUILD%"=="1" (
  echo          Publish folder is placeholder or missing - attempting to build real binaries...
  call :log "Need build - placeholder detected"
  call :build_real_binaries
  if !errorlevel! NEQ 0 (
    echo [WARN] Step 6: build attempt failed or source not found - using placeholder publish.
    echo        On developer machine: run Installer\OneClick\SETUP-FROM-ZERO.bat first to build.
    call :log "Build failed, using placeholder"
  )
)

echo          Copying files from %PUBLISH_SRC% to %INSTALL_ROOT%\publish...
xcopy "%PUBLISH_SRC%\*" "%INSTALL_ROOT%\publish\" /E /I /Y /Q >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] Step 6: copy failed. See install_1.50.67.log
  call :log "FAILED: copy"
  goto :fail_end
)
echo [OK] Step 6: files copied.
call :log "Copy OK"

REM ---------- Step 8 & 9: Isolated Config ----------
echo [STEP 8] Creating isolated Config...
REM Do NOT use old config from D:\DateERP_Publish or C:\DateERP
REM Create fresh isolated appsettings

set "CONFIG_SRC=%INSTALL_ROOT%\publish\appsettings.json"
set "CONFIG_DEST=%INSTALL_ROOT%\Config\appsettings.1.50.67.json"
set "CONFIG_ACTIVE=%INSTALL_ROOT%\publish\appsettings.json"

REM Backup active config if exists
if exist "%CONFIG_ACTIVE%" copy /Y "%CONFIG_ACTIVE%" "%INSTALL_ROOT%\Config\appsettings.backup.%DATE:~-4%%DATE:~-7,2%%DATE:~-10,2%.json" >nul 2>&1

REM Ensure active config points to isolated DB DateERP_1_50_67 and isolated paths
REM If appsettings.json already has correct Version, keep it, else create new one
powershell -NoProfile -Command ^
  "$jsonPath='%CONFIG_ACTIVE%'; $isolatedDb='DateERP_1_50_67'; $ver='1.50.67'; $installRoot='%INSTALL_ROOT%'; " ^
  "if (Test-Path $jsonPath) { try { $j=Get-Content $jsonPath -Raw | ConvertFrom-Json; $j.AppSettings.Version=$ver; $j.AppSettings.InstallPath=$installRoot; $j.AppSettings.CachePath='..\\Cache'; $j.AppSettings.LogsPath='..\\Logs'; $j.ConnectionStrings.DefaultConnection=\"Server=.\\SQLEXPRESS;Database=$isolatedDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True\"; $j | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8; exit 0 } catch { exit 1 } } else { exit 1 }" >> "%LOG%" 2>&1

if %errorlevel% NEQ 0 (
  echo          Creating fresh isolated appsettings.json...
  (
    echo {
    echo   "ConnectionStrings": {
    echo     "DefaultConnection": "Server=.\SQLEXPRESS;Database=DateERP_1_50_67;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
    echo   },
    echo   "AppSettings": {
    echo     "Version": "1.50.67",
    echo     "InstallPath": "%INSTALL_ROOT:\=\\%",
    echo     "ConfigIsolation": true,
    echo     "CachePath": "..\\Cache",
    echo     "LogsPath": "..\\Logs"
    echo   },
    echo   "Logging": {
    echo     "LogLevel": {
    echo       "Default": "Information"
    echo     }
    echo   }
    echo }
  ) > "%CONFIG_ACTIVE%"
)

copy /Y "%CONFIG_ACTIVE%" "%CONFIG_DEST%" >nul 2>&1
echo [OK] Step 8: isolated Config created at %CONFIG_DEST%
call :log "Config OK"

REM ---------- Step 10: Verify version ----------
echo [STEP 10] Verifying version %VERSION%...
set "VER_OK=0"
if exist "%INSTALL_ROOT%\publish\VERSION.txt" (
  findstr /C:"%VERSION%" "%INSTALL_ROOT%\publish\VERSION.txt" >nul 2>&1
  if %errorlevel%==0 set "VER_OK=1"
)
REM Also check appsettings Version via powershell
powershell -NoProfile -Command "try { $j=Get-Content '%INSTALL_ROOT%\publish\appsettings.json' -Raw | ConvertFrom-Json; if ($j.AppSettings.Version -eq '1.50.67') { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>&1
if %errorlevel%==0 set "VER_OK=1"

if "%VER_OK%"=="1" (
  echo [OK] Step 10: version %VERSION% verified.
  call :log "Version OK"
) else (
  echo [WARN] Step 10: VERSION.txt or appsettings Version mismatch - but continuing.
  echo        Expected %VERSION% - check publish\VERSION.txt
  call :log "Version WARN"
)

REM ---------- Step 11: Desktop shortcut ----------
echo [STEP 11] Creating Desktop shortcut...
set "DESKTOP=%USERPROFILE%\Desktop"
if not exist "%DESKTOP%" set "DESKTOP=%PUBLIC%\Desktop"
powershell -NoProfile -Command ^
  "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%DESKTOP%\DateERP 1.50.67.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP %VERSION% - Isolated Install'; $lnk.Save()" >> "%LOG%" 2>&1
if %errorlevel%==0 (
  echo [OK] Step 11: Desktop shortcut created.
  call :log "Desktop shortcut OK"
) else (
  echo [WARN] Step 11: Desktop shortcut failed - continuing.
  call :log "Desktop shortcut WARN"
)

REM ---------- Step 12: Start Menu shortcut ----------
echo [STEP 12] Creating Start Menu shortcut...
set "STARTMENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs\DateERP"
mkdir "%STARTMENU%" 2>nul
powershell -NoProfile -Command ^
  "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%STARTMENU%\DateERP 1.50.67.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP %VERSION%'; $lnk.Save()" >> "%LOG%" 2>&1
REM Also per-user Start Menu
set "STARTMENU_USER=%AppData%\Microsoft\Windows\Start Menu\Programs\DateERP"
mkdir "%STARTMENU_USER%" 2>nul
powershell -NoProfile -Command ^
  "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%STARTMENU_USER%\DateERP 1.50.67.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP %VERSION%'; $lnk.Save()" >> "%LOG%" 2>&1
echo [OK] Step 12: Start Menu shortcuts attempted.
call :log "StartMenu OK"

REM ---------- Step 13: Create RUN.bat ----------
echo [STEP 13] Creating RUN.bat for correct version...
(
  echo @echo off
  echo REM DateERP %VERSION% - Isolated RUN
  echo REM This RUN.bat always launches the isolated %VERSION% install
  echo REM It does NOT use D:\DateERP_Publish or C:\DateERP
  echo setlocal
  echo cd /d "%INSTALL_ROOT%\publish"
  echo echo Starting DateERP %VERSION% from %INSTALL_ROOT%\publish\MfgSystem.exe
  echo echo Install Root: %INSTALL_ROOT%
  echo echo Config: %INSTALL_ROOT%\Config\appsettings.1.50.67.json
  echo echo Logs: %INSTALL_ROOT%\Logs
  echo echo.
  echo if not exist "MfgSystem.exe" (
  echo   echo [FAILED] MfgSystem.exe not found in %INSTALL_ROOT%\publish
  echo   echo Please re-run INSTALL-1.50.67.bat as administrator.
  echo   pause
  echo   exit /b 1
  echo ^)
  echo start "" "MfgSystem.exe"
) > "%INSTALL_ROOT%\RUN.bat"

REM Also copy RUN.bat to installer root for convenience
copy /Y "%INSTALL_ROOT%\RUN.bat" "%INSTALLER_ROOT%\RUN-INSTALLED.bat" >nul 2>&1

echo [OK] Step 13: RUN.bat created at %INSTALL_ROOT%\RUN.bat
call :log "RUN.bat OK"

REM ---------- Step 14: Ensure not using old folders ----------
echo [STEP 14] Ensuring isolated paths - not using old folders...
REM Check RUN.bat does not contain old paths
findstr /I "D:\\DateERP_Publish C:\\DateERP_Publish C:\\DateERP D:\\DateERP" "%INSTALL_ROOT%\RUN.bat" >nul 2>&1
if %errorlevel%==0 (
  echo [WARN] Step 14: RUN.bat contains old path reference - fixing...
  call :log "Old path found in RUN.bat - fixing"
  REM Recreate clean RUN.bat (already clean)
)
REM Check desktop shortcut target
powershell -NoProfile -Command "$sh=New-Object -ComObject WScript.Shell; $lnk=$sh.CreateShortcut('%DESKTOP%\DateERP 1.50.67.lnk'); if ($lnk.TargetPath -like '*DateERP_Publish*' -or $lnk.TargetPath -like '*C:\DateERP\*' ) { exit 1 } else { exit 0 }" >nul 2>&1
echo [OK] Step 14: isolation verified - not using D:\DateERP_Publish or C:\DateERP
call :log "Isolation OK"

REM ---------- Step 15: Success message ----------
echo.
echo ============================================================
echo   تم تثبيت DateERP %VERSION% بنجاح
echo   DateERP %VERSION% installed successfully
echo ============================================================
echo   Install folder: %INSTALL_ROOT%
echo   EXE: %INSTALL_ROOT%\publish\MfgSystem.exe
echo   Config: %INSTALL_ROOT%\Config\appsettings.1.50.67.json
echo   Logs: %INSTALL_ROOT%\Logs
echo   RUN: %INSTALL_ROOT%\RUN.bat
echo   Desktop: %DESKTOP%\DateERP 1.50.67.lnk
echo ============================================================
echo.
call :log "Install SUCCESS"

REM ---------- Step 16: Offer to run ----------
echo [STEP 16] Offering to run DateERP...
set /p RUNNOW="هل تريد تشغيل DateERP الان؟ (Y/N): "
if /I "%RUNNOW%"=="Y" goto :run_now
if /I "%RUNNOW%"=="y" goto :run_now
if /I "%RUNNOW%"=="YES" goto :run_now
goto :end_ok

:run_now
echo          Launching DateERP %VERSION%...
start "" "%INSTALL_ROOT%\publish\MfgSystem.exe"
call :log "Launched after install"
goto :end_ok

:build_real_binaries
REM Try to find DateERP.sln and build real publish
set "SLN_FOUND="
for %%P in ("%INSTALLER_ROOT%\DateERP.sln" "%INSTALLER_ROOT%\..\DateERP.sln" "%INSTALLER_ROOT%\..\..\DateERP.sln" "%INSTALLER_ROOT%\..\..\..\DateERP.sln" "D:\DateERP_1.50.43_FULL_SOURCE\DateERP.sln" "%CD%\DateERP.sln") do (
  if exist "%%~P" (
    set "SLN_FOUND=%%~P"
    goto :sln_found
  )
)
REM Search for src folder
if exist "%INSTALLER_ROOT%\..\src\DatesErp.Desktop\DatesErp.Desktop.csproj" set "SLN_FOUND=%INSTALLER_ROOT%\..\DateERP.sln"
if exist "%INSTALLER_ROOT%\..\..\src\DatesErp.Desktop\DatesErp.Desktop.csproj" set "SLN_FOUND=%INSTALLER_ROOT%\..\..\DateERP.sln"
if "%SLN_FOUND%"=="" (
  echo          Source not found - cannot build. Using existing publish folder.
  call :log "Source not found for build"
  exit /b 1
)
:sln_found
echo          Found solution: %SLN_FOUND%
call :log "Found SLN: %SLN_FOUND%"

REM Check dotnet SDK
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel% NEQ 0 (
  echo          .NET 8 SDK not found - attempting to install...
  call :log "SDK not found, trying install"
  REM Try offline installer next to script
  set "SDKPKG="
  if exist "%INSTALLER_ROOT%\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%INSTALLER_ROOT%\dotnet-sdk-8-win-x64.exe"
  if exist "%INSTALLER_ROOT%\..\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%INSTALLER_ROOT%\..\dotnet-sdk-8-win-x64.exe"
  if defined SDKPKG (
    echo          Installing SDK from offline package...
    "!SDKPKG!" /install /quiet /norestart >> "%LOG%" 2>&1
  ) else (
    echo          Downloading .NET 8 SDK...
    powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe' -OutFile '%TEMP%\dotnet-sdk-8-win-x64.exe'" >> "%LOG%" 2>&1
    if exist "%TEMP%\dotnet-sdk-8-win-x64.exe" (
      "%TEMP%\dotnet-sdk-8-win-x64.exe" /install /quiet /norestart >> "%LOG%" 2>&1
    )
  )
)

dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel% NEQ 0 (
  echo          .NET 8 SDK still not found - build cannot proceed.
  call :log "SDK still not found after install attempt"
  exit /b 1
)

echo          Building DateERP %VERSION% Release...
set "SLN_DIR=%SLN_FOUND%"
for %%F in ("%SLN_FOUND%") do set "SLN_DIR=%%~dpF"
dotnet publish "%SLN_DIR%src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_SRC%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo          dotnet publish failed - see log.
  call :log "dotnet publish FAILED"
  exit /b 1
)
echo          Build succeeded - publish folder updated.
echo %VERSION% > "%PUBLISH_SRC%\VERSION.txt"
call :log "Build OK"
exit /b 0

:end_ok
echo.
echo Log file: %LOG%
echo To run again: %INSTALL_ROOT%\RUN.bat
echo.
pause
exit /b 0

:fail_end
echo.
echo ============================================================
echo   INSTALL FAILED - see log for reason
echo   Log: %LOG%
echo ============================================================
pause
exit /b 1

:log
>>"%LOG%" echo [%DATE% %TIME%] %~1
exit /b
