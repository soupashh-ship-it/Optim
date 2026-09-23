using System.Text;

namespace Optim.Core.Logging;

/// <summary>Minimal file logger for the app: %LOCALAPPDATA%\Optim\logs.</summary>
public static class FileLogger
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optim", "logs");

    /// <summary>When false, writes are dropped. Unit tests disable this to keep the real log clean.</summary>
    public static bool Enabled { get; set; } = true;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);
    private static void Write(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            PruneOldLogs();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(
                    Path.Combine(LogDirectory, $"optim-{DateTime.Now:yyyyMMdd}.log"),
                    line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>Keeps the last 14 days of logs; best-effort, never throws.</summary>
    private static void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "optim-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
