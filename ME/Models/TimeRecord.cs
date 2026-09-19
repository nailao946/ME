using System;

namespace ME.Models
{
    public class TimeRecord
    {
        public int Id { get; set; }
        public int TagId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Date { get; set; }
        public string Note { get; set; }
        /// <summary>跨设备合并用全局唯一标识（旧数据无此字段，保存/合并时自动补）</summary>
        public string Uid { get; set; }

        public TimeSpan Duration
        {
            get
            {
                if (EndTime.HasValue)
                    return TimeSpan.FromTicks(Math.Max(0, (EndTime.Value - StartTime).Ticks));
                return TimeSpan.FromTicks(Math.Max(0, (DateTime.Now - StartTime).Ticks));
            }
        }

        public bool IsRunning => !EndTime.HasValue;
    }
}
