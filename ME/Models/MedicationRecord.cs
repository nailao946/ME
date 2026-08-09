using System;
using System.Collections.Generic;
using System.Linq;

namespace ME.Models
{
    /// <summary>药品剂型</summary>
    public enum MedicationType
    {
        Capsule,    // 胶囊
        Tablet,     // 药片
        Liquid,     // 液体
        Topical,    // 外用
        Inhaler,    // 吸入
        Powder,     // 粉末
        Injection,  // 注射
        Drop,       // 滴剂
        Patch,      // 贴剂
        Other       // 其他
    }

    /// <summary>规格单位</summary>
    public enum MedicationUnit
    {
        Ml,     // 毫升
        Mg,     // 毫克
        G,      // 克
        Mcg,    // 微克
        Percent // %
    }

    /// <summary>用药频率</summary>
    public enum MedicationFrequency
    {
        Daily,          // 每天
        EveryNDays,     // 每隔 N 天
        WeeklyDays,     // 每周特定日期
        Interval,       // 循环定时（每 N 小时）
        AsNeeded        // 按需
    }

    /// <summary>用药记录，存于 medications.json</summary>
    public class MedicationRecord
    {
        public int Id { get; set; }

        /// <summary>药名</summary>
        public string Name { get; set; }

        public MedicationType Type { get; set; } = MedicationType.Tablet;

        /// <summary>规格数值（如 500、10、5）</summary>
        public double SpecValue { get; set; }

        public MedicationUnit Unit { get; set; } = MedicationUnit.Mg;

        public MedicationFrequency Frequency { get; set; } = MedicationFrequency.Daily;

        /// <summary>EveryNDays：间隔天数；Interval：间隔小时数</summary>
        public int FrequencyN { get; set; } = 1;

        /// <summary>WeeklyDays：每周第几天（1=周一 … 7=周日），逗号分隔</summary>
        public string WeeklyDays { get; set; }

        /// <summary>用药时间点列表（HH:mm，逗号分隔，可多个）</summary>
        public string Times { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string Note { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>是否处于有效期内</summary>
        public bool IsActive => !IsDeleted && (!StartDate.HasValue || StartDate.Value.Date <= DateTime.Today) &&
                                (!EndDate.HasValue || EndDate.Value.Date >= DateTime.Today);
    }
}
