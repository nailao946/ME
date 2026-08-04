using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    public class TimeRecordRepository
    {
        private const string FileName = "time_records";

        public List<TimeRecord> GetAllRecords()
        {
            return JsonStore.Load<TimeRecord>(FileName);
        }

        public List<TimeRecord> GetRecordsByDate(string date)
        {
            return GetAllRecords().Where(r => r.Date == date).OrderBy(r => r.StartTime).ToList();
        }

        public List<TimeRecord> GetRecordsByDateRange(string startDate, string endDate)
        {
            return GetAllRecords().Where(r => r.Date.CompareTo(startDate) >= 0 && r.Date.CompareTo(endDate) <= 0)
                .OrderBy(r => r.StartTime).ToList();
        }

        public List<TimeRecord> GetRecordsByTagId(int tagId, string startDate, string endDate)
        {
            return GetAllRecords().Where(r => r.TagId == tagId && r.Date.CompareTo(startDate) >= 0 && r.Date.CompareTo(endDate) <= 0)
                .OrderByDescending(r => r.StartTime).ToList();
        }

        public TimeRecord GetLatestRunningRecord()
        {
            return GetAllRecords().Where(r => r.IsRunning).OrderByDescending(r => r.StartTime).FirstOrDefault();
        }

        public int InsertRecord(TimeRecord record)
        {
            var records = JsonStore.Load<TimeRecord>(FileName);
            record.Id = records.Count > 0 ? records.Max(r => r.Id) + 1 : 1;
            records.Add(record);
            JsonStore.Save(FileName, records);
            return record.Id;
        }

        public void UpdateRecord(TimeRecord record)
        {
            var records = JsonStore.Load<TimeRecord>(FileName);
            var index = records.FindIndex(r => r.Id == record.Id);
            if (index >= 0)
            {
                records[index] = record;
                JsonStore.Save(FileName, records);
            }
        }

        public void UpdateRecordEndTime(int id, DateTime? endTime)
        {
            var records = JsonStore.Load<TimeRecord>(FileName);
            var record = records.FirstOrDefault(r => r.Id == id);
            if (record != null)
            {
                record.EndTime = endTime;
                JsonStore.Save(FileName, records);
            }
        }

        public void StopAllRunningRecords()
        {
            var records = JsonStore.Load<TimeRecord>(FileName);
            var now = DateTime.Now;
            bool changed = false;
            foreach (var r in records)
            {
                if (r.IsRunning)
                {
                    r.EndTime = now;
                    changed = true;
                }
            }
            if (changed) JsonStore.Save(FileName, records);
        }

        public void DeleteRecord(int id)
        {
            var records = JsonStore.Load<TimeRecord>(FileName);
            records.RemoveAll(r => r.Id == id);
            JsonStore.Save(FileName, records);
        }

        public void SaveAllRecords(List<TimeRecord> records)
        {
            JsonStore.Save(FileName, records);
        }

        public void ClearRecordsByDate(string date)
        {
            var records = JsonStore.Load<TimeRecord>(FileName);
            records.RemoveAll(r => r.Date == date);
            JsonStore.Save(FileName, records);
        }
    }
}
