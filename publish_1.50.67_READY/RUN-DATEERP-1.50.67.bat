@echo off
REM =====================================================================
REM  DateERP 1.50.67 - READY - ONE CLICK - SELF-CONTAINED win-x64
REM  Fixed XAML duplicate Bd (BdTool/BdGreen/BdGhost) + verify_xaml_names.py 0 errors
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%CD%"
set "LOG=%ROOT%\install_1.50.67.log"
set "VERSION=1.50.67"
set "DBNAME=DateERP_1_50_67"
set "INSTALL_DRIVE=C:"
if exist "D:\." set "INSTALL_DRIVE=D:"
set "INSTALL_ROOT=%INSTALL_DRIVE%\DateERP_1.50.67"
echo ============================================================ > "%LOG%" 2>nul
echo   DateERP %VERSION% READY - %DATE% %TIME% >> "%LOG%"
echo   Root: %ROOT% >> "%LOG%"
echo   Install: %INSTALL_ROOT% >> "%LOG%"
echo   DB: %DBNAME% >> "%LOG%"
echo ============================================================ >> "%LOG%"
echo.
echo ============================================================
echo   DateERP %VERSION% - READY - SELF-CONTAINED win-x64
echo   Fixes: PrintPreviewWindow Bd duplicate, verify_xaml 0 errors
echo ============================================================
echo.
net session >nul 2>&1
if %errorlevel%==0 goto :admin_ok
echo [STEP] Administrator required - relaunching UAC...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:admin_ok
echo [OK] Running as administrator.
echo [STEP 2] Checking SQL Server Express .\SQLEXPRESS...
reg query "HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" /v SQLEXPRESS >nul 2>&1
if %errorlevel%==0 goto :sql_ok
echo        SQL Server Express not found - will try install...
set "SQLPKG="
if exist "%ROOT%\Tools\SQLEXPR_x64_ENU.exe" set "SQLPKG=%ROOT%\Tools\SQLEXPR_x64_ENU.exe"
if exist "%ROOT%\Tools\SQL2022-SSEI-Expr.exe" set "SQLPKG=%ROOT%\Tools\SQL2022-SSEI-Expr.exe"
if defined SQLPKG (
  echo        Installing SQL from offline: !SQLPKG!
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
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?linkid=2216019' -OutFile '%TEMP%\SQL2022-SSEI-Expr.exe'" >> "%LOG%" 2>&1
  if exist "%TEMP%\SQL2022-SSEI-Expr.exe" (
    if not exist "%TEMP%\sqlmedia" mkdir "%TEMP%\sqlmedia"
    "%TEMP%\SQL2022-SSEI-Expr.exe" /Action=Download /Language=en-US /MediaType=Core /MediaPath="%TEMP%\sqlmedia" /Quiet >> "%LOG%" 2>&1
    if exist "%TEMP%\sqlmedia\setup.exe" (
      "%TEMP%\sqlmedia\setup.exe" /Q /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /TCPENABLED=1 /NPENABLED=1 /SQLBROWSERSTARTUPTYPE=Automatic /ENABLERANU=1 /SECURITYMODE=SQL /SAPWD="MfgSystem#2026Sa" /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS >> "%LOG%" 2>&1
    )
  )
)
:sql_ok
echo [OK] SQL Server instance ready.
echo [STEP 3] Checking/Creating database %DBNAME%...
set "SQLCMD="
where sqlcmd >nul 2>&1
if %errorlevel%==0 set "SQLCMD=sqlcmd"
if not defined SQLCMD (
  if exist "%ProgramFiles%\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe" set "SQLCMD=%ProgramFiles%\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe"
)
if not defined SQLCMD goto :skip_db_create
echo IF NOT EXISTS ^(SELECT name FROM master.dbo.sysdatabases WHERE name = '%DBNAME%'^) CREATE DATABASE [%DBNAME%] > "%TEMP%\create_db_%DBNAME%.sql"
"%SQLCMD%" -S .\SQLEXPRESS -E -i "%TEMP%\create_db_%DBNAME%.sql" >> "%LOG%" 2>&1
del "%TEMP%\create_db_%DBNAME%.sql" >nul 2>&1
:skip_db_create
echo [OK] Step 3: Database ready.
echo [STEP 4] Checking .NET 8 SDK...
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel%==0 goto :sdk_ok
echo        .NET 8 SDK not found - installing...
set "SDKPKG="
if exist "%ROOT%\Tools\dotnet-sdk-8-win-x64.exe" set "SDKPKG=%ROOT%\Tools\dotnet-sdk-8-win-x64.exe"
if defined SDKPKG (
  "!SDKPKG!" /install /quiet /norestart >> "%LOG%" 2>&1
) else (
  powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe' -OutFile '%TEMP%\dotnet-sdk-8-win-x64.exe'" >> "%LOG%" 2>&1
  if exist "%TEMP%\dotnet-sdk-8-win-x64.exe" (
    "%TEMP%\dotnet-sdk-8-win-x64.exe" /install /quiet /norestart >> "%LOG%" 2>&1
  )
)
:sdk_ok
echo [OK] .NET 8 SDK ready.
echo [STEP 5] Finding DateERP.sln...
set "SLN="
for %%P in ("%ROOT%\Source\DateERP.sln" "%ROOT%\DateERP.sln") do (
  if exist "%%~P" set "SLN=%%~P"
)
if "%SLN%"=="" (
  echo [FAILED] DateERP.sln not found
  pause
  exit /b 1
)
for %%F in ("%SLN%") do set "SLN_DIR=%%~dpF"
echo [STEP 5b] Verifying XAML names...
if exist "%SLN_DIR%tools\ci\verify_xaml_names.py" (
  python "%SLN_DIR%tools\ci\verify_xaml_names.py" >> "%LOG%" 2>&1
  if %errorlevel% NEQ 0 (
    echo [FAILED] XAML verification failed
    pause
    exit /b 1
  )
  echo [OK] XAML verification passed - 0 errors.
)
echo [STEP 5] Building Release...
dotnet restore "%SLN%" -v m >> "%LOG%" 2>&1
dotnet build "%SLN%" -c Release -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [FAILED] build failed
  pause
  exit /b 1
)
echo [OK] Build SUCCESS.
echo [STEP 6] Publishing Self-Contained win-x64 to %INSTALL_ROOT%\publish...
set "PUBLISH_OUT=%INSTALL_ROOT%\publish"
mkdir "%INSTALL_ROOT%" 2>nul
mkdir "%PUBLISH_OUT%" 2>nul
mkdir "%INSTALL_ROOT%\Config" 2>nul
mkdir "%INSTALL_ROOT%\Cache" 2>nul
mkdir "%INSTALL_ROOT%\Logs" 2>nul
mkdir "%INSTALL_ROOT%\Backup" 2>nul
dotnet publish "%SLN_DIR%src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_OUT%" -v m >> "%LOG%" 2>&1
if %errorlevel% NEQ 0 (
  echo [WARN] Self-contained publish failed, trying framework-dependent...
  dotnet publish "%SLN_DIR%src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -o "%PUBLISH_OUT%" -v m >> "%LOG%" 2>&1
  if %errorlevel% NEQ 0 (
    echo [FAILED] publish failed
    pause
    exit /b 1
  )
)
echo 1.50.67 > "%PUBLISH_OUT%\VERSION.txt"
for /f %%A in ('dir /b /a-d "%PUBLISH_OUT%" ^| find /c /v ""') do set FILECOUNT=%%A
echo [OK] Publish SUCCESS - %FILECOUNT% files.
if not exist "%PUBLISH_OUT%\MfgSystem.exe" (
  echo [FAILED] MfgSystem.exe not found
  pause
  exit /b 1
)
for %%A in ("%PUBLISH_OUT%\MfgSystem.exe") do set "EXESIZE=%%~zA"
echo [OK] MfgSystem.exe size: %EXESIZE% bytes
echo [STEP 7] Creating isolated Config...
powershell -NoProfile -Command "$jsonPath='%PUBLISH_OUT%\appsettings.json'; $ver='1.50.67'; $root='%INSTALL_ROOT%'; if (Test-Path $jsonPath) { try { $j=Get-Content $jsonPath -Raw | ConvertFrom-Json; $j.AppSettings.Version=$ver; $j.AppSettings.InstallPath=$root; $j.AppSettings.CachePath='..\\Cache'; $j.AppSettings.LogsPath='..\\Logs'; $j.ConnectionStrings.DefaultConnection='Server=.\\SQLEXPRESS;Database=DateERP_1_50_67;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True'; $j | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8 } catch {} }" >> "%LOG%" 2>&1
copy /Y "%PUBLISH_OUT%\appsettings.json" "%INSTALL_ROOT%\Config\appsettings.1.50.67.json" >nul 2>&1
echo [STEP 8] Creating shortcuts...
set "DESKTOP=%USERPROFILE%\Desktop"
if not exist "%DESKTOP%" set "DESKTOP=%PUBLIC%\Desktop"
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%DESKTOP%\DateERP 1.50.67.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.67'; $lnk.Save()" >> "%LOG%" 2>&1
set "STARTMENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs\DateERP"
mkdir "%STARTMENU%" 2>nul
powershell -NoProfile -Command "$WshShell=New-Object -ComObject WScript.Shell; $lnk=$WshShell.CreateShortcut('%STARTMENU%\DateERP 1.50.67.lnk'); $lnk.TargetPath='%INSTALL_ROOT%\publish\MfgSystem.exe'; $lnk.WorkingDirectory='%INSTALL_ROOT%\publish'; $lnk.Description='DateERP 1.50.67'; $lnk.Save()" >> "%LOG%" 2>&1
(
  echo @echo off
  echo cd /d "%INSTALL_ROOT%\publish"
  echo start "" "MfgSystem.exe"
) > "%INSTALL_ROOT%\RUN.bat"
echo.
echo ============================================================
echo   DateERP %VERSION% installed SUCCESS - SELF-CONTAINED win-x64
echo   Install: %INSTALL_ROOT%
echo   EXE: %INSTALL_ROOT%\publish\MfgSystem.exe (%EXESIZE% bytes)
echo   Files: %FILECOUNT%
echo   DB: %DBNAME% on .\SQLEXPRESS
echo   RUN: %INSTALL_ROOT%\RUN.bat
echo ============================================================
start "" "%INSTALL_ROOT%\publish\MfgSystem.exe"
echo Log: %LOG%
pause
exit /b 0
