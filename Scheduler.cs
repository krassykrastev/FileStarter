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
            ApplyPendingAutoStartTransition(settings);

            if (!SettingsManager.IsEffectiveAutoStartEnabled(settings, DateTime.Now))
            {
                ScheduleVacationRecheckIfNeeded(settings);
                return;
            }

            var next = ComputeNextActionTime(DateTime.Now, settings);
            if (next.HasValue)
            {
                ScheduleTimer(next.Value - DateTime.Now);
            }

            EvaluateAtStartupIfNeeded();
        }

        private void EvaluateAtStartupIfNeeded()
        {
            var settings = _getSettings();
            ApplyPendingAutoStartTransition(settings);

            if (_launchInProgress ||
                !SettingsManager.IsEffectiveAutoStartEnabled(settings, DateTime.Now) ||
                !ShouldLaunchNow(settings, DateTime.Now))
            {
                return;
            }

            EvaluateAndAct();
        }

        private void EvaluateAndAct()
        {
            var settings = _getSettings();
            ApplyPendingAutoStartTransition(settings);

            var now = DateTime.Now;
            if (_launchInProgress ||
                !SettingsManager.IsEffectiveAutoStartEnabled(settings, now) ||
                !ShouldLaunchNow(settings, now))
            {
                return;
            }

            _launchInProgress = true;
            _ = LaunchAndPersistAsync(settings);
        }

        private async Task LaunchAndPersistAsync(AppSettings settings)
        {
            try
            {
                await _launcher.TryLaunchTargetsWithRetryAsync(false, settings, _notify);
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

        private void ApplyPendingAutoStartTransition(AppSettings settings)
        {
            if (SettingsManager.ApplyScheduledAutoStartOffIfDue(settings, DateTime.Now))
            {
                _saveSettings(settings);
            }
        }

        private void ScheduleVacationRecheckIfNeeded(AppSettings settings)
        {
            if (!SettingsManager.IsAutoStartPausedByDate(settings, DateTime.Now) ||
                !settings.AutoStartOffUntilEnabled ||
                settings.AutoStartOffUntilDate == null)
            {
                return;
            }

            var nextRecheck = settings.AutoStartOffUntilDate.Value.Date.AddDays(1);
            ScheduleTimer(nextRecheck - DateTime.Now);
        }

        private void ScheduleTimer(TimeSpan due)
        {
            if (due < TimeSpan.Zero)
                due = TimeSpan.Zero;

            int ms = (int)Math.Min(due.TotalMilliseconds, int.MaxValue);
            _timer.Interval = Math.Max(1, ms);
            _timer.Start();
        }

        private static bool ShouldLaunchNow(AppSettings settings, DateTime now)
        {
            var todaySetting = SettingsManager.GetDaySetting(settings, now.DayOfWeek);
            if (!todaySetting.Enabled)
                return false;

            var todayLaunch = now.Date.Add(SettingsManager.GetDayLaunchTimeOrDefault(settings, now.DayOfWeek));
            return now >= todayLaunch;
        }

        private static DateTime? ComputeNextActionTime(DateTime now, AppSettings settings)
        {
            for (int offset = 0; offset < 8; offset++)
            {
                var date = now.Date.AddDays(offset);
                var daySetting = SettingsManager.GetDaySetting(settings, date.DayOfWeek);
                if (!daySetting.Enabled)
                    continue;

                var candidate = date.Add(SettingsManager.GetDayLaunchTimeOrDefault(settings, date.DayOfWeek));
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