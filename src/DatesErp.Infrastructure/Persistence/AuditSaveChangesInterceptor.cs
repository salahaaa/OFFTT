using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace DatesErp.Infrastructure.Persistence;

/// <summary>
/// §5/§26 — طبقة وسيطة على SaveChanges:
/// 1) ختم الطوابع الزمنية والمنشئ/المعدِّل تلقائياً.
/// 2) تحديث رمز التزامن (بديل rowversion على SQLite).
/// 3) كتابة سجل التدقيق داخل نفس المعاملة — لا تدقيق بدون حركة ولا حركة بدون تدقيق.
/// 4) ترجمة تعارض التزامن إلى رسالة عربية واضحة.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentSession _session;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    /// <summary>
    /// §B110 — حقول الاعتماد لا تُكتب في سجل التدقيق أبداً (لا القديم ولا الجديد).
    /// قبل هذا الإصلاح كان تعديل أي مستخدم يُهرّب بصمة كلمة المرور والملح نصاً إلى
    /// AuditLog — فيستطيع كل من يملك عرض التدقيق استخراجها للكسر دون اتصال.
    /// </summary>
    private static readonly HashSet<string> _redactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "PasswordSalt"
    };

    public static bool IsRedactedProperty(string propertyName) => _redactedProperties.Contains(propertyName);

    public AuditSaveChangesInterceptor(ICurrentSession session)
    {
        _session = session;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext ctx)
    {
        if (ctx == null) return;
        var now = DateTime.Now;
        var isSqlServer = ctx.Database.IsSqlServer();
        var auditEntries = new List<AuditLog>();

        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog) continue; // لا ندقق على التدقيق نفسه

            if (entry.Entity is AuditableEntity aud)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        aud.CreatedDate = now;
                        aud.CreatedBy ??= _session?.UserId;
                        if (!isSqlServer) aud.RowVersion = Guid.NewGuid().ToByteArray();
                        break;
                    case EntityState.Modified:
                        aud.ModifiedDate = now;
                        aud.ModifiedBy = _session?.UserId;
                        if (!isSqlServer) aud.RowVersion = Guid.NewGuid().ToByteArray();
                        break;
                }
            }

            // §26 — بناء سجل التدقيق للعمليات الجوهرية
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity is not SystemSetting && entry.Entity is not DbVersion)
            {
                var docType = entry.Entity.GetType().Name;
                var docNumber = (entry.Entity as WorkflowDocument)?.DocumentNumber;
                var recordId = (entry.Entity as BaseEntity)?.Id;
                string action = entry.State switch
                {
                    EntityState.Added => "Create",
                    EntityState.Modified => "Edit",
                    EntityState.Deleted => "Delete",
                    _ => null
                };
                if ((action == "Edit" || action == "Delete") && entry.Entity is WorkflowDocument wd)
                {
                    var statusProp = entry.Property(nameof(WorkflowDocument.Status));
                    var approveProp = entry.Property(nameof(WorkflowDocument.IsApproved));
                    if (approveProp != null && approveProp.CurrentValue is true && approveProp.OriginalValue is false) action = "Approve";
                    else if (statusProp != null && Equals(statusProp.CurrentValue, DocStatuses.Cancelled) && !Equals(statusProp.OriginalValue, DocStatuses.Cancelled)) action = "Cancel";
                    else if (statusProp != null && Equals(statusProp.CurrentValue, DocStatuses.Issued) && !Equals(statusProp.OriginalValue, DocStatuses.Issued)) action = "Issue";
                }

                auditEntries.Add(new AuditLog
                {
                    UserId = _session?.UserId,
                    UserName = _session?.UserName ?? "system",
                    ComputerName = Environment.MachineName,
                    MachineName = Environment.MachineName,
                    ActionDate = now,
                    ScreenName = docType,
                    ActionType = action,
                    DocumentType = docType,
                    DocumentNumber = docNumber,
                    RecordId = recordId,
                    OldValue = entry.State == EntityState.Deleted || entry.State == EntityState.Modified ? SafeSnapshot(entry, original: true) : null,
                    NewValue = entry.State == EntityState.Deleted ? null : SafeSnapshot(entry, original: false)
                });
            }
        }

        if (auditEntries.Count > 0)
            ctx.Set<AuditLog>().AddRange(auditEntries);
    }

    /// <summary>
    /// §B110 — لقطة القيم (قبل/بعد) مع حجب حقول الاعتماد نهائياً.
    /// تُبنى من خصائص الكيان خصيصةً خصيصةً بدل <c>ToObject()</c> كي لا تمر أي قيمة
    /// محجوبة عبر التسلسل مهما تغيّر الكيان مستقبلاً.
    /// </summary>
    private static string SafeSnapshot(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, bool original)
    {
        try
        {
            var values = original ? entry.OriginalValues : entry.CurrentValues;
            var dict = new Dictionary<string, object?>();
            foreach (var prop in values.Properties)
            {
                if (IsRedactedProperty(prop.Name)) continue;
                dict[prop.Name] = values[prop];
            }
            var s = JsonSerializer.Serialize(dict, _json);
            return s.Length > 4000 ? s[..4000] : s;
        }
        catch { return null; }
    }
}
