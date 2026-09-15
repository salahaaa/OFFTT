@echo off
setlocal
cd /d "%~dp0"
if not exist "ReceivingWpfSmoke.exe" (
    echo ERROR: Extract the complete Windows release before running this check.
    pause
    exit /b 1
)
set "LOG=%TEMP%\DateERP-WpfSmoke-1.50.6-%RANDOM%.log"
echo Checking real WPF templates and bindings. No database will be opened.
"%~dp0ReceivingWpfSmoke.exe" > "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%LOG%"
echo.
echo Result exit code: %RC%
echo Log: %LOG%
if not "%RC%"=="0" echo FAILED: Do not update production. Send this log to support.
if "%RC%"=="0" echo PASSED: Continue with a backed-up TEST database, not production directly.
pause
exit /b %RC%
