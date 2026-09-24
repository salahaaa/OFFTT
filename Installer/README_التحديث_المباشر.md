# التحديث المباشر لـ MfgSystem

## تشغيل التحديث

1. أغلق النظام أو شغّل `LiveUpdate.bat` (أو `تحديث_مباشر.bat`) وسيغلق نسخة MfgSystem المطابقة تلقائياً.
2. إذا كان البرنامج مثبتاً داخل `Program Files` شغّل الملف **كمسؤول**.
3. يتصل البوت بإصدار GitHub المستقر الأخير للمستودع `salahaaa/OFFTT`.
4. ينزّل الحزمة التي تطابق `MfgSystem_*_FULL.zip`.
5. يتحقق من وجود `MfgSystem.exe` ومن قيمة `SHA256.txt` إن وُجدت.
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

## متطلبات نشر الإصدار

لن يجد البوت تحديثاً حتى يتم نشر Release مستقر في GitHub يحتوي على حزمة:

```text
MfgSystem_1.50.75_FULL.zip
```

ويجب أن تحتوي الحزمة على `MfgSystem.exe` ويفضل أن تحتوي `VERSION.txt` و`SHA256.txt`. بناء الحزمة يتم من Windows عبر `Installer\1-بناء_الحزمة.bat` أو عبر CI، ولا يتم استبدال `MfgSystem.exe` قبل نجاح الاختبارات وPublish.
