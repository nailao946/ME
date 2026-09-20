using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;
using ME.Services;

namespace ME.Tests
{
    /// <summary>
    /// 轻量单元测试（零依赖，dotnet run 即可执行）：
    /// 覆盖任务日期口径（循环任务展示 / 当日完成判定 / 周起始）与新增的依赖、量化日志逻辑。
    /// 约束：只测不落盘的纯逻辑，避免污染真实数据目录。
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static readonly List<string> _failed = new();

        private static void Check(string name, bool cond)
        {
            if (cond) { _passed++; Console.WriteLine($"  ✓ {name}"); }
            else { _failed.Add(name); Console.WriteLine($"  ✗ {name}"); }
        }

        private static TaskItem RecurringTask(RecurringPattern pattern, DateTime start, string daysOfWeek = null, int? interval = null)
        {
            return new TaskItem
            {
                Title = "测试循环任务",
                Type = TaskType.Recurring,
                RecurringPattern = pattern,
                StartDate = start,
                CreatedAt = start,
                RecurringDaysOfWeek = daysOfWeek,
                RecurringInterval = interval,
            };
        }

        public static void Main()
        {
            Console.WriteLine("ME 单元测试");

            // ===== 循环任务展示口径 =====
            var monday = new DateTime(2026, 9, 14); // 周一
            var saturday = new DateTime(2026, 9, 19);
            var svc = new TaskService();

            var daily = RecurringTask(RecurringPattern.Daily, monday.AddDays(-7));
            Check("Daily 每天都显示", svc.ShouldShowRecurringTaskOnDate(daily, monday)
                && svc.ShouldShowRecurringTaskOnDate(daily, saturday));

            Check("Daily 开始前不显示", !svc.ShouldShowRecurringTaskOnDate(
                RecurringTask(RecurringPattern.Daily, monday), monday.AddDays(-1)));

            var weekday = RecurringTask(RecurringPattern.Weekday, monday.AddDays(-7));
            Check("Weekday 周一显示", svc.ShouldShowRecurringTaskOnDate(weekday, monday));
            Check("Weekday 周六不显示", !svc.ShouldShowRecurringTaskOnDate(weekday, saturday));

            var weekend = RecurringTask(RecurringPattern.Weekend, monday.AddDays(-7));
            Check("Weekend 周六显示", svc.ShouldShowRecurringTaskOnDate(weekend, saturday));
            Check("Weekend 周一不显示", !svc.ShouldShowRecurringTaskOnDate(weekend, monday));

            // Weekly：RecurringDaysOfWeek 采用 0=周一 … 6=周日；周三=2
            var weekly = RecurringTask(RecurringPattern.Weekly, monday.AddDays(-7), daysOfWeek: "2");
            Check("Weekly 周三(9/16)显示", svc.ShouldShowRecurringTaskOnDate(weekly, new DateTime(2026, 9, 16)));
            Check("Weekly 周一不显示", !svc.ShouldShowRecurringTaskOnDate(weekly, monday));

            var interval = RecurringTask(RecurringPattern.Interval, monday, interval: 3);
            Check("Interval 第0天显示", svc.ShouldShowRecurringTaskOnDate(interval, monday));
            Check("Interval 第3天显示", svc.ShouldShowRecurringTaskOnDate(interval, monday.AddDays(3)));
            Check("Interval 第2天不显示", !svc.ShouldShowRecurringTaskOnDate(interval, monday.AddDays(2)));

            var monthly = RecurringTask(RecurringPattern.Monthly, monday);
            monthly.RecurringDayOfMonth = 15;
            Check("Monthly 15号显示", svc.ShouldShowRecurringTaskOnDate(monthly, new DateTime(2026, 9, 15)));
            Check("Monthly 14号不显示", !svc.ShouldShowRecurringTaskOnDate(monthly, monday));

            // ===== 当日完成判定 =====
            var oneTime = new TaskItem { Title = "一次性", Type = TaskType.OneTime, CompletedAt = new DateTime(2026, 9, 15, 10, 0, 0) };
            Check("一次性完成日=15号", svc.TaskDoneOnDate(oneTime, new DateTime(2026, 9, 15), new List<TaskCompletionRecord>()));
            Check("一次性16号不算完成", !svc.TaskDoneOnDate(oneTime, new DateTime(2026, 9, 16), new List<TaskCompletionRecord>()));

            var recurring = RecurringTask(RecurringPattern.Daily, monday.AddDays(-7));
            var records = new List<TaskCompletionRecord> { new TaskCompletionRecord { TaskId = recurring.Id, Date = "2026-09-15" } };
            Check("循环=有当日打卡记录", svc.TaskDoneOnDate(recurring, new DateTime(2026, 9, 15), records));
            Check("循环=无记录不算完成", !svc.TaskDoneOnDate(recurring, new DateTime(2026, 9, 16), records));

            // ===== 量化进度日志 =====
            var quant = new TaskItem { Title = "量化", Type = TaskType.Quantitative, QuantitativeMode = QuantitativeMode.Accumulate };
            TaskService.AppendQuantLog(quant, new DateTime(2026, 9, 15), 2.5);
            TaskService.AppendQuantLog(quant, new DateTime(2026, 9, 15), 1.0);
            TaskService.AppendQuantLog(quant, new DateTime(2026, 9, 15), 0); // 零增量不入账
            Check("日志按日累计", Math.Abs(quant.QuantLog.Where(e => e.Date == "2026-09-15").Sum(e => e.Delta) - 3.5) < 1e-9);
            Check("零增量不写日志", quant.QuantLog.Count == 2);

            // ===== 任务依赖 =====
            var pre = new TaskItem { Title = "前置", Type = TaskType.OneTime, Uid = "uid-pre", IsCompleted = false };
            var post = new TaskItem { Title = "后置", Type = TaskType.OneTime, Uid = "uid-post", BlockedBy = new List<string> { "uid-pre" } };
            Check("前置未完成→锁定", svc.BlockingPredecessor(post, new List<TaskItem> { pre, post }) == pre);
            pre.IsCompleted = true;
            Check("前置完成→解锁", svc.BlockingPredecessor(post, new List<TaskItem> { pre, post }) == null);
            Check("无依赖→不锁定", svc.BlockingPredecessor(pre, new List<TaskItem> { pre }) == null);

            Console.WriteLine();
            Console.WriteLine($"通过 {_passed}，失败 {_failed.Count}");
            if (_failed.Count > 0)
            {
                foreach (var f in _failed) Console.WriteLine($"FAILED: {f}");
                Environment.Exit(1);
            }
        }
    }
}
