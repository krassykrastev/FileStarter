
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

        public async Task<bool> TryLaunchTargetsWithRetryAsync(bool force, AppSettings settings, Action<string, string, ToolTipIcon> notify)
        {
            if (!force && !settings.AutoStartTeamsEnabled)
            {
                Logger.Info("TryLaunch: auto-start disabled; skipping.");
                return false;
            }

            var launchedNames = new List<string>();
            bool hasAnyLaterEnabled;

            if (settings.File1Enabled)
            {
                if (await TryLaunchSlotWithRetryAsync(settings.File1Path, SlotKind.TeamsDefault))
                {
                    launchedNames.Add(SettingsManager.GetDisplayNameFromPath(settings.File1Path, "MS Teams", 100));
                }

                hasAnyLaterEnabled = settings.File2Enabled || settings.File3Enabled || settings.File4Enabled;
                if (hasAnyLaterEnabled)
                    await Task.Delay(InterAppLaunchDelayMs);
            }

            if (settings.File2Enabled)
            {
                if (await TryLaunchSlotWithRetryAsync(settings.File2Path, SlotKind.OutlookDefault))
                {
                    launchedNames.Add(SettingsManager.GetDisplayNameFromPath(settings.File2Path, "MS Outlook", 100));
                }

                hasAnyLaterEnabled = settings.File3Enabled || settings.File4Enabled;
                if (hasAnyLaterEnabled)
                    await Task.Delay(InterAppLaunchDelayMs);
            }

            if (settings.File3Enabled)
            {
                if (await TryLaunchSlotWithRetryAsync(settings.File3Path, SlotKind.CustomOnly))
                {
                    launchedNames.Add(SettingsManager.GetDisplayNameFromPath(settings.File3Path, "File 3", 100));
                }

                hasAnyLaterEnabled = settings.File4Enabled;
                if (hasAnyLaterEnabled)
                    await Task.Delay(InterAppLaunchDelayMs);
            }

            if (settings.File4Enabled)
            {
                if (await TryLaunchSlotWithRetryAsync(settings.File4Path, SlotKind.CustomOnly))
                {
                    launchedNames.Add(SettingsManager.GetDisplayNameFromPath(settings.File4Path, "File 4", 100));
                }
            }

            if (launchedNames.Count > 0)
            {
                Logger.Info($"TryLaunch: launched {launchedNames.Count} item(s).");

                if (settings.EnableDesktopNotifications)
                {
                    string msg = launchedNames.Count == 1
                        ? $"{launchedNames[0]} launched."
                        : $"{launchedNames.Count} files launched.";

                    notify("FileStarter", msg, ToolTipIcon.Info);
                }

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

        private async Task<bool> TryLaunchSlotWithRetryAsync(string? customPath, SlotKind kind)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
                return await TryLaunchCustomTargetWithRetryAsync(customPath);

            return kind switch
            {
                SlotKind.TeamsDefault => await TryLaunchDefaultTeamsWithRetryAsync(),
                SlotKind.OutlookDefault => await TryLaunchDefaultOutlookWithRetryAsync(),
                SlotKind.CustomOnly => false,
                _ => false
            };
        }

        private async Task<bool> TryLaunchCustomTargetWithRetryAsync(string targetPath)
        {
            try
            {
                if (!File.Exists(targetPath))
                {
                    Logger.Warn($"TryLaunchCustomTarget: file not found: {targetPath}");
                    return false;
                }

                bool isExe = string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase);

                for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
                {
                    if (isExe && IsExecutableRunning(targetPath))
                    {
                        Logger.Info($"TryLaunchCustomTarget: already running: {targetPath}");
                        return false;
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
                                return true;
                            }

                            Logger.Warn($"TryLaunchCustomTarget: process did not appear after launch: {targetPath}");
                        }
                        else
                        {
                            return true;
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
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"TryLaunchCustomTarget: unexpected failure for {targetPath}", ex);
                return false;
            }
        }

        private async Task<bool> TryLaunchDefaultTeamsWithRetryAsync()
        {
            Exception? lastEx = null;

            for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
            {
                if (IsTeamsRunning())
                {
                    Logger.Info("TryLaunchDefaultTeams: Teams already running.");
                    return false;
                }

                bool launchIssued = false;

                foreach (var target in TeamsLocator.GetLaunchTargets())
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
                        return true;
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
            return false;
        }

        private async Task<bool> TryLaunchDefaultOutlookWithRetryAsync()
        {
            Exception? lastEx = null;

            for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
            {
                if (IsOutlookRunning())
                {
                    Logger.Info("TryLaunchDefaultOutlook: Outlook already running.");
                    return false;
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
                        return true;
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
            return false;
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
                string wanted = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                int currentPid = Process.GetCurrentProcess().Id;

                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == currentPid) continue;

                        var name = (p.ProcessName ?? "").Trim().ToLowerInvariant();
                        if (name == wanted)
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
                Logger.Error("IsExecutableRunning: failed; assuming not running.", ex);
                return false;
            }
        }
    }
}