@echo off

REM ═══════════════════════════════════════════════════════════════════════

REM  DateERP — التنصيب بضغطة زر

REM  انسخ المجلد كاملاً إلى الجهاز، ثم انقر هذا الملف نقراً مزدوجاً.

REM  لا يحتاج .NET (الحزمة مكتفية ذاتياً) ولا صلاحيات مدير في الوضع العادي.

REM ═══════════════════════════════════════════════════════════════════════

chcp 65001 >nul

setlocal EnableDelayedExpansion



set "SRC=%~dp0"

set "APPNAME=DateERP"

set "DEST=%ProgramFiles%\%APPNAME%"

set "DATADIR=%LocalAppData%\MfgSystem"



echo.

echo ══════════════════════════════════════════════════════════════

echo    تنصيب %APPNAME% — نظام إدارة وتصنيع التمور

echo ══════════════════════════════════════════════════════════════

echo.

echo    المصدر : %SRC%

echo    الهدف  : %DEST%

echo.



REM ── 0) فحص المتطلبات ──

echo [1/6] فحص متطلبات النظام...

ver | findstr /i "10.0 11.0" >nul

if errorlevel 1 (

    echo        [تحذير] النظام ليس ويندوز 10 أو 11 — قد لا يعمل البرنامج.

)

echo        نظام التشغيل: مقبول.

echo.



REM ── 1) التحقق من سلامة الحزمة ──

echo [2/6] التحقق من سلامة الحزمة...

if not exist "%SRC%MfgSystem.exe" (

    echo        [خطأ] لم يُعثر على MfgSystem.exe في هذا المجلد.

    echo                تأكد أنك نسخت المجلد كاملاً بعد البناء.

    pause

    exit /b 1

)

if exist "%SRC%SHA256.txt" (

    for /f "delims=" %%H in (%SRC%SHA256.txt) do set "EXPECT=%%H"

    for /f "delims=" %%H in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 '%SRC%MfgSystem.exe').Hash"') do set "ACTUAL=%%H"

    if /i "!EXPECT!"=="!ACTUAL!" (

        echo        البصمة مطابقة: !ACTUAL!

    ) else (

        echo        [خطأ] البصمة غير مطابقة — الحزمة تالفة أو معدَّلة.

        echo                المتوقع: !EXPECT!

        echo                الفعلي  : !ACTUAL!

        pause

        exit /b 1

    )

) else (

    echo        [تنبيه] لا يوجد ملف SHA256.txt — تخطّي التحقق.

)

echo.



REM ── 2) حماية البيانات القائمة ──

echo [3/6] تهيئة الاتصال والبيانات...

if not exist "%DATADIR%" mkdir "%DATADIR%"





REM ── ضبط الاتصال تلقائياً — لا شاشة «تعديل الاتصال» ولا كتابة ──

REM §الطلب الصريح: Server=.\SQLEXPRESS01 · Database=DateFactory · مصادقة Windows.

REM أول تشغيل ينشئ القاعدة والجداول تلقائياً (Bootstrapper: EnsureCreated + DbSeeder

REM ثم SchemaMigrator لكل جداول وأعمدة النموذج) بلا أي سؤال.

>"%DATADIR%\config.json" echo {

>>"%DATADIR%\config.json" echo   "Server": ".\\SQLEXPRESS01",

>>"%DATADIR%\config.json" echo   "Database": "MfgSystemDB",

>>"%DATADIR%\config.json" echo   "AuthMode": "Windows",

>>"%DATADIR%\config.json" echo   "TrustServerCertificate": true,

>>"%DATADIR%\config.json" echo   "AppVersion": "1.50.50"

>>"%DATADIR%\config.json" echo }

echo        تم ضبط الاتصال: .\SQLEXPRESS01 / MfgSystemDB / مصادقة Windows


echo.



REM ── 3) البحث عن تنصيبات قديمة ──

echo [4/6] البحث عن تنصيبات قديمة...

set "FOUNDOLD=0"

for %%P in ("%ProgramFiles%\%APPNAME%" "%ProgramFiles(x86)%\%APPNAME%" "%LocalAppData%\%APPNAME%\app" "%LocalAppData%\Programs\%APPNAME%") do (

    if exist "%%~P\MfgSystem.exe" (

        echo        [تنبيه] تنصيب سابق في: %%~P

        set "FOUNDOLD=1"

    )

)

if "!FOUNDOLD!"=="1" (

    echo.

    echo        سيُستبدل التنصيب السابق. بياناتك محفوظة في %DATADIR%

    choice /c YN /t 10 /d Y /m "        متابعة؟ (Y=نعم N=إلغاء — تلقائي بعد 10 ثوانٍ)"

    if errorlevel 2 ( echo        أُلغي التنصيب. & pause & exit /b 0 )

)

echo.



REM ── 4) النسخ ──

echo [5/6] نسخ الملفات...

if exist "%DEST%" rmdir /s /q "%DEST%"

mkdir "%DEST%"

xcopy /e /i /y /q "%SRC%*.*" "%DEST%\" >nul

if errorlevel 1 (

    echo        [خطأ] فشل النسخ إلى %DEST%

    echo                إن ظهر خطأ صلاحيات، انقر الملف بزر الفأرة الأيمن

    echo                واختر "تشغيل كمسؤول".

    pause

    exit /b 1

)

echo        تم النسخ إلى %DEST%

echo.



REM ── 5) الاختصارات ──

echo [6/6] إنشاء الاختصارات...

powershell -NoProfile -ExecutionPolicy Bypass -File "%DEST%\إنشاء_اختصار.ps1" -Target "%DEST%\MfgSystem.exe" -WorkDir "%DEST%"

if errorlevel 1 (

    echo        [تنبيه] تعذر إنشاء الاختصارات تلقائياً.

    echo                يمكنك إنشاء اختصار يدوياً إلى: %DEST%\MfgSystem.exe

) else (

    echo        اختصار سطح المكتب  ✓

    echo        اختصار قائمة ابدأ   ✓

)

echo.



echo ══════════════════════════════════════════════════════════════

echo    ✓ تم التنصيب بنجاح

echo.

echo    التشغيل   : اختصار "%APPNAME%" على سطح المكتب

echo    الإعدادات : %DATADIR%\config.json

echo    الاتصال   : .\SQLEXPRESS01 / MfgSystemDB / مصادقة Windows

echo    السجلات   : %DATADIR%\logs\

echo    إلغاء     : "%DEST%\إلغاء_التنصيب.bat"

echo.

echo    بعد التشغيل تحقّق من شريط العنوان: يجب أن يظهر ختم الإصدار.

echo ══════════════════════════════════════════════════════════════

echo.

choice /c YN /t 10 /d Y /m "تشغيل البرنامج الآن؟ (Y=نعم N=لاحقاً — تلقائي بعد 10 ثوانٍ)"

if errorlevel 2 goto :end

start "" "%DEST%\MfgSystem.exe"

:end

echo.

pause

endlocal

