using System;
using System.IO;

namespace TeamsTrayStarter
{
    public static class Logger
    {
        private static readonly object _sync = new object();
        private const long MaxLogSizeBytes = 1_048_576; // 1 MB

        public static string LogFilePath { get; private set; } = "";

        public static void Init(string appDataFolder)
        {
            Directory.CreateDirectory(appDataFolder);
            LogFilePath = Path.Combine(appDataFolder, "app.log");
            Info("Logger initialized.");
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex)
            => Write("ERROR", message + Environment.NewLine + ex);

        public static void Clear()
        {
            try
            {
                lock (_sync)
                {
                    if (string.IsNullOrWhiteSpace(LogFilePath))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    File.WriteAllText(LogFilePath, string.Empty);
                }
            }
            catch
            {
                // never crash due to logging
            }
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (_sync)
                {
                    if (string.IsNullOrWhiteSpace(LogFilePath))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);

                    if (File.Exists(LogFilePath))
                    {
                        var info = new FileInfo(LogFilePath);
                        if (info.Length > MaxLogSizeBytes)
                        {
                            File.WriteAllText(LogFilePath, string.Empty);
                        }
                    }

                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, line);
                }
            }
            catch
            {
                // never crash due to logging
            }
        }
    }
}