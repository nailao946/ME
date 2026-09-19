using System;

namespace ME.Models
{
    public class TaskCompletionRecord
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public string Date { get; set; } // "yyyy-MM-dd"
        public DateTime CompletedAt { get; set; }
        /// <summary>跨设备合并用全局唯一标识（旧数据无此字段，保存/合并时自动补）</summary>
        public string Uid { get; set; }
    }
}
