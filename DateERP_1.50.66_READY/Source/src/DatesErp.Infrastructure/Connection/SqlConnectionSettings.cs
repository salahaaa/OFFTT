using Microsoft.Data.SqlClient;

namespace DatesErp.Infrastructure.Connection;

/// <summary>سياسة واحدة لاتصال التطبيق وفحص الإعداد: قيم مهربة وتشفير مع تحقق من الشهادة افتراضياً.</summary>
public static class SqlConnectionSettings
{
    public static SqlConnectionStringBuilder CreateBuilder(string server, string database,
        string authMode, string uid = null, string password = null,
        bool trustServerCertificate = false, int timeout = 8)
    {
        if (string.IsNullOrWhiteSpace(server)) throw new ArgumentException("اسم الخادم مطلوب.", nameof(server));
        if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("اسم قاعدة البيانات مطلوب.", nameof(database));
        if (authMode != "Windows" && authMode != "Sql")
            throw new ArgumentException("طريقة مصادقة SQL غير صالحة.", nameof(authMode));

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = authMode == "Windows",
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = trustServerCertificate,
            PersistSecurityInfo = false,
            MultipleActiveResultSets = true,
            ConnectTimeout = timeout
        };
        if (authMode == "Sql")
        {
            builder.UserID = uid ?? "";
            builder.Password = password ?? "";
        }
        return builder;
    }
}
