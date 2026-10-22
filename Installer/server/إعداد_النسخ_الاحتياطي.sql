/*
  MfgSystem — نسخ احتياطي يومي اختياري عبر SQL Server Agent.
  يتطلب SQL Server Agent (غير موجود في SQL Server Express) وصلاحية إدارة المهام.
  أنشئ C:\MfgSystem_Backups يدوياً على الخادم، وامنح حساب خدمة SQL Server الكتابة إليه.
  شغّل عبر sqlcmd -b بعد مراجعة الملف:
    sqlcmd -b -S SERVER01 -E -i "إعداد_النسخ_الاحتياطي.sql"

  لا يحذف/يستبدل قاعدة البيانات أو مهمة SQL Agent موجودة؛ يتوقف إذا وجد اسماً قائماً.
  كل تشغيل ينشئ ملفاً مؤرخاً جديداً ويجري RESTORE VERIFYONLY للملف نفسه.
*/
USE [msdb];
SET NOCOUNT ON;

IF DB_ID(N'MfgSystemDB') IS NULL
BEGIN
    RAISERROR(N'قاعدة MfgSystemDB غير موجودة؛ لم تُنشأ مهمة نسخ احتياطي.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'MfgSystem_DailyBackup')
BEGIN
    RAISERROR(N'مهمة MfgSystem_DailyBackup موجودة؛ لم يتم تعديلها أو حذفها.', 16, 1);
    RETURN;
END;

DECLARE @jobId UNIQUEIDENTIFIER;
DECLARE @backupCommand NVARCHAR(MAX) = N'
DECLARE @backupPath NVARCHAR(260);
SET @backupPath = N''C:\MfgSystem_Backups\MfgSystemDB_Full_''
    + CONVERT(CHAR(8), GETDATE(), 112)
    + N''_'' + RIGHT(N''0'' + CONVERT(NVARCHAR(2), DATEPART(HOUR, GETDATE())), 2)
    + RIGHT(N''0'' + CONVERT(NVARCHAR(2), DATEPART(MINUTE, GETDATE())), 2)
    + RIGHT(N''0'' + CONVERT(NVARCHAR(2), DATEPART(SECOND, GETDATE())), 2)
    + N''.bak'';
BACKUP DATABASE [MfgSystemDB]
    TO DISK = @backupPath
    WITH COMPRESSION, CHECKSUM, STATS = 10;
RESTORE VERIFYONLY FROM DISK = @backupPath WITH CHECKSUM;
';

EXEC msdb.dbo.sp_add_job
    @job_name = N'MfgSystem_DailyBackup',
    @description = N'نسخة احتياطية يومية كاملة لقاعدة MfgSystemDB والتحقق من سلامتها',
    @enabled = 1,
    @job_id = @jobId OUTPUT;

EXEC msdb.dbo.sp_add_jobstep
    @job_id = @jobId,
    @step_name = N'Full Backup and Verify',
    @subsystem = N'TSQL',
    @database_name = N'MfgSystemDB',
    @command = @backupCommand,
    @on_success_action = 1,
    @on_fail_action = 2;

EXEC msdb.dbo.sp_add_jobschedule
    @job_id = @jobId,
    @name = N'Daily 23:00',
    @freq_type = 4,
    @freq_interval = 1,
    @active_start_time = 230000;
EXEC msdb.dbo.sp_add_jobserver @job_id = @jobId, @server_name = N'(LOCAL)';

PRINT N'تم إنشاء المهمة MfgSystem_DailyBackup؛ تحقق من سجل SQL Agent ومن ملفات C:\MfgSystem_Backups المؤرخة.';
