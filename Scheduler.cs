
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class Scheduler : IDisposable
    {
        private readonly TeamsLauncher _launcher;
        private readonly Func<AppSettings> _getSettings;
        private readonly Action<AppSettings> _saveSettings;
        private readonly Action<string, string, ToolTipIcon> _notify;

        private readonly System.Windows.Forms.Timer _timer;
        private bool _launchInProgress;

        public Scheduler(
            TeamsLauncher launcher,
            Func<AppSettings> getSettings,
            Action<AppSettings> saveSettings,
            Action<string, string, ToolTipIcon> notify)
        {
            _launcher = launcher;
            _getSettings = getSettings;
            _saveSettings = saveSettings;
            _notify = notify;

            _timer = new System.Windows.Forms.Timer();
            _timer.Tick += (_, __) =>
            {
                _timer.Stop();
                EvaluateAndAct();
                StartOrReschedule();
            };
        }

        public void StartOrReschedule()
        {
            _timer.Stop();

            var settings = _getSettings();
            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(settings, DateTime.Now))
            {
                Logger.Info("Scheduler: one-time scheduled auto-start OFF applied.");
                _saveSettings(settings);
            }

            if (!SettingsManager.IsEffectiveAutoStartEnabled(settings, DateTime.Now))
            {
                Logger.Info("Scheduler: auto-start disabled. No scheduling.");
                return;
            }

            var now = DateTime.Now;
            var next = ComputeNextActionTime(now, settings);
            if (next == null)
            {
                Logger.Info("Scheduler: no selected days to schedule.");
                return;
            }

            var due = next.Value - now;
            if (due < TimeSpan.Zero)
                due = TimeSpan.Zero;

            int ms = (int)Math.Min(due.TotalMilliseconds, int.MaxValue);
            _timer.Interval = Math.Max(1, ms);
            _timer.Start();

            Logger.Info($"Scheduler: next evaluation scheduled at {next.Value:yyyy-MM-dd HH:mm:ss} (in {due}).");

            // If user logs in after today's scheduled time, launch immediately
            EvaluateAtStartupIfNeeded();
        }

        private void EvaluateAtStartupIfNeeded()
        {
            var settings = _getSettings();
            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(settings, DateTime.Now))
            {
                Logger.Info("Scheduler: one-time scheduled auto-start OFF applied during startup evaluation.");
                _saveSettings(settings);
            }

            if (!SettingsManager.IsEffectiveAutoStartEnabled(settings, DateTime.Now))
                return;

            if (_launchInProgress)
                return;

            var now = DateTime.Now;
            var todaySetting = SettingsManager.GetDaySetting(settings, now.DayOfWeek);

            if (!todaySetting.Enabled)
            {
                Logger.Info("Scheduler: startup check skipped because today's day is not enabled.");
                return;
            }

            var todayLaunch = now.Date.Add(SettingsManager.GetDayLaunchTimeOrDefault(settings, now.DayOfWeek));
            if (now >= todayLaunch)
            {
                Logger.Info("Scheduler: startup detected after scheduled time; evaluating immediate launch.");
                EvaluateAndAct();
            }
        }

        private void EvaluateAndAct()
        {
            var settings = _getSettings();
            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(settings, DateTime.Now))
            {
                Logger.Info("Scheduler: one-time scheduled auto-start OFF applied during evaluation.");
                _saveSettings(settings);
            }

            if (!SettingsManager.IsEffectiveAutoStartEnabled(settings, DateTime.Now))
            {
                Logger.Info("Evaluate: auto-start disabled. Skipping.");
                return;
            }

            if (_launchInProgress)
            {
                Logger.Info("Evaluate: launch already in progress. Skipping.");
                return;
            }

            var now = DateTime.Now;
            var todaySetting = SettingsManager.GetDaySetting(settings, now.DayOfWeek);
            if (!todaySetting.Enabled)
            {
                Logger.Info("Evaluate: current day is not selected.");
                return;
            }

            var todayLaunch = now.Date.Add(SettingsManager.GetDayLaunchTimeOrDefault(settings, now.DayOfWeek));
            if (now < todayLaunch)
            {
                Logger.Info("Evaluate: before today's launch time.");
                return;
            }

            _launchInProgress = true;
            _ = LaunchAndPersistAsync(settings);
        }

        private async Task LaunchAndPersistAsync(AppSettings settings)
        {
            try
            {
                bool launched = await _launcher.TryLaunchTargetsWithRetryAsync(false, settings, _notify);
                if (launched)
                {
                    Logger.Info("LaunchAndPersistAsync: launch completed successfully.");
                }
                else
                {
                    Logger.Info("LaunchAndPersistAsync: nothing was launched.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LaunchAndPersistAsync: launch failed.", ex);
                _notify("FileStarter", "Launch failed. See log for details.", ToolTipIcon.Error);
            }
            finally
            {
                _launchInProgress = false;
            }
        }

        private static DateTime? ComputeNextActionTime(DateTime now, AppSettings settings)
        {
            for (int offset = 0; offset < 8; offset++)
            {
                var date = now.Date.AddDays(offset);
                var daySetting = SettingsManager.GetDaySetting(settings, date.DayOfWeek);
                if (!daySetting.Enabled)
                    continue;

                var launchTime = SettingsManager.GetDayLaunchTimeOrDefault(settings, date.DayOfWeek);
                var candidate = date.Add(launchTime);
                if (candidate > now)
                    return candidate;
            }
            return null;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}