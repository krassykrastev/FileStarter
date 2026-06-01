using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class TeamsLauncher
    {
        private const int MaxLaunchAttempts = 2;
        private const int PostLaunchVerifyDelayMs = 3000;
        private const int RetryDelayMs = 8000;
        private const int InterAppLaunchDelayMs = 10000;

        private readonly struct LaunchAttemptResult
        {
            public LaunchAttemptResult(bool launched, bool failed)
            {
                Launched = launched;
                Failed = failed;
            }

            public bool Launched { get; }
            public bool Failed { get; }
        }

        private enum SlotKind
        {
            TeamsDefault,
            OutlookDefault,
            CustomOnly
        }

        public async Task<bool> TryLaunchTargetsWithRetryAsync(bool force, AppSettings settings, Action<string, string, ToolTipIcon> notify)
        {
            if (!force && !settings.AutoStartTeamsEnabled)
                return false;

            var launchedNames = new List<string>();
            var failedNames = new List<string>();

            await TryLaunchEnabledSlotAsync(settings.File1Enabled, settings.File1Path, SlotKind.TeamsDefault, 1, settings, launchedNames, failedNames, settings.File2Enabled || settings.File3Enabled || settings.File4Enabled);
            await TryLaunchEnabledSlotAsync(settings.File2Enabled, settings.File2Path, SlotKind.OutlookDefault, 2, settings, launchedNames, failedNames, settings.File3Enabled || settings.File4Enabled);
            await TryLaunchEnabledSlotAsync(settings.File3Enabled, settings.File3Path, SlotKind.CustomOnly, 3, settings, launchedNames, failedNames, settings.File4Enabled);
            await TryLaunchEnabledSlotAsync(settings.File4Enabled, settings.File4Path, SlotKind.CustomOnly, 4, settings, launchedNames, failedNames, false);

            NotifyOutcome(launchedNames, failedNames, settings.EnableDesktopNotifications, notify);
            return launchedNames.Count > 0;
        }

        public bool TryLaunchTeams(bool force, AppSettings settings, Action<string, string, ToolTipIcon> notify)
            => TryLaunchTargetsWithRetryAsync(force, settings, notify).GetAwaiter().GetResult();

        private async Task TryLaunchEnabledSlotAsync(
            bool enabled,
            string? path,
            SlotKind kind,
            int slotIndex,
            AppSettings settings,
            List<string> launchedNames,
            List<string> failedNames,
            bool delayAfter)
        {
            if (!enabled)
                return;

            var result = await TryLaunchSlotWithRetryAsync(path, kind);
            string displayName = SettingsManager.GetSlotDisplayName(slotIndex, path, 100);

            if (result.Launched)
                launchedNames.Add(displayName);
            else if (result.Failed)
                failedNames.Add(displayName);

            if (delayAfter)
                await Task.Delay(InterAppLaunchDelayMs);
        }

        private static void NotifyOutcome(List<string> launchedNames, List<string> failedNames, bool desktopNotificationsEnabled, Action<string, string, ToolTipIcon> notify)
        {
            if (failedNames.Count > 0)
            {
                string failMsg = failedNames.Count == 1
                    ? $"{failedNames[0]} failed to launch."
                    : $"{failedNames.Count} files failed to launch.";
                notify("FileStarter", failMsg, ToolTipIcon.Error);
                return;
            }

            if (launchedNames.Count > 0 && desktopNotificationsEnabled)
            {
                string msg = launchedNames.Count == 1
                    ? $"{launchedNames[0]} launched."
                    : $"{launchedNames.Count} files launched.";
                notify("FileStarter", msg, ToolTipIcon.Info);
            }
        }

        private async Task<LaunchAttemptResult> TryLaunchSlotWithRetryAsync(string? customPath, SlotKind kind)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
                return await TryLaunchCustomTargetWithRetryAsync(customPath);

            return kind switch
            {
                SlotKind.TeamsDefault => await TryLaunchDefaultAppWithRetryAsync(
                    "Teams",
                    IsTeamsRunning,
                    GetTeamsLaunchTargets()),
                SlotKind.OutlookDefault => await TryLaunchDefaultAppWithRetryAsync(
                    "Outlook",
                    IsOutlookRunning,
                    GetOutlookLaunchTargets()),
                _ => new LaunchAttemptResult(false, false)
            };
        }

        private async Task<LaunchAttemptResult> TryLaunchCustomTargetWithRetryAsync(string targetPath)
        {
            try
            {
                if (!File.Exists(targetPath))
                {
                    Logger.Warn($"TryLaunchCustomTarget: file not found: {targetPath}");
                    return new LaunchAttemptResult(false, true);
                }

                bool isExe = string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase);
                for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
                {
                    if (isExe && IsExecutableRunning(targetPath))
                        return new LaunchAttemptResult(false, false);

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetPath,
                            UseShellExecute = true
                        });

                        if (!isExe)
                            return new LaunchAttemptResult(true, false);

                        await Task.Delay(PostLaunchVerifyDelayMs);
                        if (IsExecutableRunning(targetPath))
                            return new LaunchAttemptResult(true, false);

                        Logger.Warn($"TryLaunchCustomTarget: process did not appear after launch: {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"TryLaunchCustomTarget: launch failed for {targetPath} (attempt {attempt}) => {ex.Message}");
                    }

                    if (attempt < MaxLaunchAttempts)
                        await Task.Delay(RetryDelayMs);
                }

                Logger.Warn($"TryLaunchCustomTarget: all attempts failed for {targetPath}");
                return new LaunchAttemptResult(false, true);
            }
            catch (Exception ex)
            {
                Logger.Error($"TryLaunchCustomTarget: unexpected failure for {targetPath}", ex);
                return new LaunchAttemptResult(false, true);
            }
        }

        private async Task<LaunchAttemptResult> TryLaunchDefaultAppWithRetryAsync(
            string appDisplayName,
            Func<bool> isRunning,
            IEnumerable<string> launchTargets)
        {
            Exception? lastEx = null;

            for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
            {
                if (isRunning())
                    return new LaunchAttemptResult(false, false);

                if (TryStartFirstAvailableTarget(launchTargets, out lastEx))
                {
                    await Task.Delay(PostLaunchVerifyDelayMs);
                    if (isRunning())
                        return new LaunchAttemptResult(true, false);

                    Logger.Warn($"TryLaunchDefault{appDisplayName}: {appDisplayName} not detected after launch.");
                }

                if (attempt < MaxLaunchAttempts)
                    await Task.Delay(RetryDelayMs);
            }

            Logger.Error($"TryLaunchDefault{appDisplayName}: all launch attempts failed.", lastEx ?? new Exception($"Unknown {appDisplayName} error"));
            return new LaunchAttemptResult(false, true);
        }

        private static bool TryStartFirstAvailableTarget(IEnumerable<string> targets, out Exception? lastEx)
        {
            lastEx = null;

            foreach (var target in targets)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Logger.Warn($"TryStartTarget: failed target {target} => {ex.Message}");
                }
            }

            return false;
        }

        private static IEnumerable<string> GetTeamsLaunchTargets()
        {
            yield return "msteams:";
            yield return "ms-teams:";

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string newTeamsAlias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "ms-teams.exe");
            string classicPerUser = Path.Combine(localAppData, "Microsoft", "Teams", "current", "Teams.exe");
            string classicMachine = Path.Combine(programFilesX86, "Microsoft", "Teams", "current", "Teams.exe");

            if (File.Exists(newTeamsAlias)) yield return newTeamsAlias;
            if (File.Exists(classicPerUser)) yield return classicPerUser;
            if (File.Exists(classicMachine)) yield return classicMachine;
        }

        private static IEnumerable<string> GetOutlookLaunchTargets()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string newOutlookAlias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "olk.exe");
            string office16x64 = Path.Combine(programFiles, "Microsoft Office", "root", "Office16", "OUTLOOK.EXE");
            string office16x86 = Path.Combine(programFilesX86, "Microsoft Office", "root", "Office16", "OUTLOOK.EXE");

            // Prefer a running Outlook instance's executable path first (user is actively using it).
            string? runningPath = null;
            try
            {
                var running = Process.GetProcesses()
                    .FirstOrDefault(p => string.Equals((p.ProcessName ?? string.Empty).Trim(), "OUTLOOK", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals((p.ProcessName ?? string.Empty).Trim(), "olk", StringComparison.OrdinalIgnoreCase));
                if (running != null)
                    runningPath = TryGetProcessPath(running);
            }
            catch
            {
                // ignore and continue to fallback logic
            }

            if (!string.IsNullOrWhiteSpace(runningPath) && File.Exists(runningPath))
            {
                yield return runningPath;
                yield break;
            }

            // When not running, prefer classic Office (older Outlook) over new AppX Outlook.
            // Users explicitly installing classic Office indicates they prefer it.
            if (File.Exists(office16x64)) yield return office16x64;
            if (File.Exists(office16x86)) yield return office16x86;
            
            // Fall back to new Outlook only if classic is not found.
            if (File.Exists(newOutlookAlias)) yield return newOutlookAlias;
            
            // Generic handlers as last resort.
            yield return "outlook.exe";
            yield return "outlook:";
        }

        public static bool IsTeamsRunning()
        {
            try
            {
                return IsAnyProcessRunning(new[] { "teams", "ms-teams" });
            }
            catch (Exception ex)
            {
                Logger.Error("IsTeamsRunning: failed; assuming not running.", ex);
                return false;
            }
        }

        private static bool IsOutlookRunning()
        {
            try
            {
                return IsAnyProcessRunning(new[] { "outlook", "olk" });
            }
            catch (Exception ex)
            {
                Logger.Error("IsOutlookRunning: failed; assuming not running.", ex);
                return false;
            }
        }

        private static bool IsAnyProcessRunning(IEnumerable<string> processNames)
        {
            var wanted = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
            int currentPid = Process.GetCurrentProcess().Id;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id != currentPid && wanted.Contains((process.ProcessName ?? string.Empty).Trim()))
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool IsExecutableRunning(string fullPath)
        {
            try
            {
                string wantedName = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                string wantedPath = NormalizePath(fullPath);
                int currentPid = Process.GetCurrentProcess().Id;

                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id == currentPid)
                            continue;

                        var processName = (process.ProcessName ?? string.Empty).Trim().ToLowerInvariant();
                        if (processName != wantedName)
                            continue;

                        string? processPath = TryGetProcessPath(process);
                        if (!string.IsNullOrWhiteSpace(processPath) &&
                            string.Equals(NormalizePath(processPath), wantedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("IsExecutableRunning: failed; assuming not running.", ex);
                return false;
            }
        }

        private static string? TryGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path)
                .Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}