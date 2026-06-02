using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TeamsTrayStarter
{
    public sealed class TrayAppContext : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "FileStarter";
        private const int TrayIconSize = 32;

        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _autoStartToggleItem;
        private readonly ToolStripMenuItem _runAtStartupItem;
        private readonly ToolStripMenuItem _desktopNotificationsItem;
        private readonly Scheduler _scheduler;
        private readonly System.Windows.Forms.Timer _singleLeftClickTimer;
        private readonly System.Windows.Forms.Timer _tooltipUpdateTimer;

        private AppSettings _settings;
        private System.Drawing.Icon? _currentIcon;
        private SettingsForm? _settingsForm;
        private AboutForm? _aboutForm;
        private HelpForm? _helpForm;
        private LogViewerForm? _logViewerForm;

        public TrayAppContext()
        {
            _settings = SettingsManager.Load();
            Logger.Init(SettingsManager.AppDataFolder);
            SaveIfAutoStartTransitionDue();

            _scheduler = new Scheduler(new TeamsLauncher(), () => _settings, SaveSettings, ShowBalloon);
            _autoStartToggleItem = CreateMenuItem("Auto-start ON", SettingsManager.IsEffectiveAutoStartEnabled(_settings, DateTime.Now), ToggleAutoStartMaster);
            _runAtStartupItem = CreateMenuItem("Run FileStarter on Windows startup", _settings.RunAppAtStartup, ToggleRunAtStartup);
            _desktopNotificationsItem = CreateMenuItem("Enable desktop notifications", _settings.EnableDesktopNotifications, ToggleDesktopNotifications);

            _trayIcon = new NotifyIcon
            {
                Visible = true,
                ContextMenuStrip = BuildContextMenu()
            };

            _singleLeftClickTimer = new System.Windows.Forms.Timer { Interval = SystemInformation.DoubleClickTime };
            _singleLeftClickTimer.Tick += (_, __) =>
            {
                _singleLeftClickTimer.Stop();
                ToggleAutoStartMaster();
            };

            _tooltipUpdateTimer = new System.Windows.Forms.Timer();
            _tooltipUpdateTimer.Tick += (_, __) => UpdateTrayUi();

            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;

            TryApplyRunAtStartupSetting();
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(_autoStartToggleItem);
            menu.Items.Add(_runAtStartupItem);
            menu.Items.Add(_desktopNotificationsItem);
            menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, __) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("Open log...", null, (_, __) => OpenLogViewer()));
            menu.Items.Add(new ToolStripMenuItem("Help", null, (_, __) => OpenHelp()));
            menu.Items.Add(new ToolStripMenuItem("About", null, (_, __) => OpenAbout()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, __) => ExitApplication()));
            return menu;
        }

        private static ToolStripMenuItem CreateMenuItem(string text, bool isChecked, Action onClick)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = isChecked,
                CheckOnClick = false
            };
            item.Click += (_, __) => onClick();
            return item;
        }

        private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _singleLeftClickTimer.Stop();
            _singleLeftClickTimer.Start();
        }

        private void TrayIcon_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _singleLeftClickTimer.Stop();
            OpenSettings();
        }

        private void SaveSettings(AppSettings settings)
        {
            _settings = settings;
            SettingsManager.Save(_settings);
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
            _autoStartToggleItem.Text = effectiveAutoStart ? "Auto-start ON" : "Auto-start OFF";
            _runAtStartupItem.Checked = _settings.RunAppAtStartup;
            _desktopNotificationsItem.Checked = _settings.EnableDesktopNotifications;

            _currentIcon?.Dispose();
            _currentIcon = TrayIconFactory.CreateSemaphoreIcon(effectiveAutoStart, TrayIconSize);
            _trayIcon.Icon = _currentIcon;
            // Compute a user-friendly next-launch tooltip when possible.
            var next = SettingsManager.GetNextLaunchDateTime(_settings, DateTime.Now);
            bool pausedByDate = SettingsManager.IsAutoStartPausedByDate(_settings, DateTime.Now) && _settings.AutoStartOffUntilEnabled;

            if (pausedByDate && _settings.AutoStartOffUntilDate.HasValue)
            {
                // Show the "until" date when paused by date
                string untilStr = _settings.AutoStartOffUntilDate.Value.ToString("dd/MM HH:mm");
                _trayIcon.Text = "Auto-start OFF until " + untilStr;
            }
            else if (next.HasValue && effectiveAutoStart)
            {
                // Show the next launch date when auto-start is ON
                string nextStr = next.Value.ToString("dd/MM HH:mm");
                _trayIcon.Text = "Auto-start ON, next " + nextStr;
            }
            else
            {
                _trayIcon.Text = effectiveAutoStart ? "Auto-start ON" : "Auto-start OFF";
            }

            ScheduleNextTooltipUpdate();
        }

        private void ScheduleNextTooltipUpdate()
        {
            _tooltipUpdateTimer.Stop();

            var now = DateTime.Now;
            var next = SettingsManager.GetNextLaunchDateTime(_settings, now);
            bool pausedByDate = SettingsManager.IsAutoStartPausedByDate(_settings, now) && _settings.AutoStartOffUntilEnabled;

            // Determine the next event time that requires a tooltip update
            DateTime? nextUpdateTime = null;

            if (pausedByDate && _settings.AutoStartOffUntilDate.HasValue)
            {
                // Next update is when the "until" date is reached
                nextUpdateTime = _settings.AutoStartOffUntilDate.Value;
            }
            else if (next.HasValue)
            {
                // Next update is when the next launch time is reached
                nextUpdateTime = next.Value;
            }

            int interval;
            if (nextUpdateTime.HasValue)
            {
                var delay = nextUpdateTime.Value - now;
                if (delay.TotalMilliseconds <= 0)
                {
                    // Already passed, update again very soon
                    interval = 100;
                }
                else
                {
                    // Calculate delay, but cap at 24 hours to handle edge cases
                    interval = (int)Math.Min(delay.TotalMilliseconds, 86400000);
                }
            }
            else
            {
                // No scheduled event, check again every minute
                interval = 60000;
            }

            _tooltipUpdateTimer.Interval = interval;
            _tooltipUpdateTimer.Start();
        }

        private void ToggleAutoStartMaster()
        {
            _settings.AutoStartTeamsEnabled = !_settings.AutoStartTeamsEnabled;
            SettingsManager.Save(_settings);
            Logger.Change(_settings.AutoStartTeamsEnabled ? "Auto-start turned ON" : "Auto-start turned OFF");
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
        }

        private void ToggleRunAtStartup()
        {
            _settings.RunAppAtStartup = !_settings.RunAppAtStartup;
            try
            {
                if (_settings.RunAppAtStartup)
                    EnableRunAtLogin();
                else
                    DisableRunAtLogin();

                Logger.Change(_settings.RunAppAtStartup ? "Run on Windows startup turned ON" : "Run on Windows startup turned OFF");
                SettingsManager.Save(_settings);
                UpdateTrayUi();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to toggle Run-at-startup.", ex);
                ShowBalloon("FileStarter", "Failed to change startup setting. See log.", ToolTipIcon.Error);
            }
        }

        private void ToggleDesktopNotifications()
        {
            try
            {
                _settings.EnableDesktopNotifications = !_settings.EnableDesktopNotifications;
                SettingsManager.Save(_settings);
                Logger.Change(_settings.EnableDesktopNotifications ? "Desktop notifications turned ON" : "Desktop notifications turned OFF");
                UpdateTrayUi();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to toggle desktop notifications.", ex);
                ShowBalloon("FileStarter", "Failed to change desktop notification setting. See log.", ToolTipIcon.Error);
            }
        }

        private void TryApplyRunAtStartupSetting()
        {
            try
            {
                if (_settings.RunAppAtStartup)
                    EnableRunAtLogin();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to apply Run-at-startup setting.", ex);
                ShowBalloon("FileStarter", "Could not set Run at startup. See log for details.", ToolTipIcon.Warning);
            }
        }

        private static void EnableRunAtLogin()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("Cannot determine executable path.");

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null)
                throw new InvalidOperationException("Cannot open HKCU Run key.");

            key.SetValue(RunValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }

        private static void DisableRunAtLogin()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }

        private void OpenSettings()
        {
            try
            {
                if (TryActivateExistingForm(_settingsForm))
                    return;

                _settingsForm = new SettingsForm(_settings);
                _settingsForm.FormClosed += (_, __) => ApplySettingsIfAccepted();
                ShowOrActivateForm(_settingsForm);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open/apply settings.", ex);
                ShowBalloon("FileStarter", "Settings update failed. See log.", ToolTipIcon.Error);
            }
        }

        private void ApplySettingsIfAccepted()
        {
            try
            {
                if (_settingsForm == null)
                    return;
                if (_settingsForm.Accepted)
                {
                    var before = SettingsManager.Clone(_settings);
                    ApplySettingsFromForm(_settingsForm);
                    SaveIfAutoStartTransitionDue();
                    SettingsManager.Save(_settings);
                    if (SettingsManager.HasSettingsChanges(before, _settings))
                    {
                        SettingsManager.LogSettingsChanges(before, _settings);
                    }
                    UpdateTrayUi();
                    _scheduler.StartOrReschedule();
                }
            }
            finally
            {
                _settingsForm = null;
            }
        }

        private void ApplySettingsFromForm(SettingsForm form)
        {
            _settings.Mon.Enabled = form.MonEnabled;
            _settings.Mon.Time = form.MonTimeHHmm;
            _settings.Tue.Enabled = form.TueEnabled;
            _settings.Tue.Time = form.TueTimeHHmm;
            _settings.Wed.Enabled = form.WedEnabled;
            _settings.Wed.Time = form.WedTimeHHmm;
            _settings.Thu.Enabled = form.ThuEnabled;
            _settings.Thu.Time = form.ThuTimeHHmm;
            _settings.Fri.Enabled = form.FriEnabled;
            _settings.Fri.Time = form.FriTimeHHmm;
            _settings.Sat.Enabled = form.SatEnabled;
            _settings.Sat.Time = form.SatTimeHHmm;
            _settings.Sun.Enabled = form.SunEnabled;
            _settings.Sun.Time = form.SunTimeHHmm;
            _settings.AutoStartOffFromEnabled = form.AutoStartOffFromEnabled;
            _settings.AutoStartOffFromDate = form.AutoStartOffFromDate;
            _settings.AutoStartOffUntilEnabled = form.AutoStartOffUntilEnabled;
            _settings.AutoStartOffUntilDate = form.AutoStartOffUntilDate;
            _settings.File1Enabled = form.File1Enabled;
            _settings.File1Path = form.File1Path;
            _settings.File2Enabled = form.File2Enabled;
            _settings.File2Path = form.File2Path;
            _settings.File3Enabled = form.File3Enabled;
            _settings.File3Path = form.File3Path;
            _settings.File4Enabled = form.File4Enabled;
            _settings.File4Path = form.File4Path;
        }

        private void OpenLogViewer()
        {
            try
            {
                if (TryActivateExistingForm(_logViewerForm))
                    return;

                _logViewerForm = new LogViewerForm();
                _logViewerForm.FormClosed += (_, __) => _logViewerForm = null;
                ShowOrActivateForm(_logViewerForm);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open log viewer.", ex);
                ShowBalloon("FileStarter", "Could not open log viewer. See log.", ToolTipIcon.Warning);
            }
        }

        private void OpenHelp()
        {
            try
            {
                if (TryActivateExistingForm(_helpForm))
                    return;

                _helpForm = new HelpForm();
                _helpForm.FormClosed += (_, __) => _helpForm = null;
                ShowOrActivateForm(_helpForm);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open Help dialog.", ex);
                ShowBalloon("FileStarter", "Could not open Help. See log for details.", ToolTipIcon.Warning);
            }
        }

        private void OpenAbout()
        {
            try
            {
                if (TryActivateExistingForm(_aboutForm))
                    return;

                _aboutForm = new AboutForm();
                _aboutForm.FormClosed += (_, __) => _aboutForm = null;
                ShowOrActivateForm(_aboutForm);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open About dialog.", ex);
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
            _scheduler.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _currentIcon?.Dispose();
            ExitThread();
        }
    }
}