using System.Text;

namespace CmdletForge.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logPath;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cmdlet Forge",
        "Logs");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        _logPath = Path.Combine(LogDirectory, $"cmdletforge-{DateTime.Now:yyyyMMdd}.log");
        Info($"Start Cmdlet Forge {AppInfo.Version}");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warning(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_logPath is null)
            return;

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [").Append(level).Append("] ")
            .AppendLine(message);

        if (exception is not null)
            line.AppendLine(exception.ToString());

        lock (Gate)
        {
            try
            {
                File.AppendAllText(_logPath, line.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Logging must never take the application down.
            }
        }
    }
}
