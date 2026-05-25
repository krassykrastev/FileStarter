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

        public async Task<bool> TryLaunchTargetsWithRetryAsync(bool force, AppSettings settings, Action<string, string, ToolTipIcon> notify)
        {
            if (!force && !settings.AutoStartTeamsEnabled)
            {
                Logger.Info("TryLaunch: auto-start disabled; skipping.");
                return false;
            }

            var launchedNames = new List<string>();
            var failedNames = new List<string>();
            bool hasAnyLaterEnabled;

            if (settings.File1Enabled)
            {
                var result = await TryLaunchSlotWithRetryAsync(settings.File1Path, SlotKind.TeamsDefault);
                if (result.Launched)
                {
                    launchedNames.Add(SettingsManager.GetSlotDisplayName(1, settings.File1Path, 100));
                }
                else if (result.Failed)
                {
                    failedNames.Add(SettingsManager.GetSlotDisplayName(1, settings.File1Path, 100));
                }

                hasAnyLaterEnabled = settings.File2Enabled || settings.File3Enabled || settings.File4Enabled;
                if (hasAnyLaterEnabled)
                    await Task.Delay(InterAppLaunchDelayMs);
            }

            if (settings.File2Enabled)
            {
                var result = await TryLaunchSlotWithRetryAsync(settings.File2Path, SlotKind.OutlookDefault);
                if (result.Launched)
                {
                    launchedNames.Add(SettingsManager.GetSlotDisplayName(2, settings.File2Path, 100));
                }
                else if (result.Failed)
                {
                    failedNames.Add(SettingsManager.GetSlotDisplayName(2, settings.File2Path, 100));
                }

                hasAnyLaterEnabled = settings.File3Enabled || settings.File4Enabled;
                if (hasAnyLaterEnabled)
                    await Task.Delay(InterAppLaunchDelayMs);
            }

            if (settings.File3Enabled)
            {
                var result = await TryLaunchSlotWithRetryAsync(settings.File3Path, SlotKind.CustomOnly);
                if (result.Launched)
                {
                    launchedNames.Add(SettingsManager.GetSlotDisplayName(3, settings.File3Path, 100));
                }
                else if (result.Failed)
                {
                    failedNames.Add(SettingsManager.GetSlotDisplayName(3, settings.File3Path, 100));
                }

                hasAnyLaterEnabled = settings.File4Enabled;
                if (hasAnyLaterEnabled)
                    await Task.Delay(InterAppLaunchDelayMs);
            }

            if (settings.File4Enabled)
            {
                var result = await TryLaunchSlotWithRetryAsync(settings.File4Path, SlotKind.CustomOnly);
                if (result.Launched)
                {
                    launchedNames.Add(SettingsManager.GetSlotDisplayName(4, settings.File4Path, 100));
                }
                else if (result.Failed)
                {
                    failedNames.Add(SettingsManager.GetSlotDisplayName(4, settings.File4Path, 100));
                }
            }

            if (failedNames.Count > 0)
            {
                string failMsg = failedNames.Count == 1
                    ? $"{failedNames[0]} failed to launch."
                    : $"{failedNames.Count} files failed to launch.";

                notify("FileStarter", failMsg, ToolTipIcon.Error);
            }
            else if (launchedNames.Count > 0 && settings.EnableDesktopNotifications)
            {
                string msg = launchedNames.Count == 1
                    ? $"{launchedNames[0]} launched."
                    : $"{launchedNames.Count} files launched.";

                notify("FileStarter", msg, ToolTipIcon.Info);
            }

            if (launchedNames.Count > 0)
            {
                Logger.Info($"TryLaunch: launched {launchedNames.Count} item(s).");
                return true;
            }

            return false;
        }

        public bool TryLaunchTeams(bool force, AppSettings settings, Action<string, string, ToolTipIcon> notify)
        {
            return TryLaunchTargetsWithRetryAsync(force, settings, notify).GetAwaiter().GetResult();
        }

        private enum SlotKind
        {
            TeamsDefault,
            OutlookDefault,
            CustomOnly
        }

        private async Task<LaunchAttemptResult> TryLaunchSlotWithRetryAsync(string? customPath, SlotKind kind)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
                return await TryLaunchCustomTargetWithRetryAsync(customPath);

            return kind switch
            {
                SlotKind.TeamsDefault => await TryLaunchDefaultTeamsWithRetryAsync(),
                SlotKind.OutlookDefault => await TryLaunchDefaultOutlookWithRetryAsync(),
                SlotKind.CustomOnly => new LaunchAttemptResult(false, false),
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
                    {
                        Logger.Info($"TryLaunchCustomTarget: already running: {targetPath}");
                        return new LaunchAttemptResult(false, false);
                    }

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetPath,
                            UseShellExecute = true
                        });

                        Logger.Info($"TryLaunchCustomTarget: launch issued for {targetPath} (attempt {attempt}).");

                        if (isExe)
                        {
                            await Task.Delay(PostLaunchVerifyDelayMs);
                            if (IsExecutableRunning(targetPath))
                            {
                                Logger.Info($"TryLaunchCustomTarget: verified running: {targetPath}");
                                return new LaunchAttemptResult(true, false);
                            }
                            Logger.Warn($"TryLaunchCustomTarget: process did not appear after launch: {targetPath}");
                        }
                        else
                        {
                            return new LaunchAttemptResult(true, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"TryLaunchCustomTarget: launch failed for {targetPath} (attempt {attempt}) => {ex.Message}");
                    }

                    if (attempt < MaxLaunchAttempts)
                    {
                        Logger.Info($"TryLaunchCustomTarget: retrying {targetPath} after {RetryDelayMs} ms.");
                        await Task.Delay(RetryDelayMs);
                    }
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

        private async Task<LaunchAttemptResult> TryLaunchDefaultTeamsWithRetryAsync()
        {
            Exception? lastEx = null;
            for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
            {
                if (IsTeamsRunning())
                {
                    Logger.Info("TryLaunchDefaultTeams: Teams already running.");
                    return new LaunchAttemptResult(false, false);
                }

                bool launchIssued = false;
                foreach (var target in GetTeamsLaunchTargets())
                {
                    try
                    {
                        Logger.Info($"TryLaunchDefaultTeams: using {target} (attempt {attempt}).");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = target,
                            UseShellExecute = true
                        });
                        launchIssued = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Logger.Warn($"TryLaunchDefaultTeams: failed target {target} => {ex.Message}");
                    }
                }

                if (launchIssued)
                {
                    await Task.Delay(PostLaunchVerifyDelayMs);
                    if (IsTeamsRunning())
                    {
                        Logger.Info("TryLaunchDefaultTeams: Teams verified as running.");
                        return new LaunchAttemptResult(true, false);
                    }
                    Logger.Warn("TryLaunchDefaultTeams: Teams not detected after launch.");
                }

                if (attempt < MaxLaunchAttempts)
                {
                    Logger.Info($"TryLaunchDefaultTeams: retrying after {RetryDelayMs} ms.");
                    await Task.Delay(RetryDelayMs);
                }
            }

            Logger.Error("TryLaunchDefaultTeams: all launch attempts failed.", lastEx ?? new Exception("Unknown Teams error"));
            return new LaunchAttemptResult(false, true);
        }

        private async Task<LaunchAttemptResult> TryLaunchDefaultOutlookWithRetryAsync()
        {
            Exception? lastEx = null;
            for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
            {
                if (IsOutlookRunning())
                {
                    Logger.Info("TryLaunchDefaultOutlook: Outlook already running.");
                    return new LaunchAttemptResult(false, false);
                }

                bool launchIssued = false;
                foreach (var target in GetOutlookLaunchTargets())
                {
                    try
                    {
                        Logger.Info($"TryLaunchDefaultOutlook: using {target} (attempt {attempt}).");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = target,
                            UseShellExecute = true
                        });
                        launchIssued = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Logger.Warn($"TryLaunchDefaultOutlook: failed target {target} => {ex.Message}");
                    }
                }

                if (launchIssued)
                {
                    await Task.Delay(PostLaunchVerifyDelayMs);
                    if (IsOutlookRunning())
                    {
                        Logger.Info("TryLaunchDefaultOutlook: Outlook verified as running.");
                        return new LaunchAttemptResult(true, false);
                    }
                    Logger.Warn("TryLaunchDefaultOutlook: Outlook not detected after launch.");
                }

                if (attempt < MaxLaunchAttempts)
                {
                    Logger.Info($"TryLaunchDefaultOutlook: retrying after {RetryDelayMs} ms.");
                    await Task.Delay(RetryDelayMs);
                }
            }

            Logger.Error("TryLaunchDefaultOutlook: all launch attempts failed.", lastEx ?? new Exception("Unknown Outlook error"));
            return new LaunchAttemptResult(false, true);
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

            if (File.Exists(newOutlookAlias)) yield return newOutlookAlias;
            if (File.Exists(office16x64)) yield return office16x64;
            if (File.Exists(office16x86)) yield return office16x86;
            yield return "outlook.exe";
            yield return "outlook:";
        }

        public static bool IsTeamsRunning()
        {
            try
            {
                string[] teamsProcessNames = { "teams", "ms-teams" };
                int currentPid = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == currentPid) continue;
                        var name = (p.ProcessName ?? "").Trim().ToLowerInvariant();
                        if (teamsProcessNames.Contains(name))
                            return true;
                    }
                    catch
                    {
                    }
                }
                return false;
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
                string[] processNames = { "outlook", "olk" };
                int currentPid = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == currentPid) continue;
                        var name = (p.ProcessName ?? "").Trim().ToLowerInvariant();
                        if (processNames.Contains(name))
                            return true;
                    }
                    catch
                    {
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("IsOutlookRunning: failed; assuming not running.", ex);
                return false;
            }
        }

        private static bool IsExecutableRunning(string fullPath)
        {
            try
            {
                string wantedName = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                string wantedPath = NormalizePath(fullPath);
                int currentPid = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == currentPid)
                            continue;
                        var name = (p.ProcessName ?? "").Trim().ToLowerInvariant();
                        if (name != wantedName)
                            continue;
                        string? processPath = TryGetProcessPath(p);
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