using System;
using System.Globalization;
using System.IO;

namespace EndfieldChargePlus.Diagnostics;

/// <summary>
/// Lightweight file logger used by the desktop app itself. It intentionally has no external
/// logging dependency so logging is still available when NuGet/provider initialization fails.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EndfieldChargePlus",
        "Logs");

    public static string CurrentLogPath { get; private set; } = Path.Combine(LogsDirectory, "latest.log");

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;
            Directory.CreateDirectory(LogsDirectory);

            string dated = $"EndfieldChargePlus-{DateTime.Now:yyyyMMdd}.log";
            CurrentLogPath = Path.Combine(LogsDirectory, dated);
            _initialized = true;

            CleanupOldLogs_NoThrow();
        }

        Info("Application logging initialized.");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);
    public static void Fatal(string message, Exception? exception = null) => Write("FATAL", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            if (!_initialized) Initialize();

            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            string line = $"[{timestamp}] [{level}] {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;
            line += Environment.NewLine;

            lock (Gate)
            {
                Directory.CreateDirectory(LogsDirectory);
                File.AppendAllText(CurrentLogPath, line);
                // latest.log always mirrors the current process/day log for quick support access.
                File.AppendAllText(Path.Combine(LogsDirectory, "latest.log"), line);
            }
        }
        catch
        {
            // Logging must never crash the HUD.
        }
    }

    private static void CleanupOldLogs_NoThrow()
    {
        try
        {
            var files = new DirectoryInfo(LogsDirectory)
                .GetFiles("EndfieldChargePlus-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(14);

            foreach (var file in files)
            {
                try { file.Delete(); }
                catch { }
            }

            string latest = Path.Combine(LogsDirectory, "latest.log");
            try
            {
                if (File.Exists(latest))
                    File.Delete(latest);
            }
            catch { }
        }
        catch { }
    }
}
