DateERP 1.50.67 - تحديث النسخة المنصبة الجاهزة بدون بناء من الصفر
================================================================

هذا الملف لك انت:
C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish  ← مجلد التشغيل عندك
C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish\MfgSystem.exe ← الملف التنفيذي
C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\Source ← الكود المصدري
C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\FULL_EXTRACTED\DateERP_1.50.66_FULL_200MB\Source\DatesErp.sln ← المشروع

المشكلة: النظام منصب جاهز لكن فيه مشاكل ظاهرة (نسخ احتياطي Access Denied + خطط الإنتاج مشوهة + تسميات XAML)

الحل: ملف واحد تضغط عليه يحدث الملفات ويفتح النظام

الملف: UPDATE-EXISTING-INSTALL-1.50.67.bat

خطوات:
1. فك OFFTT-arena-01a091dd-offtt.zip في مكان (مثلاً D:\OFFTT)
2. ادخل الى D:\OFFTT\publish_1.50.67_READY
3. كليك يمين على UPDATE-EXISTING-INSTALL-1.50.67.bat → تشغيل كمسؤول
4. انتظر:
   - يفحص مجلد التشغيل C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish
   - يفحص السورس C:\...\Source
   - يحدث PrintPreviewWindow.xaml (Bd -> BdTool/BdGreen/BdGhost)
   - يحدث verify_xaml_names.py (0 أخطاء)
   - يعمل Backup لمجلد publish القديم
   - يبني بسرعة: dotnet build -c Release (بدون clean - سريع)
   - ينشر: dotnet publish -c Release -o publish_NEW_1.50.67 (framework-dependent - سريع ~30 ثانية)
   - ينسخ فوق publish القديم
   - يفتح MfgSystem.exe

الفرق عن البناء من الصفر:
- من الصفر: dotnet clean + restore + build (2-6 دقائق)
- هذا الملف: build incremental بدون clean (30-60 ثانية) + publish بدون restore

اذا فشل البناء:
- افتح update_1.50.67.log
- ارسل لي آخر 50 سطر

اذا تريد تحديث بدون بناء نهائياً (فقط نسخ DLL جاهز):
- استخدم publish_1.50.67_READY\DateERP_1.50.67_INSTALLER\publish\ بعد بنائه على جهاز آخر
- انسخه يدوياً:
  xcopy "D:\OFFTT\publish_1.50.67_READY\DateERP_1.50.67_INSTALLER\publish\*" "C:\Users\LENOVO\.gemini\antigravity-ide\scratch\DateERP\publish\" /E /Y /I
  ثم شغل MfgSystem.exe

ملاحظات:
- لا يحذف قاعدة البيانات
- يعمل Backup تلقائياً
- يغلق MfgSystem.exe اذا كان يعمل قبل النسخ
- بعد التحديث، عند رسالة النسخ الاحتياطي اضغط "نعم" للمتابعة بدون نسخة (تم إصلاح المسار في الكود الجديد)
