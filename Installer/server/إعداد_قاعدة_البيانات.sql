/*
  MfgSystem 1.50.72 — إعداد قاعدة SQL Server اختيارية (لا يلزم للتثبيت المحلي)
  شغّل من sqlcmd بصلاحية إنشاء قاعدة:
    sqlcmd -b -S SERVER01 -E -i "إعداد_قاعدة_البيانات.sql"

  ينشئ قاعدة جديدة باسم MfgSystemDB فقط. إذا كان الاسم موجوداً، يتوقف قبل أي تغيير.
  لا يحذف أو يغيّر DateFactory أو أي قاعدة أخرى. التطبيق ينشئ الجداول والبيانات
  الأساسية تلقائياً عند أول اتصال إلى قاعدة MfgSystemDB فارغة.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'MfgSystemDB') IS NOT NULL
BEGIN
    RAISERROR(N'تم إيقاف الإعداد: MfgSystemDB موجودة مسبقاً. لم يُجر أي تعديل. اختر اسماً جديداً أو افحص القاعدة يدوياً.', 16, 1);
    RETURN;
END;

/* SQL Server يفرض أن يكون CREATE DATABASE في دفعة منفصلة؛ التنفيذ الديناميكي
   يبقي فحص الاسم وإنشاء القاعدة في الدفعة نفسها، ويمنع متابعة التعديل عند وجودها. */
EXEC(N'CREATE DATABASE [MfgSystemDB] COLLATE Arabic_CI_AS;');

IF DB_ID(N'MfgSystemDB') IS NULL
BEGIN
    RAISERROR(N'فشل إنشاء MfgSystemDB. راجع سجل SQL Server.', 16, 1);
    RETURN;
END;

ALTER DATABASE [MfgSystemDB] SET RECOVERY FULL;
ALTER DATABASE [MfgSystemDB] SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE [MfgSystemDB] SET AUTO_UPDATE_STATISTICS ON;
ALTER DATABASE [MfgSystemDB] SET AUTO_SHRINK OFF;
ALTER DATABASE [MfgSystemDB] SET AUTO_CLOSE OFF;

SELECT DB_NAME(database_id) AS [القاعدة],
       DATABASEPROPERTYEX(N'MfgSystemDB', 'Recovery') AS [نمط الاسترداد],
       DATABASEPROPERTYEX(N'MfgSystemDB', 'Collation') AS [الترميز],
       DATABASEPROPERTYEX(N'MfgSystemDB', 'Status') AS [الحالة]
FROM sys.databases
WHERE name = N'MfgSystemDB';

PRINT N'تم إنشاء قاعدة MfgSystemDB جديدة. لم تُمس أي قاعدة أخرى.';
PRINT N'أنشئ مستخدم SQL محدود الصلاحيات يدوياً عند الحاجة، أو استخدم Windows Authentication.';
PRINT N'تأكد من صلاحيات التطبيق اللازمة لإنشاء مخطط MfgSystem عند أول اتصال.';
