using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TeamsTrayStarter
{
    public sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _autoStartToggleItem;
        private readonly ToolStripMenuItem _runAtStartupItem;
        private readonly ToolStripMenuItem _desktopNotificationsItem;
        private readonly ToolStripMenuItem _openSettingsItem;
        private readonly ToolStripMenuItem _openLogItem;
        private readonly ToolStripMenuItem _emptyLogItem;
        private readonly ToolStripMenuItem _helpItem;
        private readonly ToolStripMenuItem _aboutItem;
        private readonly ToolStripMenuItem _exitItem;
        private readonly Scheduler _scheduler;
        private readonly TeamsLauncher _teamsLauncher;
        private readonly System.Windows.Forms.Timer _singleLeftClickTimer;

        private AppSettings _settings;
        private System.Drawing.Icon? _currentIcon;
        private SettingsForm? _settingsForm;
        private AboutForm? _aboutForm;
        private Process? _logViewerProcess;
        private HelpForm? _helpForm;

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "FileStarter";
        private const int NotifyIconTextMaxLength = 63;

        public TrayAppContext()
        {
            _settings = SettingsManager.Load();
            Logger.Init(SettingsManager.AppDataFolder);

            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(_settings, DateTime.Now))
            {
                SettingsManager.Save(_settings);
            }

            _teamsLauncher = new TeamsLauncher();
            _scheduler = new Scheduler(_teamsLauncher, () => _settings, SaveSettings, ShowBalloon);

            _autoStartToggleItem = new ToolStripMenuItem("Auto-start ON")
            {
                Checked = SettingsManager.IsEffectiveAutoStartEnabled(_settings, DateTime.Now),
                CheckOnClick = false
            };
            _autoStartToggleItem.Click += (_, __) => ToggleAutoStartMaster();

            _runAtStartupItem = new ToolStripMenuItem("Run FileStarter on Windows startup")
            {
                Checked = _settings.RunAppAtStartup,
                CheckOnClick = false
            };
            _runAtStartupItem.Click += (_, __) => ToggleRunAtStartup();

            _desktopNotificationsItem = new ToolStripMenuItem("Enable desktop notifications")
            {
                Checked = _settings.EnableDesktopNotifications,
                CheckOnClick = false
            };
            _desktopNotificationsItem.Click += (_, __) => ToggleDesktopNotifications();

            _openSettingsItem = new ToolStripMenuItem("Settings...");
            _openSettingsItem.Click += (_, __) => OpenSettings();

            _openLogItem = new ToolStripMenuItem("Open log file");
            _openLogItem.Click += (_, __) => OpenLogFile();

            _emptyLogItem = new ToolStripMenuItem("Empty the log file");
            _emptyLogItem.Click += (_, __) => EmptyLogFile();

            _helpItem = new ToolStripMenuItem("Help");
            _helpItem.Click += (_, __) => OpenHelp();

            _aboutItem = new ToolStripMenuItem("About");
            _aboutItem.Click += (_, __) => OpenAbout();

            _exitItem = new ToolStripMenuItem("Exit");
            _exitItem.Click += (_, __) => ExitApplication();

            var menu = new ContextMenuStrip();
            menu.Items.Add(_autoStartToggleItem);
            menu.Items.Add(_runAtStartupItem);
            menu.Items.Add(_desktopNotificationsItem);
            menu.Items.Add(_openSettingsItem);
            menu.Items.Add(_openLogItem);
            menu.Items.Add(_emptyLogItem);
            menu.Items.Add(_helpItem);
            menu.Items.Add(_aboutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitItem);

            _trayIcon = new NotifyIcon
            {
                Visible = true,
                ContextMenuStrip = menu
            };

            _singleLeftClickTimer = new System.Windows.Forms.Timer
            {
                Interval = SystemInformation.DoubleClickTime
            };
            _singleLeftClickTimer.Tick += (_, __) =>
            {
                _singleLeftClickTimer.Stop();
                ToggleAutoStartMaster();
            };

            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;

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

            RunStartupHealthCheck();
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
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

        private void SaveSettings(AppSettings s)
        {
            _settings = s;
            SettingsManager.Save(_settings);
            UpdateTrayUi();
        }

        private void UpdateTrayUi()
        {
            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(_settings, DateTime.Now))
            {
                SettingsManager.Save(_settings);
            }

            bool effectiveAutoStart = SettingsManager.IsEffectiveAutoStartEnabled(_settings, DateTime.Now);
            _autoStartToggleItem.Checked = effectiveAutoStart;
            _autoStartToggleItem.Text = effectiveAutoStart ? "Auto-start ON" : "Auto-start OFF";
            _runAtStartupItem.Checked = _settings.RunAppAtStartup;
            _desktopNotificationsItem.Checked = _settings.EnableDesktopNotifications;

            _currentIcon?.Dispose();
            const int trayIconSize = 32;
            _currentIcon = TrayIconFactory.CreateSemaphoreIcon(effectiveAutoStart, trayIconSize);
            _trayIcon.Icon = _currentIcon;
            _trayIcon.Text = GetTrayHoverText(DateTime.Now, _settings);
        }

        private static string GetTrayHoverText(DateTime now, AppSettings settings)
        {
            string text;
            bool vacationActive = SettingsManager.IsAutoStartPausedByDate(settings, now);
            if (vacationActive && settings.AutoStartOffUntilEnabled && settings.AutoStartOffUntilDate != null)
            {
                text = $"Auto-start OFF until {settings.AutoStartOffUntilDate.Value:dd/MM/yyyy}";
            }
            else if (!SettingsManager.IsEffectiveAutoStartEnabled(settings, now))
            {
                text = "Auto-start OFF";
            }
            else
            {
                var next = Scheduler.GetNextActionTime(now, settings);
                text = next != null
                    ? $"Auto-start ON, next {next.Value:ddd dd/MM HH:mm}"
                    : "Auto-start ON";
            }

            return text.Length <= NotifyIconTextMaxLength
                ? text
                : text.Substring(0, NotifyIconTextMaxLength);
        }

        private void RunStartupHealthCheck()
        {
            var missingSlots = GetMissingEnabledCustomSlots(_settings).ToList();
            if (missingSlots.Count == 0)
                return;

            foreach (var slot in missingSlots)
            {
                Logger.Warn($"Startup health check: {slot} has a selected custom path that no longer exists.");
            }

            string message = missingSlots.Count == 1
                ? $"Startup warning: {missingSlots[0]} custom path is missing."
                : $"Startup warning: {missingSlots.Count} selected custom files are missing.";

            ShowBalloon("FileStarter", message, ToolTipIcon.Warning);
        }

        private static IEnumerable<string> GetMissingEnabledCustomSlots(AppSettings settings)
        {
            if (settings.File1Enabled && !string.IsNullOrWhiteSpace(settings.File1Path) && !File.Exists(settings.File1Path))
                yield return SettingsManager.GetSlotLabel(1);
            if (settings.File2Enabled && !string.IsNullOrWhiteSpace(settings.File2Path) && !File.Exists(settings.File2Path))
                yield return SettingsManager.GetSlotLabel(2);
            if (settings.File3Enabled && !string.IsNullOrWhiteSpace(settings.File3Path) && !File.Exists(settings.File3Path))
                yield return SettingsManager.GetSlotLabel(3);
            if (settings.File4Enabled && !string.IsNullOrWhiteSpace(settings.File4Path) && !File.Exists(settings.File4Path))
                yield return SettingsManager.GetSlotLabel(4);
        }

        private void ToggleAutoStartMaster()
        {
            _settings.AutoStartTeamsEnabled = !_settings.AutoStartTeamsEnabled;
            SettingsManager.Save(_settings);
            Logger.Info($"Auto-start toggled to: {_settings.AutoStartTeamsEnabled}");
            UpdateTrayUi();
            _scheduler.StartOrReschedule();
        }

        private void ToggleRunAtStartup()
        {
            _settings.RunAppAtStartup = !_settings.RunAppAtStartup;
            try
            {
                if (_settings.RunAppAtStartup)
                {
                    EnableRunAtLogin();
                }
                else
                {
                    DisableRunAtLogin();
                }
                Logger.Info($"Run-at-startup toggled to: {_settings.RunAppAtStartup}");
                SettingsManager.Save(_settings);
                UpdateTrayUi();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to toggle Run-at-startup.", ex);
                ShowBalloon("FileStarter", "Failed to change startup setting. See log.", ToolTipIcon.Error);
            }
        }

        private static void EnableRunAtLogin()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("Cannot determine executable path.");

            var command = $"\"{exePath}\"";
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null)
                throw new InvalidOperationException("Cannot open HKCU Run key.");

            key.SetValue(RunValueName, command, RegistryValueKind.String);
        }

        private static void DisableRunAtLogin()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }

        private void ToggleDesktopNotifications()
        {
            try
            {
                _settings.EnableDesktopNotifications = !_settings.EnableDesktopNotifications;
                SettingsManager.Save(_settings);
                Logger.Info($"Desktop notifications toggled to: {_settings.EnableDesktopNotifications}");
                UpdateTrayUi();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to toggle desktop notifications.", ex);
                ShowBalloon("FileStarter", "Failed to change desktop notification setting. See log.", ToolTipIcon.Error);
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

        private void OpenSettings()
        {
            try
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    ShowOrActivateForm(_settingsForm);
                    return;
                }

                _settingsForm = new SettingsForm(_settings);
                _settingsForm.FormClosed += (_, __) =>
                {
                    if (_settingsForm != null && _settingsForm.Accepted)
                    {
                        ApplySettingsFromForm(_settingsForm);
                        if (SettingsManager.ApplyScheduledAutoStartOffIfDue(_settings, DateTime.Now))
                        {
                            Logger.Info("TrayAppContext: one-time scheduled auto-start OFF applied after saving settings.");
                        }
                        SettingsManager.Save(_settings);
                        Logger.Info("Settings updated successfully.");
                        UpdateTrayUi();
                        _scheduler.StartOrReschedule();
                    }
                    _settingsForm = null;
                };

                _settingsForm.Show();
                _settingsForm.BringToFront();
                _settingsForm.Activate();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open/apply settings.", ex);
                ShowBalloon("FileStarter", "Settings update failed. See log.", ToolTipIcon.Error);
            }
        }

        private void OpenHelp()
        {
            try
            {
                if (_helpForm != null && !_helpForm.IsDisposed)
                {
                    ShowOrActivateForm(_helpForm);
                    return;
                }
                _helpForm = new HelpForm();
                _helpForm.FormClosed += (_, __) => _helpForm = null;
                _helpForm.Show();
                _helpForm.BringToFront();
                _helpForm.Activate();
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
                if (_aboutForm != null && !_aboutForm.IsDisposed)
                {
                    ShowOrActivateForm(_aboutForm);
                    return;
                }
                _aboutForm = new AboutForm();
                _aboutForm.FormClosed += (_, __) => _aboutForm = null;
                _aboutForm.Show();
                _aboutForm.BringToFront();
                _aboutForm.Activate();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open About dialog.", ex);
            }
        }

        private void OpenLogFile()
        {
            try
            {
                var path = Logger.LogFilePath;
                if (!File.Exists(path))
                    File.WriteAllText(path, "Log created.");
                if (_logViewerProcess != null && !_logViewerProcess.HasExited)
                {
                    try
                    {
                        IntPtr hWnd = _logViewerProcess.MainWindowHandle;
                        if (hWnd != IntPtr.Zero)
                        {
                            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(hWnd);
                            return;
                        }
                    }
                    catch
                    {
                        // ignore and reopen if needed
                    }
                }
                _logViewerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open log file.", ex);
                ShowBalloon("FileStarter", "Could not open log file. See log.", ToolTipIcon.Warning);
            }
        }

        private void EmptyLogFile()
        {
            try
            {
                Logger.Clear();
                ShowBalloon("FileStarter", "Log file emptied.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to empty log file.", ex);
                ShowBalloon("FileStarter", "Could not empty log file. See log.", ToolTipIcon.Warning);
            }
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
            _scheduler.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _currentIcon?.Dispose();
            ExitThread();
        }
    }

    internal static class NativeMethods
    {
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}