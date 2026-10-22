@echo off
setlocal
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "EXE=%ROOT%\Application_1.50.72\MfgSystem.exe"
if exist "%EXE%" goto :run
if not exist "%ROOT%\INSTALL-AND-RUN.bat" goto :missing
call "%ROOT%\INSTALL-AND-RUN.bat"
exit /b %ERRORLEVEL%

:run
start "" "%EXE%"
exit /b 0

:missing
echo [FAILED] MfgSystem is not installed. Run INSTALL-AND-RUN.bat from the project root.
pause
exit /b 1
