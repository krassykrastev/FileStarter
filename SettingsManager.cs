using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TeamsTrayStarter
{
    public static class SettingsManager
    {
        private static readonly string[] SlotLabels = { "File 1", "File 2", "File 3", "File 4" };
        private static readonly string[] SlotDefaultTexts = { "MS Teams", "MS Outlook", "not yet selected", "not yet selected" };
        private static readonly TimeSpan LaunchGracePeriod = TimeSpan.FromMinutes(1);

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
                try { Logger.Other("Failed to load settings; using defaults.", ex); } catch { }
                return AppSettings.CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(AppDataFolder);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }

        public static DayLaunchSetting GetDaySetting(AppSettings settings, DayOfWeek day)
            => day switch
            {
                DayOfWeek.Monday => settings.Mon,
                DayOfWeek.Tuesday => settings.Tue,
                DayOfWeek.Wednesday => settings.Wed,
                DayOfWeek.Thursday => settings.Thu,
                DayOfWeek.Friday => settings.Fri,
                DayOfWeek.Saturday => settings.Sat,
                DayOfWeek.Sunday => settings.Sun,
                _ => settings.Mon
            };

        public static TimeSpan GetDayLaunchTimeOrDefault(AppSettings settings, DayOfWeek day)
        {
            var daySetting = GetDaySetting(settings, day);
            if (!string.IsNullOrWhiteSpace(daySetting.Time) &&
                TimeSpan.TryParse(daySetting.Time, out var time) &&
                time >= TimeSpan.Zero &&
                time < TimeSpan.FromDays(1))
            {
                return time;
            }

            Logger.Other($"Invalid time '{daySetting.Time}' for {day}. Falling back to 09:00.");
            return new TimeSpan(9, 0, 0);
        }

        public static DateTime? GetNextLaunchDateTime(AppSettings settings, DateTime nowLocal)
        {
            DateTime searchStart = nowLocal;

            if (IsAutoStartPausedByDate(settings, nowLocal) &&
                settings.AutoStartOffUntilEnabled &&
                settings.AutoStartOffUntilDate != null)
            {
                searchStart = settings.AutoStartOffUntilDate.Value.Date.AddDays(1);
            }

            for (int offset = 0; offset < 8; offset++)
            {
                var date = searchStart.Date.AddDays(offset);
                var daySetting = GetDaySetting(settings, date.DayOfWeek);

                if (!daySetting.Enabled)
                    continue;

                var candidate = date.Add(GetDayLaunchTimeOrDefault(settings, date.DayOfWeek));

                if (candidate.Date > nowLocal.Date)
                    return candidate;

                if (candidate >= nowLocal)
                    return candidate;

                if (candidate.Date == nowLocal.Date &&
                    nowLocal - candidate <= LaunchGracePeriod)
                {
                    return candidate;
                }
            }

            return null;
        }

        public static bool ShouldLaunchNow(AppSettings settings, DateTime nowLocal)
        {
            var todaySetting = GetDaySetting(settings, nowLocal.DayOfWeek);
            if (!todaySetting.Enabled)
                return false;

            var todayLaunch = nowLocal.Date.Add(GetDayLaunchTimeOrDefault(settings, nowLocal.DayOfWeek));
            return nowLocal >= todayLaunch &&
                   nowLocal - todayLaunch <= LaunchGracePeriod;
        }

        public static string ShortenDisplayName(string text, int maxLength = 12)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (maxLength <= 3)
                return text.Length <= maxLength ? text : text.Substring(0, maxLength);

            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }

        public static string GetDisplayNameFromPath(string? path, string fallbackText, int maxLength = 12)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(path.Trim());
                    if (!string.IsNullOrWhiteSpace(name))
                        return ShortenDisplayName(name, maxLength);
                }
                catch
                {
                    // Ignore and fall back.
                }
            }

            return fallbackText.Trim();
        }

        public static string GetSlotLabel(int fileIndex)
            => fileIndex >= 1 && fileIndex <= SlotLabels.Length ? SlotLabels[fileIndex - 1] : $"File {fileIndex}";

        public static string GetSlotDefaultText(int fileIndex)
            => fileIndex >= 1 && fileIndex <= SlotDefaultTexts.Length ? SlotDefaultTexts[fileIndex - 1] : "not yet selected";

        public static string GetSlotDisplayName(int fileIndex, string? path, int maxLength = 12)
            => GetDisplayNameFromPath(path, GetSlotDefaultText(fileIndex), maxLength);

        public static string GetFileLineText(int fileIndex, string? path, string fallbackText)
            => $"{GetSlotLabel(fileIndex)}: {GetDisplayNameFromPath(path, fallbackText)}";

        public static string GetFileLineText(int fileIndex, string? path)
            => $"{GetSlotLabel(fileIndex)}: {GetSlotDisplayName(fileIndex, path)}";

        public static bool IsAutoStartPausedByDate(AppSettings settings, DateTime nowLocal)
        {
            if (!settings.AutoStartOffFromEnabled || settings.AutoStartOffFromDate == null)
                return false;

            DateTime today = nowLocal.Date;
            DateTime start = settings.AutoStartOffFromDate.Value.Date;

            if (today < start)
                return false;

            if (!settings.AutoStartOffUntilEnabled || settings.AutoStartOffUntilDate == null)
                return false;

            return today <= settings.AutoStartOffUntilDate.Value.Date;
        }

        public static bool ApplyScheduledAutoStartOffIfDue(AppSettings settings, DateTime nowLocal)
        {
            if (!settings.AutoStartOffFromEnabled || settings.AutoStartOffFromDate == null)
                return false;

            DateTime today = nowLocal.Date;
            DateTime start = settings.AutoStartOffFromDate.Value.Date;

            if (settings.AutoStartOffUntilEnabled && settings.AutoStartOffUntilDate != null)
            {
                DateTime end = settings.AutoStartOffUntilDate.Value.Date;
                if (today < start || today <= end)
                    return false;

                ResetVacationWindow(settings);
                settings.AutoStartTeamsEnabled = true;
                return true;
            }

            if (today < start)
                return false;

            settings.AutoStartTeamsEnabled = false;
            ResetVacationWindow(settings);
            return true;
        }

        public static bool IsEffectiveAutoStartEnabled(AppSettings settings, DateTime nowLocal)
            => settings.AutoStartTeamsEnabled && !IsAutoStartPausedByDate(settings, nowLocal);

        public static AppSettings Clone(AppSettings source)
        {
            return new AppSettings
            {
                AutoStartTeamsEnabled = source.AutoStartTeamsEnabled,
                RunAppAtStartup = source.RunAppAtStartup,
                StartVpnFirstEnabled = source.StartVpnFirstEnabled,
                VpnConnectionName = source.VpnConnectionName,
                Mon = CloneDay(source.Mon),
                Tue = CloneDay(source.Tue),
                Wed = CloneDay(source.Wed),
                Thu = CloneDay(source.Thu),
                Fri = CloneDay(source.Fri),
                Sat = CloneDay(source.Sat),
                Sun = CloneDay(source.Sun),
                AutoStartOffFromEnabled = source.AutoStartOffFromEnabled,
                AutoStartOffFromDate = source.AutoStartOffFromDate,
                AutoStartOffUntilEnabled = source.AutoStartOffUntilEnabled,
                AutoStartOffUntilDate = source.AutoStartOffUntilDate,
                File1Enabled = source.File1Enabled,
                File1Path = source.File1Path,
                File2Enabled = source.File2Enabled,
                File2Path = source.File2Path,
                File3Enabled = source.File3Enabled,
                File3Path = source.File3Path,
                File4Enabled = source.File4Enabled,
                File4Path = source.File4Path
            };
        }

        public static bool HasSettingsChanges(AppSettings before, AppSettings after)
        {
            return before.RunAppAtStartup != after.RunAppAtStartup ||
                   before.StartVpnFirstEnabled != after.StartVpnFirstEnabled ||
                   !StringEqualsForSettings(before.VpnConnectionName, after.VpnConnectionName) ||
                   HasDayChange(before.Mon, after.Mon) ||
                   HasDayChange(before.Tue, after.Tue) ||
                   HasDayChange(before.Wed, after.Wed) ||
                   HasDayChange(before.Thu, after.Thu) ||
                   HasDayChange(before.Fri, after.Fri) ||
                   HasDayChange(before.Sat, after.Sat) ||
                   HasDayChange(before.Sun, after.Sun) ||
                   before.AutoStartOffFromEnabled != after.AutoStartOffFromEnabled ||
                   before.AutoStartOffFromDate != after.AutoStartOffFromDate ||
                   before.AutoStartOffUntilEnabled != after.AutoStartOffUntilEnabled ||
                   before.AutoStartOffUntilDate != after.AutoStartOffUntilDate ||
                   before.File1Enabled != after.File1Enabled ||
                   !StringEqualsForSettings(before.File1Path, after.File1Path) ||
                   before.File2Enabled != after.File2Enabled ||
                   !StringEqualsForSettings(before.File2Path, after.File2Path) ||
                   before.File3Enabled != after.File3Enabled ||
                   !StringEqualsForSettings(before.File3Path, after.File3Path) ||
                   before.File4Enabled != after.File4Enabled ||
                   !StringEqualsForSettings(before.File4Path, after.File4Path);
        }

        public static void LogSettingsChanges(AppSettings before, AppSettings after)
        {
            var changes = new List<string>();

            AddEnableDisableChange(changes, "Run on Windows startup", before.RunAppAtStartup, after.RunAppAtStartup);
            AddEnableDisableChange(changes, "Start VPN first & reconnect on drops", before.StartVpnFirstEnabled, after.StartVpnFirstEnabled);
            AddPathChange(changes, 1, before.File1Path, after.File1Path);
            AddPathChange(changes, 2, before.File2Path, after.File2Path);
            AddPathChange(changes, 3, before.File3Path, after.File3Path);
            AddPathChange(changes, 4, before.File4Path, after.File4Path);
            AddEnableDisableChange(changes, "File 1", before.File1Enabled, after.File1Enabled);
            AddEnableDisableChange(changes, "File 2", before.File2Enabled, after.File2Enabled);
            AddEnableDisableChange(changes, "File 3", before.File3Enabled, after.File3Enabled);
            AddEnableDisableChange(changes, "File 4", before.File4Enabled, after.File4Enabled);
            AddVacationDateChange(changes, "Auto-start disabled from", before.AutoStartOffFromDate, after.AutoStartOffFromDate);
            AddVacationDateChange(changes, "Auto-start disabled until", before.AutoStartOffUntilDate, after.AutoStartOffUntilDate);
            AddDayChange(changes, DayOfWeek.Monday, before.Mon, after.Mon);
            AddDayChange(changes, DayOfWeek.Tuesday, before.Tue, after.Tue);
            AddDayChange(changes, DayOfWeek.Wednesday, before.Wed, after.Wed);
            AddDayChange(changes, DayOfWeek.Thursday, before.Thu, after.Thu);
            AddDayChange(changes, DayOfWeek.Friday, before.Fri, after.Fri);
            AddDayChange(changes, DayOfWeek.Saturday, before.Sat, after.Sat);
            AddDayChange(changes, DayOfWeek.Sunday, before.Sun, after.Sun);
            AddStringChange(changes, "VPN connection name", before.VpnConnectionName, after.VpnConnectionName);

            foreach (var change in changes)
            {
                Logger.Change(change);
            }
        }

        private static DayLaunchSetting CloneDay(DayLaunchSetting source)
            => new DayLaunchSetting { Enabled = source.Enabled, Time = source.Time };

        private static bool HasDayChange(DayLaunchSetting before, DayLaunchSetting after)
            => before.Enabled != after.Enabled || !string.Equals(before.Time, after.Time, StringComparison.OrdinalIgnoreCase);

        private static bool StringEqualsForSettings(string? before, string? after)
            => string.Equals(NormalizeDisplay(before), NormalizeDisplay(after), StringComparison.OrdinalIgnoreCase);

        private static void AddEnableDisableChange(List<string> changes, string label, bool before, bool after)
        {
            if (before == after)
                return;

            changes.Add($"{label} {(after ? "enabled" : "disabled")}");
        }

        private static void AddStringChange(List<string> changes, string label, string? before, string? after)
        {
            string normalizedBefore = NormalizeDisplay(before);
            string normalizedAfter = NormalizeDisplay(after);
            if (!string.Equals(normalizedBefore, normalizedAfter, StringComparison.OrdinalIgnoreCase))
                changes.Add($"{label} changed to {normalizedAfter}");
        }

        private static void AddVacationDateChange(List<string> changes, string label, DateTime? before, DateTime? after)
        {
            if (before?.Date == after?.Date || after == null)
                return;

            changes.Add($"{label}: {after:yyyy-MM-dd}");
        }

        private static void AddDayChange(List<string> changes, DayOfWeek dayOfWeek, DayLaunchSetting before, DayLaunchSetting after)
        {
            if (!HasDayChange(before, after))
                return;

            string dayName = GetDayDisplayName(dayOfWeek);
            string timeText = NormalizeDayTime(after.Time);
            changes.Add($"{dayName} start at {timeText} {(after.Enabled ? "enabled" : "disabled")}");
        }

        private static void AddPathChange(List<string> changes, int fileIndex, string? before, string? after)
        {
            if (StringEqualsForSettings(before, after))
                return;

            string label = GetSlotLabel(fileIndex);
            if (string.IsNullOrWhiteSpace(after))
            {
                if (fileIndex == 1)
                    changes.Add($"{label} path changed to default (MS Teams)");
                else if (fileIndex == 2)
                    changes.Add($"{label} path changed to default (MS Outlook)");
                else
                    changes.Add($"{label} path cleared");

                return;
            }

            changes.Add($"{label} path changed to {after.Trim()}");
        }

        private static string GetDayDisplayName(DayOfWeek dayOfWeek)
            => dayOfWeek switch
            {
                DayOfWeek.Monday => "Monday",
                DayOfWeek.Tuesday => "Tuesday",
                DayOfWeek.Wednesday => "Wednesday",
                DayOfWeek.Thursday => "Thursday",
                DayOfWeek.Friday => "Friday",
                DayOfWeek.Saturday => "Saturday",
                DayOfWeek.Sunday => "Sunday",
                _ => dayOfWeek.ToString()
            };

        private static string NormalizeDayTime(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && TimeSpan.TryParse(value, out var time))
                return time.ToString(@"hh\:mm");

            return NormalizeDisplay(value);
        }

        private static string NormalizeDisplay(string? value)
            => string.IsNullOrWhiteSpace(value) ? "<none>" : value.Trim();

        private static void ResetVacationWindow(AppSettings settings)
        {
            settings.AutoStartOffFromEnabled = false;
            settings.AutoStartOffFromDate = null;
            settings.AutoStartOffUntilEnabled = false;
            settings.AutoStartOffUntilDate = null;
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
        public bool StartVpnFirstEnabled { get; set; }
        public string? VpnConnectionName { get; set; }

        public DayLaunchSetting Mon { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Tue { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Wed { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Thu { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Fri { get; set; } = new DayLaunchSetting { Enabled = true, Time = "09:00" };
        public DayLaunchSetting Sat { get; set; } = new DayLaunchSetting { Enabled = false, Time = "09:00" };
        public DayLaunchSetting Sun { get; set; } = new DayLaunchSetting { Enabled = false, Time = "09:00" };

        public bool AutoStartOffFromEnabled { get; set; }
        public DateTime? AutoStartOffFromDate { get; set; }
        public bool AutoStartOffUntilEnabled { get; set; }
        public DateTime? AutoStartOffUntilDate { get; set; }

        public bool File1Enabled { get; set; } = true;
        public string? File1Path { get; set; }
        public bool File2Enabled { get; set; }
        public string? File2Path { get; set; }
        public bool File3Enabled { get; set; }
        public string? File3Path { get; set; }
        public bool File4Enabled { get; set; }
        public string? File4Path { get; set; }

        public static AppSettings CreateDefault() => new AppSettings();
    }
}