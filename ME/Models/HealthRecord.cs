using System;

namespace ME.Models
{
    /// <summary>
    /// 健康记录（睡眠/体重/喝水/心情），统一存储于 health_records.json。
    /// Type: "sleep" | "weight" | "water" | "mood"
    /// </summary>
    public class HealthRecord
    {
        public int Id { get; set; }

        /// <summary>记录类型：sleep/weight/water/mood</summary>
        public string Type { get; set; }

        /// <summary>日期 yyyy-MM-dd（睡眠记录存"醒来当天"的日期）</summary>
        public string Date { get; set; }

        /// <summary>
        /// 数值：weight=体重kg；water=当日杯数；mood=0😊/1😐/2😔/3😢；sleep=睡眠时长分钟
        /// </summary>
        public double Value { get; set; }

        /// <summary>补充信息：sleep 存 "HH:mm|HH:mm"（入睡|起床）；weight 存身高 cm；uric_acid 存测量时间 "HH:mm"；其余可放备注</summary>
        public string Detail { get; set; }

        public string Note { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
