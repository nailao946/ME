using System;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    public static class SharedTimerService
    {
        private static readonly TimeTimerService _timer;
        private static readonly TimeRecordRepository _recordRepo;
        private static readonly TimeTagRepository _tagRepo;
        private static int _selectedTagId;
        private static TimeRecord _currentRecord;
        private static string _cachedTagName = "未计时";
        private static string _cachedTagColor = "#808080";

        public static event Action<string, string, string> TimerUpdated;
        public static event Action<bool> RunningStateChanged;
        public static event Action<bool> PausedStateChanged;
        public static event Action<PomodoroPhase, int> PomodoroPhaseChanged;

        public static TimeTimerService Timer => _timer;
        public static int SelectedTagId => _selectedTagId;
        public static TimeRecord CurrentRecord => _currentRecord;
        public static bool IsRunning => _timer.State == TimeTimerState.Running;
        public static bool IsPaused => _timer.State == TimeTimerState.Paused;

        static SharedTimerService()
        {
            _timer = new TimeTimerService();
            _recordRepo = new TimeRecordRepository();
            _tagRepo = new TimeTagRepository();
            _selectedTagId = 0;
            _currentRecord = null;

            _timer.Tick += OnTick;

            _timer.PomodoroPhaseCompleted += () =>
            {
                SoundService.PlayCompletionSound();
            };

            _timer.PomodoroPhaseChanged += (phase, pomodoroNumber) =>
            {
                PomodoroPhaseChanged?.Invoke(phase, pomodoroNumber);
            };
        }

        private static void OnTick(TimeSpan time)
        {
            var timeStr = _timer.Mode == TimeTimerMode.CountDown
                ? $"{time.Minutes:D2}:{time.Seconds:D2}"
                : $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
            TimerUpdated?.Invoke(timeStr, _cachedTagName, _cachedTagColor);
        }

        public static void StartWithTag(int tagId)
        {
            StopCurrent();
            _selectedTagId = tagId;
            var tag = _tagRepo.GetTagById(tagId);
            _cachedTagName = tag?.Name ?? "未计时";
            _cachedTagColor = tag?.Color ?? "#808080";
            _recordRepo.StopAllRunningRecords();
            var now = DateTime.Now;
            var record = new TimeRecord
            {
                TagId = tagId,
                StartTime = now,
                Date = now.ToString("yyyy-MM-dd")
            };
            record.Id = _recordRepo.InsertRecord(record);
            _currentRecord = record;
            _timer.Reset();
            _timer.Start();
            RunningStateChanged?.Invoke(true);
        }

        public static void StopCurrent()
        {
            if (_currentRecord != null)
            {
                var now = DateTime.Now;
                var startDate = _currentRecord.StartTime.ToString("yyyy-MM-dd");
                var endDate = now.ToString("yyyy-MM-dd");
                if (startDate != endDate)
                {
                    var mid = startDate + " 23:59:59";
                    _recordRepo.UpdateRecordEndTime(_currentRecord.Id, DateTime.Parse(mid));
                    var nextDay = DateTime.Parse(endDate + " 00:00:00");
                    var newRecord = new TimeRecord
                    {
                        TagId = _currentRecord.TagId,
                        StartTime = nextDay,
                        EndTime = now,
                        Date = endDate
                    };
                    _recordRepo.InsertRecord(newRecord);
                }
                else
                {
                    _recordRepo.UpdateRecordEndTime(_currentRecord.Id, now);
                }
                _currentRecord = null;
            }
            _selectedTagId = 0;
            if (_timer.State == TimeTimerState.Paused)
                PausedStateChanged?.Invoke(false);
            _timer.Stop();
            _timer.Reset();
            _timer.IsPomodoroMode = false;
            RunningStateChanged?.Invoke(false);
        }

        public static void PauseCurrent()
        {
            if (_timer.State == TimeTimerState.Running)
            {
                _timer.Pause();
                PausedStateChanged?.Invoke(true);
            }
        }

        public static void ResumeCurrent()
        {
            if (_timer.State == TimeTimerState.Paused)
            {
                _timer.Resume();
                PausedStateChanged?.Invoke(false);
            }
        }

        public static void CheckRunningState()
        {
            var running = _recordRepo.GetLatestRunningRecord();
            if (running != null)
            {
                _currentRecord = running;
                _selectedTagId = running.TagId;
                var tag = _tagRepo.GetTagById(running.TagId);
                _cachedTagName = tag?.Name ?? "未计时";
                _cachedTagColor = tag?.Color ?? "#808080";
                _timer.SetElapsed(DateTime.Now - running.StartTime);
                _timer.Start();
                RunningStateChanged?.Invoke(true);
            }
        }
    }
}
