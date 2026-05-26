using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamsTrayStarter
{
    public static class Logger
    {
        private static readonly object _sync = new object();
        private const long MaxLogSizeBytes = 1_048_576; // 1 MB
        private static readonly Regex EntryStartRegex = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}) \[(?<level>[A-Z]+)\] (?<msg>.*)$",
            RegexOptions.Compiled);

        public static string LogFilePath { get; private set; } = string.Empty;

        public static void Init(string appDataFolder)
        {
            Directory.CreateDirectory(appDataFolder);
            LogFilePath = Path.Combine(appDataFolder, "app.log");
        }

        // INFO logging intentionally disabled across the application.
        public static void Info(string message)
        {
            // no-op by design
        }

        public static void Warn(string message) => Write("WARN", message);
        public static void Change(string message) => Write("CHANGE", message);
        public static void Error(string message, Exception ex) => Write("ERROR", message + Environment.NewLine + ex);

        public static void Clear()
        {
            try
            {
                lock (_sync)
                {
                    if (string.IsNullOrWhiteSpace(LogFilePath))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
                }
            }
            catch
            {
                // Never crash due to logging.
            }
        }

        public static List<ParsedLogEntry> ReadEntries()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LogFilePath) || !File.Exists(LogFilePath))
                    return new List<ParsedLogEntry>();

                string content = File.ReadAllText(LogFilePath, Encoding.UTF8);
                return ParseEntries(content);
            }
            catch (Exception ex)
            {
                return new List<ParsedLogEntry>
                {
                    new ParsedLogEntry
                    {
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                        Level = "ERROR",
                        Message = "Failed to read log entries." + Environment.NewLine + ex.Message
                    }
                };
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

                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                    {
                        File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
                    }

                    if (File.Exists(LogFilePath))
                    {
                        RewriteLogWithoutOlderDuplicateEntries(level, message);
                    }

                    string newEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, newEntry, Encoding.UTF8);
                }
            }
            catch
            {
                // Never crash due to logging.
            }
        }

        private static void RewriteLogWithoutOlderDuplicateEntries(string levelToKeepLatest, string messageToKeepLatest)
        {
            try
            {
                string existing = File.ReadAllText(LogFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(existing))
                    return;

                var entries = ParseEntries(existing);
                bool removedAny = false;
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    if (string.Equals(entry.Level, levelToKeepLatest, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.Message, messageToKeepLatest, StringComparison.Ordinal))
                    {
                        entries.RemoveAt(i);
                        removedAny = true;
                    }
                }

                if (!removedAny)
                    return;

                var sb = new StringBuilder();
                foreach (var entry in entries)
                {
                    sb.Append(entry.Timestamp).Append(" [")
                      .Append(entry.Level).Append("] ")
                      .Append(entry.Message).Append(Environment.NewLine);
                }

                File.WriteAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Keep normal logging behavior if rewrite fails.
            }
        }

        private static List<ParsedLogEntry> ParseEntries(string content)
        {
            var result = new List<ParsedLogEntry>();
            var lines = content.Replace("\r\n", "\n").Split('\n');
            ParsedLogEntry? current = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine ?? string.Empty;
                if (line.Length == 0)
                    continue;

                var match = EntryStartRegex.Match(line);
                if (match.Success)
                {
                    if (current != null)
                        result.Add(current);

                    current = new ParsedLogEntry
                    {
                        Timestamp = match.Groups["ts"].Value,
                        Level = match.Groups["level"].Value,
                        Message = match.Groups["msg"].Value
                    };
                }
                else if (current != null)
                {
                    current.Message += Environment.NewLine + line;
                }
            }

            if (current != null)
                result.Add(current);

            return result;
        }

        public sealed class ParsedLogEntry
        {
            public string Timestamp { get; set; } = string.Empty;
            public string Level { get; set; } = "INFO";
            public string Message { get; set; } = string.Empty;
        }
    }
}