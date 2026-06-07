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
    internal sealed class VpnService
    {
        private static readonly TimeSpan VpnWaitWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan VpnStatusPollInterval = TimeSpan.FromSeconds(5);
        private const int SilentStartupRetryAttempts = 5;

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

        public async Task<bool> EnsureVpnConnectedAsync(
            AppSettings settings,
            Action<string, string, ToolTipIcon> notify,
            Action<AppSettings>? saveSettings,
            bool allowUserPrompt = true)
        {
            try
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

                string? vpnType = GetVpnType(vpnName);
                if (!string.IsNullOrWhiteSpace(vpnType))
                {
                    Logger.Change($"VPN type detected: {vpnName} = {vpnType}");
                }

                if (!allowUserPrompt)
                {
                    for (int attempt = 1; attempt <= SilentStartupRetryAttempts; attempt++)
                    {
                        TryStartVpnConnection(vpnName);

                        bool connected = await WaitForVpnConnectionAsync(vpnName, VpnWaitWindow);
                        if (connected)
                        {
                            Logger.Change($"VPN connected: {vpnName}");
                            return true;
                        }
                    }

                    Logger.Error(
                        $"VPN connection failed after {SilentStartupRetryAttempts} silent startup attempts for '{vpnName}'.",
                        new InvalidOperationException("Silent startup VPN retry limit reached."));
                    notify("FileStarter", "VPN failed to connect after 5 silent retries.", ToolTipIcon.Warning);
                    return false;
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
            catch (Exception ex)
            {
                Logger.Error("VPN connection failed.", ex);
                notify("FileStarter", "VPN failed to connect.", ToolTipIcon.Warning);
                return false;
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

        private static void TryStartVpnConnection(string vpnName)
        {
            try
            {
                var stored = RasCredentialHelper.GetStoredCredentials(null, vpnName);
                string args;

                if (stored != null && !string.IsNullOrWhiteSpace(stored.UserName))
                {
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

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
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

        private static string? GetVpnType(string vpnName)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                                    QuoteArgument($"(Get-VpnConnection -Name '{EscapePowerShellSingleQuotedString(vpnName)}' -ErrorAction SilentlyContinue).TunnelType"),
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

                return string.IsNullOrWhiteSpace(stdOut) ? null : stdOut;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GetVpnType: failed to query VPN type for '{vpnName}' => {ex.Message}");
                return null;
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
    }
}