@echo off
rem VERIFY-SOURCE 1.50.73 FINAL2
rem This BAT is intentionally ASCII-only and contains no inline PowerShell.
rem All source checks live in the independent PS1 file.
setlocal
cd /d "%~dp0"
set "VERIFY_PS1=%~dp0VERIFY-SOURCE-1.50.73.ps1"

if not exist "%VERIFY_PS1%" (
  echo [FAIL] Missing VERIFY-SOURCE-1.50.73.ps1
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%VERIFY_PS1%"
set "VERIFY_EXIT=%ERRORLEVEL%"
if not "%VERIFY_EXIT%"=="0" (
  echo [FAIL] FINAL2 source verification failed.
  exit /b %VERIFY_EXIT%
)

echo [PASS] FINAL2 source verification passed.
exit /b 0
