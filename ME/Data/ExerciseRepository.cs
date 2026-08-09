using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    /// <summary>锻炼项目仓库（exercise_items.json）</summary>
    public class ExerciseRepository
    {
        private const string FileName = "exercise_items";

        public List<ExerciseItem> GetAll()
        {
            return JsonStore.Load<ExerciseItem>(FileName)
                .Where(m => !m.IsDeleted)
                .OrderBy(m => m.CreatedAt).ToList();
        }

        public ExerciseItem GetById(int id)
        {
            return JsonStore.Load<ExerciseItem>(FileName).FirstOrDefault(m => m.Id == id && !m.IsDeleted);
        }

        public int Insert(ExerciseItem item)
        {
            var items = JsonStore.Load<ExerciseItem>(FileName);
            var maxId = items.Count > 0 ? items.Max(m => m.Id) : 0;
            item.Id = maxId + 1;
            item.CreatedAt = DateTime.Now;
            items.Add(item);
            JsonStore.Save(FileName, items);
            return item.Id;
        }

        public void Update(ExerciseItem item)
        {
            var items = JsonStore.Load<ExerciseItem>(FileName);
            var existing = items.FirstOrDefault(m => m.Id == item.Id);
            if (existing != null)
            {
                existing.Name = item.Name;
                existing.TargetValue = item.TargetValue;
                existing.Unit = item.Unit;
                existing.Frequency = item.Frequency;
                existing.WeeklyDays = item.WeeklyDays;
                existing.Note = item.Note;
            }
            JsonStore.Save(FileName, items);
        }

        public void Delete(int id)
        {
            var items = JsonStore.Load<ExerciseItem>(FileName);
            var existing = items.FirstOrDefault(m => m.Id == id);
            if (existing != null) existing.IsDeleted = true;
            JsonStore.Save(FileName, items);
        }

        // ---- 展示辅助 ----

        public static string FrequencyName(string frequency)
        {
            switch (frequency)
            {
                case "every_other_day": return "隔日";
                case "weekly_days": return "每周指定";
                default: return "每日";
            }
        }

        public static string FrequencyDesc(ExerciseItem item)
        {
            switch (item.Frequency)
            {
                case "every_other_day": return "隔日一次";
                case "weekly_days":
                {
                    if (string.IsNullOrEmpty(item.WeeklyDays)) return "每周指定";
                    var names = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
                    return "每周" + string.Join("、", item.WeeklyDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out var d) && d >= 1 && d <= 7 ? names[d - 1] : s));
                }
                default: return "每日";
            }
        }

        /// <summary>今天是否该做该项目（每日=是；隔日=今天或昨天达标过即可；每周指定=今天在周几列表中）</summary>
        public static bool IsDueToday(ExerciseItem item, bool doneYesterday)
        {
            switch (item.Frequency)
            {
                case "every_other_day":
                    return !doneYesterday; // 隔日：昨天达标过 → 今天不需要；昨天没做 → 今天该做
                case "weekly_days":
                {
                    var todayDow = ((int)DateTime.Today.DayOfWeek + 6) % 7 + 1; // 周一=1 … 周日=7
                    return (item.WeeklyDays ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Any(s => int.TryParse(s, out var d) && d == todayDow);
                }
                default:
                    return true;
            }
        }

        /// <summary>目标文字，如 "30 分钟"</summary>
        public static string TargetText(ExerciseItem item)
        {
            return $"{item.TargetValue:0.##} {item.Unit}";
        }
    }
}
