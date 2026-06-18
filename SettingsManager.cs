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
            return nowLocal >= todayLaunch;
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

            AddChange(changes, "Run at Windows startup", before.RunAppAtStartup, after.RunAppAtStartup);
            AddChange(changes, "Start VPN first", before.StartVpnFirstEnabled, after.StartVpnFirstEnabled);
            AddChange(changes, "VPN connection name", before.VpnConnectionName, after.VpnConnectionName);
            AddChange(changes, "Monday", before.Mon, after.Mon);
            AddChange(changes, "Tuesday", before.Tue, after.Tue);
            AddChange(changes, "Wednesday", before.Wed, after.Wed);
            AddChange(changes, "Thursday", before.Thu, after.Thu);
            AddChange(changes, "Friday", before.Fri, after.Fri);
            AddChange(changes, "Saturday", before.Sat, after.Sat);
            AddChange(changes, "Sunday", before.Sun, after.Sun);
            AddChange(changes, "Turn auto-start OFF from this date", before.AutoStartOffFromDate, after.AutoStartOffFromDate);
            AddChange(changes, "Turn auto-start OFF until this date", before.AutoStartOffUntilDate, after.AutoStartOffUntilDate);
            AddChange(changes, "File 1 enabled", before.File1Enabled, after.File1Enabled);
            AddChange(changes, "File 1 path", before.File1Path, after.File1Path);
            AddChange(changes, "File 2 enabled", before.File2Enabled, after.File2Enabled);
            AddChange(changes, "File 2 path", before.File2Path, after.File2Path);
            AddChange(changes, "File 3 enabled", before.File3Enabled, after.File3Enabled);
            AddChange(changes, "File 3 path", before.File3Path, after.File3Path);
            AddChange(changes, "File 4 enabled", before.File4Enabled, after.File4Enabled);
            AddChange(changes, "File 4 path", before.File4Path, after.File4Path);

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

        private static void AddChange(List<string> changes, string label, bool before, bool after)
        {
            if (before != after)
                changes.Add($"{label}: {before} -> {after}");
        }

        private static void AddChange(List<string> changes, string label, string? before, string? after)
        {
            string normalizedBefore = NormalizeDisplay(before);
            string normalizedAfter = NormalizeDisplay(after);
            if (!string.Equals(normalizedBefore, normalizedAfter, StringComparison.OrdinalIgnoreCase))
                changes.Add($"{label}: {normalizedBefore} -> {normalizedAfter}");
        }

        private static void AddChange(List<string> changes, string label, DateTime? before, DateTime? after)
        {
            string normalizedBefore = before?.ToString("yyyy-MM-dd") ?? "<none>";
            string normalizedAfter = after?.ToString("yyyy-MM-dd") ?? "<none>";
            if (!string.Equals(normalizedBefore, normalizedAfter, StringComparison.OrdinalIgnoreCase))
                changes.Add($"{label}: {normalizedBefore} -> {normalizedAfter}");
        }

        private static void AddChange(List<string> changes, string label, DayLaunchSetting before, DayLaunchSetting after)
        {
            if (HasDayChange(before, after))
            {
                changes.Add($"{label}: Enabled={before.Enabled}, Time={NormalizeDisplay(before.Time)} -> Enabled={after.Enabled}, Time={NormalizeDisplay(after.Time)}");
            }
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