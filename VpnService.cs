using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private static readonly Dictionary<string, string?> _vpnTypeCache = new(StringComparer.OrdinalIgnoreCase);

        private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, bool utf8 = false)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = utf8 ? Encoding.UTF8 : null,
                    StandardErrorEncoding = utf8 ? Encoding.UTF8 : null
                }
            };

            process.Start();
            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdOut, stdErr);
        }

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

        public static bool DisconnectVpn(string vpnName)
        {
            try
            {
                vpnName = vpnName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(vpnName))
                    return false;

                if (!IsVpnConnected(vpnName))
                {
                    Logger.Change($"VPN already disconnected: {vpnName}");
                    return true;
                }

                var result = RunProcess("rasdial.exe", $"\"{vpnName}\" /disconnect");
                if (result.ExitCode == 0)
                {
                    Logger.Change($"VPN disconnected: {vpnName}");
                    return true;
                }

                string details = (result.StdOut + " " + result.StdErr)
                    .Replace(Environment.NewLine, " ")
                    .Trim();
                Logger.Other($"VPN disconnect failed for '{vpnName}'. rasdial returned {result.ExitCode} => {details}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Other($"VPN disconnect failed for '{vpnName}'.", ex);
                return false;
            }
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
                    Logger.Other("VPN start requested but no VPN connection is selected.");
                    notify("FileStarter", "Start VPN first & reconnect on drops is enabled, but no VPN connection is selected.", ToolTipIcon.Warning);
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

                    Logger.Other(
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
                        "Click Cancel to disable 'Start VPN first & reconnect on drops'.",
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

                        Logger.Change("Start VPN first & reconnect on drops disabled");
                        notify("FileStarter", "Start VPN first & reconnect on drops was disabled because the VPN connection could not be established.", ToolTipIcon.Warning);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Other("VPN connection failed.", ex);
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
                    Logger.Other($"No stored RAS credentials found for VPN entry: {vpnName}");
                    args = $"\"{vpnName}\"";
                }

                var result = RunProcess("rasdial.exe", args);
                if (result.ExitCode != 0)
                {
                    string details = (result.StdOut + " " + result.StdErr)
                        .Replace(Environment.NewLine, " ")
                        .Trim();
                    Logger.Other($"TryStartVpnConnection: rasdial returned {result.ExitCode} => {details}");
                }
            }
            catch (Exception ex)
            {
                Logger.Other($"TryStartVpnConnection failed: {ex.Message}");
            }
        }

        public static bool IsVpnConnected(string vpnName)
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
                Logger.Other($"IsVpnConnected: failed to query VPN status for '{vpnName}' => {ex.Message}");
                return false;
            }
        }

        private static string? GetVpnType(string vpnName)
        {
            if (_vpnTypeCache.TryGetValue(vpnName, out var cached))
                return cached;

            try
            {
                var result = RunProcess(
                    "powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command " +
                    QuoteArgument($"(Get-VpnConnection -Name '{EscapePowerShellSingleQuotedString(vpnName)}' -ErrorAction SilentlyContinue).TunnelType"),
                    utf8: true);

                string value = result.StdOut.Trim();
                var final = string.IsNullOrWhiteSpace(value) ? null : value;
                _vpnTypeCache[vpnName] = final;
                return final;
            }
            catch (Exception ex)
            {
                Logger.Other($"GetVpnType failed: {ex.Message}");
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
                Logger.Other($"AddVpnNamesFromPowerShell: failed => {ex.Message}");
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
                Logger.Other($"AddVpnNamesFromPhoneBook: failed for {phoneBookPath} => {ex.Message}");
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

        private static class RasCredentialHelper
        {
            private const int RASCM_UserName = 0x00000001;
            private const int RASCM_Password = 0x00000002;
            private const int RASCM_Domain = 0x00000004;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct RASCREDENTIALS
            {
                public int dwSize;
                public int dwMask;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
                public string szUserName;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
                public string szPassword;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
                public string szDomain;
            }

            [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern uint RasGetCredentials(string? lpszPhonebook, string lpszEntry, ref RASCREDENTIALS lpCredentials);

            internal sealed class RasStoredCredential
            {
                public string UserName { get; init; } = string.Empty;
                public string PasswordHandle { get; init; } = string.Empty;
                public string Domain { get; init; } = string.Empty;
            }

            internal static RasStoredCredential? GetStoredCredentials(string? phonebookPath, string entryName)
            {
                var creds = new RASCREDENTIALS
                {
                    dwSize = Marshal.SizeOf<RASCREDENTIALS>(),
                    dwMask = RASCM_UserName | RASCM_Password | RASCM_Domain,
                    szUserName = string.Empty,
                    szPassword = string.Empty,
                    szDomain = string.Empty
                };

                uint result = RasGetCredentials(phonebookPath, entryName, ref creds);
                if (result != 0)
                    return null;

                return new RasStoredCredential
                {
                    UserName = creds.szUserName ?? string.Empty,
                    PasswordHandle = creds.szPassword ?? string.Empty,
                    Domain = creds.szDomain ?? string.Empty
                };
            }
        }
    }
}