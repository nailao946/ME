using System;
using System.Windows;
using System.Windows.Threading;
using ME.Core;
using ME.Services;

namespace ME
{
    public partial class App : Application
    {
        private DispatcherTimer _dayTimer;
        private DateTime _lastCheckedDate;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Data.DatabaseHelper.Initialize();
            ThemeService.Initialize();
            StartDayWatcher();
        }

        private void StartDayWatcher()
        {
            _lastCheckedDate = DateTime.Today;
            _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _dayTimer.Tick += (s, ev) =>
            {
                var today = DateTime.Today;
                if (today == _lastCheckedDate) return;
                _lastCheckedDate = today;
                try { SharedPomodoroService.Instance.RefreshToday(); } catch { }
                EventAggregator.Instance.Publish("DayChanged");
            };
            _dayTimer.Start();
        }
    }
}
