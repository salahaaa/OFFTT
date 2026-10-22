@echo off
REM =====================================================================
REM  MfgSystem - open the project in Visual Studio 2022 [the BIG product,
REM  not VS Code]. This helper installs nothing and requires Visual Studio 2022.
REM  Opens DateERP.sln so you can see and edit every screen:
REM  screens live under src\DatesErp.Desktop\Views  [XAML + code].
REM  Press F5 inside Visual Studio to run the app with debugging.
REM =====================================================================
setlocal
cd /d "%~dp0.."
set "ROOT=%CD%"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :no_vs
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do set "VSPATH=%%i"
if not defined VSPATH goto :no_vs
if not exist "%VSPATH%\Common7\IDE\devenv.exe" goto :no_vs
echo Opening Visual Studio 2022 with DateERP.sln - first open takes a minute...
start "" "%VSPATH%\Common7\IDE\devenv.exe" "%ROOT%\DateERP.sln"
exit /b
:no_vs
echo [ERROR] Visual Studio 2022 not found on this machine.
echo         Install Visual Studio 2022 with the .NET desktop workload, then retry.
pause
exit /b 1
