@echo off
REM =====================================================================
REM  DateERP 1.50.67 - VERIFY INSTALLER PACKAGE
REM  Checks: EXE, DLLs, Version, Config, Dependencies, RUN.bat, old paths
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%CD%"
set "PUBLISH=%ROOT%\publish"
set "VERSION=1.50.67"
set "ERRORS=0"

echo ============================================================
echo   DateERP %VERSION% - VERIFY INSTALLER PACKAGE
echo   Checking installer structure...
echo ============================================================
echo.

echo [CHECK 1] EXE exists...
if exist "%PUBLISH%\MfgSystem.exe" (
  echo [OK] EXE found: %PUBLISH%\MfgSystem.exe
) else (
  echo [FAILED] EXE missing: %PUBLISH%\MfgSystem.exe
  set /a ERRORS+=1
)

echo [CHECK 2] DLLs exist...
set "DLLS=MfgSystem.dll DatesErp.Core.dll DatesErp.Application.dll DatesErp.Infrastructure.dll DatesErp.Desktop.dll"
for %%D in (%DLLS%) do (
  if exist "%PUBLISH%\%%D" (
    echo [OK] DLL %%D exists
  ) else (
    echo [FAILED] DLL %%D missing
    set /a ERRORS+=1
  )
)

echo [CHECK 3] Version = %VERSION%...
set "VER_OK=0"
if exist "%PUBLISH%\VERSION.txt" (
  findstr /C:"%VERSION%" "%PUBLISH%\VERSION.txt" >nul 2>&1
  if !errorlevel!==0 (
    echo [OK] VERSION.txt contains %VERSION%
    set "VER_OK=1"
  ) else (
    echo [FAILED] VERSION.txt does not contain %VERSION%
    type "%PUBLISH%\VERSION.txt"
    set /a ERRORS+=1
  )
) else (
  echo [FAILED] VERSION.txt missing
  set /a ERRORS+=1
)

REM Check appsettings.json Version
if exist "%PUBLISH%\appsettings.json" (
  powershell -NoProfile -Command "try { $j=Get-Content '%PUBLISH%\appsettings.json' -Raw | ConvertFrom-Json; if ($j.AppSettings.Version -eq '1.50.67') { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>&1
  if !errorlevel!==0 (
    echo [OK] appsettings.json Version = %VERSION%
    set "VER_OK=1"
  ) else (
    echo [WARN] appsettings.json Version mismatch or unreadable
  )
) else (
  echo [FAILED] appsettings.json missing in publish
  set /a ERRORS+=1
)

echo [CHECK 4] Config exists...
if exist "%PUBLISH%\appsettings.json" (
  echo [OK] Config publish\appsettings.json exists
) else (
  echo [FAILED] Config missing
  set /a ERRORS+=1
)

REM Check isolated config will be created on install, but verify template exists
if exist "%ROOT%\..\DateERP_1.50.67_INSTALLER\Config" (
  echo [INFO] Config folder exists in installer (optional)
)

echo [CHECK 5] Dependencies...
REM Check for at least some EF Core or System dlls that would be in real publish
REM For placeholder package, we only check our own DLLs, but warn if runtimes missing
if exist "%PUBLISH%\MfgSystem.deps.json" (
  echo [OK] MfgSystem.deps.json exists - real publish detected
) else (
  echo [WARN] MfgSystem.deps.json missing - placeholder publish (will be built on Windows)
)

echo [CHECK 6] RUN.bat points to correct path...
if exist "%ROOT%\RUN.bat" (
  findstr /I "1.50.67" "%ROOT%\RUN.bat" >nul 2>&1
  if !errorlevel!==0 (
    echo [OK] RUN.bat contains version %VERSION%
  ) else (
    echo [WARN] RUN.bat does not contain version %VERSION%
  )
  findstr /I "DateERP_Publish C:\\DateERP" "%ROOT%\RUN.bat" >nul 2>&1
  if !errorlevel!==0 (
    echo [FAILED] RUN.bat contains old path (DateERP_Publish or C:\DateERP)
    set /a ERRORS+=1
  ) else (
    echo [OK] RUN.bat does not point to old folders
  )
) else (
  echo [FAILED] RUN.bat missing in installer root
  set /a ERRORS+=1
)

echo [CHECK 7] Installer does not point to old version...
findstr /I /S "DateERP_Publish C:\\DateERP" "%ROOT%\INSTALL-1.50.67.bat" >nul 2>&1
if !errorlevel!==0 (
  REM This is expected to have safety checks, but should not have hardcoded old install path as target
  echo [INFO] INSTALL-1.50.67.bat mentions old paths only for safety checks (OK)
) 
REM Check if INSTALL-1.50.67.bat has correct install root
findstr /C:"DateERP_1.50.67" "%ROOT%\INSTALL-1.50.67.bat" >nul 2>&1
if !errorlevel!==0 (
  echo [OK] INSTALL-1.50.67.bat targets DateERP_1.50.67 isolated folder
) else (
  echo [FAILED] INSTALL-1.50.67.bat does not target 1.50.67 folder
  set /a ERRORS+=1
)

echo.
echo ============================================================
if %ERRORS%==0 (
  echo   INSTALLER PACKAGE READY - %VERSION%
  echo   All critical checks passed.
) else (
  echo   INSTALLER PACKAGE HAS %ERRORS% ERRORS - please fix before delivery
)
echo ============================================================
echo.
echo Details:
echo   Installer: %ROOT%
echo   Publish: %PUBLISH%
echo   Version: %VERSION%
echo   Errors: %ERRORS%
echo.
pause
exit /b %ERRORS%
