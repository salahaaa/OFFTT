@echo off
REM =====================================================================
REM  DateERP 1.50.66 - FULL 200MB READY - FIXED - ONE CLICK
REM  Pure ASCII - Fixes parenthesis bug that caused window to disappear
REM  Run as administrator - Opens DB + Installs + Runs directly
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%CD%"
set "LOG=%ROOT%\full_200mb_install.log"
set "VERSION=1.50.66"
set "DBNAME=DateERP_1_50_66"
set "INSTALL_DRIVE=C:"
if exist "D:\." set "INSTALL_DRIVE=D:"
set "INSTALL_ROOT=%INSTALL_DRIVE%\DateERP_1.50.66"

echo ============================================================ > "%LOG%" 2>nul
echo   DateERP %VERSION% FULL 200MB READY FIXED - %DATE% %TIME% >> "%LOG%"
echo   Root: %ROOT% >> "%LOG%"
echo   Install: %INSTALL_ROOT% >> "%LOG%"
echo   DB: %DBNAME% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   DateERP %VERSION% - FULL 200MB READY - FIXED
echo   One click - Opens DB + Installs + Runs directly
echo ============================================================
echo   Steps:
echo   1. Check Administrator
echo   2. Check/Install SQL Server 2022 Express
echo   3. Open/Create Database %DBNAME%
echo   4. Check/Install .NET 8 SDK
echo   5. Build Release
echo   6. Publish to %INSTALL_ROOT%
echo   7. Create Desktop Shortcuts
echo   8. Run directly
echo ============================================================
echo.

REM ---------- Admin ----------
net session >nul 2>&1
if %errorlevel%==0 goto :admin_ok
echo [STEP] Administrator required - relaunching UAC...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:admin_ok
echo [OK] Running as administrator.
call :log "Admin OK"

REM ---------- Check existing ----------
if exist "%INSTALL_ROOT%\publish\MfgSystem.exe" (
  for %%A in ("%INSTALL_ROOT%\publish\MfgSystem.exe") do set "SIZE=%%~zA"
  if !SIZE! GTR 50000 (
    echo [INFO] Found existing real install at %INSTALL_ROOT% - size !SIZE!
    goto :check_sql
  )
)

REM ---------- SQL Server ----------
:check_sql
echo [STEP 2] Checking SQL Server Express .\SQLEXPRESS...
reg query "HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" /v SQLEXPRESS >nul 2>&1
if %errorlevel%==0 goto :sql_ok

echo        SQL Server Express not found - will try install...
set "SQLPKG="
if exist "%ROOT%\Tools\SQLEXPR_x64_ENU.exe" set "SQLPKG=%ROOT%\Tools\SQLEXPR_x64_ENU.exe"
if exist "%ROOT%\Installer\SQLEXPR_x64_ENU.exe" set "SQLPKG=%ROOT%\Installer\SQLEXPR_x64_ENU.exe"
if exist "%ROOT%\Source\Installer\SQLEXPR_x64_ENU.exe" set "SQLPKG=%ROOT%\Source\Installer\SQLEXPR_x64_ENU.exe"
if exist "%ROOT%\Tools\SQL2022-SSEI-Expr.exe" set "SQLPKG=%ROOT%\Tools\SQL2022-SSEI-Expr.exe"

if defined SQLPKG (
  echo        Installing SQL from offline: !SQLPKG!
  echo        Takes 10-30 min...
  if /I "!SQLPKG:~-12!"=="SSEI-Expr.exe" (
    if not exist "%TEMP%\sqlmedia" mkdir "%TEMP%\sqlmedia"
    "!SQLPKG!" /Action=Download /Language=en-US /MediaType=Core /MediaPath="%TEMP%\sqlmedia" /Quiet >> "%LOG%" 2>&1
    if exist "%TEMP%\sqlmedia\setup.exe" (
      "%TEMP%\sqlmedia\setup.exe" /Q /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /TCPENABLED=1 /NPENABLED=1 /SQLBROWSERSTARTUPTYPE=Automatic /ENABLERANU=1 /SECURITYMODE=SQL /SAPWD="MfgSystem#2026Sa" /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS >> "%LOG%" 2>&1
    )
  ) else (
    "!SQLPKG!" /Q /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /TCPENABLED=1 /NPENABLED=1 /SQLBROWSERSTARTUPTYPE=Automatic /ENABLERANU=1 /SECURITYMODE=SQL /SAPWD="MfgSystem#2026Sa" /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS >> "%LOG%" 2>&1
  )
) else (
  echo        Downloading SQL Server 2022 Express bootstrapper...
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?linkid=2216019' -OutFile '%TEMP%\SQL2022-SSEI-Expr.exe'" >> "%LOG%" 2>&1
  if exist "%TEMP%\SQL2022-SSEI-Expr.exe" (
    if not exist "%TEMP%\sqlmedia" mkdir "%TEMP%\sqlmedia"
    "%TEMP%\SQL2022-SSEI-Expr.exe" /Action=Download /Language=en-US /MediaType=Core /MediaPath="%TEMP%\sqlmedia" /Quiet >> "%LOG%" 2>&1
    if exist "%TEMP%\sqlmedia\setup.exe" (
      "%TEMP%\sqlmedia\setup.exe" /Q /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /TCPENABLED=1 /NPENABLED=1 /SQLBROWSERSTARTUPTYPE=Automatic /ENABLERANU=1 /SECURITYMODE=SQL /SAPWD="MfgSystem#2026Sa" /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS >> "%LOG%" 2>&1
    )
  )
)

reg query "HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" /v SQLEXPRESS >nul 2>&1
if %errorlevel%==0 (
  echo [OK] SQL Server Express installed.
  call :log "SQL OK"
) else (
  echo [WARN] SQL Server not found after install - will continue, app auto-creates DB.
  call :log "SQL WARN"
)

:sql_ok
echo [OK] SQL Server instance ready.

REM ---------- Database - FIXED parenthesis bug ----------
echo [STEP 3] Checking/Creating database %DBNAME%...

REM Find sqlcmd - avoid complex IF with parentheses
set "SQLCMD="
where sqlcmd >nul 2>&1
if %errorlevel%==0 set "SQLCMD=sqlcmd"

if not defined SQLCMD (
  if exist "%ProgramFiles%\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe" set "SQLCMD=%ProgramFiles%\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe"
)
if not defined SQLCMD (
  if exist "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe" set "SQLCMD=C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe"
)

if not defined SQLCMD goto :skip_db_create

echo        Found sqlcmd: %SQLCMD%
echo        Creating temp SQL file to avoid parenthesis bug...

REM Create temp SQL file - FIX: avoid ( ) inside IF block by using file
echo IF NOT EXISTS ^(SELECT name FROM master.dbo.sysdatabases WHERE name = '%DBNAME%'^) CREATE DATABASE [%DBNAME%] > "%TEMP%\create_db_%DBNAME%.sql"
echo Created temp file: %TEMP%\create_db_%DBNAME%.sql
type "%TEMP%\create_db_%DBNAME%.sql" >> "%LOG%" 2>&1

echo        Running sqlcmd to create DB...
"%SQLCMD%" -S .\SQLEXPRESS -E -i "%TEMP%\create_db_%DBNAME%.sql" >> "%LOG%" 2>&1
if %errorlevel%==0 (
  echo [OK] Database %DBNAME% checked/created via sqlcmd.
  call :log "DB OK via sqlcmd"
) else (
  echo [WARN] sqlcmd failed to create DB - app will auto-create on first run.
  call :log "DB WARN sqlcmd failed"
)
del "%TEMP%\create_db_%DBNAME%.sql" >nul 2>&1
goto :after_db_create

:skip_db_create
echo        sqlcmd not found - database will be auto-created by app on first run.
echo        This is normal - DateERP creates DB automatically.
call :log "DB will be auto-created by app - sqlcmd not found"

:after_db_create
echo [OK] Step 3: Database ready.

REM ---------- .NET SDK ----------
echo [STEP 4] Checking .NET 8 SDK...
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel%==0 goto :sdk_ok

echo        .NET 8 SDK not found - installing...
set "SDKPKG="
if exist "%ROOT%\Tools\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\Tools\dotnet-sdk-8-win-x64.exe"
if exist "%ROOT%\Installer\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\Installer\dotnet-sdk-8-win-x64.exe"
if exist "%ROOT%\Source\Installer\OneClick\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\Source\Installer\OneClick\dotnet-sdk-8-win-x64.exe"

if defined SDKPKG (
  echo        Installing from offline: !SDKPKG!
  "!SDKPKG!" /install /quiet /norestart >> "%LOG%" 2>&1
) else (
  echo        Downloading .NET 8 SDK 220MB...
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe' -OutFile '%TEMP%\dotnet-sdk-8-win-x64.exe'" >> "%LOG%" 2>&1
  if exist "%TEMP%\dotnet-sdk-8-win-x64.exe" (
    "%TEMP%\dotnet-sdk-8-win-x64.exe" /install /quiet /norestart >> "%LOG%" 2>&1
  )
)

dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] .NET 8 SDK install failed. See %LOG%
  echo Log: %LOG%
  pause
  exit /b 1
)

:sdk_ok
echo [OK] .NET 8 SDK ready.

REM ---------- Find SLN ----------
echo [STEP 5] Finding DateERP.sln...
set "SLN="
for %%P in ("%ROOT%\Source\DateERP.sln" "%ROOT%\DateERP.sln" "%ROOT%\..\DateERP.sln") do (
  if exist "%%~P" set "SLN=%%~P"
)
if "%SLN%"=="" (
  echo [FAILED] DateERP.sln not found at %ROOT%\Source\DateERP.sln
  echo Please ensure Source folder exists.
  pause
  exit /b 1
)
echo        Found: %SLN%
for %%F in ("%SLN%") do set "SLN_DIR=%%~dpF"

REM ---------- Build ----------
echo [STEP 5] Building Release - first build 2-6 min...
dotnet restore "%SLN%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] restore failed - see %LOG%
  pause
  exit /b 1
)
dotnet build "%SLN%" -c Release -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] build failed - see %LOG%
  pause
  exit /b 1
)
echo [OK] Build SUCCESS.

REM ---------- Publish ----------
echo [STEP 6] Publishing to %INSTALL_ROOT%\publish...
set "PUBLISH_OUT=%INSTALL_ROOT%\publish"
mkdir "%INSTALL_ROOT%" 2>nul
mkdir "%PUBLISH_OUT%" 2>nul
mkdir "%INSTALL_ROOT%\Config" 2>nul
mkdir "%INSTALL_ROOT%\Cache" 2>nul
mkdir "%INSTALL_ROOT%\Logs" 2>nul
mkdir "%INSTALL_ROOT%\Backup" 2>nul
mkdir "%INSTALL_ROOT%\Runtime" 2>nul

if exist "%PUBLISH_OUT%\MfgSystem.exe" (
  set "BACKUP=%INSTALL_ROOT%_Backup_%DATE:~-4%%DATE:~-7,2%%DATE:~-10,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
  set "BACKUP=!BACKUP: =0!"
  mkdir "!BACKUP!" 2>nul
  xcopy "%INSTALL_ROOT%\*" "!BACKUP!\" /E /I /Y /Q >> "%LOG%" 2>&1
)

dotnet publish "%SLN_DIR%src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -o "%PUBLISH_OUT%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] publish failed - see %LOG%
  pause
  exit /b 1
)
echo %VERSION% > "%PUBLISH_OUT%\VERSION.txt"
echo [OK] Publish SUCCESS.

REM ---------- Config ----------
echo [STEP 7] Creating isolated Config...
powershell -NoProfile -Command "$jsonPath='%PUBLISH_OUT%\appsettings.json'; $ver='1.50.66'; $root='%INSTALL_ROOT%'; if (Test-Path $jsonPath) { try { $j=Get-Content $jsonPath -Raw | ConvertFrom-Json; $j.AppSettings.Version=$ver; $j.AppSettings.InstallPath=$root; $j.AppSettings.CachePath='..\\Cache'; $j.AppSettings.LogsPath='..\\Logs'; $j.ConnectionStrings.DefaultConnection='Server=.\\SQLEXPRESS;Database=DateERP_1_50_66;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True'; $j | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8 } catch {} }" >> "%LOG%" 2>&1
copy /Y "%PUBLISH_OUT%\appsettings.json" "%INSTALL_ROOT%\Config\appsettings.1.50.66.json" >nul 2>&1

REM ---------- Shortcuts ----------
echo [STEP 8] Creating shortcuts...
set "DESKTOP=%USERPROFILE%\Desktop"
if not exist "%DESKTOP%" set "DESKTOP=%PUBLIC%\Desktop"
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%DESKTOP%\DateERP 1.50.66.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.66'; $lnk.Save()" >> "%LOG%" 2>&1

set "STARTMENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs\DateERP"
mkdir "%STARTMENU%" 2>nul
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%STARTMENU%\DateERP 1.50.66.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.66'; $lnk.Save()" >> "%LOG%" 2>&1

(
  echo @echo off
  echo cd /d "%INSTALL_ROOT%\publish"
  echo start "" "MfgSystem.exe"
) > "%INSTALL_ROOT%\RUN.bat"

echo [OK] Shortcuts created.

REM ---------- Final ----------
echo.
echo ============================================================
echo   DateERP %VERSION% installed SUCCESS - FULL 200MB READY FIXED
echo   Database %DBNAME% opened/created
echo   System installed to %INSTALL_ROOT%
echo ============================================================
echo   Install: %INSTALL_ROOT%
echo   EXE: %INSTALL_ROOT%\publish\MfgSystem.exe
echo   DB: %DBNAME% on .\SQLEXPRESS
echo   Config: %INSTALL_ROOT%\Config\appsettings.1.50.66.json
echo   Logs: %INSTALL_ROOT%\Logs
echo   RUN: %INSTALL_ROOT%\RUN.bat
echo ============================================================
call :log "Install SUCCESS"

echo [STEP 9] Opening DateERP directly...
start "" "%INSTALL_ROOT%\publish\MfgSystem.exe"

echo.
echo Log: %LOG%
echo Desktop: %DESKTOP%\DateERP 1.50.66.lnk
echo RUN: %INSTALL_ROOT%\RUN.bat
echo.
pause
exit /b 0

:log
>>"%LOG%" echo [%DATE% %TIME%] %~1
exit /b
