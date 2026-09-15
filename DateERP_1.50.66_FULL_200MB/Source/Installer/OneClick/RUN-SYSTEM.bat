@echo off
REM =====================================================================
REM  MfgSystem - build if needed, then run the app.
REM  Safe: never touches the database or user data; the app itself
REM  creates/migrates its database automatically at startup.
REM =====================================================================
setlocal
cd /d "%~dp0..\.."
set "ROOT=%CD%"
set "EXE=%ROOT%\src\DatesErp.Desktop\bin\Release\net8.0-windows\MfgSystem.exe"
if exist "%EXE%" goto :run
echo [INFO] App not built yet - building once, 2-6 minutes...
dotnet build "%ROOT%\DateERP.sln" -c Release -v m
if errorlevel 1 goto :fail_build
if exist "%EXE%" goto :run
:fail_build
echo [FAILED] Build failed or MfgSystem.exe missing.
echo          Run SETUP-FROM-ZERO.bat and send install_log.txt for support.
pause
exit /b 1
:run
start "" "%EXE%"
exit /b
