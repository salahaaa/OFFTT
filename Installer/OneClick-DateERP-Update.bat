@echo off
chcp 65001 >nul
setlocal EnableExtensions
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%PS%" (
    echo [ERROR] Windows PowerShell غير موجود على هذا الجهاز.
    pause
    exit /b 1
)

"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0OneClick-DateERP-Update.ps1"
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" (
    echo.
    echo [OK] تم التحديث بضغطة واحدة.
) else (
    echo.
    echo [ERROR] لم يكتمل التحديث. راجع سجل التحديث داخل LocalAppData\MfgSystem\updates.
)
pause
exit /b %RC%
