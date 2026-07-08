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

    public enum PomodoroPhase
    {
        Work,
        ShortBreak,
        LongBreak
    }

    public class TimeTimerService : IDisposable
    {
        private Timer _timer;
        private DateTime _sessionStart;
        private TimeSpan _accumulated;
        private TimeSpan _countdownTarget;

        public event Action<TimeSpan> Tick;
        public event Action CountdownFinished;
        public event Action PomodoroPhaseCompleted;
        public event Action<PomodoroPhase, int> PomodoroPhaseChanged;

        public TimeTimerMode Mode { get; private set; } = TimeTimerMode.CountUp;
        public TimeTimerState State { get; private set; } = TimeTimerState.Stopped;
        public TimeSpan Current { get; private set; } = TimeSpan.Zero;
        public int FocusMinutes { get; set; } = 25;

        public PomodoroPhase CurrentPhase { get; private set; } = PomodoroPhase.Work;
        public int CurrentPomodoro { get; private set; } = 1;
        public int ShortBreakMinutes { get; set; } = 5;
        public int LongBreakMinutes { get; set; } = 15;
        public int PomodorosBeforeLongBreak { get; set; } = 4;
        public bool AutoStartBreaks { get; set; } = true;
        public bool AutoStartPomodoros { get; set; } = true;
        public bool IsPomodoroMode { get; set; } = false;

        public TimeTimerService()
        {
            _timer = new Timer(1000);
            _timer.Elapsed += OnTimerElapsed;
            PomodoroPhaseCompleted += InternalOnPomodoroPhaseCompleted;
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
            {
                if (IsPomodoroMode)
                    _countdownTarget = TimeSpan.FromMinutes(GetCurrentPhaseMinutes());
                else
                    _countdownTarget = TimeSpan.FromMinutes(FocusMinutes);
            }
            Tick?.Invoke(Current);
        }

        public void StartPomodoro()
        {
            IsPomodoroMode = true;
            CurrentPhase = PomodoroPhase.Work;
            CurrentPomodoro = 1;
            Mode = TimeTimerMode.CountDown;
            _countdownTarget = TimeSpan.FromMinutes(FocusMinutes);
            _accumulated = TimeSpan.Zero;
            Current = _countdownTarget;
            State = TimeTimerState.Stopped;
            Tick?.Invoke(Current);
            PomodoroPhaseChanged?.Invoke(CurrentPhase, CurrentPomodoro);
        }

        public void SetPhase(PomodoroPhase phase)
        {
            CurrentPhase = phase;
            _accumulated = TimeSpan.Zero;
            _countdownTarget = TimeSpan.FromMinutes(GetCurrentPhaseMinutes());
            Current = _countdownTarget;
            State = TimeTimerState.Stopped;
            _timer.Stop();
            Tick?.Invoke(Current);
            PomodoroPhaseChanged?.Invoke(CurrentPhase, CurrentPomodoro);
        }

        private int GetCurrentPhaseMinutes()
        {
            return CurrentPhase switch
            {
                PomodoroPhase.Work => FocusMinutes,
                PomodoroPhase.ShortBreak => ShortBreakMinutes,
                PomodoroPhase.LongBreak => LongBreakMinutes,
                _ => FocusMinutes
            };
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

                if (IsPomodoroMode)
                {
                    PomodoroPhaseCompleted?.Invoke();
                }
                else
                {
                    CountdownFinished?.Invoke();
                }
            }
        }

        public void AdvancePomodoroPhase()
        {
            if (CurrentPhase == PomodoroPhase.Work)
            {
                if (CurrentPomodoro >= PomodorosBeforeLongBreak)
                {
                    CurrentPhase = PomodoroPhase.LongBreak;
                    CurrentPomodoro = 1;
                }
                else
                {
                    CurrentPhase = PomodoroPhase.ShortBreak;
                }
            }
            else
            {
                if (CurrentPhase == PomodoroPhase.LongBreak)
                    CurrentPomodoro = 1;
                else
                    CurrentPomodoro++;
                CurrentPhase = PomodoroPhase.Work;
            }

            _accumulated = TimeSpan.Zero;
            _countdownTarget = TimeSpan.FromMinutes(GetCurrentPhaseMinutes());
            Current = _countdownTarget;
            State = TimeTimerState.Stopped;
            Tick?.Invoke(Current);
            PomodoroPhaseChanged?.Invoke(CurrentPhase, CurrentPomodoro);
        }

        private void InternalOnPomodoroPhaseCompleted()
        {
            AdvancePomodoroPhase();
            if (ShouldAutoStart())
            {
                Start();
            }
        }

        public bool ShouldAutoStart()
        {
            if (CurrentPhase == PomodoroPhase.Work)
                return AutoStartPomodoros;
            else
                return AutoStartBreaks;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
