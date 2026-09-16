@echo off
chcp 65001 >nul
REM =========================================================================
REM  MfgSystem — Build the install package  (run ONCE on a dev machine)
REM  Requires: Windows + .NET 8 SDK
REM  Produces:  %ROOT%\MfgSystem_Publish  (self-contained, ~150-200 MB)
REM             ready to be zipped and installed via 2-تنصيب.bat
REM =========================================================================
setlocal

set "ROOT=%~dp0.."
set "OUT=%ROOT%\MfgSystem_Publish"
set "PROJ=%ROOT%\src\DatesErp.Desktop\DatesErp.Desktop.csproj"

echo.
echo ======================================================================
echo    MfgSystem — Build install package
echo ======================================================================
echo.

REM ── 1) SDK check ──
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo         Install from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
echo [1/6] .NET SDK:
dotnet --version
echo.

REM ── 2) Clean ──
echo [2/6] Cleaning previous outputs...
if exist "%OUT%" rmdir /s /q "%OUT%"
for /d /r "%ROOT%\src" %%D in (bin obj) do if exist "%%D" rmdir /s /q "%%D"
for /d /r "%ROOT%\tests" %%D in (bin obj) do if exist "%%D" rmdir /s /q "%%D"
for /d /r "%ROOT%\audit" %%D in (bin obj) do if exist "%%D" rmdir /s /q "%%D"
echo        Done.
echo.

REM ── 3) Restore ──
echo [3/6] Restoring packages...
dotnet restore "%ROOT%\DateERP.sln"
if errorlevel 1 (
    echo [ERROR] Restore failed — check network/NuGet access.
    pause
    exit /b 1
)
echo.

REM ── 4) Tests (never ship a broken build) ──
echo [4/6] Running tests...
dotnet test "%ROOT%\tests\DatesErp.Tests\DatesErp.Tests.csproj" -c Release --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Tests failed — build stopped. No package is shipped with failing tests.
    pause
    exit /b 1
)
echo.

REM ── 5) Publish self-contained win-x64 ──
echo [5/6] Publishing (win-x64, self-contained — no .NET needed on target)...
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%"
if errorlevel 1 (
    echo [ERROR] Publish failed.
    pause
    exit /b 1
)
if not exist "%OUT%\MfgSystem.exe" (
    echo [ERROR] Publish finished but MfgSystem.exe was not found in %OUT%.
    pause
    exit /b 1
)
echo        Published to %OUT%
echo.

REM ── 6) Installer tools + VERSION + SHA256 ──
echo [6/6] Adding installer tools, VERSION.txt and SHA256.txt...
copy /y "%~dp02-تنصيب.bat"        "%OUT%\" >nul
copy /y "%~dp0فحص_المتطلبات.bat"   "%OUT%\" >nul
copy /y "%~dp0إلغاء_التنصيب.bat"    "%OUT%\" >nul
copy /y "%~dp0README_التنصيب.md"    "%OUT%\" >nul

powershell -NoProfile -Command "$v=(Select-String -Path '%PROJ%' -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value; Set-Content -Path '%OUT%\VERSION.txt' -Value $v -Encoding ascii; Write-Host 'VERSION:' $v"
if not exist "%OUT%\VERSION.txt" (
    echo 1.50.67 > "%OUT%\VERSION.txt"
)

powershell -NoProfile -Command "Get-FileHash -Algorithm SHA256 '%OUT%\MfgSystem.exe' | Select-Object -ExpandProperty Hash | Out-File -Encoding ascii '%OUT%\SHA256.txt'"
if not exist "%OUT%\SHA256.txt" (
    echo [ERROR] SHA256 generation failed — install integrity check would be skipped.
    pause
    exit /b 1
)
echo        SHA256:
type "%OUT%\SHA256.txt"
echo.

REM ── Optional zip ──
if exist "%OUT%\MfgSystem.exe" (
    powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ROOT%\MfgSystem_FULL.zip' -Force"
    echo        Zip: %ROOT%\MfgSystem_FULL.zip
)

echo.
echo ======================================================================
echo   PACKAGE READY:  %OUT%
echo   Copy the folder (or zip) to any Windows 10/11 x64 machine,
echo   then double-click "2-تنصيب.bat" there.
echo ======================================================================
echo.
pause
endlocal
