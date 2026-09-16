# تفعيل CI — ملاحظة هامة (أذونات Workflow)

الملف `CI_WORKFLOW_READY.yml` هو **ملف CI الكامل** مع خطوة جديدة:
**«Build Windows installer package»** — تنشر `MfgSystem.exe` مكتفياً ذاتياً (win-x64)،
تضيف أدوات التنصيب، تولّد `VERSION.txt` و`SHA256.txt`، وتضغط الحزمة وترفعها كـ artifact
اسمها **`mfgsystem-installer-package`** (صلاح 30 يومًا) عند كل دفع ناجح على ويندوز.

## لماذا لا يوجد في `.github/workflows/` الآن؟

أداة النشر في هذه البيئة (GitHub App) **لا تملك صلاحية `workflows`** لإنشاء أو تحديث
ملفات داخل `.github/workflows/`. لهذا السبب يُسلَّم الملف جاهزًا هنا بدلًا من كتابته مباشرة.

## كيف تُفعّله (اختر أحد الطريقين)

### الطريقة 1 — عن طريق واجهة GitHub (الأسرع)
1. افتح المستودع في المتصفح ← تبويب **Actions** ← **New workflow** ← **Set up a workflow yourself**.
2. انسخ كامل محتوى `CI/CI_WORKFLOW_READY.yml` والصقه في المحرر.
3. اضغط **Commit new file** (اسم الملف: `ci.yml`).

### الطريقة 2 — عن طريق سطر الأوامر (بصلاحية workflows)
1. أضف للملف مسار workflows ثم ارفع:
   ```bat
   mkdir .github\workflows
   copy CI\CI_WORKFLOW_READY.yml .github\workflows\ci.yml
   git add .github\workflows\ci.yml
   git commit -m "Enable CI with Windows installer packaging"
   git push
   ```
   (هذه الخطوة تتطلب حسابًا/توكنًا يملك صلاحية `workflows` — أي حسابك الشخصي، وليست أداة الـ App.)

## بعد التفعيل

- كل دفع إلى `main` أو `arena/**` يشغّل: restore ← build (`-warnaserror`) ← tests ←
  acceptance ← unit audit ← WPF smoke (ويندوز) ← **نشر حزمة الإنستالر** ← رفع artifact.
- لتحميل حزمة تثبيت جاهزة: **Actions → run → Artifacts → `mfgsystem-installer-package`**
  ثم استخرجها وشغّل `2-تنصيب.bat`.

> ملاحظة: الخطوة الجديدة مبنية على `runner.os == 'Windows'` (ويندوز فقط) لأن بناء WPF
> غير ممكن على Linux. البنية الباقية تعمل على لينكس وويندوز معًا.
