@echo off
chcp 65001 >nul
REM =========================================================================
REM  MfgSystem — One-Click Installer  (v1.50.74)
REM  - Self-contained: NO .NET install needed on the target machine.
REM  - Admin detected  -> installs to %ProgramFiles%\MfgSystem
REM  - No admin        -> installs to %LocalAppData%\Programs\MfgSystem
REM                        (no UAC prompt, works on any user account)
REM  - Does NOT write any database config: the app's AutoDbResolver
REM    discovers local SQL Server instances at first launch (including
REM    .\SQLEXPRESS01) and falls back to local SQLite if none is found.
REM  - Uses the dedicated MfgSystem executable, data folder (%LocalAppData%\MfgSystem), and database.
REM =========================================================================
setlocal EnableDelayedExpansion

set "SRC=%~dp0"
set "APPNAME=MfgSystem"
set "EXE=%APPNAME%.exe"
set "DATADIR=%LocalAppData%\%APPNAME%"

echo.
echo ======================================================================
echo    %APPNAME% — Manufacturing System — One-Click Installer
echo ======================================================================
echo.
echo    Source : %SRC%
echo.

REM ── 0) Close a running instance (releases locked files) ──
taskkill /f /im "%EXE%" >nul 2>&1

REM ── 1) Package integrity ──
echo [1/6] Verifying package integrity...
if not exist "%SRC%%EXE%" (
    echo        [ERROR] %EXE% not found next to this script.
    echo        Extract the FULL package and run this file from inside it.
    pause
    exit /b 1
)
if exist "%SRC%SHA256.txt" (
    set "EXPECT="
    for /f "delims=" %%H in (%SRC%SHA256.txt) do set "EXPECT=%%H"
    if defined EXPECT (
        for /f "delims=" %%H in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 '%SRC%%EXE%').Hash"') do set "ACTUAL=%%H"
        if /i "!EXPECT!"=="!ACTUAL!" (
            echo        SHA256 OK
        ) else (
            echo        [ERROR] SHA256 mismatch — package is corrupted or modified.
            echo        Expected: !EXPECT!
            echo        Actual  : !ACTUAL!
            pause
            exit /b 1
        )
    ) else (
        echo        [WARN] SHA256.txt is empty — integrity check skipped.
    )
) else (
    echo        [WARN] SHA256.txt not found — integrity check skipped.
)
echo.

REM ── 2) Choose install target based on privileges ──
echo [2/6] Selecting install location...
set "ADMIN=0"
net session >nul 2>&1 && set "ADMIN=1"
if "%ADMIN%"=="1" (
    set "DEST=%ProgramFiles%\%APPNAME%"
    echo        Administrator detected -> %DEST%
) else (
    set "DEST=%LocalAppData%\Programs\%APPNAME%"
    echo        No admin rights -> %DEST%  (no UAC needed)
)
echo.

REM ── 3) Prepare application data folder ──
echo [3/6] Preparing isolated data folder...
if not exist "%DATADIR%" mkdir "%DATADIR%"
if not exist "%DATADIR%\logs" mkdir "%DATADIR%\logs"
if exist "%DATADIR%\config.json" (
    copy /y "%DATADIR%\config.json" "%DATADIR%\config.backup.json" >nul
    echo        Existing config.json backed up (config.backup.json).
)
echo        Data folder: %DATADIR%
echo        (database connection is auto-detected by the app at first launch)
echo.

REM ── 4) Copy files ──
echo [4/6] Copying files...
if exist "%DEST%" rmdir /s /q "%DEST%" >nul 2>&1
mkdir "%DEST%"
if not exist "%DEST%" (
    echo        [ERROR] Cannot create %DEST% — check permissions.
    pause
    exit /b 1
)
xcopy /e /i /y /q "%SRC%*.*" "%DEST%\" >nul
if errorlevel 1 (
    echo        [ERROR] Copy failed to %DEST%.
    echo        Close any program using the folder and retry,
    echo        or run this file as administrator.
    pause
    exit /b 1
)
echo        Copied to %DEST%
echo.

REM ── 5) Shortcuts (inline — no external dependency) ──
echo [5/6] Creating shortcuts...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws=New-Object -ComObject WScript.Shell; $d=$ws.CreateShortcut([Environment]::GetFolderPath('Desktop')+'\%APPNAME%.lnk'); $d.TargetPath='%DEST%\%APPNAME%.exe'; $d.WorkingDirectory='%DEST%'; $d.IconLocation='%DEST%\%APPNAME%.exe,0'; $d.Description='MfgSystem 1.50.74 - Manufacturing System'; $d.Save(); $s=$ws.CreateShortcut([Environment]::GetFolderPath('Programs')+'\%APPNAME%.lnk'); $s.TargetPath='%DEST%\%APPNAME%.exe'; $s.WorkingDirectory='%DEST%'; $s.IconLocation='%DEST%\%APPNAME%.exe,0'; $s.Save()" >nul 2>&1
if errorlevel 1 (
    echo        [WARN] Could not create shortcuts automatically.
    echo        Create one manually to: %DEST%\%APPNAME%.exe
) else (
    echo        Desktop shortcut   OK
    echo        Start menu shortcut OK
)
echo.

REM ── 6) Done ──
echo ======================================================================
echo    INSTALL COMPLETE
echo.
echo    Run       : MfgSystem shortcut on the desktop
echo    Location  : %DEST%
echo    Data      : %DATADIR%
echo    Logs      : %DATADIR%\logs
echo    Uninstall : "%DEST%\إلغاء_التنصيب.bat"
echo.
echo    First launch: the app auto-detects local SQL Server instances
echo    (including .\SQLEXPRESS01) and creates MfgSystemDB; if none is
echo    found it starts in local SQLite mode. Change the connection later
echo    from: System Info -^> Edit Connection.
echo ======================================================================
echo.

choice /c YN /t 10 /d Y /m "Launch MfgSystem now? (Y/N — auto after 10s)"
if errorlevel 2 goto :end
start "" "%DEST%\%APPNAME%.exe"
:end
echo.
pause
endlocal
