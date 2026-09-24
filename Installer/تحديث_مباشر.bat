@echo off
chcp 65001 >nul
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0تحديث_مباشر.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo [ERROR] فشل التحديث. راجع الرسالة أعلاه أو سجل التحديث.
) else (
  echo [OK] انتهى التحديث.
)
pause
exit /b %RC%
