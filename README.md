# MfgSystem — نظام تصنيع وتعبئة التمور

**الإصدار الحالي: 1.50.72.** يحتوي هذا الفرع على مصدر المشروع الحالي فقط؛ لا يضم أرشيفات ZIP أو نسخاً تنفيذية قديمة أو مجلدات مصدر مكررة. تبقى تقارير الإصدارات السابقة داخل `Documentation/` كمرجع تاريخي.

## تنزيل الحزمة

[تنزيل ZIP المصدر الحالي](https://github.com/salahaaa/OFFTT/archive/refs/heads/arena/01a091dd-offtt.zip)

بعد فك الضغط، لا تستخرج الملفات فوق تثبيت سابق. المستودع لا يضم ملفات النشر الثنائية؛ يبنيها المثبّت من المصدر على جهاز Windows.

## تثبيت وتشغيل بنقرة واحدة

1. استخرج ZIP إلى مجلد جديد، مثلاً `D:\MfgSystem_1.50.72`.
2. شغّل `INSTALL-AND-RUN.bat` من جذر المجلد بصفة مسؤول، ثم وافق على UAC.
3. يتحقق السكربت من .NET 8 SDK، ويثبته إن لزم، ثم يستعيد NuGet وينشر تطبيق Windows x64 ذاتي التشغيل داخل `Application_1.50.72`.
4. ينشئ اختصار سطح مكتب ويشغّل `MfgSystem.exe`.

قد يحتاج تنزيل SDK أو حزم NuGet إلى إنترنت. عند التحضير من جهاز آخر، يمكن تنزيل مثبت SDK الرسمي x64 من [رابط .NET 8](https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe)، ثم وضعه بجانب السكربت باسم `dotnet-sdk-8-win-x64.exe`. هذا يغني عن تنزيل SDK فقط؛ أما الحزم فتحتاج مخزناً محلياً مكتملاً إذا لم يتوفر الإنترنت.

### قاعدة البيانات والعزل

- عند أول تشغيل دون إعداد سابق لهوية MfgSystem، يهيئ التطبيق SQLite محلياً في `%LocalAppData%\MfgSystem\mfgsystem_local.db`.
- الدخول الأولي: اسم المستخدم `admin` وكلمة المرور `Admin@123`؛ يطلب النظام تغييرها فور الدخول الأول. لا تترك كلمة المرور الأولية للاستخدام الإنتاجي.
- إعدادات MfgSystem والسجلات والثيمات والمسودات تحت `%LocalAppData%\MfgSystem`؛ النسخ ما قبل ترحيل SQL تحت `Documents\MfgSystem\Backups`. شاشة النسخ في البرنامج مخصصة لـ SQL Server وتكتب إلى مجلد على الخادم؛ لا تدعم نسخة SQLite المحلية. لا تُقرأ إعدادات DateERP القديمة أو تُنقل.
- المثبّت لا يحذف أو يستبدل قواعد بيانات/ملفات قديمة. إذا كان ملف إعداد MfgSystem نفسه موجوداً من استخدام سابق، يحافظ عليه ولا يمسح بياناته لفرض قاعدة جديدة.
- لتثبيت مستقل لمستخدم Windows جديد، استخدم حساب/ملف مستخدم جديد؛ لا تحذف مجلدات بيانات إصدار قديم.

## ملفات التشغيل والتوثيق

| الملف | الغرض |
|---|---|
| `INSTALL-AND-RUN.bat` | المثبّت الرئيسي من جذر مصدر المشروع |
| `Installer/SETUP-FROM-ZERO.bat` | مدخل توافق يستدعي المثبّت الرئيسي |
| `Installer/RUN-SYSTEM.bat` | فتح النسخة المنشورة، أو تشغيل المثبّت إن لم تُبنَ بعد |
| `UPDATE-AND-RUN.bat` | أداة تحديث اختيارية إلى `D:\MfgSystem`؛ ليست لتثبيت DateERP قديم |
| `Installer/INSTALL-GUIDE-AR.md` | دليل التثبيت العربي |
| `Documentation/Installation_Guide.md` | دليل التركيب والتحديث |
| `Documentation/Server_Setup.md` | SQL Server اختياري متعدد المستخدمين |
| `Documentation/تقرير_تجهيز_الحزمة_المستقلة_1.50.72.md` | سجل التعديلات ونتائج الفحص وحدود الاختبار |
| `docs/تحديثات_آخر_3_أيام.md` | سجل آخر التغييرات |

## SQL Server اختياري

التثبيت بضغطة واحدة يعمل محلياً باستخدام SQLite، ولا يثبت SQL Server أو SSMS. لاستخدام قاعدة مركزية متعددة المستخدمين لاحقاً، جهّز SQL Server يدوياً واستخدم قاعدة مستقلة `MfgSystemDB`؛ راجع الدليل قبل تشغيل ملفات `Installer/server`. لا تستخدم قاعدة `DateFactory` التابعة لإصدارات DateERP السابقة.

## التطوير والاختبار

الحل `DateERP.sln`، والمشروع المكتبي WPF يستهدف .NET 8 وWindows x64.

```powershell
dotnet restore DateERP.sln
dotnet build DateERP.sln -c Release
dotnet test tests/DatesErp.Tests/DatesErp.Tests.csproj -c Release
```

يمكن فتح **جذر المشروع فقط** أو ملف `DateERP.sln` في Visual Studio 2022. `Installer/OPEN-VISUAL-STUDIO.bat` مخصص لـ Visual Studio، وليس VS Code.

## حدود التحقق لهذا الإصدار

لم يُشغّل `INSTALL-AND-RUN.bat` أو أي BAT/CMD/PowerShell، ولم يُنفّذ بناء .NET أو اختبار WPF على Windows في بيئة إعداد هذه الحزمة. يلزم اختبار التثبيت النظيف وأول تشغيل على جهاز Windows قبل الاعتماد الإنتاجي.
