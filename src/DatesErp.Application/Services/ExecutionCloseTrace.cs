using System.Globalization;

namespace DatesErp.Application.Services;

/// <summary>تشخيص مؤقت لمسار إقفال الإنتاج فقط — لا يغيّر قاعدة البيانات أو قواعد التشغيل.</summary>
internal static class ExecutionCloseTrace
{
    private static readonly object Sync = new();

    private static string PathName =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DateERP", "logs", "execution-close.log");

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                var directory = System.IO.Path.GetDirectoryName(PathName);
                if (!string.IsNullOrWhiteSpace(directory)) System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(PathName,
                    $"[{DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // التشخيص لا يجوز أن يعطل عملية الإنتاج.
        }
    }
}
