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
goto NoPowerShell
:HavePowerShell
"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0تحديث_مباشر.ps1" %*
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" goto Success
echo [ERROR] Live update failed. Read the updater log for details.
goto End
:NoPowerShell
echo [ERROR] Windows PowerShell was not found on this computer.
echo Install Windows PowerShell 5.1 or PowerShell 7, then run this file again.
set "RC=1"
goto End
:Success
echo [OK] Live update completed.
set "RC=0"
:End
pause
exit /b %RC%
