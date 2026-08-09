using System;
using System.Collections.Generic;
using System.Linq;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    public static class TimeStatsHelper
    {
        private static readonly SettingsRepository _settingsRepo = new SettingsRepository();

        public static List<TimeRecord> FilterRecords(List<TimeRecord> records)
        {
            var includedIds = GetIncludedTagIds();
            if (includedIds.Count == 0)
                return records;
            return records.Where(r => includedIds.Contains(r.TagId)).ToList();
        }

        public static TimeSpan GetFilteredTotal(List<TimeRecord> records)
        {
            var filtered = FilterRecords(records);
            return filtered.Aggregate(TimeSpan.Zero, (a, b) => a + b.Duration);
        }

        public static Dictionary<int, TimeSpan> GetTagTimes(List<TimeRecord> records)
        {
            var includedIds = GetIncludedTagIds();
            var filtered = includedIds.Count > 0
                ? records.Where(r => includedIds.Contains(r.TagId)).ToList()
                : records;

            var tagTimes = new Dictionary<int, TimeSpan>();
            foreach (var r in filtered)
            {
                var dur = TimeSpan.FromTicks(Math.Max(0, ((r.EndTime ?? DateTime.Now) - r.StartTime).Ticks));
                if (!tagTimes.ContainsKey(r.TagId))
                    tagTimes[r.TagId] = TimeSpan.Zero;
                tagTimes[r.TagId] += dur;
            }
            return tagTimes;
        }

        public static bool IsTagIncluded(int tagId)
        {
            var includedIds = GetIncludedTagIds();
            if (includedIds.Count == 0)
                return true;
            return includedIds.Contains(tagId);
        }

        public static HashSet<int> GetIncludedTagIds()
        {
            var setting = _settingsRepo.GetValue(SettingsKeys.StatsIncludedTags);
            if (string.IsNullOrWhiteSpace(setting))
                return new HashSet<int>();
            return new HashSet<int>(setting.Split(',')
                .Select(s => { int.TryParse(s.Trim(), out var id); return id; })
                .Where(id => id > 0));
        }
    }
}
