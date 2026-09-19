using System;

namespace ME.Models
{
    public enum TimerMode
    {
        Stopwatch,
        Countdown
    }

    public class FocusSession
    {
        public int Id { get; set; }
        public int? GoalId { get; set; }
        public int? TaskId { get; set; }
        public TimerMode Mode { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsCompleted { get; set; }
        public string Notes { get; set; }
        /// <summary>跨设备合并用全局唯一标识（旧数据无此字段，保存/合并时自动补）</summary>
        public string Uid { get; set; }
    }
}
