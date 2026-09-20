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
        private bool _globalEventsSubscribed;

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

            SubscribeGlobalEvents();
            this.Loaded += (s, e) => SubscribeGlobalEvents();

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
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateMiniActions();
                    if (state == PomodoroState.Idle && SharedTimerService.IsRunning)
                        SharedTimerService.StopCurrent();
                });
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
                    MiniTag.Text = $"本轮 {cycle}/{SharedPomodoroService.Instance.BeforeLongBreak} 个";
                });
            };
            pomo.WorkPhaseEnded += OnMiniWorkPhaseEnded;
            SharedTimerService.TimerUpdated += OnMiniTimerUpdated;
            SharedTimerService.RunningStateChanged += OnMiniRunningChanged;
            SharedTimerService.PausedStateChanged += OnMiniPausedChanged;
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) =>
            {
                UnsubscribeGlobalEvents();
                SharedTimerService.TimerUpdated -= OnMiniTimerUpdated;
                SharedTimerService.RunningStateChanged -= OnMiniRunningChanged;
                SharedTimerService.PausedStateChanged -= OnMiniPausedChanged;
                var pomo = SharedPomodoroService.Instance;
                pomo.WorkPhaseEnded -= OnMiniWorkPhaseEnded;
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

        private void OnMiniWorkPhaseEnded()
        {
            if (!this.IsVisible) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PomodoroService.IsBreakConfirmShowing) return;
                PomodoroService.IsBreakConfirmShowing = true;
                try
                {
                    var win = Window.GetWindow(this);
                    if (win == null) return;
                    var pomo = SharedPomodoroService.Instance;
                    bool confirmed = ConfirmDialog.Show(win,
                        "番茄时间到！", "是否开始休息？",
                        "开始休息", "跳过");
                    if (confirmed)
                        pomo.ConfirmBreak();
                    else
                        pomo.SkipBreak();
                }
                finally
                {
                    PomodoroService.IsBreakConfirmShowing = false;
                }
            }));
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

        private void MiniTagScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            MiniTagScroller.ScrollToHorizontalOffset(MiniTagScroller.HorizontalOffset - e.Delta);
            e.Handled = true;
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
                    if (pomo.State != PomodoroState.Idle && pomo.SelectedTagId == captured.Id)
                    {
                        SharedTimerService.StopCurrent();
                        pomo.Stop();
                    }
                    else
                    {
                        if (SharedTimerService.IsRunning) SharedTimerService.StopCurrent();
                        pomo.SelectedTagId = captured.Id;
                        pomo.SelectedTagName = captured.Name;
                        pomo.SelectedTagColor = captured.Color;
                        SharedTimerService.StartWithTag(captured.Id);
                        pomo.Restart();
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
            var idleTagId = tags.FirstOrDefault(t => t.IsDefault)?.Id;
            var tagTime = new Dictionary<int, TimeSpan>();
            foreach (var r in records)
            {
                if (r.TagId == idleTagId) continue;
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
            // 较昨日（时间投入）
            var yRecords = recordRepo.GetRecordsByDate(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"));
            double yMins = 0;
            foreach (var r in yRecords)
            {
                if (r.TagId == idleTagId) continue;
                var yEnd = r.EndTime ?? DateTime.Now;
                yMins += (yEnd - r.StartTime).TotalMinutes;
            }
            if (totalSpan.TotalMinutes >= 1 || yMins >= 1)
            {
                var dMins = (int)(totalSpan.TotalMinutes - yMins);
                var am = Math.Abs(dMins);
                var durStr = am >= 60 ? $"{am / 60}h{am % 60:D2}m" : $"{am}m";
                MiniTimeTrend.Text = $"较昨日{(dMins >= 0 ? "+" : "-")}{durStr}";
                MiniTimeTrend.Foreground = dMins >= 0
                    ? (Brush)FindResource("AccentGreenBrush")
                    : new SolidColorBrush(Color.FromRgb(255, 59, 48));
            }
            else
            {
                MiniTimeTrend.Text = "";
            }
            if (tagTime.Count == 0)
            {
                MiniStatsPanel.Children.Add(new TextBlock { Text = "暂无数据", FontSize = 11, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush") });
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

                // 统一口径（与定期盘点一致）：应做才算总任务数（子任务、未设每日目标的量化、非当日的循环任务都不计）
                int completed = 0;
                int total = 0;
                foreach (var task in allTasks)
                {
                    if (task.ParentTaskId.HasValue) continue;
                    var due = taskService.TaskDueOnDate(task, _selectedDate);
                    if (due) total++;
                    if (due && taskService.TaskDoneOnDate(task, _selectedDate)) completed++;
                }

                // 较昨日（选中非今天时对比前一天）
                var cmpLabel = _selectedDate.Date == DateTime.Today ? "较昨日" : "较前日";
                var prevDate = _selectedDate.AddDays(-1);
                int prevCompleted = 0, prevTotal = 0;
                foreach (var task in allTasks)
                {
                    if (task.ParentTaskId.HasValue) continue;
                    var prevDue = taskService.TaskDueOnDate(task, prevDate);
                    if (prevDue) prevTotal++;
                    if (prevDue && taskService.TaskDoneOnDate(task, prevDate)) prevCompleted++;
                }

                if (total > 0)
                {
                    MiniTaskSummary.Visibility = Visibility.Visible;
                    MiniCompletedCount.Text = $"{completed}/{total}";
                    if (prevTotal > 0 || prevCompleted > 0)
                    {
                        var d = completed - prevCompleted;
                        MiniCompletedTrend.Text = $"{cmpLabel}{(d >= 0 ? "+" : "")}{d}";
                        MiniCompletedTrend.Foreground = d >= 0
                            ? (Brush)FindResource("AccentGreenBrush")
                            : new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    }
                    else
                    {
                        MiniCompletedTrend.Text = "";
                    }
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

        private void SubscribeGlobalEvents()
        {
            if (_globalEventsSubscribed) return;
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
            _globalEventsSubscribed = true;
        }

        private void UnsubscribeGlobalEvents()
        {
            if (!_globalEventsSubscribed) return;
            EventAggregator.Instance.Unsubscribe<string>(OnGlobalEvent);
            _globalEventsSubscribed = false;
        }

        private void RefreshTaskSurface()
        {
            if (!this.IsVisible) return;
            LoadData();
            LoadMiniStats();
        }

        private void OnGlobalEvent(string message)
        {
            if (message != "DayChanged" && message != "TaskCompleted") return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (message == "DayChanged")
                {
                    _selectedDate = DateTime.Today;
                    _stripStartDate = DateTime.Today.AddDays(-3);
                    BuildDateStrip();
                    BuildTagFilter();
                }
                RefreshTaskSurface();
            }));
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
                        RefreshTaskSurface();
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
            RefreshTaskSurface();
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
            var allTimeTags = new TimeTagRepository().GetAllTags();
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
                            QuantSnapDate = task.QuantSnapDate, QuantSnapValue = task.QuantSnapValue,
                            TimeTagId = task.TimeTagId,
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

                    // 已永久完成的任务在任意查看日期都保留；完成日仅决定今日/过去分组。
                    bool permanentlyCompleted = task.IsCompleted ||
                        (task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue &&
                         task.QuantitativeTarget.Value > 0 &&
                         (task.QuantitativeCurrent ?? 0) >= task.QuantitativeTarget.Value);
                    if (!showByDate && permanentlyCompleted)
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

            // Build task sections: active + done-today + past-done
            TasksPanel.Children.Clear();

            var activeTasks = new List<TaskItem>();
            var doneTasks = new List<TaskItem>();
            var pastDoneTasks = new List<TaskItem>();
            foreach (var t in mainTasks)
            {
                if (TryGetCompletionDate(t, out var compDate) && compDate < _selectedDate.Date)
                    pastDoneTasks.Add(t);
                else if (IsDoneOnSelectedDate(t))
                    doneTasks.Add(t);
                else
                    activeTasks.Add(t);
            }

            if (activeTasks.Count > 0)
            {
                TasksPanel.Children.Add(new TextBlock
                {
                    Text = $"进行中 ({activeTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 0, 0, 8)
                });
                BuildTaskTree(TasksPanel, activeTasks, subtasksMap, tagColorMap, false, allTimeTags);
            }

            if (doneTasks.Count > 0)
            {
                TasksPanel.Children.Add(new TextBlock
                {
                    Text = _selectedDate.Date == DateTime.Today ? $"今日已完成 ({doneTasks.Count})" : $"已完成 ({doneTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("AccentGreenBrush"),
                    Margin = new Thickness(0, 12, 0, 8)
                });
                BuildTaskTree(TasksPanel, doneTasks, subtasksMap, tagColorMap, true, allTimeTags);
            }

            if (pastDoneTasks.Count > 0)
            {
                TasksPanel.Children.Add(new TextBlock
                {
                    Text = $"过去完成 ({pastDoneTasks.Count})",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 12, 0, 8)
                });
                BuildTaskTree(TasksPanel, pastDoneTasks, subtasksMap, tagColorMap, true, allTimeTags);
            }

            if (activeTasks.Count == 0 && doneTasks.Count == 0 && pastDoneTasks.Count == 0)
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

        // ============ HELPER: 完成日期与当日完成判定 ============
        /// <summary>
        /// 一次性/周期/纯量化任务"永久完成"的日期（循环类按天打卡，无永久完成日）。
        /// </summary>
        private bool TryGetCompletionDate(TaskItem t, out DateTime completionDate)
        {
            completionDate = default;
            bool isCombined = t.Type == TaskType.Quantitative && t.RecurringPattern.HasValue;
            if (t.Type == TaskType.Recurring || isCombined) return false;
            if (t.Type == TaskType.Quantitative)
            {
                bool reached = t.QuantitativeTarget.HasValue && t.QuantitativeTarget > 0
                    && (t.QuantitativeCurrent ?? 0) >= t.QuantitativeTarget.Value;
                if (reached && t.CompletedAt.HasValue) { completionDate = t.CompletedAt.Value.Date; return true; }
                return false;
            }
            if (t.IsCompleted)
            {
                completionDate = (t.CompletedAt ?? t.LastCompletedDate ?? t.StartDate ?? t.CreatedAt).Date;
                return true;
            }
            return false;
        }

        /// <summary>任务在选中日期是否处于"已完成"状态（按日判定，供分组用）。</summary>
        private bool IsDoneOnSelectedDate(TaskItem t)
        {
            bool isCombined = t.Type == TaskType.Quantitative && t.RecurringPattern.HasValue;
            if (t.Type == TaskType.Recurring || isCombined)
                return t.IsCompleted; // 循环/组合任务加载时已按当日判定写入 IsCompleted
            if (t.Type == TaskType.Quantitative)
            {
                if (t.QuantitativeTarget.HasValue && t.QuantitativeTarget > 0
                    && (t.QuantitativeCurrent ?? 0) >= t.QuantitativeTarget.Value)
                    return true;
                return new TaskService().IsTaskCompletedForDisplay(t, _selectedDate);
            }
            if (t.IsCompleted)
            {
                TryGetCompletionDate(t, out var cd);
                return cd == _selectedDate.Date;
            }
            return false;
        }

        /// <summary>量化任务设了每日目标时，给出「今日还需 +N」的口径提示（已达标返回空）。</summary>
        private static string QuantDailyHint(TaskItem task)
        {
            if (task.Type != TaskType.Quantitative) return "";
            double dailyMin = task.QuantitativeDailyMin ?? 0;
            if (dailyMin <= 0) return "";
            double cur = task.QuantitativeCurrent ?? 0;
            double baseLine = task.QuantSnapDate.HasValue && task.QuantSnapDate.Value.Date == DateTime.Today
                ? (task.QuantSnapValue ?? cur) : cur;
            double remain = Math.Max(0, dailyMin - (cur - baseLine));
            return remain <= 0
                ? $"今日已达每日目标 +{dailyMin:0.#}"
                : $"今日还需 +{remain:0.#}（每日目标 {dailyMin:0.#}）";
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

        // ============ HELPERS: time tag lookup & hex color ============
        private static TimeTag FindTimeTag(int? id, List<TimeTag> timeTags)
        {
            if (!id.HasValue || timeTags == null) return null;
            return timeTags.Find(t => t.Id == id.Value);
        }

        private static Color? ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        /// <summary>卡片边框颜色：优先时间标签，其次目标标签（半透明描边）</summary>
        private static Brush TagBorderBrush(string hex)
        {
            var c = ParseHexColor(hex);
            if (!c.HasValue) return null;
            return new SolidColorBrush(Color.FromArgb(150, c.Value.R, c.Value.G, c.Value.B));
        }

        // ============ DRAG AND DROP (matching GoalsView pattern) ============
        // The tasks panel mixes section headers (TextBlock) and task wrappers (StackPanel),
        // and the drag source list covers only one section, so the drop position must be
        // computed among the contiguous wrapper run that contains the dragged card.
        private List<StackPanel> GetSectionWrappers(StackPanel panel, StackPanel draggedWrapper)
        {
            var wrappers = new List<StackPanel>();
            if (panel == null || draggedWrapper == null) return wrappers;
            int idx = panel.Children.IndexOf(draggedWrapper);
            if (idx < 0) return wrappers;
            int start = idx;
            while (start > 0 && panel.Children[start - 1] is StackPanel) start--;
            int end = idx;
            while (end + 1 < panel.Children.Count && panel.Children[end + 1] is StackPanel) end++;
            for (int i = start; i <= end; i++)
                if (panel.Children[i] is StackPanel sp) wrappers.Add(sp);
            return wrappers;
        }

        private int ComputeDropIndexInSection(List<StackPanel> wrappers, Point mousePos, StackPanel panel)
        {
            for (int i = 0; i < wrappers.Count; i++)
            {
                var childPos = wrappers[i].TransformToAncestor(panel).Transform(new Point(0, 0));
                if (mousePos.Y < childPos.Y + wrappers[i].ActualHeight / 2)
                    return i;
            }
            return wrappers.Count;
        }

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

                if (_isDragging && _dragMainPanel != null && _dragPanel != null)
                {
                    bool hadPlaceholder = _placeholderBorder != null && _dragMainPanel.Children.Contains(_placeholderBorder);
                    if (hadPlaceholder) _dragMainPanel.Children.Remove(_placeholderBorder);

                    var sectionWrappers = GetSectionWrappers(_dragMainPanel, _dragPanel);
                    int dropIndex = ComputeDropIndexInSection(sectionWrappers, e.GetPosition(_dragMainPanel), _dragMainPanel);
                    if (_placeholderBorder != null && sectionWrappers.Count > 0)
                    {
                        int anchor = dropIndex < sectionWrappers.Count ? dropIndex : sectionWrappers.Count - 1;
                        int insertAt = _dragMainPanel.Children.IndexOf(sectionWrappers[anchor]);
                        if (dropIndex >= sectionWrappers.Count) insertAt++;
                        _dragMainPanel.Children.Insert(insertAt, _placeholderBorder);
                    }
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

                    var sectionWrappers = _dragMainPanel != null && _dragPanel != null
                        ? GetSectionWrappers(_dragMainPanel, _dragPanel)
                        : new List<StackPanel>();
                    int dropIndex = ComputeDropIndexInSection(sectionWrappers, e.GetPosition(_dragMainPanel), _dragMainPanel);

                    if (dropIndex != _dragSourceIndex && _dragSourceList != null &&
                        _dragSourceIndex >= 0 && _dragSourceIndex < _dragSourceList.Count &&
                        dropIndex >= 0 && dropIndex <= _dragSourceList.Count)
                    {
                        var item = _dragSourceList[_dragSourceIndex];
                        _dragSourceList.RemoveAt(_dragSourceIndex);
                        if (dropIndex > _dragSourceIndex) dropIndex--;
                        if (dropIndex < 0) dropIndex = 0;
                        if (dropIndex > _dragSourceList.Count) dropIndex = _dragSourceList.Count;
                        _dragSourceList.Insert(dropIndex, item);

                        var repo = new TaskRepository();
                        for (int i = 0; i < _dragSourceList.Count; i++)
                        {
                            _dragSourceList[i].Priority = _dragSourceList.Count - i;
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
                        if (dropIndex < 0) dropIndex = 0;
                        if (dropIndex > _dragSourceList.Count) dropIndex = _dragSourceList.Count;
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
                if (current >= task.QuantitativeTarget.Value)
                    return task.CompletedAt.HasValue && task.CompletedAt.Value.Date == date.Date;
                // 未达标：每日目标按当日记录/基线口径判定
                double dailyMin = task.QuantitativeDailyMin ?? 0;
                if (dailyMin <= 0) return false;
                return ts.IsTaskCompletedForDisplay(task, date);
            }
            if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
            {
                if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
                    return ts.GetCustomRecurringCountOnDate(task.Id, date) >= task.RecurringTargetCount.Value;
                return ts.IsRecurringTaskCompletedOnDate(task, date);
            }
            return task.IsCompleted;
        }

        /// <summary>
        /// 今日目标区域的任务过滤：只显示该日期存在的任务；量化任务须设置了每日目标，
        /// 且已达标（完成日早于该日期）的量化任务不再出现。
        /// </summary>
        private bool TaskOccursForGoals(TaskItem t, DateTime date)
        {
            if (t.IsDeleted || t.ParentTaskId.HasValue) return false;

            bool isCycle = t.Type == TaskType.Recurring || (t.Type == TaskType.Quantitative && t.RecurringPattern.HasValue);

            // 量化任务：未设置每日目标的不计入统计；已达总目标的只算到达标当天
            if (t.Type == TaskType.Quantitative)
            {
                if (!t.QuantitativeDailyMin.HasValue || t.QuantitativeDailyMin.Value <= 0) return false;
                if (t.QuantitativeTarget.HasValue && t.QuantitativeTarget > 0
                    && (t.QuantitativeCurrent ?? 0) >= t.QuantitativeTarget.Value
                    && (!t.CompletedAt.HasValue || t.CompletedAt.Value.Date < date.Date))
                    return false;
            }

            // 非循环类的永久完成项（单次任务 / 纯量化达标）：只在完成当日出现，
            // 其余日期已归入任务列表的「过去完成」，今日目标里不再展示历史完成项
            if (!isCycle && t.IsCompleted)
            {
                var cd = (t.CompletedAt ?? t.LastCompletedDate ?? t.StartDate ?? t.CreatedAt).Date;
                if (cd != date.Date) return false;
            }

            if (isCycle)
                return new TaskService().ShouldShowRecurringTaskOnDate(t, date);

            if (t.StartDate.HasValue && t.EndDate.HasValue)
                return t.StartDate.Value.Date <= date.Date && t.EndDate.Value.Date >= date.Date;
            if (t.StartDate.HasValue)
                return t.StartDate.Value.Date == date.Date;
            // 无日期任务：量化从创建日起每天计；其余只算创建当天
            if (t.Type == TaskType.Quantitative) return date.Date >= t.CreatedAt.Date;
            return date.Date == t.CreatedAt.Date;
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
                    show = goal.StartDate.Value.Date <= _selectedDate.Date && goal.EndDate.Value.Date >= _selectedDate.Date;
                else if (goal.StartDate.HasValue)
                    show = goal.StartDate.Value.Date == _selectedDate.Date;
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

                // Tasks under this goal（只显示该日期存在的任务；量化须设每日目标）
                var goalTasks = allTasks.FindAll(t => t.GoalId == goal.Id && TaskOccursForGoals(t, _selectedDate));
                if (goalTasks.Count > 0)
                {
                    foreach (var task in goalTasks)
                    {
                        var taskPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 4) };

                        // Task row
                        bool taskDone = IsTaskDisplayCompleted(task, _selectedDate);
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
                                bool subDone = IsTaskDisplayCompleted(sub, _selectedDate);
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
            // 前置依赖未完成时锁定，不允许打卡/记进度
            var svcDep = new TaskService();
            var blocker = svcDep.BlockingPredecessor(task, new TaskRepository().GetAllTasks());
            if (blocker != null)
            {
                MessageBox.Show($"需先完成前置任务「{blocker.Title}」", "依赖未满足",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (task.Type == TaskType.Quantitative && task.QuantitativeMode.HasValue)
            {
                var dialog = new QuantitativeInputDialog(task) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true)
                {
                    var repo = new TaskRepository();
                    var tsq = new TaskService();
                    var oldValue = task.QuantitativeCurrent ?? 0;
                    var qDate = dialog.SelectedDate ?? DateTime.Today;

                    if (qDate != DateTime.Today && task.QuantitativeMode.Value == QuantitativeMode.Accumulate)
                    {
                        // 补记：增量记到所选历史日期（日志+总进度），再判目标达成
                        double delta = dialog.NewValue - oldValue;
                        if (delta != 0)
                        {
                            tsq.RecordQuantProgress(task, qDate, delta);
                            if (task.QuantitativeTarget.HasValue && (task.QuantitativeCurrent ?? 0) >= task.QuantitativeTarget.Value)
                            {
                                task.IsCompleted = true;
                                task.CompletedAt = DateTime.Now;
                                repo.UpdateTask(task);
                            }
                            if (task.CountTowardsParent)
                            {
                                if (task.ParentTaskId.HasValue)
                                    SyncParentTaskProgress(task.ParentTaskId.Value, delta, repo);
                                if (task.GoalId.HasValue)
                                    RecalcGoalProgressFromSubtasks(task.GoalId.Value, repo);
                            }
                            EventAggregator.Instance.Publish("TaskCompleted");
                            RefreshTaskSurface();
                        }
                        return;
                    }

                    // 先落今日基线（以修改前的值为准），再应用新值，否则首次操作会把本次增量算进基线
                    tsq.EnsureQuantBaseline(task);
                    task.QuantitativeCurrent = dialog.NewValue;
                    // 今日口径以日志为准：本次变动入账（含负增量，改正用）
                    TaskService.AppendQuantLog(task, DateTime.Today, dialog.NewValue - oldValue);
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
                    }
                    else
                    {
                        task.IsCompleted = false;
                        task.CompletedAt = null;
                    }
                    // 设了每日目标：按"当前值-今日基线 >= 每日目标"统一重算当日完成记录（组合/纯量化一致）
                    if (!reachedTarget && task.QuantitativeDailyMin.HasValue && task.QuantitativeDailyMin.Value > 0)
                    {
                        tsq.EvalQuantitativeDaily(task, _selectedDate);
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
                    RefreshTaskSurface();
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
            RefreshTaskSurface();
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
