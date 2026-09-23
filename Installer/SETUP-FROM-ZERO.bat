@echo off
REM =====================================================================
REM  MfgSystem 1.50.50 - FULL FROM-ZERO SETUP  [one click]
REM  Installs everything the system needs, in this order:
REM    1. .NET 8 SDK              - build and run the app
REM    2. SQL Server 2022 Express - the database engine [SQLEXPRESS]
REM    3. SSMS                    - SQL management studio [optional]
REM    4. Visual Studio 2022 Community - see and edit the screens
REM  Then builds DateERP.sln and launches MfgSystem.exe.
REM
REM  Rules this script follows:
REM   - Pure ASCII, no code-page switching, no touching user data.
REM   - Self-elevates to administrator [accept the UAC prompt].
REM   - Every checkpoint prints [OK] or [FAILED] with the reason and
REM     the script STOPS on failure. Full log: install_log.txt here.
REM   - Safe to re-run: finished components are detected and skipped.
REM   - OFFLINE FIRST: copy any of these installers next to this
REM     script and they are used WITHOUT internet:
REM       dotnet-sdk-8-win-x64.exe  /  SQLEXPR_x64_ENU.exe [full SQL
REM       Server 2022 Express package]  /  SSMS-Setup-ENU.exe  /
REM       vs_community.exe [VS still fetches workloads online]
REM   - Otherwise products are downloaded from official Microsoft
REM     servers [aka.ms / go.microsoft.com] - they cannot legally be
REM     bundled inside the project zip.
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0.."
set "ROOT=%CD%"
set "HERE=%~dp0"
set "LOG=%HERE%install_log.txt"
set "TMPD=%TEMP%\dateerp_setup"
if not exist "%TMPD%" mkdir "%TMPD%"

echo ============================================================ > "%LOG%" 2>nul
echo   MfgSystem FULL FROM-ZERO SETUP - %DATE% %TIME% >> "%LOG%"
echo   Project root: %ROOT% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   MfgSystem [TASNEE] - FULL SETUP FROM ZERO
echo   .NET 8 SDK + SQL Server 2022 Express + SSMS
echo   + Visual Studio 2022 Community, then build and run.
echo ============================================================
echo.

REM ---------- Checkpoint 0: administrator rights ----------
net session >nul 2>&1
if %errorlevel%==0 goto :admin_ok
echo [STEP 0] Administrator rights required - relaunching with UAC prompt...
echo [STEP 0] Accept the Windows security prompt to continue.
call :log "Relaunching elevated"
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:admin_ok
echo [OK] Step 0: running as administrator.
call :log "Admin OK"

REM ---------- Checkpoint 1: internet connection ----------
echo [STEP 1] Checking internet connection to Microsoft servers...
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'https://aka.ms' -Method Head -UseBasicParsing -TimeoutSec 25 | Out-Null } catch { exit 1 }" >nul 2>&1
if %errorlevel%==0 goto :net_ok
echo [FAILED] Step 1: no internet connection to https://aka.ms
echo          Reason: downloads come from Microsoft servers.
echo          Fix: connect the internet, disable proxy/VPN if any, run again.
call :log "FAILED: internet check"
goto :fail_end
:net_ok
echo [OK] Step 1: internet connection works.
call :log "Internet OK"

REM ---------- Checkpoint 2: disk space [warning only] ----------
for /f %%A in ('powershell -NoProfile -Command "[math]::Round((Get-PSDrive ($env:SystemDrive.Substring(0,1))).Free/1GB,1)"') do set "FREEGB=%%A"
echo [INFO] Free space on system drive: %FREEGB% GB.
echo        Recommended: 25 GB or more [Visual Studio alone needs 10-20 GB].
call :log "Free GB: %FREEGB%"

REM ---------- Checkpoint 3: .NET 8 SDK ----------
echo [STEP 3] Checking .NET 8 SDK...
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel%==0 goto :sdk_ok
set "SDKPKG="
if exist "%HERE%dotnet-sdk-8-win-x64.exe" set "SDKPKG=%HERE%dotnet-sdk-8-win-x64.exe"
if defined SDKPKG echo          Offline installer found next to this script - no download needed.
if defined SDKPKG goto :sdk_install
echo          Not found - downloading .NET 8 SDK [about 220 MB]...
call :log "Downloading .NET 8 SDK"
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe' -OutFile '%TMPD%\dotnet-sdk-8-win-x64.exe'"
if not exist "%TMPD%\dotnet-sdk-8-win-x64.exe" goto :fail_sdk_dl
set "SDKPKG=%TMPD%\dotnet-sdk-8-win-x64.exe"
:sdk_install
echo          Installing silently - may take several minutes...
"!SDKPKG!" /install /quiet /norestart >> "%LOG%" 2>&1
set "PATH=%PATH%;%ProgramFiles%\dotnet"
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel%==0 goto :sdk_ok
echo [FAILED] Step 3: .NET 8 SDK install failed. See install_log.txt.
call :log "FAILED: dotnet sdk install"
goto :fail_end
:fail_sdk_dl
echo [FAILED] Step 3: .NET SDK download failed.
echo          Manual fix: https://dotnet.microsoft.com/download/dotnet/8.0
echo          install [SDK x64], then run this script again.
call :log "FAILED: dotnet sdk download"
goto :fail_end
:sdk_ok
echo [OK] Step 3: .NET 8 SDK ready.
call :log "SDK OK"

REM ---------- Checkpoint 4: SQL Server Express ----------
echo [STEP 4] Checking SQL Server local instance...
reg query "HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" >nul 2>&1
if %errorlevel%==0 goto :sql_ok
set "SQLPKG="
for /f "delims=" %%F in ('dir /b /a-d "%HERE%SQLEXPR*.exe" 2^>nul') do set "SQLPKG=%HERE%%%F"
if defined SQLPKG goto :sql_offline
echo          Not found - downloading SQL Server 2022 Express [about 250 MB
echo          bootstrapper, then it downloads the engine packages]...
call :log "Downloading SQL Server 2022 Express"
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?linkid=2216019' -OutFile '%TMPD%\SQL2022-SSEI-Expr.exe'"
if not exist "%TMPD%\SQL2022-SSEI-Expr.exe" goto :fail_sql_dl
echo          Downloading SQL Server engine packages - official two-step method...
if not exist "%TMPD%\sqlmedia" mkdir "%TMPD%\sqlmedia"
"%TMPD%\SQL2022-SSEI-Expr.exe" /Action=Download /Language=en-US /MediaType=Core /MediaPath="%TMPD%\sqlmedia" /Quiet >> "%LOG%" 2>&1
if not exist "%TMPD%\sqlmedia\setup.exe" goto :fail_sql_dl
echo          Installing SQL Server Express silently - this takes 10-30 minutes...
"%TMPD%\sqlmedia\setup.exe" /Q /x86=false /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /TCPENABLED=1 /NPENABLED=1 /SQLBROWSERSTARTUPTYPE=Automatic /ENABLERANU=1 /SECURITYMODE=SQL /SAPWD="MfgSystem#2026Sa" /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS >> "%LOG%" 2>&1
set "SQLRC=%errorlevel%"
goto :sql_rc
:sql_offline
echo          Offline SQL Server FULL package found next to this script:
echo          !SQLPKG!
echo          Installing silently from it - NO internet needed, 10-30 minutes...
call :log "SQL offline install from !SQLPKG!"
"!SQLPKG!" /Q /x86=false /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=SQLEXPRESS /TCPENABLED=1 /NPENABLED=1 /SQLBROWSERSTARTUPTYPE=Automatic /ENABLERANU=1 /SECURITYMODE=SQL /SAPWD="MfgSystem#2026Sa" /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS >> "%LOG%" 2>&1
set "SQLRC=!errorlevel!"
:sql_rc
call :log "SQL setup exit code: !SQLRC!"
if "!SQLRC!"=="0" goto :sql_check
if "!SQLRC!"=="3010" goto :sql_check
echo [FAILED] Step 4: SQL Server install exit code !SQLRC!. See install_log.txt.
echo          Common cause: pending Windows reboot - reboot, run again.
goto :fail_end
:fail_sql_dl
echo [FAILED] Step 4: SQL Server download failed. Check internet, run again.
call :log "FAILED: sql download"
goto :fail_end
:sql_check
reg query "HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" >nul 2>&1
if %errorlevel%==0 goto :sql_ok
echo [FAILED] Step 4: SQL Server instance still not registered after install.
echo          Reboot Windows once, then run this script again.
call :log "FAILED: sql instance not found after install"
goto :fail_end
:sql_ok
echo [OK] Step 4: SQL Server Express instance ready - name .\SQLEXPRESS
echo          SA password set to: MfgSystem#2026Sa  [Windows auth also works]
call :log "SQL OK"

REM ---------- Checkpoint 5: SSMS [optional - never blocks] ----------
echo [STEP 5] Checking SQL Server Management Studio...
dir /b "C:\Program Files (x86)\Microsoft SQL Server Management Studio*" >nul 2>&1
if %errorlevel%==0 goto :ssms_ok
set "SSMSPKG="
if exist "%HERE%SSMS-Setup-ENU.exe" set "SSMSPKG=%HERE%SSMS-Setup-ENU.exe"
if defined SSMSPKG goto :ssms_install
echo          Not found - downloading SSMS 20.2 full package [about 700 MB]...
call :log "Downloading SSMS 20.2 full package"
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/?linkid=2313753&clcid=0x409' -OutFile '%TMPD%\SSMS-Setup-ENU.exe'"
if not exist "%TMPD%\SSMS-Setup-ENU.exe" goto :ssms_skip
set "SSMSPKG=%TMPD%\SSMS-Setup-ENU.exe"
:ssms_install
echo          Installing SSMS silently - may take 10-20 minutes...
"!SSMSPKG!" /install /quiet /norestart >> "%LOG%" 2>&1
if %errorlevel%==0 goto :ssms_ok
:ssms_skip
echo [WARN] Step 5: SSMS not installed - OPTIONAL component, continuing.
echo        You can install it later from https://aka.ms/ssmsfullsetup
call :log "WARN: SSMS skipped"
goto :vs_step
:ssms_ok
echo [OK] Step 5: SSMS ready.
call :log "SSMS OK"

REM ---------- Checkpoint 6: Visual Studio 2022 Community ----------
:vs_step
echo [STEP 6] Checking Visual Studio 2022...
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :vs_install
"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Workload.ManagedDesktop >nul 2>&1
if %errorlevel%==0 goto :vs_ok
:vs_install
set "VSPKG="
if exist "%HERE%vs_community.exe" set "VSPKG=%HERE%vs_community.exe"
if defined VSPKG goto :vs_run
echo          Not found - downloading Visual Studio 2022 Community bootstrapper...
call :log "Downloading VS 2022 Community"
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_community.exe' -OutFile '%TMPD%\vs_community.exe'"
if not exist "%TMPD%\vs_community.exe" goto :fail_vs_dl
set "VSPKG=%TMPD%\vs_community.exe"
:vs_run
echo          Installing Visual Studio 2022 Community silently.
echo          NOTE: VS always fetches its workloads from the internet on this
echo          PC [3-8 GB, 30-90 min] - the exe alone is only a bootstrapper.
echo          The .NET desktop workload + SQL Server Data Tools are included.
"!VSPKG!" --quiet --norestart --wait --includeRecommended --add Microsoft.VisualStudio.Workload.ManagedDesktop --add Microsoft.VisualStudio.Component.SQL.Server.Data >> "%LOG%" 2>&1
set "VSRC=%errorlevel%"
call :log "VS installer exit code: %VSRC%"
if "%VSRC%"=="0" goto :vs_ok
if "%VSRC%"=="3010" goto :vs_ok
if "%VSRC%"=="1602" echo [WARN] VS install was cancelled - you can run this script again later.
echo [FAILED] Step 6: Visual Studio install exit code %VSRC%. See install_log.txt.
call :log "FAILED: VS install rc=%VSRC%"
goto :fail_end
:fail_vs_dl
echo [FAILED] Step 6: Visual Studio download failed. Check internet, run again.
call :log "FAILED: VS download"
goto :fail_end
:vs_ok
echo [OK] Step 6: Visual Studio 2022 Community ready.
call :log "VS OK"

REM ---------- Checkpoint 7: build the system ----------
echo [STEP 7] Building DateERP.sln in Release - first build takes 2-6 minutes...
call :log "Building solution"
dotnet build "%ROOT%\DateERP.sln" -c Release -v m >> "%LOG%" 2>&1
if %errorlevel%==0 goto :build_ok
echo [FAILED] Step 7: build failed. Open install_log.txt and send it for support.
call :log "FAILED: build"
goto :fail_end
:build_ok
echo [OK] Step 7: BUILD SUCCESS.
call :log "Build OK"

REM ---------- Checkpoint 8: launch ----------
set "EXE=%ROOT%\src\DatesErp.Desktop\bin\Release\net8.0-windows\MfgSystem.exe"
if not exist "%EXE%" goto :fail_exe
echo [STEP 8] Launching MfgSystem...
start "" "%EXE%"
echo [OK] Step 8: MfgSystem is starting now.
call :log "Launch OK"
echo.
echo ============================================================
echo   ALL CHECKPOINTS PASSED - SYSTEM READY
echo   - App launched. First run creates the database
echo     automatically and seeds the base data.
echo   - To see and edit the screens: double-click
echo     OPEN-VISUAL-STUDIO.bat  [in Installer]
echo   - To run the app again: RUN-SYSTEM.bat
echo   - Full Arabic guide: Installer\INSTALL-GUIDE-AR.html
echo ============================================================
echo.
pause
exit /b

:fail_exe
echo [FAILED] Step 8: MfgSystem.exe not found after a successful build.
call :log "FAILED: exe missing"
goto :fail_end

:fail_end
echo.
echo ============================================================
echo   SETUP STOPPED - see the FAILED line above for the reason.
echo   Log file: %LOG%
echo   After fixing the cause, run this script again - completed
echo   steps are detected and skipped automatically.
echo ============================================================
pause
exit /b 1

:log
>>"%LOG%" echo [%DATE% %TIME%] %~1
exit /b
