@echo off
REM =====================================================================
REM  MfgSystem (DateERP) 1.50.69 - ONE CLICK UPDATE + DESKTOP SHORTCUT
REM
REM  What it does (in order, stops on the first failure):
REM    1. Finds the installed system folder (default:
REM       D:\DateERP_1.50.20_Full\DateERP_1.50.20  - or pass a path as
REM       the first argument, or set TARGET below).
REM    2. Backs up the current project files of the target to
REM       <TARGET>\_backup_before_1.50.69_<date>\  (src + sln only).
REM    3. Copies the updated project files from THIS folder over the
REM       target (src, tools, tests, Installer, Documentation, docs,
REM       DateERP.sln). bin/obj are never copied.
REM    4. Builds + publishes the app into <TARGET>\Application\
REM    5. Creates a desktop shortcut "DateERP" -> MfgSystem.exe
REM    6. Launches the system.
REM
REM  Rules:
REM   - Pure ASCII, no code-page switching.
REM   - NEVER touches the database, config.json (%LocalAppData%\MfgSystem)
REM     or any user data. Nothing is deleted from the target.
REM   - [OK] / [FAILED] per step, full log: update_log.txt next to this file.
REM   - Needs .NET 8 SDK (dotnet). If missing and an offline installer
REM     dotnet-sdk-8-win-x64.exe is next to this script it is installed.
REM =====================================================================
REM --- keep the window open no matter what happens (errors stay visible) ---
if not defined DATEERP_KEEPOPEN (
    set "DATEERP_KEEPOPEN=1"
    start "MfgSystem UPDATE" cmd /k ""%~f0" %*"
    exit /b
)
setlocal EnableDelayedExpansion
set "HERE=%~dp0."
for %%I in ("%HERE%") do set "HERE=%%~fI"
echo [BOOT] script started from: %HERE%
set "LOG=%HERE%\update_log.txt"
set "VER=1.50.69"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=D:\DateERP_1.50.20_Full\DateERP_1.50.20"

echo ============================================================ > "%LOG%" 2>nul
echo   MfgSystem UPDATE %VER% - %DATE% %TIME% >> "%LOG%"
echo   Source : %HERE% >> "%LOG%"
echo   Target : %TARGET% >> "%LOG%"
echo ============================================================ >> "%LOG%"

echo.
echo ============================================================
echo   MfgSystem [DateERP] - UPDATE TO %VER% + DESKTOP SHORTCUT
echo   Source : %HERE%
echo   Target : %TARGET%
echo ============================================================
echo.

REM ---------- Step 1: source sanity ----------
if not exist "%HERE%\DateERP.sln" (
    echo [FAILED] Step 1: DateERP.sln not found next to this script.
    echo          Extract the full update zip and run this file from its root folder.
    call :log "FAILED: source DateERP.sln missing"
    goto :fail_end
)
echo [OK] Step 1: update source is complete.
call :log "Source OK"

REM ---------- Step 2: find the target project root ----------
if not exist "%TARGET%" (
    echo [FAILED] Step 2: target folder does not exist: %TARGET%
    echo          Fix: run as   UPDATE-AND-RUN.bat "D:\path\to\DateERP_1.50.20"
    call :log "FAILED: target missing"
    goto :fail_end
)
set "ROOT="
if exist "%TARGET%\DateERP.sln" set "ROOT=%TARGET%"
if "%ROOT%"=="" for /f "delims=" %%F in ('dir /s /b "%TARGET%\DateERP.sln" 2^>nul') do if "!ROOT!"=="" set "ROOT=%%~dpF"
if "%ROOT%"=="" (
    echo [INFO] No DateERP.sln inside the target - the project will be placed in the target root.
    set "ROOT=%TARGET%"
)
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
echo [OK] Step 2: project root = %ROOT%
call :log "Root: %ROOT%"

REM ---------- Step 3: close a running instance ----------
tasklist /FI "IMAGENAME eq MfgSystem.exe" 2>nul | find /I "MfgSystem.exe" >nul
if %errorlevel%==0 (
    echo [INFO] MfgSystem.exe is running - closing it so files can be replaced...
    taskkill /IM MfgSystem.exe /F >nul 2>&1
    timeout /t 2 /nobreak >nul
)
echo [OK] Step 3: no running instance.

REM ---------- Step 4: backup current project files ----------
if /I "%HERE%"=="%ROOT%" (
    echo [OK] Step 4: update was extracted directly into the target - backup not needed.
    echo [OK] Step 5: update files already in place [COPY SUCCESS].
    call :log "Source == Target: steps 4-5 skipped"
    set "BK=%ROOT% [files were extracted in place - no backup]"
    goto :after_copy
)
set "STAMP="
for /f "delims=" %%S in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmm"') do set "STAMP=%%S"
if "%STAMP%"=="" set "STAMP=%RANDOM%"
set "BK=%ROOT%\_backup_before_%VER%_%STAMP%"
if exist "%ROOT%\src" (
    robocopy "%ROOT%\src" "%BK%\src" /E /XD bin obj /NFL /NDL /NJH /NJS /NP >nul
    if errorlevel 8 (
        echo [FAILED] Step 4: backup of src failed. See %LOG%
        call :log "FAILED: backup"
        goto :fail_end
    )
)
if exist "%ROOT%\DateERP.sln" copy /Y "%ROOT%\DateERP.sln" "%BK%\DateERP.sln" >nul 2>&1
echo [OK] Step 4: backup saved to %BK%
call :log "Backup: %BK%"

REM ---------- Step 5: copy the update (nothing deleted on the target) ----------
set "COPYFAIL=0"
for %%D in (src tools tests Installer Documentation docs) do (
    if exist "%HERE%\%%D" (
        robocopy "%HERE%\%%D" "%ROOT%\%%D" /E /XD bin obj .vs /XF *.user /NFL /NDL /NJH /NJS /NP >nul
        if errorlevel 8 set "COPYFAIL=1"
    )
)
copy /Y "%HERE%\DateERP.sln" "%ROOT%\DateERP.sln" >nul || set "COPYFAIL=1"
if exist "%HERE%\README.md" copy /Y "%HERE%\README.md" "%ROOT%\README.md" >nul
if exist "%HERE%\.editorconfig" copy /Y "%HERE%\.editorconfig" "%ROOT%\.editorconfig" >nul
if "%COPYFAIL%"=="1" (
    echo [FAILED] Step 5: copying update files failed [files locked or no permission].
    echo          Your previous files are intact in %BK%
    call :log "FAILED: copy"
    goto :fail_end
)
echo [OK] Step 5: update files copied [COPY SUCCESS].
call :log "Copy OK"
:after_copy

REM ---------- Step 6: .NET 8 SDK ----------
dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
if %errorlevel%==0 goto :sdk_ok
if exist "%HERE%\dotnet-sdk-8-win-x64.exe" (
    echo [INFO] .NET 8 SDK not found - installing from the offline installer...
    "%HERE%\dotnet-sdk-8-win-x64.exe" /install /quiet /norestart
    set "PATH=%ProgramFiles%\dotnet;%PATH%"
    dotnet --list-sdks 2>nul | findstr /R /C:"^8\." >nul 2>&1
    if !errorlevel!==0 goto :sdk_ok
)
echo [FAILED] Step 6: .NET 8 SDK [dotnet] is not installed - the app cannot be built.
echo          Fix: put dotnet-sdk-8-win-x64.exe next to this script and run again,
echo          or run Installer\SETUP-FROM-ZERO.bat once (needs internet).
call :log "FAILED: no .NET 8 SDK"
goto :fail_end
:sdk_ok
echo [OK] Step 6: .NET 8 SDK present.
call :log "SDK OK"

REM ---------- Step 7: build + publish ----------
set "APP=%ROOT%\Application"
echo [STEP 7] Building %VER% - this takes 2-6 minutes, please wait...
dotnet publish "%ROOT%\src\DatesErp.Desktop\DatesErp.Desktop.csproj" -c Release -r win-x64 --self-contained false -o "%APP%" -v m >> "%LOG%" 2>&1
if errorlevel 1 (
    echo [FAILED] Step 7: BUILD FAILED. Full compiler output is in:
    echo          %LOG%
    echo          Send that file for support. Your previous files are in %BK%
    call :log "FAILED: build"
    goto :fail_end
)
if not exist "%APP%\MfgSystem.exe" (
    echo [FAILED] Step 7: build finished but MfgSystem.exe was not produced.
    call :log "FAILED: exe missing"
    goto :fail_end
)
echo [OK] Step 7: BUILD SUCCESS - %APP%\MfgSystem.exe
call :log "Build OK"

REM ---------- Step 8: version check ----------
set "BUILTVER="
powershell -NoProfile -Command "$v=(Get-Item '%APP%\MfgSystem.exe').VersionInfo.ProductVersion; Set-Content -Path '%HERE%\_ver.tmp' -Value $v" >nul 2>&1
if exist "%HERE%\_ver.tmp" set /p BUILTVER=<"%HERE%\_ver.tmp"
if exist "%HERE%\_ver.tmp" del "%HERE%\_ver.tmp" >nul 2>&1
echo [INFO] Built version: %BUILTVER%
call :log "Built version: %BUILTVER%"
echo %BUILTVER% | findstr /C:"%VER%" >nul || echo [WARN] Version string differs from %VER% - continuing.

REM ---------- Step 9: desktop shortcut ----------
powershell -NoProfile -Command "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $d 'DateERP.lnk')); $s.TargetPath='%APP%\MfgSystem.exe'; $s.WorkingDirectory='%APP%'; $s.IconLocation='%APP%\MfgSystem.exe,0'; $s.Description='MfgSystem DateERP %VER%'; $s.Save()" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo [FAILED] Step 9: could not create the desktop shortcut.
    call :log "FAILED: shortcut"
    goto :fail_end
)
echo [OK] Step 9: desktop shortcut "DateERP" created.
call :log "Shortcut OK"

REM ---------- Step 10: launch ----------
echo.
echo ============================================================
echo   UPDATE %VER% COMPLETE  [BUILD SUCCESS] [COPY SUCCESS]
echo   App     : %APP%\MfgSystem.exe
echo   Shortcut: Desktop\DateERP.lnk
echo   Backup  : %BK%
echo   Log     : %LOG%
echo ============================================================
call :log "DONE"
start "" "%APP%\MfgSystem.exe"
echo.
echo   You can close this window now.
exit /b 0

:fail_end
echo.
echo ============================================================
echo   UPDATE STOPPED - nothing further was changed.
echo   Log: %LOG%
echo ============================================================
pause
exit /b 1

:log
echo [%TIME%] %~1 >> "%LOG%"
exit /b 0
