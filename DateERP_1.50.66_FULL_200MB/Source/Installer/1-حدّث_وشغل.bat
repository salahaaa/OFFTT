@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "SRC=%~dp0"
set "APPNAME=DateERP"
set "DATADIR=%LocalAppData%\MfgSystem"

echo.
echo ==============================================================
echo   MfgSystem 1.50.50 — تهيئة وتشغيل بنقرة واحدة
echo   هوية منفصلة تماماً: بيانات MfgSystem وقاعدة MfgSystemDB
echo   بلا أي نسخ احتياطية وبلا كتابة وبلا نوافذ أوامر
echo ==============================================================
echo.

REM ── 1) حارس: هذا الملف يعمل من مجلد النظام الكامل فقط ──
REM (الملفات النظامية مثل coreclr.dll موجودة في مجلد الحزمة الكاملة فقط.
REM  إن نقرتَ من مجلد الحزمة المصغّرة قبل دمجها فستظهر هذه الرسالة.)
if exist "%SRC%coreclr.dll" goto :inplace
REM §v1.50.26: تثبيت ذاتي — إن نُقر السكربت من مجلد التحديث وهو داخل مجلد النسخة
REM (مثال: DateERP_1.50.20\DateERP_1.50.26_Update) يكتشف مجلد النسخة الأب،
REM يثبّت ملفات التحديث فيه تلقائياً ثم يشغّل النظام من هناك — بلا أي خطوات يدوية.
taskkill /f /im MfgSystem.exe >nul 2>&1
for %%P in ("%~dp0..") do set "PARENT=%%~fP"
if exist "%PARENT%\coreclr.dll" if exist "%PARENT%\MfgSystem.exe" goto :installparent
echo [تنبيه] هذا السكربت لا يعمل من مكانه الحالي.
echo.
echo   المطلوب: ضع ملفات التحديث كلها داخل مجلد النسخة DateERP_1.50.20
echo   بحيث تصبح بجانب ملف MfgSystem.exe — وافق على الاستبدال إن سُئلت —
echo   ثم انقر هذا السكربت من هناك.
powershell -NoProfile -WindowStyle Hidden -Command "Add-Type -AssemblyName System.Windows.Forms;[void][System.Windows.Forms.MessageBox]::Show('ضع ملفات التحديث كلها داخل مجلد النسخة DateERP_1.50.20 بحيث تصبح بجانب ملف MfgSystem.exe ووافق على الاستبدال ثم انقر السكربت من هناك.','DateERP — مكان التحديث غير صحيح',0,48)"
exit /b 1
:installparent
robocopy "%SRC%" "%PARENT%" /e /r:1 /w:1 /njh /njs /ndl /nfl >nul
if errorlevel 8 (
    echo [تنبيه] تعذّر تثبيت التحديث — أغلق النظام ثم أعد النقر على السكربت.
    pause
    exit /b 1
)
set "SRC=%PARENT%\"
echo [تم] ثُبّت التحديث في مجلد النسخة تلقائياً — سيشتغل النظام من هناك.
echo [1/5] مجلد النظام: %SRC%

REM ── 2) إغلاق النظام إن كان يعمل (لتطبيق الإصدار الجديد) ──
taskkill /f /im MfgSystem.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo [2/5] النظام مغلق — الملفات في مكانها ^(بلا أي نسخ أو احتياطيات^)

REM ── 3) ملف الاتصال في مجلد بيانات خاص بهذه النسخة وحدها ──
REM القاعدة: MfgSystemDB — منفصلة عن أي DateFactory لأي نسخة أخرى،
REM وتُنشأ تلقائياً مع جداولها عند أول تشغيل بلا أي سؤال.
if not exist "%DATADIR%" mkdir "%DATADIR%"
>"%DATADIR%\config.json" echo {
>>"%DATADIR%\config.json" echo   "Server": ".\\SQLEXPRESS01",
>>"%DATADIR%\config.json" echo   "Database": "MfgSystemDB",
>>"%DATADIR%\config.json" echo   "AuthMode": "Windows",
>>"%DATADIR%\config.json" echo   "TrustServerCertificate": true,
>>"%DATADIR%\config.json" echo   "AppVersion": "1.50.50"
>>"%DATADIR%\config.json" echo }
echo [3/5] الاتصال: .\SQLEXPRESS01 / MfgSystemDB / مصادقة Windows ✓

REM ── 4) اختصار سطح المكتب «DateERP» ──
powershell -NoProfile -WindowStyle Hidden -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\MfgSystem.lnk');$s.TargetPath='%SRC%MfgSystem.exe';$s.WorkingDirectory='%SRC%';$s.IconLocation='%SRC%MfgSystem.exe,0';$s.Description='MfgSystem 1.50.50 — نظام إدارة وتصنيع التمور';$s.Save()"
echo [4/5] اختصار سطح المكتب «DateERP» ✓

REM ── 5) التشغيل ──
echo [5/5] تشغيل النظام...
start "" "%SRC%MfgSystem.exe"
echo.
echo   تم. من الآن: افتح النظام من أيقونة «DateERP» على سطح المكتب فقط.
echo   (قاعدة MfgSystemDB وجداولها تُنشأ تلقائياً في أول تشغيل)
timeout /t 4 /nobreak >nul
endlocal
