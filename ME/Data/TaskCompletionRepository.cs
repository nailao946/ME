using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    public class TaskCompletionRepository
    {
        private const string FileName = "task_completions";

        public List<TaskCompletionRecord> GetAll()
        {
            return JsonStore.Load<TaskCompletionRecord>(FileName);
        }

        public List<TaskCompletionRecord> GetByTaskId(int taskId)
        {
            return GetAll().Where(r => r.TaskId == taskId).ToList();
        }

        public TaskCompletionRecord GetByTaskAndDate(int taskId, string date)
        {
            return GetAll().FirstOrDefault(r => r.TaskId == taskId && r.Date == date);
        }

        public bool IsCompletedOnDate(int taskId, string date)
        {
            return GetAll().Any(r => r.TaskId == taskId && r.Date == date);
        }

        public int CountCompletedDays(int taskId)
        {
            return GetAll().Count(r => r.TaskId == taskId);
        }

        public int CountCompletedDaysInRange(int taskId, string startDate, string endDate)
        {
            return GetAll().Count(r => r.TaskId == taskId
                && string.Compare(r.Date, startDate) >= 0
                && string.Compare(r.Date, endDate) <= 0);
        }

        public int Insert(TaskCompletionRecord record)
        {
            var records = GetAll();
            var maxId = records.Count > 0 ? records.Max(r => r.Id) : 0;
            record.Id = maxId + 1;
            record.CompletedAt = record.CompletedAt == default ? DateTime.Now : record.CompletedAt;
            record.Uid = string.IsNullOrEmpty(record.Uid) ? Guid.NewGuid().ToString("N") : record.Uid;
            records.Add(record);
            JsonStore.Save(FileName, records);
            return record.Id;
        }

        public void Delete(int id)
        {
            var records = GetAll();
            records.RemoveAll(r => r.Id == id);
            JsonStore.Save(FileName, records);
        }

        public void DeleteByTaskAndDate(int taskId, string date)
        {
            var records = GetAll();
            records.RemoveAll(r => r.TaskId == taskId && r.Date == date);
            JsonStore.Save(FileName, records);
        }
    }
}
