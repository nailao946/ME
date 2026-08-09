using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    public class HealthRepository
    {
        private const string FileName = "health_records";

        public List<HealthRecord> GetAll()
        {
            return JsonStore.Load<HealthRecord>(FileName).ToList();
        }

        public List<HealthRecord> GetByType(string type)
        {
            return GetAll().Where(r => r.Type == type)
                .OrderBy(r => r.Date).ToList();
        }

        public List<HealthRecord> GetByTypeDateRange(string type, string startDate, string endDate)
        {
            return GetAll().Where(r => r.Type == type &&
                                       string.CompareOrdinal(r.Date, startDate) >= 0 &&
                                       string.CompareOrdinal(r.Date, endDate) <= 0)
                .OrderBy(r => r.Date).ToList();
        }

        public HealthRecord GetByTypeAndDate(string type, string date)
        {
            return GetAll().FirstOrDefault(r => r.Type == type && r.Date == date);
        }

        public int Insert(HealthRecord record)
        {
            var records = JsonStore.Load<HealthRecord>(FileName);
            var maxId = records.Count > 0 ? records.Max(r => r.Id) : 0;
            record.Id = maxId + 1;
            record.CreatedAt = DateTime.Now;
            records.Add(record);
            JsonStore.Save(FileName, records);
            return record.Id;
        }

        public void Upsert(HealthRecord record)
        {
            var records = JsonStore.Load<HealthRecord>(FileName);
            var existing = records.FirstOrDefault(r => r.Type == record.Type && r.Date == record.Date);
            if (existing != null)
            {
                existing.Value = record.Value;
                existing.Detail = record.Detail;
                existing.Note = record.Note;
            }
            else
            {
                var maxId = records.Count > 0 ? records.Max(r => r.Id) : 0;
                record.Id = maxId + 1;
                record.CreatedAt = DateTime.Now;
                records.Add(record);
            }
            JsonStore.Save(FileName, records);
        }

        public void Delete(int id)
        {
            var records = JsonStore.Load<HealthRecord>(FileName);
            var target = records.FirstOrDefault(r => r.Id == id);
            if (target != null)
            {
                records.Remove(target);
                JsonStore.Save(FileName, records);
            }
        }

        public void DeleteByTypeAndDate(string type, string date)
        {
            var records = JsonStore.Load<HealthRecord>(FileName);
            var target = records.FirstOrDefault(r => r.Type == type && r.Date == date);
            if (target != null)
            {
                records.Remove(target);
                JsonStore.Save(FileName, records);
            }
        }
    }
}
