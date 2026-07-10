using System;
using System.Windows.Threading;
using ME.Data;

namespace ME.Services
{
    public enum UnifiedTimerMode { Simple, Pomodoro }
    public enum PomodoroState { Idle, Running, Paused }
    public enum PomodoroPhase { Work, ShortBreak, LongBreak }

    public class PomodoroService : IDisposable
    {
        private DispatcherTimer _timer;
        private DateTime _sessionStart;
        private TimeSpan _accumulated;
        private TimeSpan _pausedRemaining;
        private int _workMinutes;
        private int _shortBreakMinutes;
        private int _longBreakMinutes;
        private int _beforeLongBreak;
        private bool _autoStartBreaks;
        private bool _autoStartPomodoros;

        public event Action<string, UnifiedTimerMode> TimerUpdated;
        public event Action<PomodoroState> StateChanged;
        public event Action<PomodoroPhase, int, int> PhaseChanged;
        public event Action PhaseCompleted;

        public UnifiedTimerMode Mode { get; set; } = UnifiedTimerMode.Simple;
        public PomodoroState State { get; private set; } = PomodoroState.Idle;
        public PomodoroPhase Phase { get; set; } = PomodoroPhase.Work;
        public TimeSpan Current { get; set; }
        public int TotalCompletions { get; private set; }
        public int CycleCount { get; private set; }
        public int TodayCompletions { get; private set; }
        public string Today { get; private set; }

        public int WorkMinutes
        {
            get => _workMinutes;
            set { _workMinutes = Math.Max(1, value); PersistSetting("PomodoroWorkMinutes", value.ToString()); }
        }
        public int ShortBreakMinutes
        {
            get => _shortBreakMinutes;
            set { _shortBreakMinutes = Math.Max(1, value); PersistSetting("PomodoroShortBreakMinutes", value.ToString()); }
        }
        public int LongBreakMinutes
        {
            get => _longBreakMinutes;
            set { _longBreakMinutes = Math.Max(1, value); PersistSetting("PomodoroLongBreakMinutes", value.ToString()); }
        }
        public int BeforeLongBreak
        {
            get => _beforeLongBreak;
            set { _beforeLongBreak = Math.Max(1, value); PersistSetting("PomodoroBeforeLongBreak", value.ToString()); }
        }
        public bool AutoStartBreaks
        {
            get => _autoStartBreaks;
            set { _autoStartBreaks = value; PersistSetting("PomodoroAutoStartBreaks", value ? "1" : "0"); }
        }
        public bool AutoStartPomodoros
        {
            get => _autoStartPomodoros;
            set { _autoStartPomodoros = value; PersistSetting("PomodoroAutoStartPomodoros", value ? "1" : "0"); }
        }

        public int SelectedTagId { get; set; }
        public string SelectedTagName { get; set; } = "未计时";
        public string SelectedTagColor { get; set; } = "#808080";

        public PomodoroService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
            Today = DateTime.Now.ToString("yyyy-MM-dd");
            LoadSettings();
        }

        private void LoadSettings()
        {
            var repo = new SettingsRepository();
            _workMinutes = int.TryParse(repo.GetValue("PomodoroWorkMinutes", "25"), out var w) ? Math.Max(1, w) : 25;
            _shortBreakMinutes = int.TryParse(repo.GetValue("PomodoroShortBreakMinutes", "5"), out var sb) ? Math.Max(1, sb) : 5;
            _longBreakMinutes = int.TryParse(repo.GetValue("PomodoroLongBreakMinutes", "15"), out var lb) ? Math.Max(1, lb) : 15;
            _beforeLongBreak = int.TryParse(repo.GetValue("PomodoroBeforeLongBreak", "4"), out var bl) ? Math.Max(1, bl) : 4;
            _autoStartBreaks = repo.GetValue("PomodoroAutoStartBreaks", "1") == "1";
            _autoStartPomodoros = repo.GetValue("PomodoroAutoStartPomodoros", "0") == "1";
        }

        private void PersistSetting(string key, string value)
        {
            try { new SettingsRepository().SetValue(key, value); } catch { }
        }

        public string FormatTime()
        {
            if (Mode == UnifiedTimerMode.Pomodoro)
                return $"{Current.Minutes:D2}:{Current.Seconds:D2}";
            var h = (int)Current.TotalHours;
            return $"{h:D2}:{Current.Minutes:D2}:{Current.Seconds:D2}";
        }

        public double PhaseDurationSeconds
        {
            get
            {
                if (Mode == UnifiedTimerMode.Simple) return 0;
                return TimeSpan.FromMinutes(Phase switch
                {
                    PomodoroPhase.Work => _workMinutes,
                    PomodoroPhase.ShortBreak => _shortBreakMinutes,
                    PomodoroPhase.LongBreak => _longBreakMinutes,
                    _ => _workMinutes
                }).TotalSeconds;
            }
        }

        public void Start()
        {
            if (State != PomodoroState.Idle) return;
            var now = DateTime.Now;
            if (now.ToString("yyyy-MM-dd") != Today)
            {
                Today = now.ToString("yyyy-MM-dd");
                TodayCompletions = 0;
            }
            _sessionStart = now;
            _accumulated = TimeSpan.Zero;
            State = PomodoroState.Running;
            Current = Mode == UnifiedTimerMode.Pomodoro ? GetPhaseDuration() : TimeSpan.Zero;
            _timer.Start();
            StateChanged?.Invoke(State);
            FireTimerUpdated();
        }

        public void Pause()
        {
            if (State != PomodoroState.Running) return;
            _timer.Stop();
            _accumulated = Current;
            State = PomodoroState.Paused;
            StateChanged?.Invoke(State);
        }

        public void Resume()
        {
            if (State != PomodoroState.Paused) return;
            _sessionStart = DateTime.Now;
            _pausedRemaining = _accumulated;
            State = PomodoroState.Running;
            _timer.Start();
            StateChanged?.Invoke(State);
            FireTimerUpdated();
        }

        public void Stop()
        {
            if (State == PomodoroState.Idle) return;
            _timer.Stop();
            State = PomodoroState.Idle;
            if (Mode == UnifiedTimerMode.Pomodoro)
            {
                Current = GetPhaseDuration();
                PhaseChanged?.Invoke(Phase, TotalCompletions, CycleCount);
            }
            else
            {
                Current = TimeSpan.Zero;
            }
            StateChanged?.Invoke(State);
            FireTimerUpdated();
        }

        public void Skip()
        {
            if (State == PomodoroState.Idle || Mode == UnifiedTimerMode.Simple) return;
            _timer.Stop();
            AdvancePhase();
        }

        public void AddTime(int minutes)
        {
            if (State != PomodoroState.Running || Mode == UnifiedTimerMode.Simple) return;
            Current = Current.Add(TimeSpan.FromMinutes(minutes));
            FireTimerUpdated();
        }

        public void SetPhase(PomodoroPhase phase)
        {
            if (State != PomodoroState.Idle)
            {
                _timer.Stop();
                State = PomodoroState.Idle;
                StateChanged?.Invoke(State);
            }
            Phase = phase;
            Current = GetPhaseDuration();
            PhaseChanged?.Invoke(Phase, TotalCompletions, CycleCount);
            FireTimerUpdated();
        }

        public void ResetCount()
        {
            CycleCount = 0;
            PhaseChanged?.Invoke(Phase, TotalCompletions, CycleCount);
        }

        public void ResetTodayCount()
        {
            TodayCompletions = 0;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Mode == UnifiedTimerMode.Pomodoro)
            {
                if (State == PomodoroState.Running)
                {
                    var totalElapsed = DateTime.Now - _sessionStart;
                    Current = GetPhaseDuration() - totalElapsed;
                    if (Current.TotalSeconds <= 0)
                    {
                        Current = TimeSpan.Zero;
                        _timer.Stop();
                        FireTimerUpdated();
                        OnPhaseCompleted();
                        return;
                    }
                }
            }
            else
            {
                var totalElapsed = DateTime.Now - _sessionStart + _pausedRemaining;
                Current = totalElapsed;
            }

            FireTimerUpdated();
        }

        private void OnPhaseCompleted()
        {
            PhaseCompleted?.Invoke();
            SoundService.PlayCompletionSound();
            AdvancePhase();
        }

        private void AdvancePhase()
        {
            if (Phase == PomodoroPhase.Work)
            {
                TotalCompletions++;
                TodayCompletions++;
                CycleCount++;
                Phase = (CycleCount >= _beforeLongBreak)
                    ? PomodoroPhase.LongBreak
                    : PomodoroPhase.ShortBreak;
            }
            else
            {
                if (Phase == PomodoroPhase.LongBreak)
                    CycleCount = 0;
                Phase = PomodoroPhase.Work;
            }

            State = PomodoroState.Idle;
            Current = GetPhaseDuration();
            PhaseChanged?.Invoke(Phase, TotalCompletions, CycleCount);
            StateChanged?.Invoke(State);
            FireTimerUpdated();

            if (State == PomodoroState.Idle)
            {
                bool shouldAutoStart = Phase == PomodoroPhase.Work
                    ? _autoStartPomodoros : _autoStartBreaks;
                if (shouldAutoStart)
                {
                    _sessionStart = DateTime.Now;
                    _accumulated = TimeSpan.Zero;
                    State = PomodoroState.Running;
                    _timer.Start();
                    StateChanged?.Invoke(State);
                }
            }
        }

        private TimeSpan GetPhaseDuration()
        {
            return TimeSpan.FromMinutes(Phase switch
            {
                PomodoroPhase.Work => _workMinutes,
                PomodoroPhase.ShortBreak => _shortBreakMinutes,
                PomodoroPhase.LongBreak => _longBreakMinutes,
                _ => _workMinutes
            });
        }

        private void FireTimerUpdated()
        {
            TimerUpdated?.Invoke(FormatTime(), Mode);
        }

        public void Dispose()
        {
            _timer?.Stop();
        }
    }
}
