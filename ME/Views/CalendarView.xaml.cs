using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ME.Data;
using ME.Models;
using ME.Services;
using ME.Core;

namespace ME.Views
{
    public partial class CalendarView : UserControl
    {
        private DateTime _currentMonth;
        private DateTime _selectedDate;
        private TaskItem _selectedTask;
        private int? _filterTagId;

        public CalendarView()
        {
            InitializeComponent();
            _currentMonth = DateTime.Today;
            _selectedDate = DateTime.Today;
            BuildTagFilter();
            LoadCalendar();
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
        }

        private void BuildTagFilter()
        {
            TagFilterPanel.Children.Clear();
            var tagRepo = new TagRepository();
            var tags = tagRepo.GetAllTags();

            var allBtn = new Button
            {
                Content = "全部",
                Style = (Style)FindResource(_filterTagId == null ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0),
                FontSize = 11, Tag = (int?)null
            };
            allBtn.Click += TagFilter_Click;
            TagFilterPanel.Children.Add(allBtn);

            foreach (var tag in tags)
            {
                var isSelected = _filterTagId == tag.Id;
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }
                var btn = new Button
                {
                    Content = (isSelected ? "✓ " : "") + tag.Name,
                    Background = isSelected
                        ? new SolidColorBrush(tagColor)
                        : new SolidColorBrush(Color.FromArgb(38, tagColor.R, tagColor.G, tagColor.B)),
                    Foreground = isSelected ? Brushes.White : (Brush)FindResource("TextBrush"),
                    BorderBrush = isSelected
                        ? new SolidColorBrush(tagColor)
                        : new SolidColorBrush(Color.FromArgb(70, tagColor.R, tagColor.G, tagColor.B)),
                    BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
                    Style = (Style)FindResource(isSelected ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
                    Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0),
                    FontSize = 11,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    Tag = (int?)tag.Id
                };
                btn.Click += TagFilter_Click;
                TagFilterPanel.Children.Add(btn);
            }
        }

        private void TagFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _filterTagId = (int?)btn.Tag;
                BuildTagFilter();
                LoadCalendar();
            }
        }

        private void OnGlobalEvent(string message)
        {
            if (message == "TaskCompleted")
                Dispatcher.BeginInvoke(new Action(() => { if (this.IsVisible) LoadCalendar(); }));
            else if (message == "DayChanged")
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _currentMonth = DateTime.Today;
                    _selectedDate = DateTime.Today;
                    if (this.IsVisible) LoadCalendar();
                }));
        }

        private void CalendarView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible) LoadCalendar();
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(() => LoadCalendar());
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            LoadCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(1);
            LoadCalendar();
        }

        private void Day_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is CalendarDay day)
            {
                _selectedDate = day.Date;
                LoadCalendar();
            }
        }

        /// <summary>
        /// 日历统计口径：只显示该日期存在的任务；量化任务须设置了每日目标，
        /// 已达标的量化任务只算到达标当天。
        /// </summary>
        private bool TaskOccursOnCalendar(TaskItem task, TaskService taskService, DateTime date)
        {
                bool isCombined = task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue;
                bool isCycle = task.Type == TaskType.Recurring || isCombined;

                if (task.Type == TaskType.Quantitative)
                {
                    // 未设每日目标的量化任务不参与日历统计
                    if (!task.QuantitativeDailyMin.HasValue || task.QuantitativeDailyMin.Value <= 0)
                        return false;
                    // 已达标的量化任务只算到达标当天
                    if (task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0
                        && (task.QuantitativeCurrent ?? 0) >= task.QuantitativeTarget.Value
                        && (!task.CompletedAt.HasValue || task.CompletedAt.Value.Date < date.Date))
                        return false;
                }

                // 非循环类的永久完成项（单次任务 / 纯量化达标）只算完成当天，
                // 之后归入任务列表的「过去完成」，不再出现在日历的任务统计里
                if (!isCycle && task.IsCompleted)
                {
                    var cd = (task.CompletedAt ?? task.LastCompletedDate ?? task.StartDate ?? task.CreatedAt).Date;
                    if (cd != date.Date) return false;
                }

            if ((task.Type == TaskType.Recurring || isCombined) && task.RecurringPattern.HasValue)
                return taskService.ShouldShowRecurringTaskOnDate(task, date);

            if (task.StartDate.HasValue && task.EndDate.HasValue)
                return task.StartDate.Value.Date <= date.Date && task.EndDate.Value.Date >= date.Date;
            if (task.StartDate.HasValue)
                return task.StartDate.Value.Date <= date.Date;
            return task.CreatedAt.Date == date.Date;
        }

        private void LoadCalendar()
        {
            MonthTitle.Text = _currentMonth.ToString("yyyy年MM月");
            var taskRepo = new TaskRepository();
            var tagRepo = new TagRepository();
            var goalRepo = new GoalRepository();
            var taskService = new TaskService();
            var allTasks = taskRepo.GetAllTasks();
            var allTags = tagRepo.GetAllTags();

            // Tag filter (from Dashboard)
            if (_filterTagId.HasValue)
            {
                var allGoals = goalRepo.GetAllGoals();
                allTasks = allTasks.Where(t => t.GoalId.HasValue &&
                    allGoals.Any(g => g.Id == t.GoalId.Value && g.TagId == _filterTagId.Value)).ToList();
            }

            var days = new List<CalendarDay>();
            var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            var startDay = firstDay.AddDays(-(int)firstDay.DayOfWeek);

            // First pass: collect all tasks per day
            var dayTaskMap = new Dictionary<string, List<(TaskItem task, string key, Color color, bool completed)>>();
            for (int i = 0; i < 42; i++)
            {
                var date = startDay.AddDays(i);
                var dateKey = date.ToString("yyyy-MM-dd");
                var dayTasks = new List<(TaskItem, string, Color, bool)>();

                foreach (var task in allTasks)
                {
                    if (task.IsDeleted) continue;
                    if (task.ParentTaskId.HasValue) continue;

                    bool showOnThisDate = TaskOccursOnCalendar(task, taskService, date);
                    bool isCompletedOnDate = showOnThisDate && taskService.IsTaskCompletedForDisplay(task, date);

                    if (showOnThisDate)
                    {
                        var color = GetTaskColor(task, allTags);
                        var key = $"t{task.Id}";
                        dayTasks.Add((task, key, color, isCompletedOnDate));
                    }
                }

                dayTaskMap[dateKey] = dayTasks;
            }

            // Second pass: build CalendarDay objects with connected bars
            for (int i = 0; i < 42; i++)
            {
                var date = startDay.AddDays(i);
                var dateKey = date.ToString("yyyy-MM-dd");
                var prevDateKey = date.AddDays(-1).ToString("yyyy-MM-dd");
                var nextDateKey = date.AddDays(1).ToString("yyyy-MM-dd");

                var taskBars = new List<CalendarTaskBar>();
                var currentTasks = dayTaskMap.ContainsKey(dateKey) ? dayTaskMap[dateKey] : new List<(TaskItem, string, Color, bool)>();
                var prevTasks = dayTaskMap.ContainsKey(prevDateKey) ? dayTaskMap[prevDateKey].Select(t => t.Item2).ToHashSet() : new HashSet<string>();
                var nextTasks = dayTaskMap.ContainsKey(nextDateKey) ? dayTaskMap[nextDateKey].Select(t => t.Item2).ToHashSet() : new HashSet<string>();

                foreach (var (task, key, color, isCompleted) in currentTasks)
                {
                    bool hasPrev = prevTasks.Contains(key);
                    bool hasNext = nextTasks.Contains(key);

                    CornerRadius corner;
                    Thickness margin;

                    if (hasPrev && hasNext)
                    {
                        // Middle: square corners, no gap
                        corner = new CornerRadius(0);
                        margin = new Thickness(-1, 1, -1, 1);
                    }
                    else if (hasPrev)
                    {
                        // End: rounded right only
                        corner = new CornerRadius(0, 3, 3, 0);
                        margin = new Thickness(-1, 1, 0, 1);
                    }
                    else if (hasNext)
                    {
                        // Start: rounded left only
                        corner = new CornerRadius(3, 0, 0, 3);
                        margin = new Thickness(0, 1, -1, 1);
                    }
                    else
                    {
                        // Standalone: all rounded
                        corner = new CornerRadius(3);
                        margin = new Thickness(0, 1, 0, 1);
                    }

                    taskBars.Add(new CalendarTaskBar
                    {
                        Title = task.Title,
                        Color = new SolidColorBrush(color),
                        Opacity = isCompleted ? 0.4 : 0.85,
                        CornerRadius = corner,
                        BarMargin = margin,
                        TaskKey = key
                    });
                }

                bool isToday = date.Date == DateTime.Today;
                bool isSelected = date.Date == _selectedDate.Date;
                bool isOtherMonth = date.Month != _currentMonth.Month;

                var cellBg = Brushes.Transparent;
                var cellBorder = Brushes.Transparent;
                var dayBg = Brushes.Transparent;
                var dayFg = (SolidColorBrush)FindResource("TextBrush");

                if (isOtherMonth)
                {
                    dayFg = new SolidColorBrush(Color.FromArgb(80, 142, 142, 147));
                }
                else if (isSelected)
                {
                    dayBg = (SolidColorBrush)FindResource("PrimaryBrush");
                    dayFg = Brushes.White;
                }
                else if (isToday)
                {
                    cellBorder = (SolidColorBrush)FindResource("PrimaryBrush");
                }

                days.Add(new CalendarDay
                {
                    Date = date,
                    Day = date.Day.ToString(),
                    IsToday = isToday,
                    IsOtherMonth = isOtherMonth,
                    IsSelected = isSelected,
                    Tasks = taskBars,
                    CellBackground = cellBg,
                    CellBorder = cellBorder,
                    DayBackground = dayBg,
                    DayForeground = dayFg
                });
            }

            CalendarGrid.ItemsSource = days;
            LoadDayTasks(_selectedDate);
            UpdateGlobalStats();
            AnimateCalendarCells();
        }

        private void AnimateCalendarCells()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < CalendarGrid.Items.Count; i++)
                {
                    var container = CalendarGrid.ItemContainerGenerator.ContainerFromIndex(i) as UIElement;
                    if (container == null) continue;
                    container.Opacity = 0;
                    var delay = TimeSpan.FromMilliseconds(i * 15);
                    var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    container.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                    var scale = new ScaleTransform(0.85, 0.85);
                    container.RenderTransform = scale;
                    container.RenderTransformOrigin = new Point(0.5, 0.5);
                    var scaleX = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(350))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var scaleY = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(350))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private Color GetTaskColor(TaskItem task, List<GoalTag> allTags)
        {
            if (task.GoalId.HasValue)
            {
                var goalRepo = new GoalRepository();
                var goal = goalRepo.GetGoalById(task.GoalId.Value);
                if (goal != null && goal.TagId.HasValue)
                {
                    var tag = allTags.Find(t => t.Id == goal.TagId.Value);
                    if (tag != null)
                    {
                        try { return (Color)ColorConverter.ConvertFromString(tag.Color); }
                        catch { }
                    }
                }
            }

            switch (task.Type)
            {
                case TaskType.Recurring: return Color.FromRgb(90, 200, 250);
                case TaskType.Quantitative: return Color.FromRgb(88, 86, 214);
                default: return Color.FromRgb(0, 122, 255);
            }
        }

        private void LoadDayTasks(DateTime date)
        {
            SelectedDateTitle.Text = date.ToString("yyyy年MM月dd日");
            DayTaskPanel.Children.Clear();

            var taskRepo = new TaskRepository();
            var tagRepo = new TagRepository();
            var goalRepo = new GoalRepository();
            var taskService = new TaskService();
            var allTasks = taskRepo.GetAllTasks();
            var allTags = tagRepo.GetAllTags();
            var allGoals = goalRepo.GetAllGoals();

            // Tag filter (from Dashboard)
            if (_filterTagId.HasValue)
            {
                allTasks = allTasks.Where(t => t.GoalId.HasValue &&
                    allGoals.Any(g => g.Id == t.GoalId.Value && g.TagId == _filterTagId.Value)).ToList();
            }

            var pendingTasks = new List<(TaskItem task, string tagName, string tagColor)>();
            var completedTasks = new List<(TaskItem task, string tagName, string tagColor)>();

            foreach (var task in allTasks)
            {
                if (task.IsDeleted) continue;
                if (task.ParentTaskId.HasValue) continue;

                bool show = TaskOccursOnCalendar(task, taskService, date);
                bool isCompletedOnDate = show && taskService.IsTaskCompletedForDisplay(task, date);

                if (!show) continue;

                string tagName = null, tagColor = null;
                if (task.GoalId.HasValue)
                {
                    var goal = allGoals.Find(g => g.Id == task.GoalId.Value);
                    if (goal != null && goal.TagId.HasValue)
                    {
                        var tag = allTags.Find(t => t.Id == goal.TagId.Value);
                        if (tag != null) { tagName = tag.Name; tagColor = tag.Color; }
                    }
                }

                if (isCompletedOnDate)
                    completedTasks.Add((task, tagName, tagColor));
                else
                    pendingTasks.Add((task, tagName, tagColor));
            }

            // 统计卡一在 UpdateGlobalStats 里按「今天」的数据刷新（与所选日期无关）

            if (pendingTasks.Count == 0 && completedTasks.Count == 0)
            {
                DayTaskPanel.Children.Add(new TextBlock
                {
                    Text = "当天没有任务",
                    FontSize = 12,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            // Pending section
            if (pendingTasks.Count > 0)
            {
                DayTaskPanel.Children.Add(new TextBlock
                {
                    Text = $"待完成 ({pendingTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)FindResource("TextBrush"),
                    Margin = new Thickness(0, 0, 0, 8)
                });

                foreach (var (task, tagName, tagColor) in pendingTasks)
                {
                    DayTaskPanel.Children.Add(CreateTaskInfoCard(task, tagName, tagColor, false));
                }
            }

            // Completed section
            if (completedTasks.Count > 0)
            {
                DayTaskPanel.Children.Add(new TextBlock
                {
                    Text = $"已完成 ({completedTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)FindResource("AccentGreenBrush"),
                    Margin = new Thickness(0, pendingTasks.Count > 0 ? 12 : 0, 0, 8)
                });

                foreach (var (task, tagName, tagColor) in completedTasks)
                {
                    DayTaskPanel.Children.Add(CreateTaskInfoCard(task, tagName, tagColor, true));
                }
            }

            AnimateDayTaskCards();
        }

        /// <summary>
        /// 统计卡一「今日任务」：只显示 已完成 / 未完成，不展示任何百分比式的打卡率。
        /// 计数（完成数/总数）以小字附在下方，方便对照当天的任务量。
        /// </summary>
        private void UpdateTodayTaskStatus(int completedCount, int pendingCount)
        {
            int total = completedCount + pendingCount;
            bool allDone = total > 0 && pendingCount == 0;
            CheckInRateText.Text = total == 0 ? "无任务" : (allDone ? "已完成" : "未完成");
            CheckInRateText.Foreground = total == 0
                ? (SolidColorBrush)FindResource("SecondaryTextBrush")
                : allDone ? (SolidColorBrush)FindResource("AccentGreenBrush")
                          : (SolidColorBrush)FindResource("PrimaryBrush");
            TodayTaskCountText.Text = total == 0 ? "" : $"{completedCount}/{total}";
        }

        /// <summary>
        /// 初始化统计卡二三：剩余天数 = 今天起到本月末仍有未完成任务的天数；
        /// 连续打卡 = 当日完成任意一个任务即算打卡成功（无任务日跳过不断签）。
        /// 点选具体任务卡片后会被该任务的数据覆盖。
        /// </summary>
        private void UpdateGlobalStats()
        {
            var taskService = new TaskService();
            var allTasks = new TaskRepository().GetAllTasks()
                .Where(t => !t.IsDeleted && !t.ParentTaskId.HasValue).ToList();
            var records = new TaskCompletionRepository().GetAll();
            var today = DateTime.Today;

            // 剩余天数：本月内（含今天）还存在未完成任务的天数
            var monthEnd = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            int remaining = 0;
            for (var d = today; d <= monthEnd; d = d.AddDays(1))
            {
                bool hasDue = allTasks.Any(t => taskService.TaskDueOnDate(t, d));
                bool anyDone = allTasks.Any(t => taskService.TaskDoneOnDate(t, d, records));
                if (hasDue && !anyDone) remaining++;
            }
            RemainingDaysText.Text = remaining.ToString();

            // 统计卡一：今日任务 已完成 / 未完成（不展示打卡率）
            int todayDone = allTasks.Count(t => taskService.TaskDueOnDate(t, today) && taskService.TaskDoneOnDate(t, today, records));
            int todayDue = allTasks.Count(t => taskService.TaskDueOnDate(t, today));
            UpdateTodayTaskStatus(todayDone, Math.Max(0, todayDue - todayDone));

            // 连续打卡：当天只要完成任意一个任务即打卡成功
            int streak = taskService.GetGlobalCheckInStreak();
            StreakDaysText.Text = $"{streak}天";
        }

        private void AnimateDayTaskCards()
        {
            for (int i = 0; i < DayTaskPanel.Children.Count; i++)
            {
                var child = DayTaskPanel.Children[i] as FrameworkElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 50);
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var slide = new TranslateTransform(0, 10);
                child.RenderTransform = slide;
                var slideAnim = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                slide.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            }
        }

        private Border CreateTaskInfoCard(TaskItem task, string tagName, string tagColor, bool isCompleted)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
            card.MouseLeftButtonDown += (s, e) => ShowTaskStats(task);

            var mainPanel = new StackPanel();

            // Row 1: Tag badge + Name
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(tagName))
            {
                nameRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(string.IsNullOrEmpty(tagColor)
                        ? Color.FromRgb(0, 122, 255)
                        : (Color)ColorConverter.ConvertFromString(tagColor)),
                    Child = new TextBlock { Text = tagName, FontSize = 10, Foreground = Brushes.White }
                });
            }
            nameRow.Children.Add(new TextBlock
            {
                Text = task.Title,
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = isCompleted
                    ? (SolidColorBrush)FindResource("SecondaryTextBrush")
                    : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = isCompleted ? TextDecorations.Strikethrough : null
            });
            mainPanel.Children.Add(nameRow);

            // Row 2: Description (if any)
            if (!string.IsNullOrEmpty(task.Description))
            {
                mainPanel.Children.Add(new TextBlock
                {
                    Text = task.Description,
                    FontSize = 11,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    MaxHeight = 30,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            // Row 3: Progress + Type + Time
            var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

            bool isQuant = task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0;
            bool isCustomRecurring = task.Type == TaskType.Recurring && task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1;

            var progressColor = string.IsNullOrEmpty(tagColor)
                ? (SolidColorBrush)FindResource("PrimaryBrush")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagColor));

            if (isQuant)
            {
                var pct = task.QuantitativeTarget > 0
                    ? Math.Min((task.QuantitativeCurrent ?? 0) / task.QuantitativeTarget.Value * 100, 100) : 0;
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"进度 {pct:F0}%",
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = progressColor,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"{task.QuantitativeCurrent ?? 0:F0}/{task.QuantitativeTarget.Value:F0}",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                });
            }
            else if (isCustomRecurring)
            {
                var taskSvc = new TaskService();
                var current = taskSvc.GetCustomRecurringCountOnDate(task.Id, DateTime.Today);
                var target = task.RecurringTargetCount ?? 1;
                var pct = target > 0 ? Math.Min((double)current / target * 100, 100) : 0;
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"进度 {pct:F0}%",
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = progressColor,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"{current}/{target}",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                });
            }

            // Task type
            var typeText = task.Type == TaskType.Recurring ? "循环" : task.Type == TaskType.Quantitative ? "量化" : "单次";
            infoRow.Children.Add(new TextBlock
            {
                Text = typeText,
                FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(8, 0, 0, 0)
            });

            // End date
            if (task.EndDate.HasValue)
            {
                infoRow.Children.Add(new TextBlock
                {
                    Text = $"截止 {task.EndDate.Value:MM/dd}",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(8, 0, 0, 0)
                });
            }

            mainPanel.Children.Add(infoRow);

            // Progress bar for quantitative/recurring
            if (isQuant || isCustomRecurring)
            {
                double pbValue = 0;
                if (isQuant)
                    pbValue = task.QuantitativeTarget > 0 ? Math.Min((task.QuantitativeCurrent ?? 0) / task.QuantitativeTarget.Value * 100, 100) : 0;
                else
                {
                    var taskSvc2 = new TaskService();
                    pbValue = task.RecurringTargetCount > 0 ? Math.Min((double)taskSvc2.GetCustomRecurringCountOnDate(task.Id, DateTime.Today) / task.RecurringTargetCount.Value * 100, 100) : 0;
                }

                mainPanel.Children.Add(new ProgressBar
                {
                    Value = pbValue, Maximum = 100, Height = 6,
                    Margin = new Thickness(0, 4, 0, 0),
                    Background = ME.Services.ThemeService.Solid("BackgroundBrush"),
                    Foreground = progressColor
                });
            }

            // Completion status indicator
            if (isCompleted)
            {
                mainPanel.Children.Add(new TextBlock
                {
                    Text = "✓ 已完成",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("AccentGreenBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            card.Child = mainPanel;
            return card;
        }

        private void ShowTaskStats(TaskItem task)
        {
            _selectedTask = task;
            var stats = new TaskService().GetTaskCheckInStats(task);
            RemainingDaysText.Text = stats.remainingDays.ToString();
            StreakDaysText.Text = $"{stats.streakDays}天";
            StatsHint.Visibility = Visibility.Collapsed;
            AnimateStatsCards();
        }

        private void AnimateStatsCards()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var values = new[] { CheckInRateText, RemainingDaysText, StreakDaysText };
                for (int i = 0; i < values.Length; i++)
                {
                    var border = FindVisualParent<Border>(values[i]);
                    if (border == null) continue;
                    border.Opacity = 0;
                    var delay = TimeSpan.FromMilliseconds(i * 70);
                    var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    border.BeginAnimation(UIElement.OpacityProperty, fade);
                    var scale = new ScaleTransform(0.9, 0.9);
                    border.RenderTransform = scale;
                    border.RenderTransformOrigin = new Point(0.5, 0.5);
                    var sx = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(350))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var sy = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(350))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = VisualTreeHelper.GetParent(parent);
            return parent as T;
        }
    }

    public class CalendarDay
    {
        public DateTime Date { get; set; }
        public string Day { get; set; }
        public bool IsToday { get; set; }
        public bool IsOtherMonth { get; set; }
        public bool IsSelected { get; set; }
        public List<CalendarTaskBar> Tasks { get; set; } = new List<CalendarTaskBar>();
        public Brush CellBackground { get; set; }
        public Brush CellBorder { get; set; }
        public Brush DayBackground { get; set; }
        public Brush DayForeground { get; set; }
    }

    public class CalendarTaskBar
    {
        public string Title { get; set; }
        public SolidColorBrush Color { get; set; }
        public double Opacity { get; set; }
        public CornerRadius CornerRadius { get; set; } = new CornerRadius(3);
        public Thickness BarMargin { get; set; } = new Thickness(0, 1, 0, 1);
        public string TaskKey { get; set; } // For tracking continuity
    }
}
