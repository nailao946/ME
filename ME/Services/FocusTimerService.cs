using System;
using System.Timers;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    public class FocusTimerService
    {
        private readonly FocusSessionRepository _repo;
        private Timer _timer;
        private TimeSpan _elapsed;
        private bool _isRunning;

        public FocusTimerService()
        {
            _repo = new FocusSessionRepository();
        }

        public bool IsRunning => _isRunning;
        public TimeSpan Elapsed => _elapsed;

        public event Action<TimeSpan> Tick;

        public void Start(TimerMode mode, TimeSpan? countdownDuration = null)
        {
            if (_isRunning) return;

            _isRunning = true;
            _elapsed = mode == TimerMode.Countdown ? (countdownDuration ?? TimeSpan.FromMinutes(25)) : TimeSpan.Zero;

            _timer = new Timer(1000);
            _timer.Elapsed += OnTick;
            _timer.Start();
        }

        public void Pause()
        {
            _timer?.Stop();
            _isRunning = false;
        }

        public void Resume()
        {
            if (!_isRunning)
            {
                _isRunning = true;
                _timer?.Start();
            }
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _isRunning = false;
        }

        public FocusSession SaveSession(int? goalId, int? taskId, TimerMode mode)
        {
            var session = new FocusSession
            {
                GoalId = goalId,
                TaskId = taskId,
                Mode = mode,
                Duration = _elapsed,
                StartTime = DateTime.Now - _elapsed,
                EndTime = DateTime.Now,
                IsCompleted = true
            };

            _repo.InsertSession(session);
            _elapsed = TimeSpan.Zero;
            return session;
        }

        public TimeSpan GetTotalFocusTime(int goalId) => _repo.GetTotalFocusTimeByGoalId(goalId);

        private void OnTick(object sender, ElapsedEventArgs e)
        {
            Tick?.Invoke(_elapsed);

            if (_elapsed.TotalSeconds > 0)
            {
                _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
            }
        }
    }
}
