@echo off
setlocal
REM Compatibility entry point. The one-click installer lives at the project root.
set "INSTALLER=%~dp0..\INSTALL-AND-RUN.bat"
if not exist "%INSTALLER%" (
  echo [FAILED] INSTALL-AND-RUN.bat is missing. Extract the complete source ZIP first.
  pause
  exit /b 1
)
call "%INSTALLER%" %*
exit /b %ERRORLEVEL%
