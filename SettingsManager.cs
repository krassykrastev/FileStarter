
using System;
using System.IO;
using System.Text.Json;

namespace TeamsTrayStarter
{
    public static class SettingsManager
    {
        public static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TeamsTrayStarter");

        private static string SettingsFilePath => Path.Combine(AppDataFolder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);

                if (!File.Exists(SettingsFilePath))
                {
                    var defaults = AppSettings.CreateDefault();
                    Save(defaults);
                    return defaults;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? AppSettings.CreateDefault();
            }
            catch (Exception ex)
            {
                try { Logger.Error("Failed to load settings; using defaults.", ex); } catch { }
                return AppSettings.CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(AppDataFolder);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }

        public static DayLaunchSetting GetDaySetting(AppSettings s, DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => s.Mon,
                DayOfWeek.Tuesday => s.Tue,
                DayOfWeek.Wednesday => s.Wed,
                DayOfWeek.Thursday => s.Thu,
                DayOfWeek.Friday => s.Fri,
                DayOfWeek.Saturday => s.Sat,
                DayOfWeek.Sunday => s.Sun,
                _ => s.Mon
            };
        }

        public static TimeSpan GetDayLaunchTimeOrDefault(AppSettings s, DayOfWeek day)
        {
            var ds = GetDaySetting(s, day);
            if (!string.IsNullOrWhiteSpace(ds.Time)
                && TimeSpan.TryParse(ds.Time, out var t)
                && t >= TimeSpan.Zero
                && t < TimeSpan.FromDays(1))
            {
                return t;
            }

            Logger.Warn($"Invalid time '{ds.Time}' for {day}. Falling back to 09:00.");
            return new TimeSpan(9, 0, 0);
        }

        public static string ShortenDisplayName(string text, int maxLength = 12)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (maxLength <= 3)
                return text.Length <= maxLength ? text : text.Substring(0, maxLength);

            return text.Length <= maxLength
                ? text
                : text.Substring(0, maxLength - 3) + "...";
        }

        public static string GetDisplayNameFromPath(string? path, string fallbackText, int maxLength = 12)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    string trimmed = path.Trim();
                    string name = Path.GetFileNameWithoutExtension(trimmed);
                    if (!string.IsNullOrWhiteSpace(name))
                        return ShortenDisplayName(name, maxLength);
                }
                catch
                {
                    // ignore and fall back
                }
            }

            return fallbackText.Trim();
        }

        public static string GetFileLineText(int fileIndex, string? path, string fallbackText)
        {
            return $"File {fileIndex}: {GetDisplayNameFromPath(path, fallbackText)}";
        }

        public static bool IsAutoStartPausedByDate(AppSettings s, DateTime nowLocal)
        {
            if (!s.AutoStartOffFromEnabled || s.AutoStartOffFromDate == null)
                return false;

            DateTime today = nowLocal.Date;
            DateTime start = s.AutoStartOffFromDate.Value.Date;

            if (today < start)
                return false;

            if (!s.AutoStartOffUntilEnabled || s.AutoStartOffUntilDate == null)
                return false;

            DateTime end = s.AutoStartOffUntilDate.Value.Date;
            return today <= end;
        }

        public static bool ApplyScheduledAutoStartOffIfDue(AppSettings s, DateTime nowLocal)
        {
            if (!s.AutoStartOffFromEnabled || s.AutoStartOffFromDate == null)
                return false;

            if (s.AutoStartOffUntilEnabled && s.AutoStartOffUntilDate != null)
                return false;

            DateTime today = nowLocal.Date;
            DateTime start = s.AutoStartOffFromDate.Value.Date;

            if (today < start)
                return false;

            bool changed = false;

            if (s.AutoStartTeamsEnabled)
            {
                s.AutoStartTeamsEnabled = false;
                changed = true;
            }

            s.AutoStartOffFromEnabled = false;
            s.AutoStartOffFromDate = null;
            s.AutoStartOffUntilEnabled = false;
            s.AutoStartOffUntilDate = null;
            changed = true;

            return changed;
        }

        public static bool IsEffectiveAutoStartEnabled(AppSettings s, DateTime nowLocal)
        {
            return s.AutoStartTeamsEnabled && !IsAutoStartPausedByDate(s, nowLocal);
        }
    }

    public sealed class DayLaunchSetting
    {
        public bool Enabled { get; set; }
        public string Time { get; set; } = "09:00";
    }

    public sealed class AppSettings
    {
        public bool AutoStartTeamsEnabled { get; set; } = true;
        public bool RunAppAtStartup { get; set; } = true;
        public bool EnableDesktopNotifications { get; set; } = true;

        public DayLaunchSetting Mon { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Tue { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Wed { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Thu { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Fri { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Sat { get; set; } = new DayLaunchSetting { Enabled = false, Time = "09:00" };
        public DayLaunchSetting Sun { get; set; } = new DayLaunchSetting { Enabled = false, Time = "09:00" };

        public bool AutoStartOffFromEnabled { get; set; } = false;
        public DateTime? AutoStartOffFromDate { get; set; } = null;
        public bool AutoStartOffUntilEnabled { get; set; } = false;
        public DateTime? AutoStartOffUntilDate { get; set; } = null;

        public bool File1Enabled { get; set; } = true;
        public string? File1Path { get; set; } = null;

        public bool File2Enabled { get; set; } = false;
        public string? File2Path { get; set; } = null;

        public bool File3Enabled { get; set; } = false;
        public string? File3Path { get; set; } = null;

        public bool File4Enabled { get; set; } = false;
        public string? File4Path { get; set; } = null;

        public static AppSettings CreateDefault() => new AppSettings();
    }
}