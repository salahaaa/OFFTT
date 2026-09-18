بسم الله الرحمن الرحيم
═══════════════════════════════════════════════════════════════
  تصحيح صلاحية حفظ الخطة — 1.50.73
  planning/Edit عبر نظام الصلاحيات الهرمي
═══════════════════════════════════════════════════════════════

■ سبب المشكلة
───────────────────────────────────────────────────────────────
شريط PlanningView ينشئ زر الحفظ عبر ErpToolbar.WithSave(..., "Edit").
ErpToolbar يمرر الفحص إلى PermissionGate، ثم يخفي الزر عندما تكون
planning/Edit = false. زر Save_Click وF10 كانا موصولين بصورة صحيحة؛
المصفوفة القديمة RoleResourcePermissions هي التي كانت ناقصة/مرفوضة
لدور التخطيط بعد ترقية القواعد.

■ الإصلاح
───────────────────────────────────────────────────────────────
PermissionService.EnsureCatalog يستدعي ترقية idempotent لمرة واحدة:
- يمنح planning/Edit لدوري Production وManagement، وهما الدوران المقصودان
  في التصميم الحالي لإنشاء/تعديل خطط الإنتاج.
- لا يمنح Edit لكل الأدوار ولا يستخدم Administrator كحل التفافي.
- لا يغير planning/Approve؛ الاعتماد عملية مستقلة.
- يحترم أي سحب يدوي لاحق بعد وضع علامة الترحيل.

■ الاختبارات المضافة/المحدثة
───────────────────────────────────────────────────────────────
- Production يملك planning/Edit وplanning/Approve.
- Quality وWarehouse لا يملكان planning/Edit أو planning/Approve.
- قاعدة قديمة بصف مرفوض تُصلح Production وManagement فقط.
- مستخدم بلا Edit يُرفض من PlanningService.UpdatePlan على الخادم.
- شريط الحفظ وF10 والزر Save_Click موثقة في اختبار wiring، ولا يوجد إجبار
  لـ SaveActionBtn على Visibility.Visible.

■ التطبيق والبناء
───────────────────────────────────────────────────────────────
طبّق الملفات فوق UPDATE_WORK الموجود مسبقاً. لا تغيّر PlanningView.xaml
أو PlanningView.xaml.cs لهذا التصحيح.

الإصدار يبقى كما هو:
  Version / ProductVersion: 1.50.73
  FileVersion: 1.50.73.0

على Windows شغّل اختبارات الحل ثم BUILD-PUBLISH-1.50.73-FINAL-WINDOWS.bat.
سيظل فحص CHECK-EXE يتحقق من FileVersion 1.50.73.0 وProductVersion 1.50.73.
