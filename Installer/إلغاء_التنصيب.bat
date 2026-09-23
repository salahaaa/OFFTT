@echo off
chcp 65001 >nul
REM =========================================================================
REM  MfgSystem — Uninstall
REM  Removes the application + shortcuts. DATA (%LocalAppData%\MfgSystem)
REM  is KEPT by default and only deleted with explicit confirmation.
REM  Keeps application data unless removal is explicitly confirmed.
REM =========================================================================
setlocal EnableDelayedExpansion

set "APPNAME=MfgSystem"
set "DATADIR=%LocalAppData%\%APPNAME%"

set "DEST="
if exist "%ProgramFiles%\%APPNAME%\%APPNAME%.exe" set "DEST=%ProgramFiles%\%APPNAME%"
if not defined DEST if exist "%LocalAppData%\Programs\%APPNAME%\%APPNAME%.exe" set "DEST=%LocalAppData%\Programs\%APPNAME%"

echo.
echo ======================================================================
echo    MfgSystem — Uninstall
echo ======================================================================
echo.

if not defined DEST (
    echo    MfgSystem is not installed (not found in the usual locations).
    echo.
    pause
    exit /b 0
)

echo    Install location : %DEST%
echo    Data folder      : %DATADIR%   (KEPT by default)
echo.

taskkill /f /im "%APPNAME%.exe" >nul 2>&1

echo [1/3] Removing application files...
rmdir /s /q "%DEST%"
if errorlevel 1 (
    echo        [WARN] Could not remove all files — close MfgSystem and retry.
)

echo [2/3] Removing shortcuts...
del /q "%UserProfile%\Desktop\%APPNAME%.lnk" >nul 2>&1
del /q "%AppData%\Microsoft\Windows\Start Menu\Programs\%APPNAME%.lnk" >nul 2>&1
echo        Done.

echo [3/3] Data folder...
choice /c KD /m "Keep data? (K=keep [default]  D=delete data + local DB)"
if errorlevel 2 (
    if exist "%DATADIR%" rmdir /s /q "%DATADIR%"
    echo        Data deleted: %DATADIR%
) else (
    echo        Data kept: %DATADIR%
)

echo.
echo    Uninstall complete.
echo ======================================================================
echo.
pause
endlocal
