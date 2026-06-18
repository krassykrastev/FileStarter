using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamsTrayStarter
{
    public static class Logger
    {
        private static readonly object Sync = new();
        private static readonly Regex EntryStartRegex = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}) \[(?<level>CHANGE|OTHER)\] (?<msg>.*)$",
            RegexOptions.Compiled);
        private const long MaxLogSizeBytes = 1_048_576;

        public static string LogFilePath { get; private set; } = string.Empty;

        public static void Init(string appDataFolder)
        {
            Directory.CreateDirectory(appDataFolder);
            LogFilePath = Path.Combine(appDataFolder, "app.log");
        }

        public static void Change(string message) => Write("CHANGE", message);
        public static void Other(string message) => Write("OTHER", message);
        public static void Warn(string message) => Write("OTHER", message);
        public static void Error(string message, Exception ex) => Write("OTHER", message + Environment.NewLine + ex);

        public static void Clear()
        {
            try
            {
                lock (Sync)
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
                        Level = "OTHER",
                        Message = "Failed to read log entries." + Environment.NewLine + ex.Message
                    }
                };
            }
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    if (string.IsNullOrWhiteSpace(LogFilePath))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                    {
                        File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
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
            public string Level { get; set; } = "OTHER";
            public string Message { get; set; } = string.Empty;
        }
    }
}