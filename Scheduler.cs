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
            _timer.Tick += (_, _) =>
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
            var now = DateTime.Now;

            if (!SettingsManager.IsEffectiveAutoStartEnabled(settings, now))
            {
                ScheduleVacationRecheckIfNeeded(settings, now);
                return;
            }

            var next = SettingsManager.GetNextLaunchDateTime(settings, now);
            if (next.HasValue)
            {
                ScheduleTimer(next.Value - now);
            }

            EvaluateAtStartupIfNeeded(settings, now);
        }

        private void EvaluateAtStartupIfNeeded(AppSettings settings, DateTime now)
        {
            if (_launchInProgress ||
                !SettingsManager.IsEffectiveAutoStartEnabled(settings, now) ||
                !SettingsManager.ShouldLaunchNow(settings, now))
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
                !SettingsManager.ShouldLaunchNow(settings, now))
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
                await _launcher.TryLaunchTargetsWithRetryAsync(false, settings, _notify, _saveSettings);
            }
            catch (Exception ex)
            {
                Logger.Other("LaunchAndPersistAsync: launch failed.", ex);
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

        private void ScheduleVacationRecheckIfNeeded(AppSettings settings, DateTime now)
        {
            if (!SettingsManager.IsAutoStartPausedByDate(settings, now) ||
                !settings.AutoStartOffUntilEnabled ||
                settings.AutoStartOffUntilDate == null)
            {
                return;
            }

            var nextRecheck = settings.AutoStartOffUntilDate.Value.Date.AddDays(1);
            ScheduleTimer(nextRecheck - now);
        }

        private void ScheduleTimer(TimeSpan due)
        {
            if (due < TimeSpan.Zero)
                due = TimeSpan.Zero;

            int ms = (int)Math.Min(due.TotalMilliseconds, int.MaxValue);
            _timer.Interval = Math.Max(1, ms);
            _timer.Start();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}