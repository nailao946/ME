using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ME.Data;
using ME.Models;
using ME.Core;
using ME.Services;

namespace ME.Views
{
    public partial class ReviewView : UserControl
    {
        private enum ReviewPeriod { Today, Week, Month, All }
        private ReviewPeriod _currentPeriod = ReviewPeriod.Week;
        private readonly TaskCompletionRepository _completionRepo;
        private readonly TimeRecordRepository _timeRecordRepo;
        private readonly TimeTagRepository _timeTagRepo;
        private HashSet<int> _selectedTagIds = new HashSet<int>();
        private ReviewPeriod _statsPeriod = ReviewPeriod.Week;
        private bool _tagsInited = false;

        private static readonly Color[] LineChartColors = new[]
        {
            Color.FromRgb(0, 122, 255),
            Color.FromRgb(52, 199, 89),
            Color.FromRgb(255, 149, 0),
            Color.FromRgb(175, 82, 222),
            Color.FromRgb(255, 45, 85),
            Color.FromRgb(90, 200, 250),
            Color.FromRgb(255, 204, 0),
            Color.FromRgb(88, 86, 214),
        };
        private bool _hasAnimated = false;

        public ReviewView()
        {
            InitializeComponent();
            _completionRepo = new TaskCompletionRepository();
            _timeRecordRepo = new TimeRecordRepository();
            _timeTagRepo = new TimeTagRepository();
            UpdatePeriodButtons();
            this.IsVisibleChanged += (s, e) =>
            {
                if (this.IsVisible)
                {
                    LoadData();
                    if (!_hasAnimated) { _hasAnimated = true; AnimateEntrance(); }
                }
            };
            this.SizeChanged += (s, e) =>
            {
                if (this.IsVisible) LoadTimeStats();
            };
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
            LoadData();
        }

        private void OnGlobalEvent(string message)
        {
            if (message == "TaskCompleted" || message == "DayChanged")
                Dispatcher.BeginInvoke(new Action(() => { if (this.IsVisible) { LoadData(); } }));
        }

        private void PeriodBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string period)
            {
                switch (period)
                {
                    case "Today": _currentPeriod = ReviewPeriod.Today; break;
                    case "Week": _currentPeriod = ReviewPeriod.Week; break;
                    case "Month": _currentPeriod = ReviewPeriod.Month; break;
                    case "All": _currentPeriod = ReviewPeriod.All; break;
                }
                UpdatePeriodButtons();
                LoadData();
            }
        }

        private void UpdatePeriodButtons()
        {
            TodayBtn.Style = (Style)FindResource(_currentPeriod == ReviewPeriod.Today ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WeeklyBtn.Style = (Style)FindResource(_currentPeriod == ReviewPeriod.Week ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            MonthlyBtn.Style = (Style)FindResource(_currentPeriod == ReviewPeriod.Month ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            AllBtn.Style = (Style)FindResource(_currentPeriod == ReviewPeriod.All ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        }

        private void AnimateEntrance()
        {
            // Animate the ring after a short delay
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                // Re-trigger ring animation with current rate
                var rateText = CompletionRateText.Text.Replace("%", "");
                if (double.TryParse(rateText, out var rate))
                    AnimateRateRing(rate);
            };
            timer.Start();
        }

        private void LoadData()
        {
            var taskRepo = new TaskRepository();
            var goalRepo = new GoalRepository();
            var allTasks = taskRepo.GetAllTasks();
            var taskService = new TaskService();

            var now = DateTime.Now;
            DateTime startDate;
            switch (_currentPeriod)
            {
                case ReviewPeriod.Today:
                    startDate = now.Date;
                    break;
                case ReviewPeriod.Week:
                    startDate = TaskService.GetWeekStartForDate(now);
                    break;
                case ReviewPeriod.Month:
                    startDate = new DateTime(now.Year, now.Month, 1);
                    break;
                case ReviewPeriod.All:
                    startDate = now.Date.AddYears(-1);
                    break;
                default:
                    startDate = TaskService.GetWeekStartForDate(now);
                    break;
            }
            var startStr = startDate.ToString("yyyy-MM-dd");
            var endStr = now.ToString("yyyy-MM-dd");

            DateRangeText.Text = _currentPeriod == ReviewPeriod.All
                ? $"全部 — {now:yyyy/MM/dd}"
                : $"{startDate:MM/dd} — {now:MM/dd}";

            // 统计口径：只算主任务按天计——应做(总任务数)与已完成；
            // 子任务不计；未设每日目标的量化任务不计；周六周日的循环任务周一不计入总任务数
            var mainTasks = allTasks.Where(t => taskService.TaskCountsForStats(t)).ToList();
            var allCompletions = _completionRepo.GetAll();

            int completed = 0, total = 0;
            var dailyData = new SortedDictionary<string, DailyData>();
            foreach (var t in mainTasks)
            {
                var taskRecords = allCompletions.Where(c => c.TaskId == t.Id).ToList();
                for (var d = startDate; d <= now; d = d.AddDays(1))
                {
                    var key = d.ToString("yyyy-MM-dd");
                    if (!dailyData.ContainsKey(key))
                        dailyData[key] = new DailyData { Date = d.ToString("MM/dd") };
                    if (taskService.TaskDueOnDate(t, d)) { total++; dailyData[key].Due++; }
                    if (taskService.TaskDoneOnDate(t, d, taskRecords)) { completed++; dailyData[key].Completed++; }
                }
            }

            // 完成率 = 完成任务 / 总任务数
            var rate = total > 0 ? (double)completed / total * 100 : 0;
            CompletionRateText.Text = $"{rate:F0}%";
            AnimateRateRing(rate);

            CompletedCountText.Text = $"{completed} / {total}";

            // 较上期（今天=比昨天、本周=比上周、本月=比上月；全部不比）
            DateTime prevStart, prevEnd;
            switch (_currentPeriod)
            {
                case ReviewPeriod.Today:
                    prevStart = startDate.AddDays(-1);
                    prevEnd = startDate.AddDays(-1);
                    break;
                case ReviewPeriod.Week:
                    prevStart = startDate.AddDays(-7);
                    prevEnd = startDate.AddDays(-1);
                    break;
                case ReviewPeriod.Month:
                    prevStart = startDate.AddMonths(-1);
                    prevEnd = startDate.AddDays(-1);
                    break;
                default:
                    prevStart = startDate;
                    prevEnd = startDate;
                    break;
            }
            bool hasPrev = _currentPeriod != ReviewPeriod.All;
            int prevCompleted = 0, prevTotal = 0;
            double prevRate = 0;
            if (hasPrev)
            {
                foreach (var t in mainTasks)
                {
                    var taskRecords = allCompletions.Where(c => c.TaskId == t.Id).ToList();
                    for (var d = prevStart; d <= prevEnd; d = d.AddDays(1))
                    {
                        if (taskService.TaskDueOnDate(t, d)) prevTotal++;
                        if (taskService.TaskDoneOnDate(t, d, taskRecords)) prevCompleted++;
                    }
                }
                prevRate = prevTotal > 0 ? (double)prevCompleted / prevTotal * 100 : 0;
            }

            // 完成任务较上期
            if (hasPrev && (prevCompleted > 0 || completed > 0))
                SetTrendText(CompletedTrendText, completed - prevCompleted, "较上期");
            else
                CompletedTrendText.Text = "";

            // 完成率较上期（百分点）
            if (hasPrev && (prevTotal > 0 || total > 0))
            {
                var rateDiff = Math.Round(rate - prevRate);
                var sign = rateDiff >= 0 ? "+" : "";
                RateTrendText.Text = $"较上期{sign}{rateDiff:F0}%";
                RateTrendText.Foreground = rateDiff >= 0
                    ? (Brush)FindResource("AccentGreenBrush")
                    : new SolidColorBrush(Color.FromRgb(255, 59, 48));
            }
            else
            {
                RateTrendText.Text = "";
            }

            // Time invested
            var timeRecords = _timeRecordRepo.GetRecordsByDateRange(startStr, endStr);
            var totalMinutes = timeRecords.Sum(r => r.Duration.TotalMinutes);
            var hours = (int)(totalMinutes / 60);
            var mins = (int)(totalMinutes % 60);
            TimeInvestedText.Text = hours > 0 ? $"{hours}h{mins:D2}" : $"{mins}m";
            var days = Math.Max(1, DaysBetween(startDate, now) + 1);
            var avgDaily = totalMinutes / days;
            TimeDailyAvgText.Text = $"日均 {(int)(avgDaily / 60)}h{(int)(avgDaily % 60):D2}m";

            // 时间投入较上期
            if (hasPrev)
            {
                var prevRecords = _timeRecordRepo.GetRecordsByDateRange(prevStart.ToString("yyyy-MM-dd"), prevEnd.ToString("yyyy-MM-dd"));
                var prevMinutes = (int)prevRecords.Sum(r => r.Duration.TotalMinutes);
                var curMinutes = (int)totalMinutes;
                if (prevMinutes > 0 || curMinutes > 0)
                {
                    var tDiff = curMinutes - prevMinutes;
                    var am = Math.Abs(tDiff);
                    var durStr = am >= 60 ? $"{am / 60}h{am % 60:D2}m" : $"{am}m";
                    TimeTrendText.Text = $"较上期{(tDiff >= 0 ? "+" : "-")}{durStr}";
                    TimeTrendText.Foreground = tDiff >= 0
                        ? (Brush)FindResource("AccentGreenBrush")
                        : new SolidColorBrush(Color.FromRgb(255, 59, 48));
                }
                else TimeTrendText.Text = "";
            }
            else
            {
                TimeTrendText.Text = "";
            }

            // Streak
            int streak = 0, bestStreak = 0, tempStreak = 0;
            var checkDate = DateTime.Today;
            while (checkDate >= startDate)
            {
                var dateKey = checkDate.ToString("yyyy-MM-dd");
                if (dailyData.ContainsKey(dateKey) && dailyData[dateKey].Completed > 0)
                {
                    streak++;
                    checkDate = checkDate.AddDays(-1);
                }
                else break;
            }
            foreach (var kv in dailyData)
            {
                if (kv.Value.Completed > 0) { tempStreak++; if (tempStreak > bestStreak) bestStreak = tempStreak; }
                else tempStreak = 0;
            }
            StreakDaysText.Text = $"{streak}";
            StreakBestText.Text = $"最长 {bestStreak} 天";

            // Goal tree (replaces heatmap)
            BuildGoalTreeView();

            // Time allocation
            BuildTimeAllocation(timeRecords);

            // Time stats
            if (!_tagsInited) InitTagSelection();
            LoadTimeStats();
        }

        private void SetTrendText(TextBlock tb, int diff, string prefix)
        {
            var sign = diff >= 0 ? "+" : "";
            tb.Text = $"{prefix}{sign}{diff}";
            tb.Foreground = diff >= 0
                ? (Brush)FindResource("AccentGreenBrush")
                : new SolidColorBrush(Color.FromRgb(255, 59, 48));
        }

        private int DaysBetween(DateTime a, DateTime b) => Math.Max(0, (int)(b.Date - a.Date).TotalDays);

        private void AnimateRateRing(double rate)
        {
            // Circumference = 2πr, r = (48-4)/2 = 22, C ≈ 138.23
            // DashArray value = C / StrokeThickness = 138.23 / 4 ≈ 34.56
            var dashLen = 34.56;
            var targetOffset = dashLen * (1 - rate / 100);
            var anim = new DoubleAnimation(dashLen, targetOffset, TimeSpan.FromSeconds(0.8))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            RateRing.BeginAnimation(Ellipse.StrokeDashOffsetProperty, anim);
        }

        private void BuildGoalTreeView()
        {
            GoalTreeView.Items.Clear();
            var goalRepo = new GoalRepository();
            var taskRepo = new TaskRepository();
            var tagRepo = new TagRepository();
            var allGoals = goalRepo.GetAllGoals();
            var allTasks = taskRepo.GetAllTasks();
            var allTags = tagRepo.GetAllTags();

            foreach (var goal in allGoals)
            {
                if (!goal.ParentId.HasValue)
                {
                    var goalColor = GetGoalDisplayColor(goal, allTags);
                    var goalItem = CreateGoalTreeItem(goal, goalColor);

                    var reviewTs = new TaskService();

                    foreach (var childGoal in allGoals)
                    {
                        if (childGoal.ParentId == goal.Id)
                        {
                            var childColor = GetGoalDisplayColor(childGoal, allTags);
                            var childItem = CreateGoalTreeItem(childGoal, childColor);

                            foreach (var task in allTasks)
                            {
                                if (task.GoalId == childGoal.Id && !task.IsDeleted && !reviewTs.IsTaskCompletedForDisplay(task))
                                {
                                    childItem.Items.Add(new TreeViewItem
                                    {
                                        Header = $"○ {task.Title}",
                                        Tag = task,
                                        Foreground = (SolidColorBrush)FindResource("TextBrush")
                                    });
                                }
                            }
                            goalItem.Items.Add(childItem);
                        }
                    }

                    foreach (var task in allTasks)
                    {
                        if (task.GoalId == goal.Id && !task.IsDeleted)
                        {
                            var done = reviewTs.IsTaskCompletedForDisplay(task);
                            var status = done ? "✓" : "○";
                            goalItem.Items.Add(new TreeViewItem
                            {
                                Header = $"{status} {task.Title}",
                                Tag = task,
                                Foreground = (SolidColorBrush)FindResource("TextBrush")
                            });
                        }
                    }

                    GoalTreeView.Items.Add(goalItem);
                }
            }
        }

        private TreeViewItem CreateGoalTreeItem(Goal goal, Color progressColor)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            var circle = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
            var bgCircle = new Ellipse
            {
                Width = 16, Height = 16,
                Stroke = new SolidColorBrush(Color.FromRgb(229, 229, 234)),
                StrokeThickness = 2, Fill = Brushes.Transparent
            };
            circle.Children.Add(bgCircle);
            if (goal.Progress > 0)
            {
                var angle = goal.Progress / 100.0 * 360.0;
                var radius = 7;
                var center = new Point(8, 8);
                var startAngle = -90;
                var endAngle = startAngle + angle;
                var startPoint = new Point(
                    center.X + radius * Math.Cos(startAngle * Math.PI / 180),
                    center.Y + radius * Math.Sin(startAngle * Math.PI / 180));
                var endPoint = new Point(
                    center.X + radius * Math.Cos(endAngle * Math.PI / 180),
                    center.Y + radius * Math.Sin(endAngle * Math.PI / 180));
                var fig = new PathFigure { StartPoint = startPoint, IsClosed = false };
                fig.Segments.Add(new ArcSegment
                {
                    Point = endPoint, Size = new Size(radius, radius),
                    IsLargeArc = angle > 180, SweepDirection = SweepDirection.Clockwise, IsStroked = true
                });
                circle.Children.Add(new Path
                {
                    Data = new PathGeometry { Figures = { fig } },
                    Stroke = new SolidColorBrush(progressColor),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
            panel.Children.Add(circle);

            panel.Children.Add(new TextBlock
            {
                Text = $"  {goal.Name}  [{goal.Progress:F0}%]",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)FindResource("TextBrush")
            });

            return new TreeViewItem { Header = panel, Tag = goal };
        }

        private Color GetGoalDisplayColor(Goal goal, List<GoalTag> allTags)
        {
            if (goal.TagId.HasValue)
            {
                var tag = allTags.Find(t => t.Id == goal.TagId.Value);
                if (tag != null)
                {
                    try { return (Color)ColorConverter.ConvertFromString(tag.Color); }
                    catch { }
                }
            }
            switch (goal.Color)
            {
                case GoalColor.Red: return Color.FromRgb(255, 59, 48);
                case GoalColor.Green: return Color.FromRgb(52, 199, 89);
                case GoalColor.Blue: return Color.FromRgb(0, 122, 255);
                case GoalColor.Pink: return Color.FromRgb(255, 45, 85);
                case GoalColor.Gray: return Color.FromRgb(142, 142, 147);
                case GoalColor.Yellow: return Color.FromRgb(255, 204, 0);
                default: return Color.FromRgb(0, 122, 255);
            }
        }

        private void BuildTimeAllocation(List<TimeRecord> records)
        {
            TimeAllocPanel.Children.Clear();
            var tags = _timeTagRepo.GetAllTags();

            var idleTagId = tags.FirstOrDefault(t => t.IsDefault)?.Id;
            var tagTimes = new Dictionary<int, double>();
            foreach (var r in records)
            {
                if (r.TagId == idleTagId) continue;
                if (!tagTimes.ContainsKey(r.TagId)) tagTimes[r.TagId] = 0;
                tagTimes[r.TagId] += r.Duration.TotalMinutes;
            }

            if (tagTimes.Count == 0)
            {
                TimeAllocPanel.Children.Add(new TextBlock
                {
                    Text = "暂无时间记录",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            var maxMinutes = tagTimes.Values.Max();
            var sorted = tagTimes.OrderByDescending(kv => kv.Value);
            int idx = 0;

            foreach (var kv in sorted)
            {
                var tag = tags.FirstOrDefault(t => t.Id == kv.Key);
                var name = tag?.Name ?? "未标记";
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag?.Color ?? "#808080"); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }

                var mins = kv.Value;
                var h = (int)(mins / 60);
                var m = (int)(mins % 60);
                var timeStr = h > 0 ? $"{h}h{m:D2}m" : $"{m}m";
                var pct = maxMinutes > 0 ? mins / maxMinutes : 0;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });

                var dotRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                dotRow.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(tagColor),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                dotRow.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(dotRow);

                // Animated bar
                var barBg = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(30, tagColor.R, tagColor.G, tagColor.B)),
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(6, 0, 6, 0)
                };
                var bar = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(tagColor),
                    Width = 0, // Start at 0, animate to target
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                barBg.Child = bar;
                Grid.SetColumn(barBg, 1);
                row.Children.Add(barBg);

                row.Children.Add(new TextBlock
                {
                    Text = timeStr,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(tagColor),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right
                });
                Grid.SetColumn(row.Children[row.Children.Count - 1], 2);

                TimeAllocPanel.Children.Add(row);

                // Animate bar width with staggered delay
                var targetWidth = pct * 100;
                var delay = TimeSpan.FromMilliseconds(200 + idx * 80);
                var widthAnim = new DoubleAnimation(0, targetWidth, TimeSpan.FromSeconds(0.5))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    BeginTime = delay
                };
                bar.BeginAnimation(Border.WidthProperty, widthAnim);
                idx++;
            }
        }

        private void StatsPeriodBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string period)
            {
                switch (period)
                {
                    case "Today": _statsPeriod = ReviewPeriod.Today; break;
                    case "Week": _statsPeriod = ReviewPeriod.Week; break;
                    case "Month": _statsPeriod = ReviewPeriod.Month; break;
                    case "All": _statsPeriod = ReviewPeriod.All; break;
                }
                UpdateStatsPeriodButtons();
                LoadTimeStats();
            }
        }

        private void UpdateStatsPeriodButtons()
        {
            StatsTodayBtn.Style = (Style)FindResource(_statsPeriod == ReviewPeriod.Today ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            StatsWeeklyBtn.Style = (Style)FindResource(_statsPeriod == ReviewPeriod.Week ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            StatsMonthlyBtn.Style = (Style)FindResource(_statsPeriod == ReviewPeriod.Month ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            StatsAllBtn.Style = (Style)FindResource(_statsPeriod == ReviewPeriod.All ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        }

        private void InitTagSelection()
        {
            _selectedTagIds.Clear();
            var allTags = _timeTagRepo.GetAllTags();
            _tagsInited = true;
            foreach (var tag in allTags)
            {
                if (!tag.IsDefault)
                    _selectedTagIds.Add(tag.Id);
            }
        }

        private void TagChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is int tagId)
            {
                if (_selectedTagIds.Contains(tagId))
                    _selectedTagIds.Remove(tagId);
                else
                    _selectedTagIds.Add(tagId);
                LoadTimeStats();
            }
        }

        private void LoadTimeStats()
        {
            if (StatsChartCanvas == null) return;

            var now = DateTime.Now;
            DateTime startDate;
            switch (_statsPeriod)
            {
                case ReviewPeriod.Today: startDate = now.Date; break;
                case ReviewPeriod.Week: startDate = TaskService.GetWeekStartForDate(now); break;
                case ReviewPeriod.Month: startDate = new DateTime(now.Year, now.Month, 1); break;
                case ReviewPeriod.All: startDate = now.Date.AddYears(-1); break;
                default: startDate = TaskService.GetWeekStartForDate(now); break;
            }
            var startStr = startDate.ToString("yyyy-MM-dd");
            var endStr = now.ToString("yyyy-MM-dd");
            var totalDays = Math.Max(1, (int)(now.Date - startDate.Date).TotalDays + 1);

            var allTags = _timeTagRepo.GetAllTags();
            var records = _timeRecordRepo.GetRecordsByDateRange(startStr, endStr);

            bool monthly = _statsPeriod == ReviewPeriod.All;
            var dateKeys = new List<(string display, string full)>();
            if (monthly)
            {
                // 全部：近 12 个月按月分组，月度对比（按天太密看不清日期）
                var curMonth = new DateTime(now.Year, now.Month, 1);
                for (int i = 11; i >= 0; i--)
                {
                    var m = curMonth.AddMonths(-i);
                    dateKeys.Add((m.ToString("yy/MM"), m.ToString("yyyy-MM")));
                }
            }
            else
            {
                for (var d = startDate; d <= now; d = d.AddDays(1))
                    dateKeys.Add((d.ToString("MM/dd"), d.ToString("yyyy-MM-dd")));
            }

            var tagData = new Dictionary<int, List<(string date, TimeSpan dur)>>();
            foreach (var tag in allTags)
            {
                if (!_selectedTagIds.Contains(tag.Id)) continue;
                var dailyList = new List<(string date, TimeSpan dur)>();
                foreach (var (displayKey, fullKey) in dateKeys)
                {
                    var total = TimeSpan.Zero;
                    foreach (var r in records)
                    {
                        if (r.TagId != tag.Id) continue;
                        if (monthly ? r.Date.StartsWith(fullKey) : r.Date == fullKey)
                            total += r.Duration;
                    }
                    dailyList.Add((displayKey, total));
                }
                tagData[tag.Id] = dailyList;
            }

            tagData = PrepareChartTagData(tagData);

            BuildTagChips(allTags);
            BuildTimeStatsChart(tagData, allTags);
            BuildLegend(tagData, allTags);
            BuildStatsSummary(tagData, allTags, totalDays);
        }

        private const int MaxChartTags = 6;
        private const int OthersKey = -1;

        private Dictionary<int, List<(string date, TimeSpan dur)>> PrepareChartTagData(Dictionary<int, List<(string date, TimeSpan dur)>> tagData)
        {
            var withData = tagData
                .Where(kv => kv.Value.Sum(d => d.dur.TotalSeconds) > 0)
                .OrderByDescending(kv => kv.Value.Sum(d => d.dur.TotalSeconds))
                .ToList();
            if (withData.Count == 0)
                return new Dictionary<int, List<(string date, TimeSpan dur)>>();
            if (withData.Count <= MaxChartTags)
                return withData.ToDictionary(kv => kv.Key, kv => kv.Value);

            var top = withData.Take(MaxChartTags).ToList();
            var rest = withData.Skip(MaxChartTags).ToList();
            var count = top[0].Value.Count;
            var others = new List<(string date, TimeSpan dur)>();
            for (int i = 0; i < count; i++)
            {
                var total = TimeSpan.Zero;
                foreach (var kv in rest) total += kv.Value[i].dur;
                others.Add((top[0].Value[i].date, total));
            }
            var result = top.ToDictionary(kv => kv.Key, kv => kv.Value);
            result[OthersKey] = others;
            return result;
        }

        private void BuildLegend(Dictionary<int, List<(string date, TimeSpan dur)>> tagData, List<TimeTag> allTags)
        {
            StatsLegendPanel.Children.Clear();
            int colorIdx = 0;
            foreach (var kv in tagData)
            {
                var tag = kv.Key == OthersKey ? null : allTags.FirstOrDefault(t => t.Id == kv.Key);
                Color tagColor;
                if (kv.Key == OthersKey)
                {
                    tagColor = Color.FromRgb(142, 142, 147);
                }
                else
                {
                    try { tagColor = (Color)ColorConverter.ConvertFromString(tag?.Color ?? "#007AFF"); }
                    catch { tagColor = LineChartColors[colorIdx % LineChartColors.Length]; }
                }
                var name = kv.Key == OthersKey ? "其他" : (tag?.Name ?? "未标记");

                var legendItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4) };
                legendItem.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(tagColor),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                legendItem.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                StatsLegendPanel.Children.Add(legendItem);
                colorIdx++;
            }
        }

        private void BuildTagChips(List<TimeTag> allTags)
        {
            TagSelectorPanel.Children.Clear();
            foreach (var tag in allTags)
            {
                if (tag.IsDefault) continue;
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }
                var isSelected = _selectedTagIds.Contains(tag.Id);

                var chip = new Border
                {
                    Tag = tag.Id,
                    CornerRadius = new CornerRadius(12),
                    Background = isSelected ? new SolidColorBrush(tagColor) : new SolidColorBrush(Color.FromArgb(30, tagColor.R, tagColor.G, tagColor.B)),
                    BorderBrush = new SolidColorBrush(tagColor),
                    BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var tb = new TextBlock
                {
                    Text = tag.Name,
                    FontSize = 12,
                    Foreground = isSelected ? Brushes.White : new SolidColorBrush(tagColor)
                };
                chip.Child = tb;
                chip.MouseLeftButtonDown += TagChip_Click;
                TagSelectorPanel.Children.Add(chip);
            }
        }

        private void BuildTimeStatsChart(Dictionary<int, List<(string date, TimeSpan dur)>> tagData, List<TimeTag> allTags)
        {
            StatsChartCanvas.Children.Clear();
            var canvasWidth = StatsChartCanvas.ActualWidth > 0 ? StatsChartCanvas.ActualWidth : StatsChartGrid.ActualWidth;
            if (canvasWidth < 10) canvasWidth = 600;
            var canvasHeight = StatsChartCanvas.ActualHeight > 0 ? StatsChartCanvas.ActualHeight : 200;

            if (tagData.Count == 0)
            {
                StatsChartCanvas.Children.Add(new TextBlock
                {
                    Text = "请选择标签查看时间统计",
                    FontSize = 13,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                Canvas.SetLeft(StatsChartCanvas.Children[StatsChartCanvas.Children.Count - 1], canvasWidth / 2 - 80);
                Canvas.SetTop(StatsChartCanvas.Children[StatsChartCanvas.Children.Count - 1], canvasHeight / 2 - 10);
                return;
            }

            var firstEntry = tagData.First().Value;
            if (firstEntry.Count == 0) return;

            int maxMinutes = 1;
            foreach (var kv in tagData)
            {
                foreach (var (_, dur) in kv.Value)
                {
                    var mins = (int)dur.TotalMinutes;
                    if (mins > maxMinutes) maxMinutes = mins;
                }
            }
            maxMinutes = ((maxMinutes + 9) / 10) * 10;
            if (maxMinutes == 0) maxMinutes = 10;

            var padding = 45;
            var chartWidth = canvasWidth - padding * 2;
            var chartHeight = canvasHeight - padding * 2;

            int colorIdx = 0;
            var tagLines = new Dictionary<int, List<Point>>();

            foreach (var kv in tagData)
            {
                var tag = kv.Key == OthersKey ? null : allTags.FirstOrDefault(t => t.Id == kv.Key);
                Color tagColor;
                if (kv.Key == OthersKey)
                {
                    tagColor = Color.FromRgb(142, 142, 147);
                }
                else
                {
                    try { tagColor = (Color)ColorConverter.ConvertFromString(tag?.Color ?? "#007AFF"); }
                    catch { tagColor = LineChartColors[colorIdx % LineChartColors.Length]; }
                }
                var tagName = kv.Key == OthersKey ? "其他" : (tag?.Name ?? "未标记");

                var points = new List<Point>();
                var step = kv.Value.Count > 1 ? chartWidth / (double)(kv.Value.Count - 1) : chartWidth / 2.0;

                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var x = padding + i * step;
                    var mins = (int)kv.Value[i].dur.TotalMinutes;
                    var y = padding + chartHeight - (mins / (double)maxMinutes * chartHeight);
                    points.Add(new Point(x, y));

                    var dot = new Ellipse
                    {
                        Width = 5,
                        Height = 5,
                        Fill = new SolidColorBrush(tagColor),
                        Stroke = (Brush)FindResource("CardBrush"),
                        StrokeThickness = 1,
                        Tag = $"{tagName}: {mins}m"
                    };
                    dot.ToolTip = $"{kv.Value[i].date}\n{tagName}: {mins}分钟";
                    Canvas.SetLeft(dot, x - 2.5);
                    Canvas.SetTop(dot, y - 2.5);
                    StatsChartCanvas.Children.Add(dot);
                }

                if (points.Count > 1)
                {
                    var lineFigure = new PathFigure { StartPoint = points[0] };
                    for (int i = 1; i < points.Count; i++)
                        lineFigure.Segments.Add(new LineSegment(points[i], true));
                    var lineGeometry = new PathGeometry();
                    lineGeometry.Figures.Add(lineFigure);
                    StatsChartCanvas.Children.Add(new Path
                    {
                        Data = lineGeometry,
                        Stroke = new SolidColorBrush(tagColor),
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round
                    });
                }

                tagLines[kv.Key] = points;
                colorIdx++;
            }
            for (int i = 0; i <= 4; i++)
            {
                var y = padding + i * chartHeight / 4;
                var labelVal = (int)(maxMinutes * (1 - i / 4.0));
                var label = new TextBlock
                {
                    Text = $"{labelVal}m",
                    FontSize = 9,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, y - 7);
                StatsChartCanvas.Children.Add(label);

                var line = new Line
                {
                    X1 = padding, Y1 = y, X2 = canvasWidth - padding, Y2 = y,
                    Stroke = (SolidColorBrush)FindResource("BorderBrush"),
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                StatsChartCanvas.Children.Add(line);
            }

            // X-axis labels（点太多时按间隔抽稀，避免日期挤在一起看不清）
            if (firstEntry.Count > 0)
            {
                var step = firstEntry.Count > 1 ? chartWidth / (double)(firstEntry.Count - 1) : chartWidth / 2.0;
                int maxLabels = Math.Max(1, (int)(chartWidth / 45));
                int labelEvery = (int)Math.Ceiling(firstEntry.Count / (double)maxLabels);
                if (labelEvery < 1) labelEvery = 1;
                int lastIdx = firstEntry.Count - 1;
                for (int i = 0; i < firstEntry.Count; i++)
                {
                    bool show = i % labelEvery == 0 || i == lastIdx;
                    // 末尾常规刻度与最后一点太近时跳过，防止重叠
                    if (show && i != lastIdx && (lastIdx - i) < labelEvery / 2.0) show = false;
                    if (!show) continue;
                    var x = padding + i * step;
                    var label = new TextBlock
                    {
                        Text = firstEntry[i].date,
                        FontSize = 9,
                        Foreground = (Brush)FindResource("SecondaryTextBrush")
                    };
                    Canvas.SetLeft(label, x - 15);
                    Canvas.SetTop(label, canvasHeight - 18);
                    StatsChartCanvas.Children.Add(label);
                }
            }
        }

        private void BuildStatsSummary(Dictionary<int, List<(string date, TimeSpan dur)>> tagData, List<TimeTag> allTags, int days)
        {
            StatsSummaryPanel.Children.Clear();

            if (tagData.Count == 0)
            {
                StatsSummaryPanel.Children.Add(new TextBlock
                {
                    Text = "暂无数据",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            header.Children.Add(new TextBlock
            {
                Text = "标签",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            });
            var totalHdr = new TextBlock
            {
                Text = "总计",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(totalHdr, 1);
            header.Children.Add(totalHdr);
            var avgHdr = new TextBlock
            {
                Text = "日均",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(avgHdr, 2);
            header.Children.Add(avgHdr);
            StatsSummaryPanel.Children.Add(header);
            var sep = new Border
            {
                Height = 1,
                Background = (Brush)FindResource("BorderBrush"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            StatsSummaryPanel.Children.Add(sep);

            int colorIdx = 0;
            foreach (var kv in tagData)
            {
                var tag = kv.Key == OthersKey ? null : allTags.FirstOrDefault(t => t.Id == kv.Key);
                Color tagColor;
                if (kv.Key == OthersKey)
                {
                    tagColor = Color.FromRgb(142, 142, 147);
                }
                else
                {
                    try { tagColor = (Color)ColorConverter.ConvertFromString(tag?.Color ?? "#007AFF"); }
                    catch { tagColor = LineChartColors[colorIdx % LineChartColors.Length]; }
                }
                var tagName = kv.Key == OthersKey ? "其他" : (tag?.Name ?? "未标记");

                var totalMin = kv.Value.Sum(d => (int)d.dur.TotalMinutes);
                var avgMin = days > 0 ? totalMin / days : 0;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

                var dotRow = new StackPanel { Orientation = Orientation.Horizontal };
                dotRow.Children.Add(new Border
                {
                    Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(tagColor),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                dotRow.Children.Add(new TextBlock
                {
                    Text = tagName,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(dotRow);

                var totalStr = totalMin >= 60 ? $"{totalMin / 60}h{totalMin % 60:D2}m" : $"{totalMin}m";
                var totalTb = new TextBlock
                {
                    Text = totalStr,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(tagColor),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(totalTb, 1);
                row.Children.Add(totalTb);

                var avgStr = avgMin >= 60 ? $"{avgMin / 60}h{avgMin % 60:D2}m" : $"{avgMin}m";
                var avgTb = new TextBlock
                {
                    Text = avgStr,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(avgTb, 2);
                row.Children.Add(avgTb);

                StatsSummaryPanel.Children.Add(row);
                colorIdx++;
            }
        }

        private class DailyData
        {
            public string Date { get; set; }
            public int Completed { get; set; }
            public int Due { get; set; }
        }
    }
}
