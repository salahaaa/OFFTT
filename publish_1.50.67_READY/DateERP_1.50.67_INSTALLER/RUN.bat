@echo off
REM DateERP 1.50.67 - RUN isolated version
REM This file in installer is a template. After install, use C:\DateERP_1.50.67\RUN.bat or D:\DateERP_1.50.67\RUN.bat
REM It does NOT use D:\DateERP_Publish or C:\DateERP

setlocal
cd /d "%~dp0"
set "ROOT=%CD%"

REM Determine installed location - prefer D:\DateERP_1.50.67 then C:\DateERP_1.50.67 then local publish
set "INSTALLED="
if exist "D:\DateERP_1.50.67\publish\MfgSystem.exe" set "INSTALLED=D:\DateERP_1.50.67"
if exist "C:\DateERP_1.50.67\publish\MfgSystem.exe" set "INSTALLED=C:\DateERP_1.50.67"
if defined INSTALLED (
  echo Starting DateERP 1.50.67 from %INSTALLED%\publish\MfgSystem.exe
  cd /d "%INSTALLED%\publish"
  start "" "MfgSystem.exe"
  exit /b 0
)

REM Fallback: run from installer publish folder (for testing)
if exist "%ROOT%\publish\MfgSystem.exe" (
  echo Starting DateERP 1.50.67 from installer publish folder (test mode)
  cd /d "%ROOT%\publish"
  start "" "MfgSystem.exe"
  exit /b 0
)

echo [FAILED] DateERP 1.50.67 not found.
echo Expected: D:\DateERP_1.50.67\publish\MfgSystem.exe or C:\DateERP_1.50.67\publish\MfgSystem.exe
echo Please run INSTALL-1.50.67.bat as administrator first.
pause
exit /b 1
