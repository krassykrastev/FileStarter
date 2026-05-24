using System;
using System.IO;

namespace TeamsTrayStarter
{
    public static class Logger
    {
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

        private static void Write(string level, string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line);
            }
            catch
            {
                // never crash due to logging
            }
        }
    }
}