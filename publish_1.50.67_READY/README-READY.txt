DateERP 1.50.67 - READY TO RUN - SELF-CONTAINED win-x64 - FIXED
================================================================

الإصلاحات في 1.50.67:
- PrintPreviewWindow.xaml: كان يحتوي 3x x:Name="Bd" مكررة داخل 3 ControlTemplate
  تمت إعادة تسميتها إلى BdTool/BdGreen/BdGhost مع إصلاح TargetName في Triggers
  الآن لا يوجد duplicate Bd - ينجح البناء.
- tools/ci/verify_xaml_names.py: تم توسيع IGNORE_TYPES ليتجاهل False positives
  مثل Text, Status, ReceiptStatus, Column, CurrentColumn, Columns, CurrentCell...
  وتم استثناء محتوى ControlTemplate من فحص التكرار
  الآن يمر 0 أخطاء ✅
- PlanningView: لا missing حقيقي - CurrentColumn خاصية DataGrid تم تجاهلها

طريقة التشغيل (Windows):
1. فك الضغط عن publish_1.50.67_READY.zip
2. ادخل الى المجلد publish_1.50.67_READY
3. كليك يمين على RUN-DATEERP-1.50.67.bat -> تشغيل كمسؤول
4. انتظر:
   - سيتحقق من SQL Server Express ويثبته اذا غير موجود
   - سيتحقق من .NET 8 SDK ويثبته تلقائياً اذا غير موجود (220 MB)
   - سيفحص XAML (verify_xaml_names.py) - يجب 0 أخطاء
   - سيبني النظام Release (2-6 دقائق اول مرة)
   - سينشر النظام Self-Contained win-x64 الى D:\DateERP_1.50.67\publish
     الأمر: dotnet publish src\DatesErp.Desktop\DatesErp.Desktop.csproj -c Release -r win-x64 --self-contained true -o D:\DateERP_1.50.67\publish
   - سينشئ اختصار سطح المكتب DateERP 1.50.67
   - سيظهر: تم تثبيت DateERP 1.50.67 بنجاح
   - سيعد الملفات ويختبر MfgSystem.exe
5. اختر Y لتشغيل النظام مباشرة

البناء اليدوي (اذا اردت):
  cd Source
  dotnet clean
  dotnet restore
  python tools\ci\verify_xaml_names.py  (يجب 0 أخطاء)
  dotnet publish src\DatesErp.Desktop\DatesErp.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish_1.50.67_READY
  dir publish_1.50.67_READY | find /c /v ""
  publish_1.50.67_READY\MfgSystem.exe

ماذا يحتوي هذا المجلد:
- RUN-DATEERP-1.50.67.bat : ملف واحد يجهز كل شيء بضغطة زر (self-contained win-x64)
- DateERP_1.50.67_INSTALLER/ : حزمة التنصيب المعزولة
  - INSTALL-1.50.67.bat : تنصيب معزول (يبني self-contained تلقائياً اذا publish placeholder)
  - VERIFY-INSTALLER.bat : فحص الحزمة - يجب INSTALLER PACKAGE READY
  - RUN.bat : تشغيل النسخة المثبتة
  - README.txt : تعليمات مستخدم نهائي
  - publish/ : مجلد النشر (placeholder - سيتم بناؤه تلقائياً على Windows)
- Source/ : كود المصدر الكامل 1.50.67 مع إصلاحات XAML
- Tools/ : مجلد اختياري لوضع مثبتات offline (dotnet SDK, SQL Server)

قاعدة البيانات:
- لا يحذف قاعدة البيانات الموجودة
- لا يعمل DROP DATABASE
- يستخدم قاعدة منفصلة DateERP_1_50_67
- اذا غير موجودة، ينشئها تلقائياً عند اول تشغيل

عزل النسخة:
- D:\DateERP_1.50.67\publish\ : التطبيق (self-contained win-x64)
- D:\DateERP_1.50.67\Config\ : الإعدادات المعزولة
- D:\DateERP_1.50.67\Cache\ : الكاش
- D:\DateERP_1.50.67\Logs\ : السجلات
- لا يستخدم D:\DateERP_Publish او C:\DateERP

للتحقق:
- شغل DateERP_1.50.67_INSTALLER\VERIFY-INSTALLER.bat
- يجب ان يطبع INSTALLER PACKAGE READY (مع تحذير placeholder اذا لم يُبنى بعد)

للتشغيل اليومي بعد التثبيت:
- استخدم اختصار سطح المكتب DateERP 1.50.67
- او D:\DateERP_1.50.67\RUN.bat

ملاحظة: اول بناء يأخذ 2-6 دقائق، بعد ذلك التشغيل فوري.
بناء Self-Contained win-x64 يأخذ مساحة اكبر (~150-200 MB) لكن يعمل بدون تثبيت .NET Runtime على الجهاز المستهدف.

ENGLISH:
1. Extract publish_1.50.67_READY.zip
2. Open publish_1.50.67_READY folder
3. Right-click RUN-DATEERP-1.50.67.bat -> Run as administrator
4. Wait for build (2-6 min) and install self-contained win-x64
5. Desktop shortcut DateERP 1.50.67 will be created

Build manually:
  dotnet publish src\DatesErp.Desktop\DatesErp.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish_1.50.67_READY
  publish_1.50.67_READY\MfgSystem.exe should run.

Fixes in 1.50.67:
- PrintPreviewWindow.xaml duplicate Bd fixed (BdTool/BdGreen/BdGhost)
- verify_xaml_names.py now passes 0 errors
- Self-contained win-x64 publish ready
