@echo off
setlocal
cd /d "%~dp0"
if not exist "PlanningCapacityWpfSmoke.exe" exit /b 1
set "LOG=%TEMP%\DateERP-Capacity-1.50.6-%RANDOM%.log"
echo Tests use isolated synthetic databases; --sql requires DATEERP_TEST_SQL.
"%~dp0PlanningCapacityWpfSmoke.exe" %* > "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%LOG%"
echo Log: %LOG%
echo Exit: %RC%
pause
exit /b %RC%
