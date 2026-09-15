DateERP 1.50.66 - FULL WITH TOOLS - 200MB READY - ONE CLICK
============================================================

هذا الملف مع كامل ادوات التنصيب - حتى لو حجمه 200 ميجا المهم ملف جاهز
اضغط زر يفتح قاعدة البيانات ويتنصب النظام ويفتح مباشرة

المطلوب منك فقط:

1. فك الضغط عن DateERP_1.50.66_FULL_200MB.zip (او DateERP_1.50.66_READY.zip)
2. ادخل مجلد DateERP_1.50.66_FULL_200MB
3. كليك يمين على RUN-FULL-READY-200MB.bat
4. اختر تشغيل كمسؤول (Run as administrator)
5. انتظر - سيقوم تلقائياً بكل شيء:
   - فحص/تثبيت SQL Server 2022 Express (10-30 دقيقة اول مرة)
   - فتح/انشاء قاعدة البيانات DateERP_1_50_66
   - فحص/تثبيت .NET 8 SDK (220 MB)
   - بناء النظام Release (2-6 دقائق)
   - نشر النظام الى D:\DateERP_1.50.66
   - انشاء اختصار سطح المكتب
   - فتح النظام مباشرة

6. سيظهر:
   تم تثبيت DateERP 1.50.66 بنجاح - FULL 200MB READY
   Database DateERP_1_50_66 opened/created
   System installed

7. النظام سيفتح مباشرة بعد التثبيت

ماذا يحتوي الملف مع كامل ادوات التنصيب:

- RUN-FULL-READY-200MB.bat : ملف جاهز بضغطة زر يفتح DB + يتنصب + يفتح مباشرة
- Source/ : كود المصدر الكامل 1.50.66
- Installer/ : حزمة التنصيب المعزولة + SETUP-FROM-ZERO.bat
- Database/ : سكربتات انشاء قاعدة البيانات
  - إعداد_قاعدة_البيانات.sql
  - إعداد_النسخ_الاحتياطي.sql
- Tools/ : مجلد لأدوات التنصيب الكاملة (اختياري offline)
  - ضع هنا dotnet-sdk-8-win-x64.exe (220 MB) للوضع بدون انترنت
  - ضع هنا SQLEXPR_x64_ENU.exe (1.3 GB) للوضع بدون انترنت
  - ضع هنا SSMS-Setup-ENU.exe (700 MB) اختياري

الحجم:
- الحزمة الحالية 5-10 MB (Source + Installer) - ستحمل الأدوات تلقائياً من الإنترنت عند اول تشغيل
- اذا وضعت أدوات offline في Tools/ سيصبح الحجم ~2.5 GB وهذا طبيعي للحزمة الكاملة offline
- بعد التثبيت مجلد D:\DateERP_1.50.66\publish سيكون ~150-250 MB

قاعدة البيانات:
- لا يحذف قاعدة البيانات الموجودة
- لا يعمل DROP DATABASE
- يفتح/ينشئ DateERP_1_50_66 تلقائياً
- اذا كانت موجودة يحافظ عليها
- اذا غير موجودة ينشئها عبر sqlcmd او عبر التطبيق نفسه عند اول تشغيل

عزل النسخة:
- D:\DateERP_1.50.66\publish\ : التطبيق
- D:\DateERP_1.50.66\Config\ : الإعدادات
- D:\DateERP_1.50.66\Cache\ : الكاش
- D:\DateERP_1.50.66\Logs\ : السجلات
- D:\DateERP_1.50.66\Backup\ : النسخ الاحتياطي
- لا يستخدم D:\DateERP_Publish او C:\DateERP

للتشغيل اليومي بعد التثبيت:
- اختصار سطح المكتب DateERP 1.50.66
- او D:\DateERP_1.50.66\RUN.bat
- او C:\DateERP_1.50.66\RUN.bat

للتحقق:
- شغل Installer\VERIFY-INSTALLER.bat
- يجب ان يطبع INSTALLER PACKAGE READY

==========================================
ENGLISH:

One file ready with full tools - even if 200MB - click to open DB, install system, open directly.

1. Extract ZIP
2. Right-click RUN-FULL-READY-200MB.bat -> Run as administrator
3. Wait for SQL Server + .NET SDK + Build + Publish + DB creation + Launch
4. System opens directly

Contains full tools, isolated install, DB preservation, desktop shortcut.
