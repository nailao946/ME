using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ME.Core;
using ME.Data;
using ME.Models;
using ME.Services;

namespace ME.Views
{
    public partial class DashboardView : UserControl
    {
        private DateTime _currentMonth;
        private TaskItem _selectedTask;
        private int? _filterTagId;

        public DashboardView()
        {
            InitializeComponent();
            _currentMonth = DateTime.Today;
            BuildTagFilter();
            LoadData();
            EventAggregator.Instance.Subscribe<string>(OnTaskCompleted);
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
        }

        private void OnTaskCompleted(string message)
        {
            if (message == "TaskCompleted" && this.IsVisible)
            {
                if (_selectedTask != null)
                {
                    LoadCalendar();
                    LoadStats();
                }
            }
            else if (message == "DayChanged")
            {
                _currentMonth = DateTime.Today;
                if (this.IsVisible)
                {
                    BuildTagFilter();
                    LoadData();
                    if (_selectedTask != null)
                    {
                        LoadCalendar();
                        LoadStats();
                    }
                }
            }
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(() =>
            {
                BuildTagFilter();
                LoadData();
                if (_selectedTask != null)
                {
                    LoadCalendar();
                    LoadStats();
                }
            });
        }

        private void DashboardView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                BuildTagFilter();
                LoadData();
                if (_selectedTask != null)
                {
                    LoadCalendar();
                    LoadStats();
                }
            }
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
                var btn = new Button
                {
                    Content = tag.Name,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color)),
                    Foreground = Brushes.White,
                    Style = (Style)FindResource(_filterTagId == tag.Id ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
                    Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0),
                    FontSize = 11, Tag = (int?)tag.Id
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
                LoadData();
            }
        }

        private void LoadData()
        {
            var taskRepo = new TaskRepository();
            var goalRepo = new GoalRepository();
            var tagRepo = new TagRepository();
            var allTasks = taskRepo.GetAllTasks();
            var allGoals = goalRepo.GetAllGoals();
            var allTags = tagRepo.GetAllTags();

            var taskDisplayList = new List<TaskDisplayItem>();
            foreach (var task in allTasks)
            {
                if (task.IsDeleted || task.ParentTaskId.HasValue) continue;

                // Tag filter
                if (_filterTagId.HasValue && task.GoalId.HasValue)
                {
                    var goal = allGoals.Find(g => g.Id == task.GoalId.Value);
                    if (goal == null || goal.TagId != _filterTagId.Value) continue;
                }
                else if (_filterTagId.HasValue && !task.GoalId.HasValue)
                {
                    continue;
                }

                var item = new TaskDisplayItem
                {
                    Task = task,
                    Title = task.Title,
                    Description = task.Description ?? "",
                    TypeText = GetTypeText(task)
                };

                if (task.GoalId.HasValue)
                {
                    var goal = allGoals.Find(g => g.Id == task.GoalId.Value);
                    if (goal != null && goal.TagId.HasValue)
                    {
                        var tag = allTags.Find(t => t.Id == goal.TagId.Value);
                        if (tag != null)
                        {
                            item.TagName = tag.Name;
                            item.TagColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color));
                            item.TagVisibility = Visibility.Visible;
                        }
                    }
                }

                if (item.TagColorBrush == null)
                {
                    item.TagColorBrush = new SolidColorBrush(Color.FromRgb(0, 122, 255));
                    item.TagVisibility = Visibility.Collapsed;
                }

                if (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0)
                {
                    item.ProgressVisibility = Visibility.Visible;
                    var current = task.QuantitativeCurrent ?? 0;
                    var target = task.QuantitativeTarget.Value;
                    item.ProgressValue = Math.Min(current / target * 100, 100);
                    item.ProgressText = $"{current:F0}/{target:F0}";
                    item.ProgressBrush = item.TagVisibility == Visibility.Visible
                        ? item.TagColorBrush
                        : new SolidColorBrush(Color.FromRgb(0, 122, 255));
                }
                else if (task.Type == TaskType.Recurring && task.RecurringPattern == RecurringPattern.Custom &&
                         task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
                {
                    item.ProgressVisibility = Visibility.Visible;
                    var taskSvc = new TaskService();
                    var current = taskSvc.GetCustomRecurringCountOnDate(task.Id, DateTime.Today);
                    var target = task.RecurringTargetCount.Value;
                    item.ProgressValue = target > 0 ? Math.Min((double)current / target * 100, 100) : 0;
                    item.ProgressText = $"{current}/{target}";
                    item.ProgressBrush = item.TagVisibility == Visibility.Visible
                        ? item.TagColorBrush
                        : new SolidColorBrush(Color.FromRgb(52, 199, 89));
                }
                else
                {
                    item.ProgressVisibility = Visibility.Collapsed;
                    item.ProgressValue = 0;
                    item.ProgressText = "";
                    item.ProgressBrush = new SolidColorBrush(Color.FromRgb(0, 122, 255));
                }

                taskDisplayList.Add(item);
            }
            TaskListBox.ItemsSource = taskDisplayList;

            if (taskDisplayList.Count > 0 && TaskListBox.SelectedIndex < 0)
                TaskListBox.SelectedIndex = 0;

            // Animate progress bars after layout
            Dispatcher.BeginInvoke(new Action(() => AnimateProgressBars()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void AnimateProgressBars()
        {
            int idx = 0;
            foreach (var item in TaskListBox.Items)
            {
                var container = TaskListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (container == null) continue;
                var pb = FindChild<ProgressBar>(container);
                if (pb != null)
                {
                    var target = pb.Value;
                    pb.Value = 0;
                    var delayMs = idx * 60;
                    var anim = new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(500))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        BeginTime = TimeSpan.FromMilliseconds(delayMs)
                    };
                    pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                }
                idx++;
            }
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private string GetTypeText(TaskItem task)
        {
            switch (task.Type)
            {
                case TaskType.Recurring:
                    if (task.RecurringPattern.HasValue)
                    {
                        switch (task.RecurringPattern.Value)
                        {
                            case RecurringPattern.Daily: return "每天";
                            case RecurringPattern.Weekday: return "工作日";
                            case RecurringPattern.Weekend: return "周末";
                            case RecurringPattern.Weekly: return "每周";
                            case RecurringPattern.Monthly: return "每月";
                            case RecurringPattern.Interval: return $"每{task.RecurringInterval}天";
                            case RecurringPattern.Custom: return "自定义";
                            default: return "循环";
                        }
                    }
                    return "循环";
                case TaskType.Quantitative: return "量化";
                default: return "单次";
            }
        }

        private void TaskListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskListBox.SelectedItem is TaskDisplayItem item)
            {
                _selectedTask = item.Task;
                LoadCalendar();
                LoadStats();
            }
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

        private void Day_Click(object sender, MouseButtonEventArgs e) { }

        private void LoadStats()
        {
            if (_selectedTask == null)
            {
                CheckInRateText.Text = "0%";
                RemainingDaysText.Text = "0";
                StreakDaysText.Text = "0天";
                return;
            }

            var taskService = new TaskService();
            var taskRepo = new TaskRepository();
            var freshTask = taskRepo.GetTaskById(_selectedTask.Id);
            if (freshTask == null)
            {
                CheckInRateText.Text = "0%";
                RemainingDaysText.Text = "0";
                StreakDaysText.Text = "0天";
                return;
            }

            int totalDays = 0, checkedDays = 0;
            var startDate = freshTask.StartDate ?? freshTask.CreatedAt;
            for (var date = startDate.Date; date <= DateTime.Today; date = date.AddDays(1))
            {
                bool shouldShow = false, isCompleted = false;
                if (freshTask.Type == TaskType.Quantitative)
                {
                    if (freshTask.StartDate.HasValue && freshTask.EndDate.HasValue)
                        shouldShow = date >= freshTask.StartDate.Value.Date && date <= freshTask.EndDate.Value.Date;
                    else if (freshTask.StartDate.HasValue)
                        shouldShow = date == freshTask.StartDate.Value.Date;
                    if (shouldShow && date == DateTime.Today && taskService.IsTaskCompletedForDisplay(freshTask, date))
                        isCompleted = true;
                }
                else if (freshTask.Type == TaskType.Recurring && freshTask.RecurringPattern.HasValue)
                {
                    shouldShow = taskService.ShouldShowRecurringTaskOnDate(freshTask, date);
                    if (shouldShow) isCompleted = taskService.IsRecurringTaskCompletedOnDate(freshTask, date);
                }
                if (shouldShow) { totalDays++; if (isCompleted) checkedDays++; }
            }

            CheckInRateText.Text = totalDays > 0 ? $"{(double)checkedDays / totalDays * 100:F0}%" : "0%";
            RemainingDaysText.Text = freshTask.EndDate.HasValue ? Math.Max(0, (freshTask.EndDate.Value.Date - DateTime.Today).Days).ToString() : "0";

            int streak = 0;
            var curDate = DateTime.Today;
            while (curDate >= startDate.Date)
            {
                bool shouldShow = false, isCompleted = false;
                if (freshTask.Type == TaskType.Quantitative)
                {
                    if (freshTask.StartDate.HasValue && freshTask.EndDate.HasValue)
                        shouldShow = curDate >= freshTask.StartDate.Value.Date && curDate <= freshTask.EndDate.Value.Date;
                    if (shouldShow && curDate == DateTime.Today && taskService.IsTaskCompletedForDisplay(freshTask, curDate)) isCompleted = true;
                }
                else if (freshTask.Type == TaskType.Recurring && freshTask.RecurringPattern.HasValue)
                {
                    shouldShow = taskService.ShouldShowRecurringTaskOnDate(freshTask, curDate);
                    if (shouldShow) isCompleted = taskService.IsRecurringTaskCompletedOnDate(freshTask, curDate);
                }
                if (shouldShow) { if (isCompleted) streak++; else break; }
                curDate = curDate.AddDays(-1);
            }
            StreakDaysText.Text = $"{streak}天";

            AnimateStatsCards();
        }

        private void AnimateStatsCards()
        {
            // Find the parent UniformGrid of CheckInRateText
            var parent = VisualTreeHelper.GetParent(CheckInRateText);
            while (parent != null && !(parent is System.Windows.Controls.Primitives.UniformGrid))
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is System.Windows.Controls.Primitives.UniformGrid grid)
            {
                for (int i = 0; i < grid.Children.Count; i++)
                {
                    var child = grid.Children[i] as UIElement;
                    if (child == null) continue;
                    child.Opacity = 0;
                    var delay = TimeSpan.FromMilliseconds(i * 80);
                    var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                    var scale = new ScaleTransform(0.9, 0.9);
                    child.RenderTransform = scale;
                    child.RenderTransformOrigin = new Point(0.5, 0.5);
                    var scaleX = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(400))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var scaleY = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(400))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                }
            }
        }

        private void LoadCalendar()
        {
            MonthTitle.Text = _currentMonth.ToString("yyyy年MM月");
            var taskService = new TaskService();
            var taskRepo = new TaskRepository();
            var days = new List<CalendarDayDisplay>();
            var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            var startDay = firstDay.AddDays(-(int)firstDay.DayOfWeek + 1);
            if (startDay > firstDay) startDay = startDay.AddDays(-7);

            TaskItem freshTask = _selectedTask != null ? taskRepo.GetTaskById(_selectedTask.Id) : null;

            for (int i = 0; i < 42; i++)
            {
                var date = startDay.AddDays(i);
                bool isCurrentMonth = date.Month == _currentMonth.Month;
                bool isToday = date.Date == DateTime.Today;
                bool shouldShow = false, isCompleted = false;

                if (freshTask != null)
                {
                    if (freshTask.Type == TaskType.Quantitative)
                    {
                        if (freshTask.StartDate.HasValue && freshTask.EndDate.HasValue)
                            shouldShow = date >= freshTask.StartDate.Value.Date && date <= freshTask.EndDate.Value.Date;
                        else if (freshTask.StartDate.HasValue)
                            shouldShow = date == freshTask.StartDate.Value.Date;
                        if (shouldShow && isToday && taskService.IsTaskCompletedForDisplay(freshTask, date)) isCompleted = true;
                    }
                    else if (freshTask.Type == TaskType.Recurring && freshTask.RecurringPattern.HasValue)
                    {
                        shouldShow = taskService.ShouldShowRecurringTaskOnDate(freshTask, date);
                        if (shouldShow) isCompleted = taskService.IsRecurringTaskCompletedOnDate(freshTask, date);
                    }
                }

                bool isPast = date.Date < DateTime.Today;
                var dayDisplay = new CalendarDayDisplay { Date = date, Day = date.Day.ToString(), IsCurrentMonth = isCurrentMonth, IsToday = isToday };

                if (!isCurrentMonth)
                {
                    dayDisplay.BackgroundBrush = Brushes.Transparent;
                    dayDisplay.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 142, 142, 147));
                    dayDisplay.ForegroundBrush = new SolidColorBrush(Color.FromArgb(80, 142, 142, 147));
                }
                else if (shouldShow && isCompleted)
                {
                    dayDisplay.BackgroundBrush = new SolidColorBrush(Color.FromRgb(52, 199, 89));
                    dayDisplay.BorderBrush = new SolidColorBrush(Color.FromRgb(52, 199, 89));
                    dayDisplay.ForegroundBrush = Brushes.White;
                }
                else if (isToday)
                {
                    dayDisplay.BackgroundBrush = Brushes.Transparent;
                    dayDisplay.BorderBrush = (SolidColorBrush)FindResource("PrimaryBrush");
                    dayDisplay.ForegroundBrush = (SolidColorBrush)FindResource("TextBrush");
                }
                else if (shouldShow && isPast && !isCompleted)
                {
                    dayDisplay.BackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    dayDisplay.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    dayDisplay.ForegroundBrush = Brushes.White;
                }
                else if (shouldShow && !isPast)
                {
                    dayDisplay.BackgroundBrush = Brushes.Transparent;
                    dayDisplay.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 255));
                    dayDisplay.ForegroundBrush = (SolidColorBrush)FindResource("TextBrush");
                }
                else
                {
                    dayDisplay.BackgroundBrush = Brushes.Transparent;
                    dayDisplay.BorderBrush = Brushes.Transparent;
                    dayDisplay.ForegroundBrush = isCurrentMonth
                        ? (SolidColorBrush)FindResource("TextBrush")
                        : new SolidColorBrush(Color.FromArgb(80, 142, 142, 147));
                }

                days.Add(dayDisplay);
            }
            CalendarGrid.ItemsSource = days;
            AnimateDashboardCalendar();
        }

        private void AnimateDashboardCalendar()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < CalendarGrid.Items.Count; i++)
                {
                    var container = CalendarGrid.ItemContainerGenerator.ContainerFromIndex(i) as UIElement;
                    if (container == null) continue;
                    container.Opacity = 0;
                    var delay = TimeSpan.FromMilliseconds(i * 12);
                    var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    container.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                    var scale = new ScaleTransform(0.88, 0.88);
                    container.RenderTransform = scale;
                    container.RenderTransformOrigin = new Point(0.5, 0.5);
                    var scaleX = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(300))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var scaleY = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(300))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    public class TaskDisplayItem
    {
        public TaskItem Task { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string TypeText { get; set; }
        public string TagName { get; set; }
        public SolidColorBrush TagColorBrush { get; set; }
        public Visibility TagVisibility { get; set; }
        public Visibility ProgressVisibility { get; set; }
        public double ProgressValue { get; set; }
        public string ProgressText { get; set; }
        public SolidColorBrush ProgressBrush { get; set; }
    }

    public class CalendarDayDisplay
    {
        public DateTime Date { get; set; }
        public string Day { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsToday { get; set; }
        public Brush BackgroundBrush { get; set; }
        public Brush BorderBrush { get; set; }
        public Brush ForegroundBrush { get; set; }
    }
}
