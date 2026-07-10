using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ME.Core;
using ME.Data;
using ME.Models;
using ME.Services;
using ME.ViewModels;

namespace ME.Views
{
    public partial class TasksView : System.Windows.Controls.UserControl
    {
        private TasksViewModel _vm;
        private DateTime _stripStartDate;
        private DateTime _selectedDate;
        private int _visibleDays = 14;
        private int? _filterTagId;
        private double _availableWidth = 800;

        // Drag state (matching GoalsView pattern)
        private bool _isDragging;
        private Point _dragStart;
        private Border _draggedBorder;
        private int _dragSourceIndex;
        private List<TaskItem> _dragSourceList;
        private StackPanel _dragPanel;
        private StackPanel _dragMainPanel;
        private Border _placeholderBorder;

        public TasksView()
        {
            InitializeComponent();
            _vm = new TasksViewModel();
            DataContext = _vm;
            _selectedDate = DateTime.Today;
            _stripStartDate = DateTime.Today.AddDays(-3);
            BuildDateStrip();
            BuildTagFilter();
            LoadData();
            LoadMiniStats();
            LoadMiniTagBar();

            var pomo = SharedPomodoroService.Instance;
            pomo.TimerUpdated += (time, mode) =>
            {
                if (!this.IsVisible) return;
                Dispatcher.BeginInvoke(() =>
                {
                    MiniTimerText.Text = time;
                    if (mode == UnifiedTimerMode.Pomodoro)
                        MiniStatus.Text = "🍅";
                });
            };
            pomo.StateChanged += (state) =>
            {
                if (!this.IsVisible) return;
                Dispatcher.BeginInvoke(() => UpdateMiniActions());
            };
            pomo.PhaseChanged += (phase, total, cycle) =>
            {
                if (!this.IsVisible) return;
                Dispatcher.BeginInvoke(() =>
                {
                    var text = phase switch
                    {
                        PomodoroPhase.Work => $"🍅 工作中",
                        PomodoroPhase.ShortBreak => "🍅 短休",
                        PomodoroPhase.LongBreak => "🍅 长休",
                        _ => ""
                    };
                    if (SharedPomodoroService.Instance.State == PomodoroState.Paused)
                        text = "暂停中";
                    MiniStatus.Text = text;
                    MiniTag.Text = $"第{cycle}个";
                });
            };
            SharedTimerService.TimerUpdated += OnMiniTimerUpdated;
            SharedTimerService.RunningStateChanged += OnMiniRunningChanged;
            SharedTimerService.PausedStateChanged += OnMiniPausedChanged;
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) =>
            {
                SharedTimerService.TimerUpdated -= OnMiniTimerUpdated;
                SharedTimerService.RunningStateChanged -= OnMiniRunningChanged;
                SharedTimerService.PausedStateChanged -= OnMiniPausedChanged;
                ThemeService.ThemeChanged -= OnThemeChanged;
            };
        }

        private void UpdateMiniActions()
        {
            var pomo = SharedPomodoroService.Instance;
            bool active = pomo.State != PomodoroState.Idle;
            bool simpleRunning = SharedTimerService.IsRunning;

            MiniPauseBtn.Visibility = (active || simpleRunning) ? Visibility.Visible : Visibility.Collapsed;
            MiniStopBtn.Visibility = (active || simpleRunning) ? Visibility.Visible : Visibility.Collapsed;

            if (pomo.State == PomodoroState.Paused)
            {
                MiniPauseBtn.Content = "▶";
                MiniPauseBtn.Style = (Style)FindResource("PrimaryButtonStyle");
            }
            else
            {
                MiniPauseBtn.Content = "⏸";
                MiniPauseBtn.Style = (Style)FindResource("SecondaryButtonStyle");
            }
        }

        private void OnMiniTimerUpdated(string timeStr, string tagName, string tagColor)
        {
            if (!this.IsVisible) return;
            if (SharedPomodoroService.Instance.Mode == UnifiedTimerMode.Pomodoro) return;
            Dispatcher.BeginInvoke(() =>
            {
                MiniTimerText.Text = timeStr;
                MiniTag.Text = tagName;
                if (SharedTimerService.IsRunning)
                    MiniStatus.Text = "计时中";
                LoadMiniTagBar();
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(tagColor);
                    MiniDot.Background = new SolidColorBrush(color);
                    MiniTimerText.Foreground = new SolidColorBrush(color);
                }
                catch { }
            });
        }

        private void OnMiniRunningChanged(bool isRunning)
        {
            if (!this.IsVisible) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!isRunning)
                {
                    if (SharedPomodoroService.Instance.State == PomodoroState.Idle)
                    {
                        MiniTimerText.Text = "00:00:00";
                        MiniTag.Text = "";
                        MiniDot.Background = Brushes.Gray;
                        MiniTimerText.Foreground = (SolidColorBrush)FindResource("TextBrush");
                        MiniStatus.Text = "";
                    }
                    UpdateMiniActions();
                    LoadMiniStats();
                    LoadMiniTaskSummary();
                    LoadMiniTagBar();
                }
                else
                {
                    UpdateMiniActions();
                    LoadMiniTagBar();
                }
            });
        }

        private void LoadMiniTagBar()
        {
            MiniTagBar.Children.Clear();
            var tagRepo = new TimeTagRepository();
            var tags = tagRepo.GetAllTags();
            var pomo = SharedPomodoroService.Instance;

            foreach (var tag in tags)
            {
                bool isActive = pomo.Mode == UnifiedTimerMode.Simple
                    && pomo.State != PomodoroState.Idle
                    && pomo.SelectedTagId == tag.Id;
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }

                var chip = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = isActive
                        ? new SolidColorBrush(tagColor)
                        : new SolidColorBrush(Color.FromArgb(20, tagColor.R, tagColor.G, tagColor.B)),
                    BorderBrush = new SolidColorBrush(tagColor),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    Tag = tag.Id
                };

                var text = new TextBlock
                {
                    Text = (isActive ? "● " : "") + tag.Name,
                    FontSize = 10,
                    Foreground = isActive ? Brushes.White : (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chip.Child = text;

                var captured = tag;
                chip.MouseLeftButtonDown += (s, e) =>
                {
                    if (pomo.Mode == UnifiedTimerMode.Simple && pomo.State != PomodoroState.Idle
                        && pomo.SelectedTagId == captured.Id)
                    {
                        SharedTimerService.StopCurrent();
                        pomo.Stop();
                    }
                    else
                    {
                        if (pomo.State != PomodoroState.Idle) pomo.Stop();
                        if (SharedTimerService.IsRunning) SharedTimerService.StopCurrent();
                        pomo.Mode = UnifiedTimerMode.Simple;
                        pomo.SelectedTagId = captured.Id;
                        pomo.SelectedTagName = captured.Name;
                        pomo.SelectedTagColor = captured.Color;
                        SharedTimerService.StartWithTag(captured.Id);
                        pomo.Start();
                    }
                    LoadMiniTagBar();
                    LoadMiniStats();
                };

                MiniTagBar.Children.Add(chip);
            }
        }

        private void OnMiniPausedChanged(bool isPaused)
        {
            if (!this.IsVisible) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (isPaused)
                {
                    MiniStatus.Text = "暂停中";
                }
                else if (SharedTimerService.IsRunning)
                {
                    MiniStatus.Text = "计时中";
                }
            });
        }

        private void MiniStopBtn_Click(object sender, RoutedEventArgs e)
        {
            var pomo = SharedPomodoroService.Instance;
            bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                "确认停止", pomo.Mode == UnifiedTimerMode.Pomodoro
                    ? "要放弃当前番茄/休息吗？" : "要停止计时吗？",
                "停止", "取消");
            if (!confirmed) return;

            if (pomo.State != PomodoroState.Idle)
            {
                if (pomo.Mode == UnifiedTimerMode.Simple)
                    SharedTimerService.StopCurrent();
                pomo.Stop();
            }
            else if (SharedTimerService.IsRunning)
            {
                SharedTimerService.StopCurrent();
            }
            LoadMiniTagBar();
            LoadMiniStats();
        }

        private void MiniPause_Click(object sender, RoutedEventArgs e)
        {
            var pomo = SharedPomodoroService.Instance;
            if (pomo.State == PomodoroState.Running)
            {
                if (pomo.Mode == UnifiedTimerMode.Simple)
                    SharedTimerService.PauseCurrent();
                pomo.Pause();
            }
            else if (pomo.State == PomodoroState.Paused)
            {
                if (pomo.Mode == UnifiedTimerMode.Simple)
                    SharedTimerService.ResumeCurrent();
                pomo.Resume();
            }
            else if (SharedTimerService.IsPaused)
            {
                SharedTimerService.ResumeCurrent();
            }
            else if (SharedTimerService.IsRunning)
            {
                SharedTimerService.PauseCurrent();
            }
        }

        private void LoadMiniStats()
        {
            MiniStatsPanel.Children.Clear();
            var recordRepo = new ME.Data.TimeRecordRepository();
            var tagRepo = new ME.Data.TimeTagRepository();
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var records = recordRepo.GetRecordsByDate(today);
            var tags = tagRepo.GetAllTags();
            var tagTime = new Dictionary<int, TimeSpan>();
            foreach (var r in records)
            {
                if (!tagTime.ContainsKey(r.TagId)) tagTime[r.TagId] = TimeSpan.Zero;
                var end = r.EndTime ?? DateTime.Now;
                tagTime[r.TagId] += end - r.StartTime;
            }
            // Total time
            var totalSpan = TimeSpan.Zero;
            foreach (var kv in tagTime) totalSpan += kv.Value;
            if (totalSpan.TotalMinutes >= 1)
            {
                var th = (int)totalSpan.TotalHours;
                var tm = totalSpan.Minutes;
                MiniTotalTime.Text = th > 0 ? $"{th}h {tm}m" : $"{tm}m";
            }
            else
            {
                MiniTotalTime.Text = "";
            }
            if (tagTime.Count == 0)
            {
                MiniStatsPanel.Children.Add(new TextBlock { Text = "暂无数据", FontSize = 11, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush") });
                return;
            }
            foreach (var kv in tagTime)
            {
                var tag = tags.Find(t => t.Id == kv.Key);
                var name = tag?.Name ?? "未知";
                var color = tag?.Color ?? "#808080";
                var dur = kv.Value;
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
                panel.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                });
                var h = (int)dur.TotalHours;
                var m = dur.Minutes;
                var timeStr = h > 0 ? $"{h}h {m}m" : $"{m}m";
                panel.Children.Add(new TextBlock { Text = $"{name} {timeStr}", FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextBrush") });
                MiniStatsPanel.Children.Add(panel);
            }
            LoadMiniTaskSummary();
            AnimateMiniStats();
        }

        private void AnimateMiniStats()
        {
            // Animate the stats panel children (tag breakdown items)
            for (int i = 0; i < MiniStatsPanel.Children.Count; i++)
            {
                var child = MiniStatsPanel.Children[i] as FrameworkElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 50 + 100);
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var slide = new TranslateTransform(0, 6);
                child.RenderTransform = slide;
                var slideAnim = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(250))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                slide.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            }
        }

        private void LoadMiniTaskSummary()
        {
            try
            {
                var taskRepo = new TaskRepository();
                var taskService = new TaskService();
                var allTasks = taskRepo.GetAllTasks();

                int completed = 0;
                int total = 0;
                foreach (var task in allTasks)
                {
                    if (task.IsDeleted) continue;
                    if (task.ParentTaskId.HasValue) continue;

                    bool isRecurringOrCombined = (task.Type == TaskType.Recurring || (task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue))
                        && task.RecurringPattern.HasValue;

                    if (isRecurringOrCombined)
                    {
                        if (!taskService.ShouldShowRecurringTaskOnDate(task, _selectedDate))
                            continue;
                    }
                    else
                    {
                        if (task.StartDate.HasValue && task.StartDate.Value > _selectedDate) continue;
                        if (task.EndDate.HasValue && task.EndDate.Value < _selectedDate) continue;
                        if (!task.StartDate.HasValue && !task.EndDate.HasValue && task.CreatedAt.Date != _selectedDate.Date) continue;
                    }

                    total++;
                    if (taskService.IsTaskCompletedForDisplay(task, _selectedDate))
                        completed++;
                }

                if (total > 0)
                {
                    MiniTaskSummary.Visibility = Visibility.Visible;
                    MiniCompletedCount.Text = $"{completed}/{total}";
                }
                else
                {
                    MiniTaskSummary.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                MiniTaskSummary.Visibility = Visibility.Collapsed;
            }
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(() =>
            {
                BuildDateStrip();
                BuildTagFilter();
                LoadData();
                LoadMiniStats();
            });
        }

        private void TasksView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                BuildTagFilter();
                LoadData();
                LoadMiniStats();
            }
        }

        private void DateStrip_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _availableWidth = e.NewSize.Width - 120;
            int newDays = Math.Max(7, (int)(_availableWidth / 52));
            if (newDays != _visibleDays)
            {
                _visibleDays = newDays;
                BuildDateStrip();
            }
        }

        // ============ TAG FILTER ============
        private void BuildTagFilter()
        {
            TagFilterPanel.Children.Clear();
            var tagRepo = new TagRepository();
            var tags = tagRepo.GetAllTags();

            var allBtn = new Button
            {
                Content = "全部",
                Style = (Style)FindResource(_filterTagId == null ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12, Tag = (int?)null
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
                    Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 0),
                    FontSize = 12, Tag = (int?)tag.Id
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

        // ============ DATE STRIP ============
        private void BuildDateStrip()
        {
            DateStrip.Children.Clear();
            var midDate = _stripStartDate.AddDays(_visibleDays / 2);
            MonthLabel.Text = midDate.ToString("M月");

            for (int i = 0; i < _visibleDays; i++)
            {
                var date = _stripStartDate.AddDays(i);
                var dayPanel = new StackPanel
                {
                    Width = 48, Margin = new Thickness(2, 0, 2, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand, Tag = date
                };

                var weekdayText = new TextBlock
                {
                    Text = GetWeekdayShort(date.DayOfWeek), FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                weekdayText.Foreground = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                    ? new SolidColorBrush(Color.FromRgb(142, 142, 147))
                    : (SolidColorBrush)FindResource("SecondaryTextBrush");
                dayPanel.Children.Add(weekdayText);

                var dayBorder = new Border
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(16),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = date.Day.ToString(), FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                if (date.Date == DateTime.Today && date.Date == _selectedDate.Date)
                {
                    dayBorder.Background = (SolidColorBrush)FindResource("PrimaryBrush");
                    ((TextBlock)dayBorder.Child).Foreground = Brushes.White;
                }
                else if (date.Date == DateTime.Today)
                {
                    dayBorder.Background = new SolidColorBrush(Color.FromRgb(230, 230, 235));
                    ((TextBlock)dayBorder.Child).Foreground = (SolidColorBrush)FindResource("TextBrush");
                }
                else if (date.Date == _selectedDate.Date)
                {
                    dayBorder.Background = (SolidColorBrush)FindResource("PrimaryBrush");
                    ((TextBlock)dayBorder.Child).Foreground = Brushes.White;
                }
                else
                {
                    dayBorder.Background = Brushes.Transparent;
                    ((TextBlock)dayBorder.Child).Foreground = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                        ? new SolidColorBrush(Color.FromRgb(142, 142, 147))
                        : (SolidColorBrush)FindResource("TextBrush");
                }

                dayPanel.Children.Add(dayBorder);
                dayPanel.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is StackPanel p && p.Tag is DateTime d)
                    {
                        _selectedDate = d;
                        BuildDateStrip();
                        LoadData();
                    }
                };
                DateStrip.Children.Add(dayPanel);
            }
        }

        private string GetWeekdayShort(DayOfWeek dow)
        {
            switch (dow)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                case DayOfWeek.Sunday: return "周日";
                default: return "";
            }
        }

        private void TodayBtn_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = DateTime.Today;
            _stripStartDate = DateTime.Today.AddDays(-3);
            BuildDateStrip();
            LoadData();
        }

        private void DateStrip_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                _stripStartDate = _stripStartDate.AddDays(-7);
            else
                _stripStartDate = _stripStartDate.AddDays(7);
            BuildDateStrip();
        }

        // ============ LOAD DATA ============
        public void LoadData()
        {
            _vm.ReloadTasks();

            var goalRepo = new GoalRepository();
            var tagRepo = new TagRepository();
            var taskService = new TaskService();
            var taskRepo = new TaskRepository();
            var allGoals = goalRepo.GetAllGoals();
            var allTags = tagRepo.GetAllTags();
            var todayGoalIds = new HashSet<int>();
            foreach (var goal in allGoals)
            {
                if (goal.IsDeleted) continue;
                bool isToday = false;
                if (goal.StartDate.HasValue && goal.EndDate.HasValue)
                    isToday = goal.StartDate.Value.Date <= _selectedDate.Date && goal.EndDate.Value.Date >= _selectedDate.Date;
                else if (goal.StartDate.HasValue)
                    isToday = goal.StartDate.Value.Date == _selectedDate.Date;
                if (isToday) todayGoalIds.Add(goal.Id);
            }

            // Separate main tasks and subtasks, build tag color cache
            var mainTasks = new List<TaskItem>();
            var subtasksMap = new Dictionary<int, List<TaskItem>>();
            var tagColorMap = new Dictionary<int, string>();

            foreach (var task in _vm.Tasks)
            {
                if (_filterTagId.HasValue && task.GoalId.HasValue)
                {
                    var goal = allGoals.Find(g => g.Id == task.GoalId.Value);
                    if (goal == null || goal.TagId != _filterTagId.Value) continue;
                }

                bool showByDate = false;

                bool isCombined = task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue;
                bool isRecurring = task.Type == TaskType.Recurring || isCombined;

                if (isRecurring && task.RecurringPattern.HasValue)
                {
                    showByDate = taskService.ShouldShowRecurringTaskOnDate(task, _selectedDate);

                    if (showByDate)
                    {
                        bool isCompletedOnDate;
                        if (isCombined)
                        {
                            // Combined: use shared display-completion check (completion record for today)
                            isCompletedOnDate = taskService.IsTaskCompletedForDisplay(task, _selectedDate);
                        }
                        else
                        {
                            isCompletedOnDate = taskService.IsRecurringTaskCompletedOnDate(task, _selectedDate);
                        }
                        var displayTask = new TaskItem
                        {
                            Id = task.Id, Title = task.Title, Description = task.Description,
                            Type = task.Type, GoalId = task.GoalId, ParentTaskId = task.ParentTaskId,
                            StartDate = task.StartDate, EndDate = task.EndDate,
                            IsCompleted = isCompletedOnDate,
                            CompletedAt = isCompletedOnDate ? task.CompletedAt : null,
                            IsDeleted = task.IsDeleted, DeletedAt = task.DeletedAt,
                            CreatedAt = task.CreatedAt, UpdatedAt = task.UpdatedAt, Priority = task.Priority,
                            RecurringPattern = task.RecurringPattern, RecurringInterval = task.RecurringInterval,
                            RecurringDaysOfWeek = task.RecurringDaysOfWeek, RecurringDayOfMonth = task.RecurringDayOfMonth,
                            IsLastDayOfMonth = task.IsLastDayOfMonth, RecurringTimesPerDay = task.RecurringTimesPerDay,
                            RecurringTimesPerWeek = task.RecurringTimesPerWeek, RecurringCurrentCount = task.RecurringCurrentCount,
                            RecurringTargetCount = task.RecurringTargetCount, IsRecurringCompleted = task.IsRecurringCompleted,
                            LastCompletedDate = task.LastCompletedDate,
                            QuantitativeMode = task.QuantitativeMode, QuantitativeStart = task.QuantitativeStart,
                            QuantitativeTarget = task.QuantitativeTarget, QuantitativeCurrent = task.QuantitativeCurrent,
                            QuantitativeUnit = task.QuantitativeUnit, QuantitativeDailyMin = task.QuantitativeDailyMin,
                            CountTowardsParent = task.CountTowardsParent,
                            SortOrder = task.SortOrder
                        };

                        if (task.ParentTaskId.HasValue)
                        {
                            if (!subtasksMap.ContainsKey(task.ParentTaskId.Value))
                                subtasksMap[task.ParentTaskId.Value] = new List<TaskItem>();
                            subtasksMap[task.ParentTaskId.Value].Add(displayTask);
                        }
                        else
                        {
                            mainTasks.Add(displayTask);
                        }
                    }
                }
                else
                {
                    if (task.StartDate.HasValue && task.EndDate.HasValue)
                        showByDate = task.StartDate.Value.Date <= _selectedDate.Date && task.EndDate.Value.Date >= _selectedDate.Date;
                    else if (task.StartDate.HasValue)
                        showByDate = task.StartDate.Value.Date == _selectedDate.Date;
                    else
                        showByDate = true;

                    if (!showByDate && task.GoalId.HasValue && todayGoalIds.Contains(task.GoalId.Value))
                        showByDate = true;

                    if (!showByDate) continue;

                    if (task.ParentTaskId.HasValue)
                    {
                        if (!subtasksMap.ContainsKey(task.ParentTaskId.Value))
                            subtasksMap[task.ParentTaskId.Value] = new List<TaskItem>();
                        subtasksMap[task.ParentTaskId.Value].Add(task);
                    }
                    else
                    {
                        mainTasks.Add(task);
                    }
                }

                if (showByDate && task.GoalId.HasValue && !tagColorMap.ContainsKey(task.GoalId.Value))
                {
                    var goal = allGoals.Find(g => g.Id == task.GoalId.Value);
                    if (goal != null && goal.TagId.HasValue)
                    {
                        var tag = allTags.Find(t => t.Id == goal.TagId.Value);
                        if (tag != null) tagColorMap[task.GoalId.Value] = tag.Color;
                    }
                }
            }

            mainTasks.Sort((a, b) =>
            {
                var cmp = b.Priority.CompareTo(a.Priority);
                return cmp != 0 ? cmp : a.SortOrder.CompareTo(b.SortOrder);
            });

            // Build task sections: active + completed
            TasksPanel.Children.Clear();

            var activeTasks = mainTasks.Where(t => !t.IsCompleted).ToList();
            var doneTasks = mainTasks.Where(t => t.IsCompleted).ToList();

            if (activeTasks.Count > 0)
            {
                TasksPanel.Children.Add(new TextBlock
                {
                    Text = $"进行中 ({activeTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 0, 0, 8)
                });
                BuildTaskTree(TasksPanel, activeTasks, subtasksMap, tagColorMap, false);
            }

            if (doneTasks.Count > 0)
            {
                TasksPanel.Children.Add(new TextBlock
                {
                    Text = $"今日已完成 ({doneTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("AccentGreenBrush"),
                    Margin = new Thickness(0, 12, 0, 8)
                });
                BuildTaskTree(TasksPanel, doneTasks, subtasksMap, tagColorMap, true);
            }

            if (activeTasks.Count == 0 && doneTasks.Count == 0)
            {
                TasksPanel.Children.Add(new TextBlock
                {
                    Text = "此日期没有任务",
                    FontSize = 13,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 30, 0, 0)
                });
            }

            LoadTodayGoals(subtasksMap, tagColorMap);
        }

        // ============ HELPER: Get tag color for a task ============
        private string GetTagColorForTask(TaskItem task, Dictionary<int, string> tagColorMap)
        {
            if (task.GoalId.HasValue && tagColorMap.ContainsKey(task.GoalId.Value))
                return tagColorMap[task.GoalId.Value];
            return null;
        }

        // ============ HELPER: Get tag name for a task ============
        private string GetTagNameForTask(TaskItem task)
        {
            if (task.GoalId.HasValue)
            {
                var goalRepo = new GoalRepository();
                var goal = goalRepo.GetAllGoals().Find(g => g.Id == task.GoalId.Value);
                if (goal != null && goal.TagId.HasValue)
                {
                    var tag = new TagRepository().GetAllTags().Find(t => t.Id == goal.TagId.Value);
                    if (tag != null) return tag.Name;
                }
            }
            return null;
        }

        private void BuildTaskTree(StackPanel panel, List<TaskItem> mainTasks, Dictionary<int, List<TaskItem>> subtasksMap, Dictionary<int, string> tagColorMap, bool isCompleted)
        {

            foreach (var task in mainTasks)
            {
                var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                string tagColor = GetTagColorForTask(task, tagColorMap);
                string tagName = GetTagNameForTask(task);

                // Main task card (matching GoalsView style)
                var card = CreateTaskCard(task, isCompleted, tagColor, tagName);
                SetupDragDrop(card, task, mainTasks, panel);
                wrapper.Children.Add(card);

                // Subtasks (matching GoalsView tree structure)
                if (subtasksMap.ContainsKey(task.Id))
                {
                    var subtasks = subtasksMap[task.Id].OrderBy(s => s.SortOrder).ToList();
                    var subtaskExpander = new Expander
                    {
                        IsExpanded = true,
                        Margin = new Thickness(24, 0, 0, 0),
                        Header = new TextBlock
                        {
                            Text = $"子任务 ({subtasks.Count})",
                            FontSize = 11,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                        }
                    };
                    var subtaskPanel = new StackPanel();
                    foreach (var sub in subtasks)
                    {
                        // Subtask uses parent task's tag color
                        var subCard = CreateSubtaskCard(sub, tagColor, subtasks, subtaskPanel);
                        subtaskPanel.Children.Add(subCard);
                    }
                    subtaskExpander.Content = subtaskPanel;
                    wrapper.Children.Add(subtaskExpander);
                }

                panel.Children.Add(wrapper);
            }
        }

        private Border CreateTaskCard(TaskItem task, bool isCompleted, string tagColor, string tagName)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Cursor = isCompleted ? Cursors.Hand : Cursors.SizeAll,
                Tag = task
            };

            var progressColor = string.IsNullOrEmpty(tagColor) ? (SolidColorBrush)FindResource("PrimaryBrush")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagColor));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Completion circle (matching GoalsView)
            bool isQuant = task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0;
            bool isCustomRecurring = task.Type == TaskType.Recurring && task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1;
            var circle = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = isQuant ? new CornerRadius(4) : new CornerRadius(12),
                BorderThickness = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand,
                Background = isQuant
                    ? (isCompleted ? (SolidColorBrush)FindResource("PrimaryBrush") : progressColor)
                    : (isCompleted ? (SolidColorBrush)FindResource("PrimaryBrush") : Brushes.Transparent),
                BorderBrush = isCompleted ? (SolidColorBrush)FindResource("PrimaryBrush")
                    : (SolidColorBrush)FindResource("BorderBrush"),
                Child = new TextBlock
                {
                    Text = isCompleted ? "✓" : (isQuant ? "+" : (isCustomRecurring ? $"{new TaskService().GetCustomRecurringCountOnDate(task.Id, _selectedDate)}" : "")),
                    Foreground = Brushes.White,
                    FontSize = isCompleted ? 12 : 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            circle.Tag = task;
            circle.MouseLeftButtonDown += (s, e) => { CompleteCircle_Click(s, e); e.Handled = true; };
            Grid.SetColumn(circle, 0);
            grid.Children.Add(circle);

            // Text area (matching GoalsView layout)
            var textPanel = new StackPanel { IsHitTestVisible = false };

            // Tag badge + Name + Expired label
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(tagName))
            {
                var tagBadge = new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(string.IsNullOrEmpty(tagColor)
                        ? Color.FromRgb(0, 122, 255)
                        : (Color)ColorConverter.ConvertFromString(tagColor)),
                    Child = new TextBlock { Text = tagName, FontSize = 10, Foreground = Brushes.White }
                };
                namePanel.Children.Add(tagBadge);
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = task.Title, FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = isCompleted ? (SolidColorBrush)FindResource("SecondaryTextBrush") : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = isCompleted ? TextDecorations.Strikethrough : null
            });
            textPanel.Children.Add(namePanel);

            // Expired label
            bool isExpired = !isCompleted && task.EndDate.HasValue && task.EndDate.Value.Date < DateTime.Today;
            if (isExpired)
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = "任务已过期", FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (!isCompleted)
            {
                if (!string.IsNullOrEmpty(task.Description))
                {
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = task.Description, FontSize = 11,
                        Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                        MaxHeight = 35, TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }

                // Progress + time frame (matching GoalsView info panel)
                var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                infoPanel.Children.Add(new TextBlock { Text = "进度:", FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush") });
                if (isQuant)
                {
                    var pct = task.QuantitativeTarget > 0
                        ? Math.Min((task.QuantitativeCurrent ?? 0) / task.QuantitativeTarget.Value * 100, 100) : 0;
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"{pct:F0}%", FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = progressColor, Margin = new Thickness(3, 0, 8, 0)
                    });
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"{task.QuantitativeCurrent ?? 0:F0}/{task.QuantitativeTarget.Value:F0}",
                        FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                    });
                }
                else if (isCustomRecurring)
                {
                    var taskSvc = new TaskService();
                    var current = taskSvc.GetCustomRecurringCountOnDate(task.Id, _selectedDate);
                    var target = task.RecurringTargetCount ?? 1;
                    var pct = target > 0 ? Math.Min((double)current / target * 100, 100) : 0;
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"{pct:F0}%", FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = progressColor, Margin = new Thickness(3, 0, 8, 0)
                    });
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"{current}/{target}",
                        FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                    });
                    if (task.RecurringTimesPerWeek.HasValue && task.RecurringTimesPerWeek > 0)
                    {
                        var weekDays = CountWeekDaysCompleted(task);
                        infoPanel.Children.Add(new TextBlock
                        {
                            Text = $" 本周:{weekDays}/{task.RecurringTimesPerWeek}",
                            FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                        });
                    }
                }
                else
                {
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = task.IsCompleted ? "100%" : "0%", FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = progressColor, Margin = new Thickness(3, 0, 8, 0)
                    });
                }
                var typeText = (task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue) ? "循环·量化"
                    : task.Type == TaskType.Recurring ? "循环" : task.Type == TaskType.Quantitative ? "量化" : "单次";
                infoPanel.Children.Add(new TextBlock { Text = typeText, FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush") });
                if (task.EndDate.HasValue)
                {
                    var deadlineColor = isExpired 
                        ? new SolidColorBrush(Color.FromRgb(255, 59, 48))
                        : (SolidColorBrush)FindResource("SecondaryTextBrush");
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $" 截止:{task.EndDate.Value:yyyy/MM/dd}", FontSize = 10,
                        Foreground = deadlineColor, Margin = new Thickness(8, 0, 0, 0)
                    });
                }
                textPanel.Children.Add(infoPanel);

                // Progress bar (matching GoalsView)
                var pbValue = isQuant
                    ? (task.QuantitativeTarget > 0 ? Math.Min((task.QuantitativeCurrent ?? 0) / task.QuantitativeTarget.Value * 100, 100) : 0)
                    : (isCustomRecurring
                        ? (task.RecurringTargetCount > 0 ? Math.Min((double)new TaskService().GetCustomRecurringCountOnDate(task.Id, _selectedDate) / task.RecurringTargetCount.Value * 100, 100) : 0)
                        : (task.IsCompleted ? 100 : 0));
                var pb = new ProgressBar
                {
                    Value = 0, Maximum = 100, Height = 8,
                    Margin = new Thickness(0, 5, 0, 0),
                    Background = (SolidColorBrush)FindResource("BackgroundBrush"),
                    Foreground = progressColor
                };
                pb.Loaded += (s, e) =>
                {
                    var anim = new DoubleAnimation(0, pbValue, TimeSpan.FromMilliseconds(600))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                };
                textPanel.Children.Add(pb);
            }

            Grid.SetColumn(textPanel, 1);
            grid.Children.Add(textPanel);

            // Buttons (matching GoalsView)
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnPanel.Children.Add(CreateTaskButton("编辑", EditTask_Click, task));
            btnPanel.Children.Add(CreateTaskButton("子任务", AddSubtaskToTask_Click, task));
            btnPanel.Children.Add(CreateTaskButton("删除", DeleteTask_Click, task));
            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            card.Child = grid;
            return card;
        }

        private Border CreateSubtaskCard(TaskItem task, string tagColor, List<TaskItem> subtaskList = null, StackPanel subtaskPanel = null)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = task
            };

            var progressColor = string.IsNullOrEmpty(tagColor) ? (SolidColorBrush)FindResource("PrimaryBrush")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagColor));

            bool isCustomRecurring = task.Type == TaskType.Recurring && task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Completion circle
            var circle = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = task.Type == TaskType.Quantitative ? new CornerRadius(3) : new CornerRadius(9),
                BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                Background = task.Type == TaskType.Quantitative ? progressColor : Brushes.Transparent,
                BorderBrush = task.IsCompleted ? progressColor : (SolidColorBrush)FindResource("BorderBrush"),
                Child = new TextBlock
                {
                    Text = task.IsCompleted ? "✓" : (task.Type == TaskType.Quantitative ? "+" : (isCustomRecurring ? $"{new TaskService().GetCustomRecurringCountOnDate(task.Id, _selectedDate)}" : "")),
                    Foreground = Brushes.White, FontSize = task.IsCompleted ? 8 : 10, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            circle.Tag = task;
            circle.MouseLeftButtonDown += (s, e) => { SubtaskCircle_Click(s, e); e.Handled = true; };
            Grid.SetColumn(circle, 0);
            grid.Children.Add(circle);

            // Text
            var textPanel = new StackPanel { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = task.Title, FontSize = 12,
                Foreground = task.IsCompleted ? (SolidColorBrush)FindResource("SecondaryTextBrush") : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = task.IsCompleted ? TextDecorations.Strikethrough : null
            });

            // Custom recurring progress for subtask
            if (isCustomRecurring)
            {
                var taskSvc2 = new TaskService();
                var current = taskSvc2.GetCustomRecurringCountOnDate(task.Id, _selectedDate);
                var target = task.RecurringTargetCount ?? 1;
                var pct = target > 0 ? Math.Min((double)current / target * 100, 100) : 0;
                
                var pb = new ProgressBar
                {
                    Maximum = 100, Height = 4,
                    Margin = new Thickness(0, 4, 120, 0),
                    Background = (SolidColorBrush)FindResource("BackgroundBrush"),
                    Foreground = progressColor,
                    Value = 0
                };
                pb.Loaded += (s, e) =>
                {
                    var anim = new DoubleAnimation(0, pct, TimeSpan.FromMilliseconds(500))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                };
                textPanel.Children.Add(pb);

                var progressLabel = $"{current}/{target}";
                if (task.RecurringTimesPerWeek.HasValue && task.RecurringTimesPerWeek > 0)
                {
                    var weekDays = CountWeekDaysCompleted(task);
                    progressLabel += $"  本周:{weekDays}/{task.RecurringTimesPerWeek}";
                }
                textPanel.Children.Add(new TextBlock
                {
                    Text = progressLabel,
                    FontSize = 9, FontWeight = FontWeights.SemiBold,
                    Foreground = progressColor,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
            // Quantitative progress bar for subtask (using parent task's tag color)
            else if (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue)
            {
                var quantPct = task.QuantitativeTarget > 0
                    ? Math.Min((task.QuantitativeCurrent ?? 0) / task.QuantitativeTarget.Value * 100, 100) : 0;
                var pb = new ProgressBar
                {
                    Maximum = 100, Height = 4,
                    Margin = new Thickness(0, 4, 120, 0),
                    Background = (SolidColorBrush)FindResource("BackgroundBrush"),
                    Foreground = progressColor,
                    Value = 0
                };
                pb.Loaded += (s, e) =>
                {
                    var anim = new DoubleAnimation(0, quantPct, TimeSpan.FromMilliseconds(500))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                };
                textPanel.Children.Add(pb);

                textPanel.Children.Add(new TextBlock
                {
                    Text = $"{task.QuantitativeCurrent ?? 0:F0}/{task.QuantitativeTarget.Value:F0}",
                    FontSize = 9, FontWeight = FontWeights.SemiBold,
                    Foreground = progressColor,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            Grid.SetColumn(textPanel, 1);
            grid.Children.Add(textPanel);

            // Edit/Delete buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnPanel.Children.Add(CreateTaskButton("编辑", EditTask_Click, task));
            btnPanel.Children.Add(CreateTaskButton("删除", DeleteTask_Click, task));
            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            card.Child = grid;

            if (subtaskList != null && subtaskPanel != null)
            {
                card.Cursor = Cursors.SizeAll;
                SetupSubtaskDragDrop(card, task, subtaskList, subtaskPanel);
            }

            return card;
        }

        private Button CreateTaskButton(string content, RoutedEventHandler handler, TaskItem task)
        {
            var btn = new Button
            {
                Content = content,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11, Tag = task
            };
            btn.Click += handler;
            return btn;
        }

        // ============ DRAG AND DROP (matching GoalsView pattern) ============
        private void SetupDragDrop(Border card, TaskItem task, List<TaskItem> sourceList, StackPanel mainPanel)
        {
            card.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var source = e.OriginalSource as DependencyObject;
                while (source != null)
                {
                    if (source is Button || source is Border b && b.Cursor == Cursors.Hand)
                        return;
                    source = VisualTreeHelper.GetParent(source);
                }
                _dragStart = e.GetPosition(null);
                _draggedBorder = card;
                _dragSourceList = sourceList;
                _dragSourceIndex = sourceList.IndexOf(task);
                _dragPanel = VisualTreeHelper.GetParent(card) as StackPanel;
                _dragMainPanel = mainPanel;
            };

            card.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _draggedBorder == null) return;
                var pos = e.GetPosition(null);
                var diff = pos - _dragStart;

                if (!_isDragging && Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    _draggedBorder.Opacity = 0.5;
                    _draggedBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black
                    };

                    _placeholderBorder = new Border
                    {
                        Style = (Style)FindResource("CardStyle"),
                        Height = Math.Max(_draggedBorder.ActualHeight, 10), Opacity = 0.3,
                        Margin = new Thickness(0, 0, 0, 12),
                        Background = (SolidColorBrush)FindResource("PrimaryBrush"),
                        IsHitTestVisible = false
                    };

                    Mouse.Capture(_draggedBorder);

                    if (_dragMainPanel != null && _dragPanel != null)
                    {
                        int srcIdx = _dragMainPanel.Children.IndexOf(_dragPanel);
                        if (srcIdx >= 0 && _placeholderBorder != null)
                            _dragMainPanel.Children.Insert(srcIdx + 1, _placeholderBorder);
                    }
                }

                if (_isDragging && _dragMainPanel != null)
                {
                    bool hadPlaceholder = _placeholderBorder != null && _dragMainPanel.Children.Contains(_placeholderBorder);
                    if (hadPlaceholder) _dragMainPanel.Children.Remove(_placeholderBorder);

                    var mousePos = e.GetPosition(_dragMainPanel);
                    int dropIndex = 0;
                    for (int i = 0; i < _dragMainPanel.Children.Count; i++)
                    {
                        var child = _dragMainPanel.Children[i] as FrameworkElement;
                        if (child == null) continue;
                        var transform = child.TransformToAncestor(_dragMainPanel);
                        var childPos = transform.Transform(new Point(0, 0));
                        if (mousePos.Y < childPos.Y + child.ActualHeight / 2)
                        {
                            dropIndex = i;
                            break;
                        }
                        dropIndex = i + 1;
                    }

                    int insertIndex = Math.Min(dropIndex, _dragMainPanel.Children.Count);
                    if (insertIndex >= 0 && _placeholderBorder != null)
                        _dragMainPanel.Children.Insert(insertIndex, _placeholderBorder);
                }
            };

            card.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging && _draggedBorder != null)
                {
                    _draggedBorder.Opacity = 1.0;
                    _draggedBorder.Effect = null;
                    Mouse.Capture(null);

                    if (_placeholderBorder != null && _dragMainPanel != null && _dragMainPanel.Children.Contains(_placeholderBorder))
                        _dragMainPanel.Children.Remove(_placeholderBorder);

                    var mousePos = e.GetPosition(_dragMainPanel);
                    int dropIndex = 0;
                    for (int i = 0; i < _dragMainPanel.Children.Count; i++)
                    {
                        var child = _dragMainPanel.Children[i] as FrameworkElement;
                        if (child == null) continue;
                        var transform = child.TransformToAncestor(_dragMainPanel);
                        var childPos = transform.Transform(new Point(0, 0));
                        if (mousePos.Y < childPos.Y + child.ActualHeight / 2)
                        {
                            dropIndex = i;
                            break;
                        }
                        dropIndex = i + 1;
                    }

                    if (dropIndex != _dragSourceIndex)
                    {
                        var item = _dragSourceList[_dragSourceIndex];
                        _dragSourceList.RemoveAt(_dragSourceIndex);
                        if (dropIndex > _dragSourceIndex) dropIndex--;
                        _dragSourceList.Insert(dropIndex, item);

                        var repo = new TaskRepository();
                        for (int i = 0; i < _dragSourceList.Count; i++)
                        {
                            _dragSourceList[i].Priority = _dragSourceList.Count - i;
                            repo.UpdateTask(_dragSourceList[i]);
                        }
                        LoadData();
                    }
                    else
                    {
                        _placeholderBorder = null;
                    }
                }
                _isDragging = false;
                _draggedBorder = null;
                _dragPanel = null;
                _dragMainPanel = null;
            };
        }

        // ============ SUBTASK DRAG AND DROP ============
        private void SetupSubtaskDragDrop(Border card, TaskItem task, List<TaskItem> subtaskList, StackPanel subtaskPanel)
        {
            card.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var source = e.OriginalSource as DependencyObject;
                while (source != null)
                {
                    if (source is Button || source is Border b && b.Cursor == Cursors.Hand)
                        return;
                    source = VisualTreeHelper.GetParent(source);
                }
                _dragStart = e.GetPosition(null);
                _draggedBorder = card;
                _dragSourceList = subtaskList;
                _dragSourceIndex = subtaskList.IndexOf(task);
                _dragPanel = subtaskPanel;
                _dragMainPanel = subtaskPanel;
            };

            card.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _draggedBorder == null) return;
                var pos = e.GetPosition(null);
                var diff = pos - _dragStart;

                if (!_isDragging && Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    _draggedBorder.Opacity = 0.5;
                    _draggedBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black
                    };

                    _placeholderBorder = new Border
                    {
                        Style = (Style)FindResource("CardStyle"),
                        Height = Math.Max(_draggedBorder.ActualHeight, 10), Opacity = 0.3,
                        Margin = new Thickness(0, 0, 0, 6),
                        Background = (SolidColorBrush)FindResource("PrimaryBrush"),
                        IsHitTestVisible = false
                    };

                    Mouse.Capture(_draggedBorder);

                    if (_dragMainPanel != null)
                    {
                        int srcIdx = _dragMainPanel.Children.IndexOf(_draggedBorder);
                        if (srcIdx >= 0 && _placeholderBorder != null)
                            _dragMainPanel.Children.Insert(srcIdx + 1, _placeholderBorder);
                    }
                }

                if (_isDragging && _dragMainPanel != null)
                {
                    bool hadPlaceholder = _placeholderBorder != null && _dragMainPanel.Children.Contains(_placeholderBorder);
                    if (hadPlaceholder) _dragMainPanel.Children.Remove(_placeholderBorder);

                    var mousePos = e.GetPosition(_dragMainPanel);
                    int dropIndex = 0;
                    for (int i = 0; i < _dragMainPanel.Children.Count; i++)
                    {
                        var child = _dragMainPanel.Children[i] as FrameworkElement;
                        if (child == null) continue;
                        var childPos = child.TransformToAncestor(_dragMainPanel).Transform(new Point(0, 0));
                        if (mousePos.Y < childPos.Y + child.ActualHeight / 2)
                        {
                            dropIndex = i;
                            break;
                        }
                        dropIndex = i + 1;
                    }

                    int insertIndex = Math.Min(dropIndex, _dragMainPanel.Children.Count);
                    if (insertIndex >= 0 && _placeholderBorder != null)
                        _dragMainPanel.Children.Insert(insertIndex, _placeholderBorder);
                }
            };

            card.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging && _draggedBorder != null)
                {
                    _draggedBorder.Opacity = 1.0;
                    _draggedBorder.Effect = null;
                    Mouse.Capture(null);

                    if (_placeholderBorder != null && _dragMainPanel != null && _dragMainPanel.Children.Contains(_placeholderBorder))
                        _dragMainPanel.Children.Remove(_placeholderBorder);

                    var mousePos = e.GetPosition(_dragMainPanel);
                    int dropIndex = 0;
                    for (int i = 0; i < _dragMainPanel.Children.Count; i++)
                    {
                        var child = _dragMainPanel.Children[i] as FrameworkElement;
                        if (child == null) continue;
                        var childPos = child.TransformToAncestor(_dragMainPanel).Transform(new Point(0, 0));
                        if (mousePos.Y < childPos.Y + child.ActualHeight / 2)
                        {
                            dropIndex = i;
                            break;
                        }
                        dropIndex = i + 1;
                    }

                    if (dropIndex != _dragSourceIndex)
                    {
                        var item = _dragSourceList[_dragSourceIndex];
                        _dragSourceList.RemoveAt(_dragSourceIndex);
                        if (dropIndex > _dragSourceIndex) dropIndex--;
                        _dragSourceList.Insert(dropIndex, item);

                        var repo = new TaskRepository();
                        for (int i = 0; i < _dragSourceList.Count; i++)
                        {
                            _dragSourceList[i].SortOrder = i;
                            repo.UpdateTask(_dragSourceList[i]);
                        }
                        LoadData();
                    }
                    else
                    {
                        _placeholderBorder = null;
                    }
                }
                _isDragging = false;
                _draggedBorder = null;
                _dragPanel = null;
                _dragMainPanel = null;
            };
        }

        // ============ TODAY GOALS ============
        private bool IsTaskDisplayCompleted(TaskItem task, DateTime date)
        {
            var ts = new TaskService();
            if (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0)
            {
                double current = task.QuantitativeCurrent ?? 0;
                if (current >= task.QuantitativeTarget.Value) return true;
                double dailyMin = task.QuantitativeDailyMin ?? 0;
                if (dailyMin > 0 && current >= dailyMin) return true;
                return false;
            }
            if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
            {
                if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
                    return ts.GetCustomRecurringCountOnDate(task.Id, date) >= task.RecurringTargetCount.Value;
                return ts.IsRecurringTaskCompletedOnDate(task, date);
            }
            return task.IsCompleted;
        }

        private void LoadTodayGoals(Dictionary<int, List<TaskItem>> subtasksMap, Dictionary<int, string> tagColorMap)
        {
            var goalRepo = new GoalRepository();
            var tagRepo = new TagRepository();
            var taskRepo = new TaskRepository();
            var allGoals = goalRepo.GetAllGoals();
            var tags = tagRepo.GetAllTags();
            var allTasks = taskRepo.GetAllTasks();
            var todayGoals = new List<Goal>();

            foreach (var goal in allGoals)
            {
                if (goal.IsArchived || goal.IsDeleted) continue;
                bool show = false;
                if (goal.StartDate.HasValue && goal.EndDate.HasValue)
                    show = goal.StartDate.Value.Date <= DateTime.Today && goal.EndDate.Value.Date >= DateTime.Today;
                else if (goal.StartDate.HasValue)
                    show = goal.StartDate.Value.Date == DateTime.Today;
                if (show)
                {
                    if (goal.TagId.HasValue)
                    {
                        var tag = tags.Find(t => t.Id == goal.TagId.Value);
                        if (tag != null) { goal.TagColor = tag.Color; goal.TagName = tag.Name; }
                    }
                    todayGoals.Add(goal);
                }
            }

            TodayGoalsSection.Visibility = todayGoals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TodayGoalsPanel.Children.Clear();

            foreach (var goal in todayGoals)
            {
                var goalWrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

                // Goal header
                var goalHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                var dotColor = string.IsNullOrEmpty(goal.TagColor) ? (SolidColorBrush)FindResource("PrimaryBrush")
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(goal.TagColor));
                goalHeader.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = dotColor, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                goalHeader.Children.Add(new TextBlock
                {
                    Text = goal.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("TextBrush")
                });
                goalWrapper.Children.Add(goalHeader);

                // Tasks under this goal
                var goalTasks = allTasks.FindAll(t => t.GoalId == goal.Id && !t.IsDeleted && !t.ParentTaskId.HasValue);
                if (goalTasks.Count > 0)
                {
                    foreach (var task in goalTasks)
                    {
                        var taskPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 4) };

                        // Task row
                        bool taskDone = IsTaskDisplayCompleted(task, DateTime.Today);
                        var taskRow = new StackPanel { Orientation = Orientation.Horizontal };
                        taskRow.Children.Add(new TextBlock
                        {
                            Text = taskDone ? "✓ " : "○ ",
                            FontSize = 11,
                            Foreground = taskDone ? (SolidColorBrush)FindResource("PrimaryBrush") : (SolidColorBrush)FindResource("SecondaryTextBrush")
                        });
                        taskRow.Children.Add(new TextBlock
                        {
                            Text = task.Title, FontSize = 11,
                            Foreground = taskDone ? (SolidColorBrush)FindResource("SecondaryTextBrush") : (SolidColorBrush)FindResource("TextBrush"),
                            TextDecorations = taskDone ? TextDecorations.Strikethrough : null
                        });
                        taskPanel.Children.Add(taskRow);

                        // Subtasks under this task
                        if (subtasksMap.ContainsKey(task.Id))
                        {
                            foreach (var sub in subtasksMap[task.Id])
                            {
                                bool subDone = IsTaskDisplayCompleted(sub, DateTime.Today);
                                var subRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 2, 0, 0) };
                                subRow.Children.Add(new TextBlock
                                {
                                    Text = subDone ? "✓ " : "○ ",
                                    FontSize = 10,
                                    Foreground = subDone ? (SolidColorBrush)FindResource("PrimaryBrush") : (SolidColorBrush)FindResource("SecondaryTextBrush")
                                });
                                subRow.Children.Add(new TextBlock
                                {
                                    Text = sub.Title, FontSize = 10,
                                    Foreground = subDone ? (SolidColorBrush)FindResource("SecondaryTextBrush") : (SolidColorBrush)FindResource("TextBrush"),
                                    TextDecorations = subDone ? TextDecorations.Strikethrough : null
                                });
                                taskPanel.Children.Add(subRow);
                            }
                        }

                        goalWrapper.Children.Add(taskPanel);
                    }
                }

                TodayGoalsPanel.Children.Add(goalWrapper);
            }

            AnimateTodayGoals();
        }

        private void AnimateTodayGoals()
        {
            for (int i = 0; i < TodayGoalsPanel.Children.Count; i++)
            {
                var child = TodayGoalsPanel.Children[i] as FrameworkElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 60);
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var slide = new TranslateTransform(0, 8);
                child.RenderTransform = slide;
                var slideAnim = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                slide.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            }
        }

        // ============ EVENT HANDLERS ============
        private void CompleteCircle_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var task = fe.Tag as TaskItem;
                if (task != null) HandleTaskCompletion(task);
            }
        }

        private void SubtaskCircle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TaskItem task)
            {
                HandleTaskCompletion(task);
            }
        }

        private void HandleTaskCompletion(TaskItem task)
        {
            if (task.Type == TaskType.Quantitative && task.QuantitativeMode.HasValue)
            {
                var dialog = new QuantitativeInputDialog(task) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true)
                {
                    var repo = new TaskRepository();
                    var oldValue = task.QuantitativeCurrent ?? 0;
                    task.QuantitativeCurrent = dialog.NewValue;
                    bool reachedTarget = task.QuantitativeTarget.HasValue && task.QuantitativeCurrent >= task.QuantitativeTarget.Value;
                    bool isCombined = task.RecurringPattern.HasValue;
                    if (reachedTarget)
                    {
                        task.IsCompleted = true;
                        task.CompletedAt = DateTime.Now;
                    }
                    else if (isCombined)
                    {
                        // Combined: don't fully complete on daily min alone - let it reappear tomorrow
                        task.IsCompleted = false;
                        task.CompletedAt = null;
                        var ts = new TaskService();
                        double dailyMin = task.QuantitativeDailyMin ?? 0;
                        if (dailyMin > 0 && (task.QuantitativeCurrent ?? 0) >= dailyMin)
                            ts.RecordCombinedTaskCompletion(task, _selectedDate);
                        else
                            ts.RemoveCombinedTaskCompletion(task, _selectedDate);
                    }
                    else
                    {
                        task.IsCompleted = false;
                        task.CompletedAt = null;
                    }
                    repo.UpdateTask(task);

                    if (task.CountTowardsParent)
                    {
                        if (task.ParentTaskId.HasValue)
                            SyncParentTaskProgress(task.ParentTaskId.Value, task.QuantitativeCurrent.Value - oldValue, repo);

                        if (task.GoalId.HasValue)
                            RecalcGoalProgressFromSubtasks(task.GoalId.Value, repo);
                    }

                    SoundService.PlayCompletionSound();
                    EventAggregator.Instance.Publish("TaskCompleted");
                    LoadData();
                }
                return;
            }

            var repo2 = new TaskRepository();
            var taskService = new TaskService();

            if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
            {
                if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
                {
                    int currentCount = taskService.GetCustomRecurringCountOnDate(task.Id, _selectedDate);
                    if (currentCount >= task.RecurringTargetCount.Value)
                    {
                        taskService.RemoveCompletion(task.Id, _selectedDate);
                    }
                    else
                    {
                        taskService.RecordCustomRecurringCompletion(task.Id, _selectedDate);
                        int count = taskService.GetCustomRecurringCountOnDate(task.Id, _selectedDate);
                        if (count >= task.RecurringTargetCount.Value)
                        {
                            task.LastCompletedDate = _selectedDate;
                            repo2.UpdateTask(task);
                        }
                    }
                }
                else
                {
                    bool isCompletedToday = taskService.IsRecurringTaskCompletedOnDate(task, _selectedDate);
                    if (isCompletedToday)
                        taskService.RemoveCompletion(task.Id, _selectedDate);
                    else
                        taskService.RecordCompletion(task.Id, _selectedDate);
                }
            }
            else
            {
                task.IsCompleted = !task.IsCompleted;
                task.CompletedAt = task.IsCompleted ? DateTime.Now : (DateTime?)null;
                repo2.UpdateTask(task);
            }

            if (task.GoalId.HasValue)
                RecalcGoalProgressFromSubtasks(task.GoalId.Value, repo2);

            SoundService.PlayCompletionSound();
            EventAggregator.Instance.Publish("TaskCompleted");
            LoadData();
        }

        private void SyncParentTaskProgress(int parentTaskId, double delta, TaskRepository repo)
        {
            var allTasks = repo.GetAllTasks();
            var parent = allTasks.Find(t => t.Id == parentTaskId && !t.IsDeleted);
            if (parent != null && parent.Type == TaskType.Quantitative)
            {
                parent.QuantitativeCurrent = (parent.QuantitativeCurrent ?? 0) + delta;
                if (parent.QuantitativeTarget.HasValue && parent.QuantitativeCurrent >= parent.QuantitativeTarget.Value)
                {
                    parent.IsCompleted = true;
                    parent.CompletedAt = DateTime.Now;
                }
                repo.UpdateTask(parent);

                if (parent.GoalId.HasValue)
                    RecalcGoalProgressFromSubtasks(parent.GoalId.Value, repo);
            }
        }

        private void RecalcGoalProgressFromSubtasks(int goalId, TaskRepository repo)
        {
            var goalRepo = new GoalRepository();
            var goal = goalRepo.GetAllGoals().Find(g => g.Id == goalId && !g.IsDeleted);
            if (goal == null) return;

            var taskService = new TaskService();
            var (progress, _) = taskService.CalcGoalProgress(goalId);
            goal.Progress = progress;
            goalRepo.UpdateGoal(goal);
        }

        private int CountWeekDaysCompleted(TaskItem task)
        {
            var taskService = new TaskService();
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            int count = 0;
            for (int i = 0; i < 7; i++)
            {
                var date = startOfWeek.AddDays(i);
                if (date > today) break;
                int dayCount = taskService.GetCustomRecurringCountOnDate(task.Id, date);
                if (dayCount >= (task.RecurringTargetCount ?? 1))
                    count++;
            }
            return count;
        }

        private void AddTask_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TaskEditDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.ResultTask != null)
            {
                var repo = new TaskRepository();
                var id = repo.InsertTask(dialog.ResultTask);
                dialog.ResultTask.Id = id;
                LoadData();
            }
        }

        private void AddSubtaskToTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TaskItem parentTask)
            {
                var dialog = new TaskEditDialog(isSubtaskMode: true) { Owner = Window.GetWindow(this), Title = "添加子任务" };
                if (dialog.ShowDialog() == true && dialog.ResultTask != null)
                {
                    dialog.ResultTask.ParentTaskId = parentTask.Id;
                    dialog.ResultTask.GoalId = parentTask.GoalId;
                    var repo = new TaskRepository();
                    var id = repo.InsertTask(dialog.ResultTask);
                    dialog.ResultTask.Id = id;

                    if (dialog.ResultTask.CountTowardsParent && parentTask.GoalId.HasValue)
                        RecalcGoalProgressFromSubtasks(parentTask.GoalId.Value, repo);

                    LoadData();
                }
            }
        }

        private void EditTask_Click(object sender, RoutedEventArgs e)
        {
            TaskItem task = null;
            if (sender is FrameworkElement fe)
            {
                task = fe.Tag as TaskItem;
                if (task == null && fe.DataContext is TaskItem dt) task = dt;
            }
            if (task != null)
            {
                var dialog = new TaskEditDialog(task) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true && dialog.ResultTask != null)
                {
                    dialog.ResultTask.Id = task.Id;
                    var repo = new TaskRepository();
                    repo.UpdateTask(dialog.ResultTask);
                    dialog.PersistSubtasks();
                    LoadData();
                }
            }
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            TaskItem task = null;
            if (sender is FrameworkElement fe)
            {
                task = fe.Tag as TaskItem;
                if (task == null && fe.DataContext is TaskItem dt) task = dt;
            }
            if (task != null)
            {
                var repo = new TaskRepository();

                if (task.CountTowardsParent && task.ParentTaskId.HasValue)
                {
                    var delta = -(task.QuantitativeCurrent ?? 0);
                    SyncParentTaskProgress(task.ParentTaskId.Value, delta, repo);
                }

                repo.SoftDeleteTask(task.Id);

                if (task.GoalId.HasValue)
                    RecalcGoalProgressFromSubtasks(task.GoalId.Value, repo);

                LoadData();
            }
        }
    }
}
