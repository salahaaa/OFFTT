بسم الله الرحمن الرحيم
═══════════════════════════════════════════════════════════════
  1.50.73 — ALL FIXES FINAL SOURCE PATCH
  OFFTT / MfgSystem
═══════════════════════════════════════════════════════════════

هذه الحزمة التراكمية تجمع آخر ملفات المصدر والتحديثات والإصلاحات
المعروفة حتى إصلاح صلاحية حفظ خطة الإنتاج.

■ ما تحتويه
───────────────────────────────────────────────────────────────
- كل ملفات المصدر والأدوات والاختبارات الموجودة في حزمة
  UPDATE_1.50.73_FINAL2_PATCH_SOURCE السابقة.
- إصلاح Verifier FINAL2: BAT بسيط يستدعي PS1 مستقل، مع بوابات F1-F6:
    F1 SaveActionBtn
    F2 WithSave
    F3 1.50.73 في csproj
    F4 AssemblyFileVersion>1.50.73
    F5 1.50.73 في PlanningView.xaml.cs
    F6 5B7595 في PlanningView.xaml
- إصلاح صلاحية حفظ الخطة من المصدر الصحيح:
    PermissionService.EnsureProductionPlanningEdit()
    يمنح planning/Edit لدوري Production وManagement فقط.
- بقاء planning/Approve مستقلة وعدم تجاوز PermissionGate.
- اختبارات الصلاحيات ومسار WithSave وF10 وSave_Click ورفض التعديل
  للمستخدم غير المصرح له.

■ الإصدار
───────────────────────────────────────────────────────────────
Version:             1.50.73
AssemblyVersion:     1.50.73.0
AssemblyFileVersion: 1.50.73.0
ProductVersion:      1.50.73

■ طريقة الاستخدام
───────────────────────────────────────────────────────────────
طبّق محتويات هذه الحزمة فوق UPDATE_WORK الحالي. لا تستبدل
publish_FINAL ولا أي نسخة نشر عاملة.

شغّل أولاً:
  VERIFY-SOURCE-1.50.73.bat
ثم استخدم سكربت بناء Windows الموجود في الحزمة:
  BUILD-PUBLISH-1.50.73-FINAL-WINDOWS.bat

النشر المقصود مستقل في:
  publish_1.50.73_FINAL

هذه حزمة مصدر فقط، ولا تحتوي MfgSystem.exe أو قاعدة بيانات.
