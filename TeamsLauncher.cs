
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        private static readonly TimeSpan VpnWaitWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan VpnStatusPollInterval = TimeSpan.FromSeconds(5);

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
                bool vpnReady = await EnsureVpnConnectedBeforeLaunchAsync(settings, notify, saveSettings);
                if (!vpnReady)
                    return false;
            }

            var launchedNames = new List<string>();
            var failedNames = new List<string>();

            await TryLaunchEnabledSlotAsync(settings.File1Enabled, settings.File1Path, SlotKind.TeamsDefault, 1, launchedNames, failedNames, settings.File2Enabled || settings.File3Enabled || settings.File4Enabled);
            await TryLaunchEnabledSlotAsync(settings.File2Enabled, settings.File2Path, SlotKind.OutlookDefault, 2, launchedNames, failedNames, settings.File3Enabled || settings.File4Enabled);
            await TryLaunchEnabledSlotAsync(settings.File3Enabled, settings.File3Path, SlotKind.CustomOnly, 3, launchedNames, failedNames, settings.File4Enabled);
            await TryLaunchEnabledSlotAsync(settings.File4Enabled, settings.File4Path, SlotKind.CustomOnly, 4, launchedNames, failedNames, false);

            NotifyOutcome(launchedNames, failedNames, notify);
            return launchedNames.Count > 0;
        }

        public bool TryLaunchTeams(
            bool force,
            AppSettings settings,
            Action<string, string, ToolTipIcon> notify,
            Action<AppSettings>? saveSettings = null)
            => TryLaunchTargetsWithRetryAsync(force, settings, notify, saveSettings).GetAwaiter().GetResult();

        public static List<string> GetAvailableVpnConnectionNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddVpnNamesFromPowerShell(result, allUserConnection: false);
            AddVpnNamesFromPowerShell(result, allUserConnection: true);

            if (result.Count == 0)
            {
                AddVpnNamesFromPhoneBook(result, GetUserPhoneBookPath());
                AddVpnNamesFromPhoneBook(result, GetAllUsersPhoneBookPath());
            }

            return result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<bool> EnsureVpnConnectedBeforeLaunchAsync(
            AppSettings settings,
            Action<string, string, ToolTipIcon> notify,
            Action<AppSettings>? saveSettings)
        {
            string vpnName = settings.VpnConnectionName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vpnName))
            {
                Logger.Warn("VPN start requested but no VPN connection is selected.");
                notify("FileStarter", "Start VPN first is enabled, but no VPN connection is selected.", ToolTipIcon.Warning);
                return false;
            }

            if (IsVpnConnected(vpnName))
                return true;

            if (!EnsureVpnProfileConfiguredForPptp(vpnName))
            {
                Logger.Warn($"EnsureVpnConnectedBeforeLaunchAsync: could not configure '{vpnName}' to PPTP before connecting.");
            }

            while (true)
            {
                TryStartVpnConnection(vpnName);

                bool connected = await WaitForVpnConnectionAsync(vpnName, VpnWaitWindow);
                if (connected)
                {
                    Logger.Change($"VPN connected: {vpnName}");
                    return true;
                }

                var result = MessageBox.Show(
                    $"The VPN connection '{vpnName}' can't be established yet.{Environment.NewLine}{Environment.NewLine}" +
                    "Click OK to wait 1 more minute and try again." + Environment.NewLine +
                    "Click Cancel to disable 'Start VPN first'.",
                    "VPN connection can't be established",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.OK)
                {
                    settings.StartVpnFirstEnabled = false;
                    settings.VpnConnectionName = null;
                    if (saveSettings != null)
                        saveSettings(settings);
                    else
                        SettingsManager.Save(settings);

                    Logger.Change("Start VPN first turned OFF");
                    notify("FileStarter", "Start VPN first was disabled because the VPN connection could not be established.", ToolTipIcon.Warning);
                    return false;
                }
            }
        }

        private static async Task<bool> WaitForVpnConnectionAsync(string vpnName, TimeSpan timeout)
        {
            DateTime deadline = DateTime.Now.Add(timeout);
            while (DateTime.Now < deadline)
            {
                if (IsVpnConnected(vpnName))
                    return true;

                TimeSpan remaining = deadline - DateTime.Now;
                TimeSpan delay = remaining < VpnStatusPollInterval ? remaining : VpnStatusPollInterval;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
            }

            return IsVpnConnected(vpnName);
        }

        private static bool EnsureVpnProfileConfiguredForPptp(string vpnName)
        {
            try
            {
                string command =
                    "$vpn = Get-VpnConnection -Name '{0}' -ErrorAction SilentlyContinue; " +
                    "if ($null -eq $vpn) {{ exit 2 }}; " +
                    "$needsUpdate = ($vpn.TunnelType -ne 'Pptp') -or (-not ($vpn.AuthenticationMethod -contains 'MsChapv2')) -or (-not $vpn.RememberCredential); " +
                    "if ($needsUpdate) {{ Set-VpnConnection -Name '{0}' -TunnelType Pptp -AuthenticationMethod MSChapv2 -RememberCredential $true -EncryptionLevel Optional -Force | Out-Null }}; " +
                    "$updated = Get-VpnConnection -Name '{0}' -ErrorAction SilentlyContinue; " +
                    "if (($null -ne $updated) -and ($updated.TunnelType -eq 'Pptp')) {{ exit 0 }} else {{ exit 1 }}";

                command = string.Format(command, EscapePowerShellSingleQuotedString(vpnName));

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteArgument(command),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Logger.Change($"VPN profile configured to PPTP: {vpnName}");
                    return true;
                }

                string details = (stdOut + " " + stdErr).Replace(Environment.NewLine, " ").Trim();
                Logger.Warn($"EnsureVpnProfileConfiguredForPptp: failed for '{vpnName}' (exit {process.ExitCode}) => {details}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"EnsureVpnProfileConfiguredForPptp: failed for '{vpnName}' => {ex.Message}");
                return false;
            }
        }

        
        private static void TryStartVpnConnection(string vpnName)
        {
            try
            {
                // For per-user VPNs, null usually uses the default phone-book.
                // If you want to be explicit, pass:
                // %AppData%\Microsoft\Network\Connections\Pbk\rasphone.pbk
                var stored = RasCredentialHelper.GetStoredCredentials(null, vpnName);

                string args;

                if (stored != null && !string.IsNullOrWhiteSpace(stored.UserName))
                {
                    // IMPORTANT:
                    // stored.PasswordHandle is NOT the actual password.
                    // Microsoft says to pass it directly to RasDial.
                    if (!string.IsNullOrWhiteSpace(stored.Domain))
                    {
                        args = $"\"{vpnName}\" \"{stored.UserName}\" \"{stored.PasswordHandle}\" /domain:{stored.Domain}";
                    }
                    else
                    {
                        args = $"\"{vpnName}\" \"{stored.UserName}\" \"{stored.PasswordHandle}\"";
                    }

                    Logger.Change($"Using stored RAS credentials for VPN: {vpnName}");
                }
                else
                {
                    Logger.Warn($"No stored RAS credentials found for VPN entry: {vpnName}");
                    args = $"\"{vpnName}\"";
                }

                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "rasdial.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string details = (stdOut + " " + stdErr).Replace(Environment.NewLine, " ").Trim();
                    Logger.Warn($"TryStartVpnConnection: rasdial returned {process.ExitCode} for '{vpnName}' => {details}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"TryStartVpnConnection: failed for '{vpnName}' => {ex.Message}");
            }
        }

        private async Task TryLaunchEnabledSlotAsync(
            bool enabled,
            string? path,
            SlotKind kind,
            int slotIndex,
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

        private static void NotifyOutcome(List<string> launchedNames, List<string> failedNames, Action<string, string, ToolTipIcon> notify)
        {
            if (failedNames.Count > 0)
            {
                string failMsg = failedNames.Count == 1
                    ? $"{failedNames[0]} failed to launch."
                    : $"{failedNames.Count} files failed to launch.";
                notify("FileStarter", failMsg, ToolTipIcon.Error);
                return;
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

        private static bool IsVpnConnected(string vpnName)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                                    QuoteArgument($"(Get-VpnConnection -Name '{EscapePowerShellSingleQuotedString(vpnName)}' -ErrorAction SilentlyContinue).ConnectionStatus"),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string stdOut = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return string.Equals(stdOut, "Connected", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Warn($"IsVpnConnected: failed to query VPN status for '{vpnName}' => {ex.Message}");
                return false;
            }
        }

        private static void AddVpnNamesFromPowerShell(HashSet<string> result, bool allUserConnection)
        {
            try
            {
                string command = allUserConnection
                    ? "Get-VpnConnection -AllUserConnection -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name"
                    : "Get-VpnConnection -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name";

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteArgument(command),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string stdOut = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (var line in stdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = line.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        result.Add(name);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"AddVpnNamesFromPowerShell: failed => {ex.Message}");
            }
        }

        private static void AddVpnNamesFromPhoneBook(HashSet<string> result, string phoneBookPath)
        {
            try
            {
                if (!File.Exists(phoneBookPath))
                    return;

                foreach (var line in File.ReadLines(phoneBookPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']')
                    {
                        string name = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            result.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"AddVpnNamesFromPhoneBook: failed for {phoneBookPath} => {ex.Message}");
            }
        }

        private static string GetUserPhoneBookPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk");

        private static string GetAllUsersPhoneBookPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk");

        private static string QuoteArgument(string value)
            => '"' + (value ?? string.Empty).Replace("\"", "\\\"") + '"';

        private static string EscapePowerShellSingleQuotedString(string value)
            => (value ?? string.Empty).Replace("'", "''");

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