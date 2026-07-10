using System;
using System.Timers;

namespace ME.Services
{
    public enum TimeTimerMode
    {
        CountUp,
        CountDown
    }

    public enum TimeTimerState
    {
        Stopped,
        Running,
        Paused
    }

    public class TimeTimerService : IDisposable
    {
        private Timer _timer;
        private DateTime _sessionStart;
        private TimeSpan _accumulated;
        private TimeSpan _countdownTarget;

        public event Action<TimeSpan> Tick;
        public event Action CountdownFinished;

        public TimeTimerMode Mode { get; private set; } = TimeTimerMode.CountUp;
        public TimeTimerState State { get; private set; } = TimeTimerState.Stopped;
        public TimeSpan Current { get; private set; } = TimeSpan.Zero;
        public int FocusMinutes { get; set; } = 25;

        public TimeTimerService()
        {
            _timer = new Timer(1000);
            _timer.Elapsed += OnTimerElapsed;
        }

        public void SetMode(TimeTimerMode mode)
        {
            Mode = mode;
            Reset();
        }

        public void Start()
        {
            _sessionStart = DateTime.Now;
            State = TimeTimerState.Running;
            _timer.Start();
        }

        public void SetElapsed(TimeSpan elapsed)
        {
            if (Mode == TimeTimerMode.CountUp)
            {
                _accumulated = elapsed;
            }
            else
            {
                _accumulated = elapsed;
                _countdownTarget = TimeSpan.FromMinutes(FocusMinutes);
            }
        }

        public void Pause()
        {
            if (State == TimeTimerState.Running)
            {
                _accumulated = Current;
                State = TimeTimerState.Paused;
                _timer.Stop();
            }
        }

        public void Resume()
        {
            if (State == TimeTimerState.Paused)
            {
                _sessionStart = DateTime.Now;
                State = TimeTimerState.Running;
                _timer.Start();
            }
        }

        public void Stop()
        {
            _timer.Stop();
            State = TimeTimerState.Stopped;
        }

        public void Reset()
        {
            _timer.Stop();
            State = TimeTimerState.Stopped;
            _accumulated = TimeSpan.Zero;
            Current = TimeSpan.Zero;
            if (Mode == TimeTimerMode.CountDown)
                _countdownTarget = TimeSpan.FromMinutes(FocusMinutes);
            Tick?.Invoke(Current);
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            var elapsed = DateTime.Now - _sessionStart + _accumulated;

            if (Mode == TimeTimerMode.CountUp)
            {
                Current = elapsed;
            }
            else
            {
                var remaining = _countdownTarget - elapsed;
                Current = remaining.TotalSeconds <= 0 ? TimeSpan.Zero : remaining;
            }

            Tick?.Invoke(Current);

            if (Mode == TimeTimerMode.CountDown && Current.TotalSeconds <= 0 && State == TimeTimerState.Running)
            {
                _timer.Stop();
                State = TimeTimerState.Stopped;
                CountdownFinished?.Invoke();
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
