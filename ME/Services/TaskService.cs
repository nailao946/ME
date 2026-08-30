using System;
using System.Collections.Generic;
using System.Linq;
using ME.Data;
using ME.Models;

namespace ME.Services
{
    public class TaskService
    {
        private readonly TaskRepository _repo;
        private readonly GoalService _goalService;
        private readonly TaskCompletionRepository _completionRepo;

        public TaskService()
        {
            _repo = new TaskRepository();
            _goalService = new GoalService();
            _completionRepo = new TaskCompletionRepository();
        }

        public List<TaskItem> GetAllTasks() => _repo.GetAllTasks();

        public List<TaskItem> GetTasksByGoalId(int goalId) => _repo.GetTasksByGoalId(goalId);

        public List<TaskItem> GetTasksByType(TaskType type) => _repo.GetTasksByType(type);

        public List<TaskItem> GetTodayTasks() => _repo.GetTodayTasks();

        public TaskItem GetTaskById(int id) => _repo.GetTaskById(id);

        public int CreateTask(TaskItem task) => _repo.InsertTask(task);

        public void UpdateTask(TaskItem task) => _repo.UpdateTask(task);

        public void DeleteTask(int id) => _repo.SoftDeleteTask(id);

        public void RestoreTask(int id) => _repo.RestoreTask(id);

        public void PermanentlyDeleteTask(int id) => _repo.PermanentlyDeleteTask(id);

        public void CompleteTask(int taskId)
        {
            var task = _repo.GetTaskById(taskId);
            if (task != null)
            {
                task.IsCompleted = true;
                task.CompletedAt = DateTime.Now;
                task.LastCompletedDate = DateTime.Today;
                _repo.UpdateTask(task);

                if (task.GoalId.HasValue)
                    RecalcGoalProgress(task.GoalId.Value);
            }
        }

        public void UncompleteTask(int taskId)
        {
            var task = _repo.GetTaskById(taskId);
            if (task != null)
            {
                task.IsCompleted = false;
                task.CompletedAt = null;
                _repo.UpdateTask(task);

                if (task.GoalId.HasValue)
                    RecalcGoalProgress(task.GoalId.Value);
            }
        }

        public void UpdateQuantitativeProgress(int taskId, double value)
        {
            var task = _repo.GetTaskById(taskId);
            if (task != null && task.QuantitativeMode.HasValue)
            {
                if (task.QuantitativeMode.Value == QuantitativeMode.Accumulate)
                    task.QuantitativeCurrent = (task.QuantitativeCurrent ?? 0) + value;
                else
                    task.QuantitativeCurrent = value;

                _repo.UpdateTask(task);

                if (task.GoalId.HasValue)
                    RecalcGoalProgress(task.GoalId.Value);
            }
        }

        public bool ShouldShowRecurringTaskOnDate(TaskItem task, DateTime date)
        {
            if (task.Type != TaskType.Recurring && task.Type != TaskType.Quantitative)
                return false;
            if (!task.RecurringPattern.HasValue)
                return false;

            // Check if task is within its date range
            if (task.StartDate.HasValue && date.Date < task.StartDate.Value.Date)
                return false;
            if (task.EndDate.HasValue && date.Date > task.EndDate.Value.Date)
                return false;

            // If no start date, use creation date
            if (!task.StartDate.HasValue && date.Date < task.CreatedAt.Date)
                return false;

            switch (task.RecurringPattern.Value)
            {
                case RecurringPattern.Daily:
                    return true;

                case RecurringPattern.Weekday:
                    return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

                case RecurringPattern.Weekend:
                    return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

                case RecurringPattern.Weekly:
                    if (string.IsNullOrEmpty(task.RecurringDaysOfWeek))
                        return false;
                    var selectedDays = task.RecurringDaysOfWeek.Split(',').Select(int.Parse).ToList();
                    // Convert DayOfWeek (0=Sunday) to our format (0=Monday, 6=Sunday)
                    int dayIndex = ((int)date.DayOfWeek + 6) % 7;
                    return selectedDays.Contains(dayIndex);

                case RecurringPattern.Monthly:
                    if (task.IsLastDayOfMonth)
                    {
                        // Check if date is the last day of its month
                        var lastDay = DateTime.DaysInMonth(date.Year, date.Month);
                        return date.Day == lastDay;
                    }
                    else if (task.RecurringDayOfMonth.HasValue)
                    {
                        return date.Day == task.RecurringDayOfMonth.Value;
                    }
                    return false;

                case RecurringPattern.Interval:
                    var startDate = task.StartDate ?? task.CreatedAt;
                    int interval = task.RecurringInterval ?? 1;
                    var daysDiff = (date.Date - startDate.Date).Days;
                    return daysDiff >= 0 && daysDiff % interval == 0;

                case RecurringPattern.Custom:
                    // Custom tasks show every day
                    return true;

                default:
                    return false;
            }
        }

        public bool IsRecurringTaskCompletedOnDate(TaskItem task, DateTime date)
        {
            if (task.Type != TaskType.Recurring)
                return task.IsCompleted;

            string dateStr = date.ToString("yyyy-MM-dd");

            if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
            {
                var records = _completionRepo.GetByTaskId(task.Id)
                    .Where(r => r.Date == dateStr).ToList();
                return records.Count >= task.RecurringTargetCount.Value;
            }

            return _completionRepo.IsCompletedOnDate(task.Id, dateStr);
        }

        public void RecordCompletion(int taskId, DateTime date)
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            var existing = _completionRepo.GetByTaskAndDate(taskId, dateStr);
            if (existing == null)
            {
                _completionRepo.Insert(new TaskCompletionRecord
                {
                    TaskId = taskId,
                    Date = dateStr,
                    CompletedAt = DateTime.Now
                });
            }
        }

        public void RemoveCompletion(int taskId, DateTime date)
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            _completionRepo.DeleteByTaskAndDate(taskId, dateStr);
        }

        public void RecordCustomRecurringCompletion(int taskId, DateTime date)
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            _completionRepo.Insert(new TaskCompletionRecord
            {
                TaskId = taskId,
                Date = dateStr,
                CompletedAt = DateTime.Now
            });
        }

        public int GetCustomRecurringCountOnDate(int taskId, DateTime date)
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            return _completionRepo.GetByTaskId(taskId).Count(r => r.Date == dateStr);
        }

        public (double completed, double total) CalcTaskProgress(TaskItem task)
        {
            if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue && task.StartDate.HasValue && task.EndDate.HasValue)
            {
                return CalcRecurringTaskProgress(task);
            }
            else if (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0)
            {
                double current = task.QuantitativeCurrent ?? 0;
                return (current, task.QuantitativeTarget.Value);
            }
            else
            {
                return (task.IsCompleted ? 1 : 0, 1);
            }
        }

        private (double completed, double total) CalcRecurringTaskProgress(TaskItem task)
        {
            if (!task.StartDate.HasValue || !task.EndDate.HasValue)
                return (0, 0);

            var start = task.StartDate.Value.Date;
            var end = task.EndDate.Value.Date;
            if (start > end) return (0, 0);

            int totalDays = 0;
            var current = start;
            while (current <= end)
            {
                if (ShouldShowRecurringTaskOnDate(task, current))
                    totalDays++;
                current = current.AddDays(1);
            }

            if (totalDays == 0) return (0, 0);

            int completedDays = _completionRepo.CountCompletedDaysInRange(task.Id,
                start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));

            return (Math.Min(completedDays, totalDays), totalDays);
        }

        public (double progress, string detail) CalcGoalProgress(int goalId)
        {
            var tasks = _repo.GetTasksByGoalId(goalId);
            if (tasks.Count == 0) return (0, "");

            double totalCompleted = 0;
            double totalWork = 0;
            int recurringDays = 0;
            int recurringCompleted = 0;

            foreach (var t in tasks)
            {
                if (t.IsDeleted) continue;

                if (t.Type == TaskType.Recurring && t.RecurringPattern.HasValue && t.StartDate.HasValue && t.EndDate.HasValue)
                {
                    var (c, w) = CalcRecurringTaskProgress(t);
                    recurringCompleted += (int)c;
                    recurringDays += (int)w;
                    totalCompleted += c;
                    totalWork += w;
                }
                else if (t.Type == TaskType.Quantitative && t.QuantitativeTarget.HasValue && t.QuantitativeTarget > 0)
                {
                    double current = t.QuantitativeCurrent ?? 0;
                    totalCompleted += current;
                    totalWork += t.QuantitativeTarget.Value;
                }
                else
                {
                    totalCompleted += t.IsCompleted ? 1 : 0;
                    totalWork += 1;
                }
            }

            if (totalWork == 0) return (0, "");

            double progress = totalCompleted / totalWork * 100;
            string detail = "";
            if (recurringDays > 0)
                detail = $"{recurringCompleted}/{recurringDays}天";

            return (Math.Min(progress, 100), detail);
        }

        public void RecalcGoalProgress(int goalId)
        {
            var (progress, _) = CalcGoalProgress(goalId);
            _goalService.UpdateGoalProgress(goalId, progress);
        }

        public List<TaskItem> GetTasksForDate(DateTime date)
        {
            var allTasks = _repo.GetAllTasks();
            var result = new List<TaskItem>();

            foreach (var task in allTasks)
            {
                if (task.IsDeleted) continue;

                // For recurring tasks (including combined recurring+quantitative), check schedule
                if ((task.Type == TaskType.Recurring || (task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue)) && task.RecurringPattern.HasValue)
                {
                    if (ShouldShowRecurringTaskOnDate(task, date))
                    {
                        result.Add(task);
                    }
                }
                // For non-recurring tasks, check date range
                else if (task.StartDate.HasValue && task.EndDate.HasValue)
                {
                    if (task.StartDate.Value.Date <= date.Date && task.EndDate.Value.Date >= date.Date)
                        result.Add(task);
                }
                else if (task.StartDate.HasValue)
                {
                    if (task.StartDate.Value.Date == date.Date)
                        result.Add(task);
                }
                else if (task.CreatedAt.Date == date.Date)
                {
                    result.Add(task);
                }
            }

            return result;
        }

        /// <summary>
        /// Determines if a task should display as completed on a given date.
        /// Handles all task types including combined recurring+quantitative.
        /// </summary>
        public bool IsTaskCompletedForDisplay(TaskItem task, DateTime? date = null)
        {
            var checkDate = date ?? DateTime.Today;
            var dateStr = checkDate.ToString("yyyy-MM-dd");

            // Quantitative tasks (including combined recurring+quantitative)
            if (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0)
            {
                double current = task.QuantitativeCurrent ?? 0;

                // Full target reached → always completed
                if (current >= task.QuantitativeTarget.Value) return true;

                // Combined recurring+quantitative: only count actual + clicks via completion record
                if (task.RecurringPattern.HasValue)
                    return _completionRepo.IsCompletedOnDate(task.Id, dateStr);

                // Non-recurring quantitative: check daily min
                double dailyMin = task.QuantitativeDailyMin ?? 0;
                if (dailyMin > 0 && current >= dailyMin) return true;
                return false;
            }

            // Recurring-only tasks
            if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
            {
                if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
                    return GetCustomRecurringCountOnDate(task.Id, checkDate) >= task.RecurringTargetCount.Value;
                return IsRecurringTaskCompletedOnDate(task, checkDate);
            }

            // One-off and other tasks
            return task.IsCompleted;
        }

        /// <summary>
        /// Records completion for a combined recurring+quantitative task when daily min is met.
        /// Updates the recurring completion store so CalendarView/DashboardView can see it.
        /// </summary>
        public void RecordCombinedTaskCompletion(TaskItem task, DateTime date)
        {
            if (task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue)
            {
                var dateStr = date.Date.ToString("yyyy-MM-dd");
                var existing = _completionRepo.GetByTaskAndDate(task.Id, dateStr);
                if (existing == null)
                {
                    var record = new TaskCompletionRecord
                    {
                        TaskId = task.Id,
                        Date = dateStr
                    };
                    _completionRepo.Insert(record);
                }
            }
        }

        /// <summary>
        /// Removes the completion record for a combined recurring+quantitative task on a given date.
        /// </summary>
        public void RemoveCombinedTaskCompletion(TaskItem task, DateTime date)
        {
            if (task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue)
            {
                _completionRepo.DeleteByTaskAndDate(task.Id, date.Date.ToString("yyyy-MM-dd"));
            }
        }

        /// <summary>
        /// 统计口径：任务是否计入"完成任务/总任务数"（子任务不计；未设每日目标的量化任务不计）。
        /// </summary>
        public bool TaskCountsForStats(TaskItem task)
        {
            if (task.IsDeleted || task.ParentTaskId.HasValue) return false;
            if (task.Type == TaskType.Quantitative && (!task.QuantitativeDailyMin.HasValue || task.QuantitativeDailyMin.Value <= 0))
                return false;
            return true;
        }

        /// <summary>
        /// 统计口径：任务在指定日期是否应做（计入当日总任务数）。
        /// 周期任务按重复规则（周六周日的任务周一不计）；已达总目标的量化任务只算到达标当天。
        /// </summary>
        public bool TaskDueOnDate(TaskItem task, DateTime date)
        {
            if (!TaskCountsForStats(task)) return false;

            if (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget.Value > 0
                && (task.QuantitativeCurrent ?? 0) >= task.QuantitativeTarget.Value)
            {
                if (!task.CompletedAt.HasValue || date.Date > task.CompletedAt.Value.Date) return false;
            }

            if ((task.Type == TaskType.Recurring || task.Type == TaskType.Quantitative) && task.RecurringPattern.HasValue)
                return ShouldShowRecurringTaskOnDate(task, date);

            bool startOk = !task.StartDate.HasValue || task.StartDate.Value.Date <= date.Date;
            bool endOk = !task.EndDate.HasValue || task.EndDate.Value.Date >= date.Date;
            if (!task.StartDate.HasValue && !task.EndDate.HasValue)
            {
                // 无日期：一次性任务只算创建当天；纯量化（设了每日目标）从创建日起每天计
                if (task.Type == TaskType.Quantitative)
                    return date.Date >= task.CreatedAt.Date;
                return date.Date == task.CreatedAt.Date;
            }
            if (date.Date < task.CreatedAt.Date) return false;
            return startOk && endOk;
        }

        /// <summary>
        /// 统计口径：任务在指定日期是否算"完成"（按天计，供盘点/打卡图使用）。
        /// 一次性=完成当天；循环=当日打卡记录（自定义按次数）；量化=当日打卡记录或达标当天。
        /// </summary>
        public bool TaskDoneOnDate(TaskItem task, DateTime date)
        {
            return TaskDoneOnDate(task, date, null);
        }

        public bool TaskDoneOnDate(TaskItem task, DateTime date, List<TaskCompletionRecord> records)
        {
            if (task.IsDeleted) return false;
            string dateStr = date.ToString("yyyy-MM-dd");
            switch (task.Type)
            {
                case TaskType.OneTime:
                case TaskType.Periodic:
                    return task.CompletedAt.HasValue && task.CompletedAt.Value.Date == date.Date;
                case TaskType.Recurring:
                    if (!task.RecurringPattern.HasValue) return false;
                    if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount.Value > 1)
                        return CountRecordsOnDate(task.Id, dateStr, records) >= task.RecurringTargetCount.Value;
                    return HasRecordOnDate(task.Id, dateStr, records);
                case TaskType.Quantitative:
                    if (HasRecordOnDate(task.Id, dateStr, records)) return true;
                    return task.CompletedAt.HasValue && task.CompletedAt.Value.Date == date.Date;
                default:
                    return false;
            }
        }

        private bool HasRecordOnDate(int taskId, string dateStr, List<TaskCompletionRecord> records)
        {
            if (records != null) return records.Any(r => r.TaskId == taskId && r.Date == dateStr);
            return _completionRepo.IsCompletedOnDate(taskId, dateStr);
        }

        private int CountRecordsOnDate(int taskId, string dateStr, List<TaskCompletionRecord> records)
        {
            if (records != null) return records.Count(r => r.TaskId == taskId && r.Date == dateStr);
            return _completionRepo.GetByTaskId(taskId).Count(r => r.Date == dateStr);
        }

        /// <summary>
        /// Returns the start-of-week date respecting the WeekStartDay setting.
        /// </summary>
        public static DateTime GetWeekStartForDate(DateTime date)
        {
            var settingsRepo = new SettingsRepository();
            bool mondayFirst = settingsRepo.GetValue(SettingsKeys.WeekStartDay, "1") == "1";

            if (mondayFirst)
                return date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
            else
                return date.Date.AddDays(-(int)date.DayOfWeek);
        }

        /// <summary>
        /// 计算单个任务的打卡率、剩余天数、连续打卡天数（原数据看板统计逻辑，抽取为共享方法）。
        /// 定量任务：今天用 IsTaskCompletedForDisplay 判定，历史日期查完成记录；无截止日期时从开始日算起。
        /// </summary>
        public (double checkInRate, int remainingDays, int streakDays) GetTaskCheckInStats(TaskItem task)
        {
            if (task == null) return (0, 0, 0);

            int totalDays = 0, checkedDays = 0;
            var startDate = (task.StartDate ?? task.CreatedAt).Date;
            for (var date = startDate; date <= DateTime.Today; date = date.AddDays(1))
            {
                bool shouldShow = false, isCompleted = false;
                if (task.Type == TaskType.Quantitative)
                {
                    shouldShow = date >= startDate &&
                                 (!task.EndDate.HasValue || date <= task.EndDate.Value.Date);
                    if (shouldShow)
                        isCompleted = date == DateTime.Today
                            ? IsTaskCompletedForDisplay(task, date)
                            : _completionRepo.IsCompletedOnDate(task.Id, date.ToString("yyyy-MM-dd"));
                }
                else if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
                {
                    shouldShow = ShouldShowRecurringTaskOnDate(task, date);
                    if (shouldShow) isCompleted = IsRecurringTaskCompletedOnDate(task, date);
                }
                if (shouldShow) { totalDays++; if (isCompleted) checkedDays++; }
            }

            double checkInRate = totalDays > 0 ? (double)checkedDays / totalDays * 100 : 0;
            int remainingDays = task.EndDate.HasValue ? Math.Max(0, (task.EndDate.Value.Date - DateTime.Today).Days) : 0;

            int streak = 0;
            var curDate = DateTime.Today;
            while (curDate >= startDate)
            {
                bool shouldShow = false, isCompleted = false;
                if (task.Type == TaskType.Quantitative)
                {
                    shouldShow = curDate >= startDate &&
                                 (!task.EndDate.HasValue || curDate <= task.EndDate.Value.Date);
                    if (shouldShow)
                        isCompleted = curDate == DateTime.Today
                            ? IsTaskCompletedForDisplay(task, curDate)
                            : _completionRepo.IsCompletedOnDate(task.Id, curDate.ToString("yyyy-MM-dd"));
                }
                else if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
                {
                    shouldShow = ShouldShowRecurringTaskOnDate(task, curDate);
                    if (shouldShow) isCompleted = IsRecurringTaskCompletedOnDate(task, curDate);
                }
                if (shouldShow) { if (isCompleted) streak++; else break; }
                curDate = curDate.AddDays(-1);
            }

            return (checkInRate, remainingDays, streak);
        }
    }
}
