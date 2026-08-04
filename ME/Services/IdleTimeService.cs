using System;
using System.Collections.Generic;
using System.Linq;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    public static class IdleTimeService
    {
        private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(1);

        public static int GetIdleTagId()
        {
            return new TimeTagRepository().GetAllTags().FirstOrDefault(t => t.IsDefault)?.Id ?? 0;
        }

        /// <summary>
        /// Rebuild idle (闲时) records for a single date so that every untimed minute
        /// of that day is covered. Fully idempotent: if the computed idle set equals the
        /// existing auto-generated idle records, nothing is written. Idle records carrying
        /// a user note are preserved and treated as occupied time.
        /// </summary>
        public static void EnsureIdleRecords(DateTime date)
        {
            var idleTagId = GetIdleTagId();
            if (idleTagId == 0) return;

            var repo = new TimeRecordRepository();
            var dateStr = date.ToString("yyyy-MM-dd");
            var records = repo.GetRecordsByDate(dateStr);

            var realRecords = records.Where(r => r.TagId != idleTagId).ToList();
            var idleRecords = records.Where(r => r.TagId == idleTagId).ToList();

            // Only fill days that actually have timed records.
            if (realRecords.Count == 0)
            {
                var autoIdle = idleRecords.Where(r => string.IsNullOrEmpty(r.Note)).ToList();
                if (autoIdle.Count > 0)
                    foreach (var idl in autoIdle)
                        repo.DeleteRecord(idl.Id);
                return;
            }

            // Preserve idle records that carry a note; they count as occupied time.
            var preservedIdle = idleRecords.Where(r => !string.IsNullOrEmpty(r.Note)).ToList();
            var autoIdleRecords = idleRecords.Where(r => string.IsNullOrEmpty(r.Note)).ToList();

            var dayStart = date.Date;
            var isToday = date.Date == DateTime.Today;
            var dayEnd = isToday ? DateTime.Now : dayStart.AddDays(1);
            if (dayEnd <= dayStart) dayEnd = dayStart.AddDays(1);
            var now = DateTime.Now;

            // Occupied intervals: real records (running record ends at now) + preserved idle.
            var intervals = new List<(DateTime start, DateTime end)>();
            foreach (var r in realRecords.Concat(preservedIdle))
            {
                var start = r.StartTime < dayStart ? dayStart : r.StartTime;
                var end = r.EndTime ?? now;
                if (end > dayEnd) end = dayEnd;
                if (end <= start) continue;
                intervals.Add((start, end));
            }
            intervals.Sort((a, b) => a.start.CompareTo(b.start));
            var merged = new List<(DateTime start, DateTime end)>();
            foreach (var iv in intervals)
            {
                if (merged.Count == 0 || iv.start > merged[merged.Count - 1].end)
                    merged.Add(iv);
                else if (iv.end > merged[merged.Count - 1].end)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].start, iv.end);
            }

            // Compute desired idle gaps.
            var desired = new List<(DateTime start, DateTime end)>();
            var cursor = dayStart;
            foreach (var iv in merged)
            {
                if (iv.start > cursor && iv.start - cursor >= MinGap)
                    desired.Add((cursor, iv.start));
                if (iv.end > cursor) cursor = iv.end;
            }
            if (dayEnd > cursor && dayEnd - cursor >= MinGap)
                desired.Add((cursor, dayEnd));

            // Compare with existing auto-generated idle records; skip write if unchanged.
            var existing = autoIdleRecords
                .OrderBy(r => r.StartTime)
                .Select(r => (start: r.StartTime, end: r.EndTime ?? now))
                .ToList();
            if (existing.Count == desired.Count)
            {
                bool same = true;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (Math.Abs((existing[i].start - desired[i].start).TotalSeconds) > 1 ||
                        Math.Abs((existing[i].end - desired[i].end).TotalSeconds) > 1)
                    {
                        same = false;
                        break;
                    }
                }
                if (same) return;
            }

            // Apply: delete auto idle, insert desired.
            foreach (var idl in autoIdleRecords)
                repo.DeleteRecord(idl.Id);
            foreach (var gap in desired)
                InsertIdle(repo, idleTagId, dateStr, gap.start, gap.end);
        }

        private static void InsertIdle(TimeRecordRepository repo, int tagId, string dateStr, DateTime start, DateTime end)
        {
            repo.InsertRecord(new TimeRecord
            {
                TagId = tagId,
                StartTime = start,
                EndTime = end,
                Date = dateStr
            });
        }

        /// <summary>
        /// Rebuild idle records for every date that has timed records.
        /// </summary>
        public static void BackfillAllDates()
        {
            var repo = new TimeRecordRepository();
            var idleTagId = GetIdleTagId();
            var dates = repo.GetAllRecords()
                .Where(r => r.TagId != idleTagId)
                .Select(r => r.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            foreach (var d in dates)
            {
                try
                {
                    EnsureIdleRecords(DateTime.Parse(d));
                }
                catch
                {
                }
            }
        }
    }
}
