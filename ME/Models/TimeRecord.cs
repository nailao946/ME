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

        public TimeSpan Duration
        {
            get
            {
                if (EndTime.HasValue)
                    return EndTime.Value - StartTime;
                return DateTime.Now - StartTime;
            }
        }

        public bool IsRunning => !EndTime.HasValue;
    }
}
