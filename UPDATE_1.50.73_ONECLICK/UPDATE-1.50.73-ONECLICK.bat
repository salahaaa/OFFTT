@echo off
chcp 65001 >nul
REM =====================================================================
REM  UPDATE 1.50.73 - ONE CLICK - آمن على قاعدة البيانات والإعدادات
REM
REM  ماذا يفعل:
REM   1) يجد مجلد التشغيل الحالي (حيث MfgSystem.exe).
REM   2) يقفل التطبيق بأمان (taskkill).
REM   3) ينسخ ملفات التطبيق الجديدة فقط (لا يحذف شيئاً من مجلد التشغيل).
REM      قاعدة البيانات والإعدادات خارج مجلد التشغيل أصلاً
REM      (%LocalAppData%\MfgSystem و %LocalAppData%\DateERP) ولا يمسّها
REM      — كما يوجد استبعاد إضافي للملفات الحساسة (حماية مزدوجة).
REM   4) يعيد تشغيل التطبيق ويطبع سطر تسجيل.
REM
REM  الاستخدام:  شغّل هذا الملف (من داخل الحزمة التي تحتوي publish_1.50.73\)
REM =====================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "VER=1.50.73"
set "NEW=%~dp0publish_%VER%"
set "LOG=%~dp0update_%VER%.log"

echo ============================================================ > "%LOG%"
echo   UPDATE %VER% - %DATE% %TIME% >> "%LOG%"
echo [LOG] كل خطوة من التحديث تُسجَّل في هذا الملف: %LOG%
echo ============================================================ >> "%LOG%"

echo ============================================================
echo   تحديث MfgSystem إلى الإصدار %VER% (بدعم قاعدة البيانات)
echo ============================================================
echo.

if not exist "%NEW%\MfgSystem.exe" (
  echo [FAILED] لم أجد publish_%VER%\MfgSystem.exe بجانب هذا الملف.
  echo          فك الحزمة كاملة أو شغّل BUILD-PUBLISH-1.50.73-WINDOWS.bat أولاً.
  exit /b 1
)
if not exist "%NEW%\coreclr.dll" (
  echo [WARN] coreclr.dll غير موجود — الحزمة ليست self-contained وقد تحتاج .NET Desktop Runtime.
)

REM ---------- تحديد مجلد التشغيل الحالي ----------
set "TARGET="
if /i not "%~1"=="" set "TARGET=%~1"
if not defined TARGET if exist "%~dp0..\publish\MfgSystem.exe" set "TARGET=%~dp0..\publish"
if not defined TARGET if exist "%LocalAppData%\Programs\MfgSystem\MfgSystem.exe" set "TARGET=%LocalAppData%\Programs\MfgSystem"
if not defined TARGET if exist "%ProgramFiles%\MfgSystem\MfgSystem.exe" set "TARGET=%ProgramFiles%\MfgSystem"
if not defined TARGET if exist "C:\Users\%USERNAME%\.gemini\antigravity-ide\scratch\DateERP\publish\MfgSystem.exe" set "TARGET=C:\Users\%USERNAME%\.gemini\antigravity-ide\scratch\DateERP\publish"
if not defined TARGET (
  echo [?] لم أجد مجلد التشغيل تلقائياً.
  set /p TARGET="مسار مجلد التشغيل الحالي (حيث MfgSystem.exe): "
)
if not exist "%TARGET%\MfgSystem.exe" (
  echo [FAILED] لا يوجد MfgSystem.exe في: %TARGET%
  exit /b 1
)

echo [INFO] مجلد التشغيل: %TARGET%
echo [INFO] الملفات الجديدة: %NEW%
echo.
choice /C YN /M "سيتم تحديث مجلد التشغيل هذا — متابعة (نعم/لا)?"
if errorlevel 2 (echo [CANCEL] أُلغي التحديث. & exit /b 0)

REM ---------- قفل التطبيق بأمان ----------
taskkill /f /im MfgSystem.exe >nul 2>nul
if errorlevel 1 (echo [LOG] التطبيق لم يكن مفتوحاً — تم المتابعة.) >> "%LOG%" else (echo [LOG] أُقفل التطبيق MfgSystem.exe.) >> "%LOG%"
timeout /t 2 /nobreak >nul

REM ---------- نسخ الملفات (بدون حذف + استبعاد الملفات الحساسة) ----------
set "EXCL=%TEMP%\upd_excl_%RANDOM%.txt"
> "%EXCL%" echo mfgsystem_local.db
echo config.json >> "%EXCL%"
echo *.db >> "%EXCL%"
echo *.sdf >> "%EXCL%"
xcopy /Y /S /Q /C /EXCLUDE:"%EXCL%" "%NEW%\*" "%TARGET%\" | find /c /v "" >nul
del "%EXCL%" 2>nul

REM ---------- §1.50.73: تحقق من سلامة النسخ (فشل التحديث السابق كان بلا تفاصيل) ----------
set "SRC_SIZE=0" & for %%F in ("%NEW%\MfgSystem.exe") do set "SRC_SIZE=%%~zF"
set "DST_SIZE=0" & if exist "%TARGET%\MfgSystem.exe" for %%F in ("%TARGET%\MfgSystem.exe") do set "DST_SIZE=%%~zF"
if "%SRC_SIZE%"=="%DST_SIZE%" (
  echo [LOG] سلامة النسخ: MfgSystem.exe متطابق الحجم (%DST_SIZE% بايت). >> "%LOG%"
) else (
  echo [FAILED] النسخ لم يكتمل — حجم MfgSystem.exe في الهدف (%DST_SIZE%) يختلف عن المصدر (%SRC_SIZE%).
  echo          تحقق من الصلاحيات أو المساحة، ثم أعد التشغيل. التفاصيل: %LOG%
  exit /b 1
)
if not exist "%TARGET%\MfgSystem.exe" (
  echo [FAILED] النسخ لم يكتمل — تحقق من الصلاحيات (جرب التشغيل كمسؤول).
  exit /b 1
)

REM ---------- توثيق + تشغيل ----------
echo %DATE% %TIME% - Updated to %VER% >> "%TARGET%\UPDATE-LOG.txt"
echo.
echo ============================================================
echo   [OK] اكتمل التحديث إلى %VER%
echo   - قاعدة البيانات: لم تُمس (خارج مجلد التشغيل)
echo   - الإعدادات:     لم تُمس (config.json مستبعد)
echo   - سطر جديد في:   %TARGET%\UPDATE-LOG.txt
echo ============================================================
echo [LOG] بدء تشغيل MfgSystem.exe من: %TARGET% >> "%LOG%"
start "" "%TARGET%\MfgSystem.exe"
echo   - لوغ التحديث: %LOG%
endlocal
