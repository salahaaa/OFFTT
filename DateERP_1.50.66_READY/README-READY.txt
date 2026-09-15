DateERP 1.50.66 - READY TO RUN - ONE CLICK
==========================================

هذا الملف يجعل النظام جاهز يفتح بضغطة زر مع جميع الأدوات

المطلوب منك فقط:

1. فك الضغط عن DateERP_1.50.66_READY.zip
2. ادخل الى المجلد DateERP_1.50.66_READY
3. كليك يمين على RUN-DATEERP-1.50.66.bat
4. اختر تشغيل كمسؤول (Run as administrator)
5. انتظر:
   - سيتحقق من .NET 8 SDK ويثبته تلقائياً اذا غير موجود (220 MB)
   - سيبني النظام Release (2-6 دقائق اول مرة)
   - سينشر النظام الى D:\DateERP_1.50.66 او C:\DateERP_1.50.66
   - سينشئ اختصار سطح المكتب DateERP 1.50.66
   - سيظهر: تم تثبيت DateERP 1.50.66 بنجاح
6. اختر Y لتشغيل النظام مباشرة، او شغل من اختصار سطح المكتب

ماذا يحتوي هذا المجلد:
- RUN-DATEERP-1.50.66.bat : ملف واحد يجهز كل شيء بضغطة زر
- DateERP_1.50.66_INSTALLER/ : حزمة التنصيب المعزولة
  - INSTALL-1.50.66.bat : تنصيب معزول فقط (بدون بناء)
  - VERIFY-INSTALLER.bat : فحص الحزمة
  - RUN.bat : تشغيل النسخة المثبتة
  - README.txt : تعليمات مستخدم نهائي
  - publish/ : مجلد النشر (سيتم بناؤه تلقائياً)
- Source/ : كود المصدر الكامل 1.50.66 (5.5 MB)
- Tools/ : مجلد اختياري لوضع مثبتات offline (dotnet SDK, SQL Server, SSMS)

قاعدة البيانات:
- لا يحذف قاعدة البيانات الموجودة
- لا يعمل DROP DATABASE
- يستخدم قاعدة منفصلة DateERP_1_50_66
- اذا غير موجودة، ينشئها تلقائياً عند اول تشغيل

عزل النسخة:
- D:\DateERP_1.50.66\publish\ : التطبيق
- D:\DateERP_1.50.66\Config\ : الإعدادات المعزولة
- D:\DateERP_1.50.66\Cache\ : الكاش
- D:\DateERP_1.50.66\Logs\ : السجلات
- لا يستخدم D:\DateERP_Publish او C:\DateERP

للتحقق:
- شغل DateERP_1.50.66_INSTALLER\VERIFY-INSTALLER.bat
- يجب ان يطبع INSTALLER PACKAGE READY

للتشغيل اليومي بعد التثبيت:
- استخدم اختصار سطح المكتب DateERP 1.50.66
- او D:\DateERP_1.50.66\RUN.bat

ملاحظة: اول بناء يأخذ 2-6 دقائق، بعد ذلك التشغيل فوري بضغطة زر.

==========================================
ENGLISH:

1. Extract DateERP_1.50.66_READY.zip
2. Open DateERP_1.50.66_READY folder
3. Right-click RUN-DATEERP-1.50.66.bat -> Run as administrator
4. Wait for build (2-6 min first time) and install
5. Desktop shortcut DateERP 1.50.66 will be created
6. Run from shortcut

The installer will automatically:
- Install .NET 8 SDK if missing
- Build DateERP.sln Release
- Publish to D:\DateERP_1.50.66\publish
- Create isolated Config, Cache, Logs
- Preserve existing DB (DateERP_1_50_66)
- Create shortcuts
- Offer to run
