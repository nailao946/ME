using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;
using ME.Services;

namespace ME.Views
{
    /// <summary>
    /// 打卡详情窗口：展示任务/子任务/目标在近 16 周内哪些天完成了、哪些天没完成（热力格），
    /// 并给出累计完成天数 / 连续打卡 / 近30天完成率统计。目标模式按「当天有任一关联任务完成」计。
    /// </summary>
    public partial class TaskDetailWindow : Window
    {
        private const int HeatWeeks = 16;
        private const int CellSize = 13;
        private const int CellGap = 2;

        private readonly TaskItem _task;   // 任务/子任务模式（目标模式为 null）
        private readonly Goal _goal;       // 目标模式（任务模式为 null）
        private readonly TaskCompletionRepository _completionRepo = new TaskCompletionRepository();
        private readonly TaskService _taskService = new TaskService();
        private List<TaskItem> _goalTasks = new List<TaskItem>();
        private Color _accent = Color.FromRgb(79, 110, 247);

        public TaskDetailWindow(TaskItem task)
        {
            InitializeComponent();
            _task = task;
            Loaded += (s, e) => Build();
        }

        public TaskDetailWindow(Goal goal)
        {
            InitializeComponent();
            _goal = goal;
            Loaded += (s, e) => Build();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ============ 构建 ============
        private void Build()
        {
            ContentPanel.Children.Clear();
            if (_task != null) BuildForTask();
            else BuildForGoal();
        }

        private void BuildForTask()
        {
            // 标签颜色：优先时间标签，其次目标标签
            var goal = _task.GoalId.HasValue ? new GoalRepository().GetAllGoals().Find(g => g.Id == _task.GoalId.Value) : null;
            string goalTagColor = null, goalTagName = null;
            if (goal?.TagId.HasValue == true)
            {
                var gTag = new TagRepository().GetAllTags().Find(t => t.Id == goal.TagId.Value);
                if (gTag != null) { goalTagColor = gTag.Color; goalTagName = gTag.Name; }
            }
            TimeTag timeTag = null;
            if (_task.TimeTagId.HasValue)
                timeTag = new TimeTagRepository().GetTagById(_task.TimeTagId.Value);
            var accentHex = timeTag?.Color ?? goalTagColor;
            if (!string.IsNullOrEmpty(accentHex))
            {
                try { _accent = (Color)ColorConverter.ConvertFromString(accentHex); }
                catch { _accent = Color.FromRgb(79, 110, 247); }
            }
            AccentDot.Background = new SolidColorBrush(_accent);
            TitleText.Text = _task.Title;

            // 徽章行：目标标签 + 时间标签
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(goalTagName))
                badgeRow.Children.Add(MakeBadge(goalTagName, goalTagColor));
            if (timeTag != null)
                badgeRow.Children.Add(MakeBadge(timeTag.Name, timeTag.Color));
            if (badgeRow.Children.Count > 0)
            {
                ContentPanel.Children.Add(badgeRow);
                ContentPanel.Children.Add(Spacer(8));
            }

            // 信息行
            var typeText = (_task.Type == TaskType.Quantitative && _task.RecurringPattern.HasValue) ? "循环·量化"
                : _task.Type == TaskType.Recurring ? "循环" + PatternSuffix()
                : _task.Type == TaskType.Quantitative ? "量化" : "单次";
            ContentPanel.Children.Add(InfoLine("类型", typeText));
            if (goal != null) ContentPanel.Children.Add(InfoLine("所属目标", goal.Name, goalTagColor));
            if (_task.EndDate.HasValue)
                ContentPanel.Children.Add(InfoLine("截止", _task.EndDate.Value.ToString("yyyy/MM/dd")));
            else
                ContentPanel.Children.Add(InfoLine("截止", "不限"));
            if (!string.IsNullOrWhiteSpace(_task.Description))
                ContentPanel.Children.Add(InfoLine("描述", _task.Description));

            ContentPanel.Children.Add(Spacer(12));
            ContentPanel.Children.Add(BuildTaskStats());
            ContentPanel.Children.Add(Spacer(14));
            ContentPanel.Children.Add(BuildHeatmap(d =>
                TaskDoneOn(_task, d) ? 2 : (TaskDueOn(_task, d) ? 1 : 0)));
        }

        private void BuildForGoal()
        {
            string goalTagColor = null, goalTagName = null;
            if (_goal.TagId.HasValue)
            {
                var gTag = new TagRepository().GetAllTags().Find(t => t.Id == _goal.TagId.Value);
                if (gTag != null) { goalTagColor = gTag.Color; goalTagName = gTag.Name; }
            }
            if (!string.IsNullOrEmpty(goalTagColor))
            {
                try { _accent = (Color)ColorConverter.ConvertFromString(goalTagColor); }
                catch { _accent = Color.FromRgb(79, 110, 247); }
            }
            AccentDot.Background = new SolidColorBrush(_accent);
            TitleText.Text = _goal.Name;

            var taskRepo = new TaskRepository();
            _goalTasks = taskRepo.GetAllTasks().Where(t => t.GoalId == _goal.Id && !t.IsDeleted).ToList();

            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(goalTagName))
                badgeRow.Children.Add(MakeBadge(goalTagName, goalTagColor));
            if (badgeRow.Children.Count > 0)
            {
                ContentPanel.Children.Add(badgeRow);
                ContentPanel.Children.Add(Spacer(8));
            }

            ContentPanel.Children.Add(InfoLine("关联任务", $"{_goalTasks.Count} 个"));
            if (_goal.StartDate.HasValue || _goal.EndDate.HasValue)
            {
                var range = (_goal.StartDate.HasValue ? _goal.StartDate.Value.ToString("yyyy/MM/dd") : "…")
                    + " ~ " + (_goal.EndDate.HasValue ? _goal.EndDate.Value.ToString("yyyy/MM/dd") : "…");
                ContentPanel.Children.Add(InfoLine("时间范围", range));
            }
            if (_goal.EndDate.HasValue)
            {
                var remain = (_goal.EndDate.Value.Date - DateTime.Today).Days;
                ContentPanel.Children.Add(InfoLine("剩余", remain >= 0 ? $"{remain} 天" : $"已超 {-remain} 天"));
            }
            if (!string.IsNullOrWhiteSpace(_goal.Description))
                ContentPanel.Children.Add(InfoLine("描述", _goal.Description));

            ContentPanel.Children.Add(Spacer(12));
            ContentPanel.Children.Add(BuildGoalStats());
            ContentPanel.Children.Add(Spacer(14));
            ContentPanel.Children.Add(BuildHeatmap(d =>
                _goalTasks.Any(t => TaskDoneOn(t, d)) ? 2 : (_goalTasks.Any(t => TaskDueOn(t, d)) ? 1 : 0)));
        }

        private string PatternSuffix()
        {
            switch (_task.RecurringPattern)
            {
                case RecurringPattern.Daily: return "·每日";
                case RecurringPattern.Weekday: return "·工作日";
                case RecurringPattern.Weekend: return "·周末";
                case RecurringPattern.Weekly: return "·每周";
                case RecurringPattern.Monthly: return "·每月";
                case RecurringPattern.Interval: return $"·每{_task.RecurringInterval ?? 1}天";
                case RecurringPattern.Custom: return "·自定义";
                default: return "";
            }
        }

        // ============ 完成判定（与任务页展示口径一致） ============
        private bool TaskDoneOn(TaskItem t, DateTime date)
        {
            if (t.IsDeleted) return false;
            switch (t.Type)
            {
                case TaskType.OneTime:
                case TaskType.Periodic:
                    return t.CompletedAt.HasValue && t.CompletedAt.Value.Date == date.Date;
                case TaskType.Recurring:
                    return t.RecurringPattern.HasValue && _taskService.IsRecurringTaskCompletedOnDate(t, date);
                case TaskType.Quantitative:
                    if (t.RecurringPattern.HasValue)
                        return _completionRepo.IsCompletedOnDate(t.Id, date.ToString("yyyy-MM-dd"));
                    return t.CompletedAt.HasValue && t.CompletedAt.Value.Date == date.Date;
                default:
                    return false;
            }
        }

        private bool TaskDueOn(TaskItem t, DateTime date)
        {
            if (t.IsDeleted) return false;
            if (t.Type == TaskType.Recurring && t.RecurringPattern.HasValue)
                return _taskService.ShouldShowRecurringTaskOnDate(t, date);

            bool startOk = !t.StartDate.HasValue || t.StartDate.Value.Date <= date.Date;
            bool endOk = !t.EndDate.HasValue || t.EndDate.Value.Date >= date.Date;
            if (!t.StartDate.HasValue && !t.EndDate.HasValue)
                endOk = date.Date == t.CreatedAt.Date;   // 与任务页一致：无起止只显示创建当天
            return startOk && endOk && date.Date <= DateTime.Today;
        }

        // ============ 统计卡 ============
        private UIElement BuildTaskStats()
        {
            var today = DateTime.Today;
            var start = _task.StartDate?.Date ?? _task.CreatedAt.Date;
            if (start > today) start = today;
            if ((today - start).TotalDays > 730) start = today.AddDays(-730);

            int totalDone = 0, streak = 0, done30 = 0, due30 = 0;
            for (var d = start; d <= today; d = d.AddDays(1))
                if (TaskDoneOn(_task, d)) totalDone++;
            for (var d = today; d >= start; d = d.AddDays(-1))
            {
                if (!TaskDoneOn(_task, d)) break;
                streak++;
            }
            for (int i = 0; i < 30; i++)
            {
                var d = today.AddDays(-i);
                bool due = TaskDueOn(_task, d);
                if (due) due30++;
                if (due && TaskDoneOn(_task, d)) done30++;
            }
            var rate = due30 > 0 ? $"{done30 * 100 / due30}%" : "—";
            var rateLabel = due30 > 0 ? "近30天完成率" : "近30天完成";

            return BuildStatsRow(new[]
            {
                ("累计完成", $"{totalDone} 天"),
                ("连续打卡", $"{streak} 天"),
                (rateLabel, rate),
            });
        }

        private UIElement BuildGoalStats()
        {
            var today = DateTime.Today;
            DateTime? earliest = _goal.StartDate?.Date;
            foreach (var t in _goalTasks)
            {
                var c = t.StartDate?.Date ?? t.CreatedAt.Date;
                if (!earliest.HasValue || c < earliest) earliest = c;
            }
            var start = earliest ?? today;
            if (start > today) start = today;
            if ((today - start).TotalDays > 730) start = today.AddDays(-730);

            int totalDone = 0, streak = 0;
            for (var d = start; d <= today; d = d.AddDays(1))
                if (_goalTasks.Any(t => TaskDoneOn(t, d))) totalDone++;
            for (var d = today; d >= start; d = d.AddDays(-1))
            {
                if (!_goalTasks.Any(t => TaskDoneOn(t, d))) break;
                streak++;
            }

            return BuildStatsRow(new[]
            {
                ("关联任务", $"{_goalTasks.Count} 个"),
                ("累计完成", $"{totalDone} 天"),
                ("连续打卡", $"{streak} 天"),
            });
        }

        private UIElement BuildStatsRow((string caption, string value)[] items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (caption, value) in items)
            {
                var card = new Border
                {
                    Background = (SolidColorBrush)FindResource("CardBrush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = caption, FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                });
                sp.Children.Add(new TextBlock
                {
                    Text = value, FontSize = 16, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(_accent), Margin = new Thickness(0, 2, 0, 0)
                });
                card.Child = sp;
                row.Children.Add(card);
            }
            return row;
        }

        // ============ 打卡热力图（近 16 周，GitHub 风格） ============
        private UIElement BuildHeatmap(Func<DateTime, int> stateFor)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = $"打卡图（近 {HeatWeeks} 周）", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 6)
            });

            var today = DateTime.Today;
            // 最后一列 = 本周（周一开始）；整体向左推 HeatWeeks 周
            var thisMonday = today.AddDays(-(int)today.DayOfWeek + 1);
            var gridStart = thisMonday.AddDays(-7 * (HeatWeeks - 1));

            var gridRow = new StackPanel { Orientation = Orientation.Horizontal };

            // 星期标签列（一 / 三 / 五）
            var labelCol = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            string[] labels = { "一", "", "三", "", "五", "", "日" };
            foreach (var lb in labels)
            {
                labelCol.Children.Add(new TextBlock
                {
                    Text = lb, FontSize = 8,
                    Width = CellSize, Height = CellSize + CellGap,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    TextAlignment = TextAlignment.Center
                });
            }
            gridRow.Children.Add(labelCol);

            var dueBrush = (SolidColorBrush)FindResource("BorderBrush");
            var faintBrush = (SolidColorBrush)FindResource("CardBrush");

            for (int w = 0; w < HeatWeeks; w++)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, CellGap, 0) };
                for (int day = 0; day < 7; day++)
                {
                    var date = gridStart.AddDays(w * 7 + day);
                    int state = date > today ? 0 : stateFor(date);
                    var cell = new Border
                    {
                        Width = CellSize, Height = CellSize,
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(0, 0, 0, CellGap),
                        ToolTip = $"{date:M月d日}　" + (state == 2 ? "已完成" : state == 1 ? "未完成" : "无安排")
                    };
                    switch (state)
                    {
                        case 2:
                            cell.Background = new SolidColorBrush(_accent);
                            break;
                        case 1:
                            cell.Background = dueBrush;
                            break;
                        default:
                            cell.Background = faintBrush;
                            cell.Opacity = date > today ? 0.25 : 0.6;
                            break;
                    }
                    if (date == today)
                        cell.BorderBrush = new SolidColorBrush(_accent);
                    if (date == today && state != 2)
                        cell.BorderThickness = new Thickness(1.5);
                    col.Children.Add(cell);
                }
                gridRow.Children.Add(col);
            }
            panel.Children.Add(gridRow);

            // 图例
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            legend.Children.Add(LegendItem(faintBrush, 0.6, "无安排"));
            legend.Children.Add(LegendItem(dueBrush, 1.0, "未完成"));
            legend.Children.Add(LegendItem(new SolidColorBrush(_accent), 1.0, "已完成"));
            panel.Children.Add(legend);
            return panel;
        }

        private UIElement LegendItem(Brush brush, double opacity, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            row.Children.Add(new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                Background = brush, Opacity = opacity,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text = text, FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        // ============ 小控件 ============
        private Border MakeBadge(string text, string colorHex)
        {
            var color = Color.FromRgb(0, 122, 255);
            if (!string.IsNullOrEmpty(colorHex))
            {
                try { color = (Color)ColorConverter.ConvertFromString(colorHex); }
                catch { }
            }
            return new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(color),
                Child = new TextBlock { Text = text, FontSize = 10, Foreground = Brushes.White }
            };
        }

        private StackPanel InfoLine(string label, string value, string dotColorHex = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            if (!string.IsNullOrEmpty(dotColorHex))
            {
                var c = Color.FromRgb(128, 128, 128);
                try { c = (Color)ColorConverter.ConvertFromString(dotColorHex); } catch { }
                row.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(c),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
                });
            }
            row.Children.Add(new TextBlock
            {
                Text = $"{label}：", FontSize = 12,
                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
            });
            row.Children.Add(new TextBlock
            {
                Text = value, FontSize = 12,
                Foreground = (SolidColorBrush)FindResource("TextBrush"),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 460
            });
            return row;
        }

        private FrameworkElement Spacer(double height) =>
            new Border { Height = height, Focusable = false };
    }
}
