using System;
using System.Collections.Concurrent;
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

        private readonly VpnService _vpnService = new();
        private static readonly ConcurrentDictionary<string, (bool Result, DateTime Time)> RunCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);

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

        private readonly record struct SlotDescriptor(bool Enabled, string? Path, SlotKind Kind, int SlotIndex);

        public async Task<bool> TryLaunchTargetsWithRetryAsync(
            bool force,
            AppSettings settings,
            Action<string, string, ToolTipIcon> notify,
            Action<AppSettings>? saveSettings = null)
        {
            if (!force && !settings.AutoStartTeamsEnabled)
                return false;

            if (settings.StartVpnFirstEnabled)
            {
                bool vpnReady = await _vpnService.EnsureVpnConnectedAsync(settings, notify, saveSettings);
                if (!vpnReady)
                    return false;
            }

            var launchedNames = new List<string>();
            var failedNames = new List<string>();

            var slots = new[]
            {
                new SlotDescriptor(settings.File1Enabled, settings.File1Path, SlotKind.TeamsDefault, 1),
                new SlotDescriptor(settings.File2Enabled, settings.File2Path, SlotKind.OutlookDefault, 2),
                new SlotDescriptor(settings.File3Enabled, settings.File3Path, SlotKind.CustomOnly, 3),
                new SlotDescriptor(settings.File4Enabled, settings.File4Path, SlotKind.CustomOnly, 4)
            };

            for (int i = 0; i < slots.Length; i++)
            {
                bool delayAfter = slots.Skip(i + 1).Any(slot => slot.Enabled);
                await TryLaunchEnabledSlotAsync(slots[i], launchedNames, failedNames, delayAfter);
            }

            NotifyOutcome(failedNames, notify);
            return launchedNames.Count > 0;
        }

        private async Task TryLaunchEnabledSlotAsync(
            SlotDescriptor slot,
            List<string> launchedNames,
            List<string> failedNames,
            bool delayAfter)
        {
            if (!slot.Enabled)
                return;

            var result = await TryLaunchSlotWithRetryAsync(slot.Path, slot.Kind);
            string displayName = SettingsManager.GetSlotDisplayName(slot.SlotIndex, slot.Path, 100);

            if (result.Launched)
                launchedNames.Add(displayName);
            else if (result.Failed)
                failedNames.Add(displayName);

            if (delayAfter)
                await Task.Delay(InterAppLaunchDelayMs);
        }

        private static void NotifyOutcome(List<string> failedNames, Action<string, string, ToolTipIcon> notify)
        {
            if (failedNames.Count == 0)
                return;

            string failMsg = failedNames.Count == 1
                ? $"{failedNames[0]} failed to launch."
                : $"{failedNames.Count} files failed to launch.";

            notify("FileStarter", failMsg, ToolTipIcon.Error);
        }

        private async Task<LaunchAttemptResult> TryLaunchSlotWithRetryAsync(string? customPath, SlotKind kind)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
                return await TryLaunchCustomTargetWithRetryAsync(customPath);

            return kind switch
            {
                SlotKind.TeamsDefault => await TryLaunchDefaultAppWithRetryAsync("Teams", IsTeamsRunning, GetTeamsLaunchTargets()),
                SlotKind.OutlookDefault => await TryLaunchDefaultAppWithRetryAsync("Outlook", IsOutlookRunning, GetOutlookLaunchTargets()),
                _ => new LaunchAttemptResult(false, false)
            };
        }

        private async Task<LaunchAttemptResult> TryLaunchCustomTargetWithRetryAsync(string targetPath)
        {
            try
            {
                if (!File.Exists(targetPath))
                {
                    Logger.Other($"TryLaunchCustomTarget: file not found: {targetPath}");
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

                        Logger.Other($"TryLaunchCustomTarget: process did not appear after launch: {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Other($"TryLaunchCustomTarget: launch failed for {targetPath} (attempt {attempt}) => {ex.Message}");
                    }

                    if (attempt < MaxLaunchAttempts)
                        await Task.Delay(RetryDelayMs);
                }

                Logger.Other($"TryLaunchCustomTarget: all attempts failed for {targetPath}");
                return new LaunchAttemptResult(false, true);
            }
            catch (Exception ex)
            {
                Logger.Other($"TryLaunchCustomTarget: unexpected failure for {targetPath}", ex);
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

                    Logger.Other($"TryLaunchDefault{appDisplayName}: {appDisplayName} not detected after launch.");
                }

                if (attempt < MaxLaunchAttempts)
                    await Task.Delay(RetryDelayMs);
            }

            Logger.Other($"TryLaunchDefault{appDisplayName}: all launch attempts failed.", lastEx ?? new Exception($"Unknown {appDisplayName} error"));
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
                    Logger.Other($"TryStartTarget: failed target {target} => {ex.Message}");
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
            string? runningPath = null;

            try
            {
                var running = Process.GetProcesses()
                    .FirstOrDefault(p =>
                        string.Equals((p.ProcessName ?? string.Empty).Trim(), "OUTLOOK", StringComparison.OrdinalIgnoreCase) ||
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

            if (File.Exists(office16x64)) yield return office16x64;
            if (File.Exists(office16x86)) yield return office16x86;
            if (File.Exists(newOutlookAlias)) yield return newOutlookAlias;
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
                Logger.Other("IsTeamsRunning: failed; assuming not running.", ex);
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
                Logger.Other("IsOutlookRunning: failed; assuming not running.", ex);
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
                if (RunCache.TryGetValue(fullPath, out var cached) && DateTime.Now - cached.Time < CacheTtl)
                    return cached.Result;

                string wantedName = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                bool result = Process.GetProcessesByName(wantedName).Length > 0;
                RunCache[fullPath] = (result, DateTime.Now);
                return result;
            }
            catch (Exception ex)
            {
                Logger.Other("IsExecutableRunning failed.", ex);
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
    }
}