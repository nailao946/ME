using System;

namespace ME.Models
{
    /// <summary>
    /// 锻炼项目，存于 exercise_items.json。
    /// Frequency: "daily" 每日 / "every_other_day" 隔日 / "weekly_days" 每周指定几天
    /// Unit: "次" / "分钟" / "千卡"
    /// </summary>
    public class ExerciseItem
    {
        public int Id { get; set; }

        /// <summary>项目名称，如"跑步""俯卧撑"</summary>
        public string Name { get; set; }

        /// <summary>目标数值（每次/每天的量）</summary>
        public double TargetValue { get; set; }

        /// <summary>目标单位：次 / 分钟 / 千卡</summary>
        public string Unit { get; set; } = "次";

        /// <summary>频率：daily / every_other_day / weekly_days</summary>
        public string Frequency { get; set; } = "daily";

        /// <summary>weekly_days 时：每周第几天（1=周一 … 7=周日），逗号分隔</summary>
        public string WeeklyDays { get; set; }

        /// <summary>分类标签，如"力量""塑形"（空 = 未分类）</summary>
        public string Category { get; set; }

        /// <summary>排序序号（越小越靠前，手动排序用）</summary>
        public int SortOrder { get; set; }

        public string Note { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
