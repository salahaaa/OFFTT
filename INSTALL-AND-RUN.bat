@echo off
setlocal EnableExtensions
REM MfgSystem 1.50.72 - build, install and launch from this clean source folder.
REM The application uses its isolated local SQLite profile on first launch.
REM This script never copies over an older DateERP folder or deletes database data.

if /I "%~1"=="--elevated" goto :elevated
net session >nul 2>&1
if not errorlevel 1 goto :elevated
set "INSTALLER_PATH=%~f0"
echo [STEP 0] Requesting administrator permission...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = Start-Process -FilePath $env:INSTALLER_PATH -ArgumentList '--elevated' -Verb RunAs -Wait -PassThru; exit $p.ExitCode"
exit /b %errorlevel%

:elevated
for %%I in ("%~dp0.") do set "ROOT=%%~fI"
set "VER=1.50.72"
if /I "%ROOT%"=="D:\DateERP_1.50.20_Full" goto :legacy_root
if /I "%ROOT%"=="D:\DateERP_1.50.20_Full\DateERP_1.50.20" goto :legacy_root
if /I "%ROOT%"=="D:\DateERP_1.50.43_FULL_SOURCE" goto :legacy_root
set "APP=%ROOT%\Application_%VER%"
set "LOG=%ROOT%\install_log.txt"
set "SDKPKG=%ROOT%\dotnet-sdk-8-win-x64.exe"
set "TEMP_SDK=%TEMP%\MfgSystem_dotnet_sdk_8.exe"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

>"%LOG%" echo MfgSystem %VER% installer - %DATE% %TIME%
>>"%LOG%" echo Source root: %ROOT%
echo.
echo ============================================================
echo   MfgSystem %VER% - ONE CLICK INSTALL AND RUN
echo   Source: %ROOT%
echo ============================================================
echo.

if not exist "%ROOT%\DateERP.sln" goto :source_missing
if not exist "%ROOT%\src\DatesErp.Desktop\DatesErp.Desktop.csproj" goto :source_missing
echo [OK] Step 1: complete source project found.
>>"%LOG%" echo [OK] Step 1: source project found.

if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
if exist "%LocalAppData%\Microsoft\dotnet\dotnet.exe" set "PATH=%LocalAppData%\Microsoft\dotnet;%PATH%"
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if not errorlevel 1 goto :sdk_ready

echo [STEP 2] .NET 8 SDK not found. Checking offline installer...
if exist "%SDKPKG%" goto :install_sdk
if exist "%TEMP_SDK%" set "SDKPKG=%TEMP_SDK%"
if exist "%SDKPKG%" goto :install_sdk

echo [INFO] Downloading .NET 8 SDK installer. If offline, place dotnet-sdk-8-win-x64.exe beside this BAT and retry.
>>"%LOG%" echo [INFO] Downloading .NET 8 SDK installer.
set "SDKPKG=%TEMP_SDK%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; try { Invoke-WebRequest -UseBasicParsing -Uri 'https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe' -OutFile $env:SDKPKG; exit 0 } catch { Write-Error $_; exit 1 }" >>"%LOG%" 2>&1
if errorlevel 1 goto :sdk_download_failed
if not exist "%SDKPKG%" goto :sdk_download_failed

:install_sdk
echo [STEP 2] Installing .NET 8 SDK...
>>"%LOG%" echo [STEP 2] Installing .NET 8 SDK.
start /wait "" "%SDKPKG%" /install /quiet /norestart >>"%LOG%" 2>&1
set "SDKRC=%ERRORLEVEL%"
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if not errorlevel 1 goto :sdk_ready
if "%SDKRC%"=="3010" echo [INFO] Windows requested a restart after SDK installation.
echo [FAILED] Step 2: .NET 8 SDK is unavailable after installation.
>>"%LOG%" echo [FAILED] Step 2: .NET 8 SDK unavailable. Installer exit code %SDKRC%.
goto :fail

:sdk_ready
echo [OK] Step 2: .NET 8 SDK is ready.
>>"%LOG%" echo [OK] Step 2: .NET 8 SDK ready.
if not exist "%APP%" mkdir "%APP%"
if errorlevel 1 goto :output_folder_failed
tasklist /FI "IMAGENAME eq MfgSystem.exe" 2>nul | find /I "MfgSystem.exe" >nul
if not errorlevel 1 goto :app_running

echo [STEP 3] Restoring project dependencies. Internet or a complete local NuGet cache may be required...
>>"%LOG%" echo [STEP 3] Restoring Desktop project dependencies.
dotnet restore "%ROOT%\src\DatesErp.Desktop\DatesErp.Desktop.csproj" -r win-x64 --ignore-failed-sources >>"%LOG%" 2>&1
if errorlevel 1 goto :restore_failed
echo [OK] Step 3: dependencies restored.

:build_step
echo [STEP 4] Publishing self-contained Windows x64 application...
>>"%LOG%" echo [STEP 4] Publishing self-contained Windows x64 application.
dotnet publish "%ROOT%\src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -r win-x64 --self-contained true --no-restore -o "%APP%" >>"%LOG%" 2>&1
if errorlevel 1 goto :build_failed
if not exist "%APP%\MfgSystem.exe" goto :exe_missing
echo [OK] Step 4: BUILD SUCCESS.
>>"%LOG%" echo [OK] BUILD SUCCESS.

set "MFG_EXE=%APP%\MfgSystem.exe"
set "MFG_WORK=%APP%"
set "MFG_LINK=%USERPROFILE%\Desktop\MfgSystem %VER%.lnk"
echo [STEP 5] Creating desktop shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut($env:MFG_LINK); $s.TargetPath=$env:MFG_EXE; $s.WorkingDirectory=$env:MFG_WORK; $s.Description='MfgSystem %VER%'; $s.Save()" >>"%LOG%" 2>&1
if errorlevel 1 goto :shortcut_failed
echo [OK] Step 5: INSTALL SUCCESS - desktop shortcut created.
>>"%LOG%" echo [OK] INSTALL SUCCESS.
if exist "%LOCALAPPDATA%\MfgSystem\config.json" (
  echo [INFO] Existing MfgSystem profile preserved; no database or user data was deleted.
  >>"%LOG%" echo [INFO] Existing MfgSystem profile detected and preserved.
) else (
  echo [INFO] First launch creates a new local database under %%LOCALAPPDATA%%\MfgSystem.
  >>"%LOG%" echo [INFO] First launch will create the isolated local SQLite database.
)
echo [INFO] Previous DateERP folders and databases are not used by this installer.
echo [INFO] Local database: %LOCALAPPDATA%\MfgSystem\mfgsystem_local.db
echo [INFO] Log: %LOG%
echo.
echo ============================================================
echo   BUILD SUCCESS - INSTALL SUCCESS
echo   Application: %APP%\MfgSystem.exe
echo   Shortcut:    %MFG_LINK%
echo   Database:    isolated local SQLite (first install)
echo ============================================================
start "" "%MFG_EXE%"
exit /b 0

:legacy_root
echo [FAILED] Refusing to publish inside a protected previous-version folder.
echo Extract this package into a new folder, for example D:\MfgSystem_1.50.72.
pause
exit /b 1

:source_missing
echo [FAILED] Required source files are missing. Extract the complete ZIP, then run this BAT from its root.
>>"%LOG%" echo [FAILED] Required source files are missing.
goto :fail

:sdk_download_failed
echo [FAILED] .NET SDK download failed. Put dotnet-sdk-8-win-x64.exe beside this BAT and retry.
>>"%LOG%" echo [FAILED] SDK download failed.
goto :fail

:app_running
echo [FAILED] Close the running MfgSystem application before installing or publishing. It was not terminated.
>>"%LOG%" echo [FAILED] A MfgSystem.exe process is running.
goto :fail

:output_folder_failed
echo [FAILED] Could not create the application output folder.
>>"%LOG%" echo [FAILED] Could not create output folder.
goto :fail

:restore_failed
echo [FAILED] Dependency restore failed. See install_log.txt; NuGet internet access or a complete local cache is required.
>>"%LOG%" echo [FAILED] Dependency restore failed.
goto :fail

:build_failed
echo [FAILED] BUILD FAILED. See install_log.txt for compiler output.
>>"%LOG%" echo [FAILED] BUILD FAILED.
goto :fail

:exe_missing
echo [FAILED] Publish ended but MfgSystem.exe was not produced.
>>"%LOG%" echo [FAILED] MfgSystem.exe missing.
goto :fail

:shortcut_failed
echo [FAILED] Could not create the desktop shortcut.
>>"%LOG%" echo [FAILED] Desktop shortcut creation failed.
goto :fail

:fail
echo.
echo INSTALL STOPPED. No existing database was deleted or overwritten.
echo Review: %LOG%
pause
exit /b 1
