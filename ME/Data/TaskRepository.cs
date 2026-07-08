using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    public class TaskRepository
    {
        private const string FileName = "tasks";

        public List<TaskItem> GetAllTasks(bool includeDeleted = false)
        {
            var tasks = JsonStore.Load<TaskItem>(FileName);
            return includeDeleted ? tasks : tasks.Where(t => !t.IsDeleted).OrderByDescending(t => t.Priority).ThenBy(t => t.SortOrder).ToList();
        }

        public List<TaskItem> GetTasksByGoalId(int goalId)
        {
            return JsonStore.Load<TaskItem>(FileName)
                .Where(t => !t.IsDeleted && t.GoalId == goalId)
                .OrderByDescending(t => t.Priority).ThenBy(t => t.SortOrder).ToList();
        }

        public List<TaskItem> GetTasksByType(TaskType type)
        {
            return JsonStore.Load<TaskItem>(FileName)
                .Where(t => !t.IsDeleted && t.Type == type)
                .OrderByDescending(t => t.CreatedAt).ToList();
        }

        public List<TaskItem> GetTodayTasks()
        {
            var today = DateTime.Today;
            return JsonStore.Load<TaskItem>(FileName)
                .Where(t => !t.IsDeleted && !t.IsCompleted &&
                    (t.Type == TaskType.OneTime ||
                    (t.Type == TaskType.Quantitative && t.RecurringPattern.HasValue) ||
                    (t.StartDate.HasValue && t.StartDate.Value.Date <= today) ||
                    (t.EndDate.HasValue && t.EndDate.Value.Date >= today)))
                .OrderByDescending(t => t.Priority).ThenBy(t => t.SortOrder).ToList();
        }

        public TaskItem GetTaskById(int id)
        {
            return JsonStore.Load<TaskItem>(FileName).FirstOrDefault(t => t.Id == id);
        }

        public int InsertTask(TaskItem task)
        {
            var tasks = JsonStore.Load<TaskItem>(FileName);
            var maxId = tasks.Count > 0 ? tasks.Max(t => t.Id) : 0;
            task.Id = maxId + 1;
            task.CreatedAt = task.CreatedAt == default ? DateTime.Now : task.CreatedAt;
            task.UpdatedAt = DateTime.Now;
            tasks.Add(task);
            JsonStore.Save(FileName, tasks);
            return task.Id;
        }

        public void UpdateTask(TaskItem task)
        {
            var tasks = JsonStore.Load<TaskItem>(FileName);
            var index = tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0)
            {
                task.UpdatedAt = DateTime.Now;
                tasks[index] = task;
                JsonStore.Save(FileName, tasks);
            }
        }

        public void SoftDeleteTask(int id)
        {
            var tasks = JsonStore.Load<TaskItem>(FileName);
            var task = tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.IsDeleted = true;
                task.DeletedAt = DateTime.Now;
                JsonStore.Save(FileName, tasks);
            }
        }

        public void RestoreTask(int id)
        {
            var tasks = JsonStore.Load<TaskItem>(FileName);
            var task = tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.IsDeleted = false;
                task.DeletedAt = default;
                JsonStore.Save(FileName, tasks);
            }
        }

        public void PermanentlyDeleteTask(int id)
        {
            var tasks = JsonStore.Load<TaskItem>(FileName);
            tasks.RemoveAll(t => t.Id == id);
            JsonStore.Save(FileName, tasks);
        }
    }
}
