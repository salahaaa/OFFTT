@echo off
setlocal
cd /d "%~dp0"
if not exist "PrintWpfSmoke.exe" (
 echo ERROR: This check requires the complete updated DateERP 1.50.6 folder.
 pause
 exit /b 1
)
set "EVIDENCE=%TEMP%\DateERP-Print-1.50.6-%RANDOM%"
mkdir "%EVIDENCE%"
echo This test renders fabricated documents. No database or printer jobs are used.
echo Evidence folder: %EVIDENCE%
"%~dp0PrintWpfSmoke.exe" "%EVIDENCE%" > "%EVIDENCE%\run.log" 2>&1
set "RC=%ERRORLEVEL%"
type "%EVIDENCE%\run.log"
echo Exit code: %RC%
echo Inspect the PNG and PDF files for Arabic, headers, row boundaries and signatures.
if not "%RC%"=="0" echo FAILED: Do not deploy to production. Share run.log and failure.txt.
pause
exit /b %RC%
