using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TeamsTrayStarter
{
    public sealed class TrayAppContext : ApplicationContext
    {
        private const string RunKeyPath = @"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunValueName = "FileStarter";
        private const int TrayIconSize = 32;
        private const int WM_NULL = 0x0000;
        private const int VpnReconnectIntervalMs = 30000;

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly Form _menuHostForm;
        private readonly ToolStripMenuItem _autoStartToggleItem;
        private readonly ToolStripMenuItem _runAtStartupItem;
        private readonly ToolStripMenuItem _startVpnFirstItem;
        private readonly Scheduler _scheduler;
        private readonly System.Windows.Forms.Timer _singleLeftClickTimer;
        private readonly System.Windows.Forms.Timer _tooltipUpdateTimer;
        private readonly System.Windows.Forms.Timer _vpnReconnectTimer;
        private readonly VpnService _vpnService = new();

        private AppSettings _settings;
        private Icon? _currentIcon;
        private SettingsForm? _settingsForm;
        private AboutForm? _aboutForm;
        private HelpForm? _helpForm;
        private LogViewerForm? _logViewerForm;
        private bool? _lastEffectiveAutoStartState;
        private bool? _lastVpnReconnectFailedState;
        private bool _vpnReconnectInProgress;
        private bool _vpnReconnectFailed;
        private bool _vpnReconnectFailureNotified;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public TrayAppContext()
        {
            TrayIconFactory.Initialize();
            _settings = SettingsManager.Load();
            Logger.Init(SettingsManager.AppDataFolder);
            SaveIfAutoStartTransitionDue();

            _scheduler = new Scheduler(new TeamsLauncher(), () => _settings, SaveSettings, ShowBalloon);
            _autoStartToggleItem = CreateMenuItem(
                "Auto-start enabled",
                SettingsManager.IsEffectiveAutoStartEnabled(_settings, DateTime.Now),
                ToggleAutoStartMaster);
            _runAtStartupItem = CreateMenuItem(
                "Run FileStarter on Windows startup",
                IsRunAtStartupEnabled(),
                ToggleRunAtStartup);
            _startVpnFirstItem = CreateMenuItem(
                "Start VPN first && reconnect on drops",
                _settings.StartVpnFirstEnabled,
                ToggleStartVpnFirst);

            _trayMenu = BuildContextMenu();
            _menuHostForm = CreateMenuHostForm();
            _trayMenu.Closed += (_, _) =>
            {
                if (_menuHostForm.Visible)
                    _menuHostForm.Hide();
                PostMessage(_menuHostForm.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            };

            _trayIcon = new NotifyIcon { Visible = true };
            _singleLeftClickTimer = new System.Windows.Forms.Timer { Interval = SystemInformation.DoubleClickTime };
            _singleLeftClickTimer.Tick += (_, _) =>
            {
                _singleLeftClickTimer.Stop();
                ToggleAutoStartMaster();
            };
            _tooltipUpdateTimer = new System.Windows.Forms.Timer();
            _tooltipUpdateTimer.Tick += (_, _) => UpdateTrayUi();
            _vpnReconnectTimer = new System.Windows.Forms.Timer { Interval = VpnReconnectIntervalMs };
            _vpnReconnectTimer.Tick += async (_, _) => await MonitorVpnConnectionAsync();
            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;

            TryApplyRunAtStartupSetting();
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
            UpdateVpnReconnectMonitorState(connectImmediately: true);
            _ = ConnectVpnAtStartupAsync();
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                Padding = new Padding(6),
                BackColor = Color.White,
                RenderMode = ToolStripRenderMode.System
            };
            menu.Items.Add(_autoStartToggleItem);
            menu.Items.Add(_runAtStartupItem);
            menu.Items.Add(_startVpnFirstItem);
            menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("View log", null, (_, _) => OpenLogViewer()));
            menu.Items.Add(new ToolStripMenuItem("Suggestions / bugs", null, (_, _) =>
                AppUiHelpers.TryOpenUrl(null, "https://github.com/krassykrastev/FileStarter/issues/new", "Could not open the link.", "Error")));
            menu.Items.Add(new ToolStripMenuItem("Check for new version", null, (_, _) =>
                AppUiHelpers.TryOpenUrl(null, "https://github.com/krassykrastev/FileStarter/releases", "Could not open the link.", "Error")));
            menu.Items.Add(new ToolStripMenuItem("Help", null, (_, _) => OpenHelp()));
            menu.Items.Add(new ToolStripMenuItem("About", null, (_, _) => OpenAbout()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
            return menu;
        }

        private static Form CreateMenuHostForm()
        {
            return new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(1, 1),
                Location = new Point(-32000, -32000),
                Opacity = 0,
                TopMost = true
            };
        }

        private static ToolStripMenuItem CreateMenuItem(string text, bool isChecked, Action onClick)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = isChecked,
                CheckOnClick = false
            };
            item.Click += (_, _) => onClick();
            return item;
        }

        private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _singleLeftClickTimer.Stop();
                _singleLeftClickTimer.Start();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                _singleLeftClickTimer.Stop();
                ShowTrayContextMenu();
            }
        }

        private void TrayIcon_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            _singleLeftClickTimer.Stop();
            OpenSettings();
        }

        private void ShowTrayContextMenu()
        {
            var cursorPos = Cursor.Position;
            var menuSize = _trayMenu.GetPreferredSize(Size.Empty);
            var screen = Screen.FromPoint(cursorPos).WorkingArea;
            int x = cursorPos.X - (menuSize.Width / 2);
            int y = cursorPos.Y;
            if (y + menuSize.Height > screen.Bottom)
            {
                y = cursorPos.Y - menuSize.Height;
            }
            x = Math.Max(screen.Left, Math.Min(x, screen.Right - menuSize.Width));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - menuSize.Height));
            var adjustedPos = new Point(x, y);
            _menuHostForm.Location = adjustedPos;
            _menuHostForm.Show();
            _menuHostForm.Activate();
            SetForegroundWindow(_menuHostForm.Handle);
            _trayMenu.Show(_menuHostForm, _menuHostForm.PointToClient(adjustedPos));
        }

        private async Task ConnectVpnAtStartupAsync()
        {
            try
            {
                if (!_settings.StartVpnFirstEnabled || string.IsNullOrWhiteSpace(_settings.VpnConnectionName))
                    return;

                bool connected = await _vpnService.EnsureVpnConnectedAsync(_settings, ShowBalloon, SaveSettings, allowUserPrompt: false);
                SetVpnReconnectFailed(!connected, notifyOnce: false);
            }
            catch (Exception ex)
            {
                Logger.Other("Startup VPN connection failed.", ex);
                SetVpnReconnectFailed(true, notifyOnce: false);
                ShowBalloon("FileStarter", "VPN failed to connect.", ToolTipIcon.Warning);
            }
        }

        private void SaveSettings(AppSettings settings)
        {
            _settings = settings;
            SettingsManager.Save(_settings);
            UpdateVpnReconnectMonitorState(connectImmediately: false);
            UpdateTrayUi();
        }

        private void SaveIfAutoStartTransitionDue()
        {
            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(_settings, DateTime.Now))
            {
                SettingsManager.Save(_settings);
            }
        }

        private void UpdateTrayUi()
        {
            SaveIfAutoStartTransitionDue();
            bool effectiveAutoStart = SettingsManager.IsEffectiveAutoStartEnabled(_settings, DateTime.Now);
            _autoStartToggleItem.Checked = effectiveAutoStart;
            _autoStartToggleItem.Text = effectiveAutoStart ? "Auto-start enabled" : "Auto-start disabled";
            _runAtStartupItem.Checked = IsRunAtStartupEnabled();
            _startVpnFirstItem.Checked = _settings.StartVpnFirstEnabled;
            _startVpnFirstItem.Text = "Start VPN first && reconnect on drops";

            if (_lastEffectiveAutoStartState != effectiveAutoStart || _lastVpnReconnectFailedState != _vpnReconnectFailed)
            {
                _currentIcon?.Dispose();
                _currentIcon = TrayIconFactory.CreateAutoStartStateIcon(effectiveAutoStart, TrayIconSize, _vpnReconnectFailed);
                _trayIcon.Icon = _currentIcon;
                _lastEffectiveAutoStartState = effectiveAutoStart;
                _lastVpnReconnectFailedState = _vpnReconnectFailed;
            }

            var next = SettingsManager.GetNextLaunchDateTime(_settings, DateTime.Now);
            bool pausedByDate = SettingsManager.IsAutoStartPausedByDate(_settings, DateTime.Now) && _settings.AutoStartOffUntilEnabled;
            if (_vpnReconnectFailed)
            {
                _trayIcon.Text = "unable to auto-reconnect to VPN, try manually";
            }
            else if (pausedByDate && _settings.AutoStartOffUntilDate.HasValue)
            {
                string untilStr = _settings.AutoStartOffUntilDate.Value.ToString("dd/MM HH:mm");
                _trayIcon.Text = "Auto-start disabled until " + untilStr;
            }
            else if (next.HasValue && effectiveAutoStart)
            {
                string nextStr = next.Value.ToString("dd/MM HH:mm");
                _trayIcon.Text = "Auto-start enabled, next " + nextStr;
            }
            else
            {
                _trayIcon.Text = effectiveAutoStart ? "Auto-start enabled" : "Auto-start disabled";
            }
            ScheduleNextTooltipUpdate();
        }

        private void UpdateVpnReconnectMonitorState(bool connectImmediately)
        {
            bool shouldMonitor = _settings.StartVpnFirstEnabled && !string.IsNullOrWhiteSpace(_settings.VpnConnectionName);
            if (shouldMonitor)
            {
                if (!_vpnReconnectTimer.Enabled)
                    _vpnReconnectTimer.Start();
                if (connectImmediately)
                    _ = MonitorVpnConnectionAsync();
                return;
            }
            _vpnReconnectTimer.Stop();
            SetVpnReconnectFailed(false, notifyOnce: false);
        }

        private async Task MonitorVpnConnectionAsync()
        {
            if (_vpnReconnectInProgress)
                return;
            if (!_settings.StartVpnFirstEnabled || string.IsNullOrWhiteSpace(_settings.VpnConnectionName))
            {
                UpdateVpnReconnectMonitorState(connectImmediately: false);
                return;
            }
            string vpnName = _settings.VpnConnectionName.Trim();
            if (VpnService.IsVpnConnected(vpnName))
            {
                SetVpnReconnectFailed(false, notifyOnce: false);
                return;
            }
            _vpnReconnectInProgress = true;
            try
            {
                Logger.Other($"VPN disconnected; attempting auto-reconnect: {vpnName}");
                bool connected = await _vpnService.EnsureVpnConnectedAsync(_settings, (_, _, _) => { }, SaveSettings, allowUserPrompt: false);
                if (connected)
                {
                    Logger.Change($"VPN auto-reconnected: {vpnName}");
                    SetVpnReconnectFailed(false, notifyOnce: false);
                }
                else
                {
                    Logger.Other($"VPN auto-reconnect failed: {vpnName}");
                    SetVpnReconnectFailed(true, notifyOnce: true);
                }
            }
            catch (Exception ex)
            {
                Logger.Other($"VPN auto-reconnect failed: {vpnName}", ex);
                SetVpnReconnectFailed(true, notifyOnce: true);
            }
            finally
            {
                _vpnReconnectInProgress = false;
            }
        }

        private void SetVpnReconnectFailed(bool failed, bool notifyOnce)
        {
            bool changed = _vpnReconnectFailed != failed;
            _vpnReconnectFailed = failed;
            if (!failed)
                _vpnReconnectFailureNotified = false;
            else if (notifyOnce && !_vpnReconnectFailureNotified)
            {
                _vpnReconnectFailureNotified = true;
                ShowBalloon("FileStarter", "Unable to auto-reconnect to VPN, try manually", ToolTipIcon.Error);
            }
            if (changed)
                UpdateTrayUi();
        }

        private void ScheduleNextTooltipUpdate()
        {
            _tooltipUpdateTimer.Stop();
            var now = DateTime.Now;
            var next = SettingsManager.GetNextLaunchDateTime(_settings, now);
            bool pausedByDate = SettingsManager.IsAutoStartPausedByDate(_settings, now) && _settings.AutoStartOffUntilEnabled;
            DateTime? nextUpdateTime = null;
            if (pausedByDate && _settings.AutoStartOffUntilDate.HasValue)
            {
                nextUpdateTime = _settings.AutoStartOffUntilDate.Value;
            }
            else if (next.HasValue)
            {
                nextUpdateTime = next.Value;
            }

            int interval;
            if (nextUpdateTime.HasValue)
            {
                var delay = nextUpdateTime.Value - now;
                interval = delay.TotalMilliseconds <= 0 ? 100 : (int)Math.Min(delay.TotalMilliseconds, 86400000);
            }
            else
            {
                interval = 60000;
            }
            _tooltipUpdateTimer.Interval = interval;
            _tooltipUpdateTimer.Start();
        }

        private void ToggleAutoStartMaster()
        {
            _settings.AutoStartTeamsEnabled = !_settings.AutoStartTeamsEnabled;
            SettingsManager.Save(_settings);
            Logger.Change(_settings.AutoStartTeamsEnabled ? "Auto-start enabled" : "Auto-start disabled");
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
        }

        private void ToggleRunAtStartup()
        {
            bool enable = !IsRunAtStartupEnabled();
            _settings.RunAppAtStartup = enable;
            try
            {
                if (enable)
                    EnableRunAtLogin();
                else
                    DisableRunAtLogin();
                Logger.Change(enable ? "Run on Windows startup enabled" : "Run on Windows startup disabled");
                SettingsManager.Save(_settings);
                UpdateTrayUi();
            }
            catch (Exception ex)
            {
                Logger.Other("Failed to update run-at-startup setting.", ex);
            }
        }

        private void DisconnectSelectedVpnIfNeeded()
        {
            string vpnName = _settings.VpnConnectionName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vpnName))
                return;

            bool disconnected = VpnService.DisconnectVpn(vpnName);
            if (!disconnected)
            {
                ShowBalloon("FileStarter", "Failed to disconnect VPN. Try manually.", ToolTipIcon.Warning);
            }
        }

        private void ToggleStartVpnFirst()
        {
            try
            {
                if (!_settings.StartVpnFirstEnabled)
                {
                    var vpnConnections = VpnService.GetAvailableVpnConnectionNames();
                    if (vpnConnections.Count == 0)
                    {
                        MessageBox.Show("No Windows VPN connections were found on this PC.", "No VPN connections found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _settings.StartVpnFirstEnabled = false;
                        _settings.VpnConnectionName = null;
                        UpdateVpnReconnectMonitorState(connectImmediately: false);
                        UpdateTrayUi();
                        return;
                    }
                    using var selectionForm = new VpnSelectionForm(vpnConnections);
                    if (selectionForm.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(selectionForm.SelectedConnectionName))
                    {
                        _settings.StartVpnFirstEnabled = false;
                        _settings.VpnConnectionName = null;
                        UpdateVpnReconnectMonitorState(connectImmediately: false);
                        UpdateTrayUi();
                        return;
                    }
                    _settings.StartVpnFirstEnabled = true;
                    _settings.VpnConnectionName = selectionForm.SelectedConnectionName.Trim();
                    Logger.Change("Start VPN first & reconnect on drops enabled");
                    Logger.Change($"VPN connection selected: {_settings.VpnConnectionName}");
                }
                else
                {
                    DisconnectSelectedVpnIfNeeded();
                    _settings.StartVpnFirstEnabled = false;
                    _settings.VpnConnectionName = null;
                    Logger.Change("Start VPN first & reconnect on drops disabled");
                }
                SettingsManager.Save(_settings);
                UpdateVpnReconnectMonitorState(connectImmediately: _settings.StartVpnFirstEnabled);
                UpdateTrayUi();
            }
            catch (Exception ex)
            {
                Logger.Other("Failed to toggle Start VPN first & reconnect on drops.", ex);
                ShowBalloon("FileStarter", "Failed to change VPN startup setting. See log.", ToolTipIcon.Error);
            }
        }

        private void TryApplyRunAtStartupSetting()
        {
            try
            {
                if (_settings.RunAppAtStartup)
                    EnableRunAtLogin();
                else
                    DisableRunAtLogin();
            }
            catch (Exception ex)
            {
                Logger.Other("Failed to apply Run-at-startup setting.", ex);
                ShowBalloon("FileStarter", "Could not set Run at startup. See log for details.", ToolTipIcon.Warning);
            }
        }

        private static void EnableRunAtLogin()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("Cannot determine executable path.");

            try
            {
                CreateStartupTaskWithSchtasks(exePath);
                RemoveRegistryRunEntry();
            }
            catch (Exception taskEx)
            {
                Logger.Other("Failed to create Task Scheduler startup entry for FileStarter. Falling back to registry startup.", taskEx);
                try
                {
                    EnableRunAtLoginRegistry(exePath);
                    Logger.Change("Task Scheduler startup setup failed. Fallback registry startup setup succeeded.");
                }
                catch (Exception registryEx)
                {
                    Logger.Other("Task Scheduler startup setup failed and fallback registry startup setup also failed.", registryEx);
                    throw;
                }
            }
        }

        private static void DisableRunAtLogin()
        {
            Exception? taskDeleteEx = null;
            Exception? registryDeleteEx = null;

            try
            {
                DeleteStartupTaskWithSchtasks();
            }
            catch (Exception ex)
            {
                taskDeleteEx = ex;
                Logger.Other("Failed to remove Task Scheduler startup entry for FileStarter.", ex);
            }

            try
            {
                RemoveRegistryRunEntry();
            }
            catch (Exception ex)
            {
                registryDeleteEx = ex;
                Logger.Other("Failed to remove registry startup entry for FileStarter.", ex);
            }

            if (taskDeleteEx != null && registryDeleteEx != null)
                throw new AggregateException(taskDeleteEx, registryDeleteEx);
        }

        private static bool IsRunAtStartupEnabled()
            => StartupTaskExists() || RegistryStartupExists();

        private static bool StartupTaskExists()
        {
            var result = RunSchtasks($"/Query /TN \"{RunValueName}\"");
            return result.ExitCode == 0;
        }

        private static bool RegistryStartupExists()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static void CreateStartupTaskWithSchtasks(string exePath)
        {
            string currentUser = WindowsIdentity.GetCurrent().Name;
            if (string.IsNullOrWhiteSpace(currentUser))
                throw new InvalidOperationException("Cannot determine current Windows user.");

            string escapedExePath = System.Security.SecurityElement.Escape(exePath) ?? exePath;
            string workingDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            string escapedWorkingDir = System.Security.SecurityElement.Escape(workingDir) ?? workingDir;
            string escapedUser = System.Security.SecurityElement.Escape(currentUser) ?? currentUser;
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts FileStarter at user logon</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{escapedUser}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{escapedUser}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escapedExePath}</Command>
      <WorkingDirectory>{escapedWorkingDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

            string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{RunValueName}_startup_task.xml");
            try
            {
                File.WriteAllText(tempXmlPath, xml, Encoding.Unicode);
                if (StartupTaskExists())
                {
                    var deleteResult = RunSchtasks($"/Delete /TN \"{RunValueName}\" /F");
                    if (deleteResult.ExitCode != 0)
                        throw new InvalidOperationException($"Failed to delete existing startup task. {deleteResult.CombinedOutput}");
                }

                var createResult = RunSchtasks($"/Create /TN \"{RunValueName}\" /XML \"{tempXmlPath}\" /F");
                if (createResult.ExitCode != 0)
                    throw new InvalidOperationException($"Failed to create startup task. {createResult.CombinedOutput}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempXmlPath))
                        File.Delete(tempXmlPath);
                }
                catch
                {
                    // intentionally ignored
                }
            }
        }

        private static void DeleteStartupTaskWithSchtasks()
        {
            if (!StartupTaskExists())
                return;
            var result = RunSchtasks($"/Delete /TN \"{RunValueName}\" /F");
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Failed to delete startup task. {result.CombinedOutput}");
        }

        private static (int ExitCode, string StdOut, string StdErr, string CombinedOutput) RunSchtasks(string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            string combined = (stdOut + Environment.NewLine + stdErr).Trim();
            return (process.ExitCode, stdOut, stdErr, combined);
        }

        private static void EnableRunAtLoginRegistry(string exePath)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null)
                throw new InvalidOperationException("Cannot open HKCU Run key.");
            key.SetValue(RunValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }

        private static void RemoveRegistryRunEntry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }

        private void OpenSettings()
        {
            OpenSingleInstanceForm(
                () => _settingsForm,
                value => _settingsForm = value,
                () => new SettingsForm(_settings),
                ApplySettingsIfAccepted);
        }

        private void ApplySettingsIfAccepted()
        {
            if (_settingsForm == null || !_settingsForm.Accepted || _settingsForm.ResultSettings == null)
                return;

            var before = SettingsManager.Clone(_settings);
            _settings = _settingsForm.ResultSettings;
            SaveIfAutoStartTransitionDue();
            SettingsManager.Save(_settings);
            if (SettingsManager.HasSettingsChanges(before, _settings))
            {
                SettingsManager.LogSettingsChanges(before, _settings);
            }

            bool vpnOptionChanged = before.StartVpnFirstEnabled != _settings.StartVpnFirstEnabled ||
                                    !string.Equals(before.VpnConnectionName, _settings.VpnConnectionName, StringComparison.OrdinalIgnoreCase);
            bool shouldDisconnectVpn = before.StartVpnFirstEnabled && !_settings.StartVpnFirstEnabled &&
                                       !string.IsNullOrWhiteSpace(before.VpnConnectionName);
            if (shouldDisconnectVpn)
            {
                bool disconnected = VpnService.DisconnectVpn(before.VpnConnectionName!.Trim());
                if (!disconnected)
                {
                    ShowBalloon("FileStarter", "Failed to disconnect VPN. Try manually.", ToolTipIcon.Warning);
                }
            }
            UpdateVpnReconnectMonitorState(connectImmediately: _settings.StartVpnFirstEnabled && vpnOptionChanged);
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
        }

        private void OpenLogViewer()
        {
            OpenSingleInstanceForm(
                () => _logViewerForm,
                value => _logViewerForm = value,
                () => new LogViewerForm());
        }

        private void OpenHelp()
        {
            OpenSingleInstanceForm(
                () => _helpForm,
                value => _helpForm = value,
                () => new HelpForm());
        }

        private void OpenAbout()
        {
            OpenSingleInstanceForm(
                () => _aboutForm,
                value => _aboutForm = value,
                () => new AboutForm());
        }

        private void OpenSingleInstanceForm<T>(Func<T?> getter, Action<T?> setter, Func<T> factory, Action? onClosed = null) where T : Form
        {
            try
            {
                T? existing = getter();
                if (TryActivateExistingForm(existing))
                    return;

                T form = factory();
                setter(form);
                form.FormClosed += (_, __) =>
                {
                    onClosed?.Invoke();
                    if (ReferenceEquals(getter(), form))
                    {
                        setter(null);
                    }
                };
                ShowOrActivateForm(form);
            }
            catch (Exception ex)
            {
                Logger.Other($"Failed to open {typeof(T).Name}.", ex);
                ShowBalloon("FileStarter", $"Could not open {typeof(T).Name}. See log.", ToolTipIcon.Warning);
            }
        }

        private static bool TryActivateExistingForm(Form? form)
        {
            if (form == null || form.IsDisposed)
                return false;
            ShowOrActivateForm(form);
            return true;
        }

        private static void ShowOrActivateForm(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;
            if (!form.Visible)
                form.Show();
            form.BringToFront();
            form.Activate();
            form.Focus();
        }

        private void ShowBalloon(string title, string message, ToolTipIcon icon)
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = message;
            _trayIcon.BalloonTipIcon = icon;
            _trayIcon.ShowBalloonTip(4000);
        }

        private void ExitApplication()
        {
            _singleLeftClickTimer.Stop();
            _singleLeftClickTimer.Dispose();
            _tooltipUpdateTimer.Stop();
            _tooltipUpdateTimer.Dispose();
            _vpnReconnectTimer.Stop();
            _vpnReconnectTimer.Dispose();
            _scheduler.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _menuHostForm.Dispose();
            _currentIcon?.Dispose();
            ExitThread();
        }
    }

    internal static class AppUiHelpers
    {
        public static void TryOpenUrl(IWin32Window? owner, string url, string failureMessage, string failureTitle)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Other($"Failed to open URL: {url}", ex);
                if (owner != null)
                {
                    MessageBox.Show(owner, failureMessage, failureTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(failureMessage, failureTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        public static Bitmap? TryLoadAppIconBitmap()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                    return null;
                using Icon? appIcon = Icon.ExtractAssociatedIcon(exePath);
                return appIcon?.ToBitmap();
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class TrayIconFactory
    {
        private static readonly string? BaseIconPath = ResolveBaseIconPath();

        public static void Initialize()
        {
            _ = BaseIconPath;
        }

        public static Icon CreateAutoStartStateIcon(bool enabled, int renderSize, bool vpnReconnectFailed = false)
        {
            renderSize = Math.Clamp(renderSize, 16, 128);
            using Icon baseIcon = LoadBaseIcon(renderSize);
            using var bmp = new Bitmap(renderSize, renderSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawIcon(baseIcon, new Rectangle(-1, -1, renderSize + 4, renderSize + 4));

            if (!enabled)
                DrawBusyBadge(g, renderSize);

            if (vpnReconnectFailed)
                DrawVpnReconnectFailureBadge(g, renderSize);

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static void DrawBusyBadge(Graphics g, int renderSize)
        {
            int diameter = Math.Max(20, ((renderSize * 2) / 3) - 2);
            int margin = Math.Max(1, renderSize / 20);
            int x = renderSize - diameter - margin;
            int y = renderSize - diameter - margin;
            var badgeRect = new Rectangle(x, y, diameter, diameter);
            using var fillBrush = new SolidBrush(Color.FromArgb(196, 49, 75));
            using var borderPen = new Pen(Color.Black, 1f);
            g.FillEllipse(fillBrush, badgeRect);
            g.DrawEllipse(borderPen, badgeRect);
        }

        private static void DrawVpnReconnectFailureBadge(Graphics g, int renderSize)
        {
            int diameter = Math.Max(14, renderSize / 2);
            int margin = Math.Max(1, renderSize / 18);
            int x = renderSize - diameter - margin;
            int y = margin;
            var badgeRect = new Rectangle(x, y, diameter, diameter);
            using var fillBrush = new SolidBrush(Color.Crimson);
            using var borderPen = new Pen(Color.White, Math.Max(1f, renderSize / 32f));
            using var textBrush = new SolidBrush(Color.White);
            using var textFont = new Font("Segoe UI", Math.Max(9f, renderSize * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.FillEllipse(fillBrush, badgeRect);
            g.DrawEllipse(borderPen, badgeRect);
            g.DrawString("!", textFont, textBrush, badgeRect, format);
        }

        private static Icon LoadBaseIcon(int renderSize)
        {
            if (!string.IsNullOrWhiteSpace(BaseIconPath) && File.Exists(BaseIconPath))
                return new Icon(BaseIconPath, new Size(renderSize, renderSize));

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                Icon? exeIcon = Icon.ExtractAssociatedIcon(exePath);
                if (exeIcon != null)
                    return new Icon(exeIcon, new Size(renderSize, renderSize));
            }

            return new Icon(SystemIcons.Application, new Size(renderSize, renderSize));
        }

        private static string? ResolveBaseIconPath()
        {
            string[] candidatePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico"),
                Path.Combine(Application.StartupPath, "Assets", "app.ico"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico")
            };
            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }

}