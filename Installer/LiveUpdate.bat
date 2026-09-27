@echo off
setlocal EnableExtensions
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%PS%" goto HavePowerShell
where powershell.exe >nul 2>&1
if not errorlevel 1 (
  set "PS=powershell.exe"
  goto HavePowerShell
)
where pwsh.exe >nul 2>&1
if not errorlevel 1 (
  set "PS=pwsh.exe"
  goto HavePowerShell
)
echo [ERROR] Windows PowerShell was not found.
pause
exit /b 1
:HavePowerShell
"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0تحديث_مباشر.ps1" %*
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" (echo [OK] Live update completed.) else (echo [ERROR] Live update failed. Read updater.log.)
pause
exit /b %RC%
