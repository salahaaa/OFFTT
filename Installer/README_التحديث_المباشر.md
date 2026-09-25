# التحديث المباشر لـ MfgSystem

## تشغيل التحديث

1. أغلق النظام أو شغّل `LiveUpdate.bat` (أو `تحديث_مباشر.bat`) وسيغلق نسخة MfgSystem المطابقة تلقائياً.
2. إذا كان البرنامج مثبتاً داخل `Program Files` شغّل الملف **كمسؤول**.
3. يتصل البوت برابط الحزمة المحدد في `update-manifest.json`، وليس بواجهة `releases/latest`.
4. ينزّل الحزمة المحددة التي تطابق `MfgSystem_*_FULL.zip`.
5. يتحقق من SHA256 لحزمة ZIP إن كان رابطها موجوداً، ثم يتحقق من وجود `MfgSystem.exe` و`SHA256.txt` داخلها.
6. ينشئ نسخة رجوع تحت:

```text
%LOCALAPPDATA%\MfgSystem\updates\backups
```

7. يستبدل ملفات البرنامج فقط ثم يعيد تشغيله.

## أوامر مفيدة

```bat
تحديث_مباشر.bat -CheckOnly
تحديث_مباشر.bat -Force
تحديث_مباشر.bat -NoLaunch
تحديث_مباشر.bat -InstallDir "C:\Program Files\MfgSystem"
تحديث_مباشر.bat -CheckOnly -NonInteractive
```

- `-CheckOnly`: فحص وجود إصدار أحدث دون تنزيل أو تعديل.
- `-Force`: إعادة تنزيل الإصدار المنشور حتى لو كان رقمه مساوياً للإصدار الحالي.
- `-NoLaunch`: عدم تشغيل النظام بعد انتهاء التحديث.
- `-InstallDir`: تحديد مجلد التثبيت يدوياً.

## حماية البيانات

البوت لا ينفذ ترحيلاً أو Migration على قاعدة البيانات، ولا يغير إعداد الاتصال، ولا يحذف:

- `%LOCALAPPDATA%\MfgSystem\config.json`
- ملفات قاعدة البيانات الموجودة في مجلد البيانات.

سجل العملية:

```text
%LOCALAPPDATA%\MfgSystem\updates\updater.log
```

## مصدر الحزمة الحالي

المصدر المحدد حالياً في `update-manifest.json` هو:

```text
https://github.com/salahaaa/OFFTT/releases/download/live-current/MfgSystem_1.50.74_FULL.zip
```

وتوجد قيمة SHA256 المقابلة في:

```text
https://github.com/salahaaa/OFFTT/releases/download/live-current/MfgSystem_1.50.74_FULL.zip.sha256
```

يتم إنشاء هذين الملفين آلياً بواسطة Workflow Windows، ولا يستخدم البوت `releases/latest`. يجب أن تحتوي الحزمة على `MfgSystem.exe` و`VERSION.txt` و`SHA256.txt`. لا يتم استبدال `MfgSystem.exe` قبل نجاح الاختبارات وPublish.
