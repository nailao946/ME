using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ME.Data;
using ME.Models;
using ME.Services;
using ME.Core;

namespace ME.Views
{
    public partial class TimeTrackView : UserControl
    {
        private readonly PomodoroService _pomo;
        private readonly TimeRecordRepository _recordRepo;
        private readonly TimeTagRepository _tagRepo;
        private List<TimeTag> _allTags = new();
        private int _selectedTagId = 0;
        private DispatcherTimer _clockTimer;
        private DateTime _currentMonth;
        private DateTime _selectedDate;
        private bool _eventsWired = false;
        private string _statsMode = "day";
        private double _ganttWidth = 400;
        private int _detailTagId = -1;
        private string _detailFilter = "day";
        private int _highlightRecordId = -1;
        private Border _highlightedRecordBorder = null;
        private ScrollViewer _detailRecordsScroll = null;
        private StackPanel _detailRecordsPanel = null;
        private bool _sortAsc = false;

        public TimeTrackView()
        {
            InitializeComponent();

            _pomo = SharedPomodoroService.Instance;
            _recordRepo = new TimeRecordRepository();
            _tagRepo = new TimeTagRepository();
            _currentMonth = DateTime.Now;
            _selectedDate = DateTime.Now;

            Loaded += (s, e) =>
            {
                if (!_eventsWired)
                {
                    _pomo.TimerUpdated += OnTimerUpdated;
                    _pomo.StateChanged += OnStateChanged;
                    _pomo.PhaseChanged += OnPhaseChanged;
                    _pomo.WorkPhaseEnded += OnWorkPhaseEnded;
                    ThemeService.ThemeChanged += OnThemeChanged;
                    _eventsWired = true;
                }
            };

            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);

            Unloaded += (s, e) =>
            {
                _pomo.TimerUpdated -= OnTimerUpdated;
                _pomo.StateChanged -= OnStateChanged;
                _pomo.PhaseChanged -= OnPhaseChanged;
                _pomo.WorkPhaseEnded -= OnWorkPhaseEnded;
                ThemeService.ThemeChanged -= OnThemeChanged;
                _eventsWired = false;
                _clockTimer?.Stop();
            };

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) =>
            {
                ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            };
            _clockTimer.Start();

            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            UpdateUI();
        }

        private void TimeTrackView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTags();
            LoadRecords();
            GenerateCalendar();
            LoadStats();
            DrawGanttChart();
            DrawPieCharts();
        }

        private void TimeTrackView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                _clockTimer?.Start();
                LoadTags();
                LoadRecords();
                GenerateCalendar();
                LoadStats();
                DrawGanttChart();
                DrawPieCharts();
            }
            else
            {
                _clockTimer?.Stop();
            }
        }

        // ========== UNIFIED TIMER ==========
        private void OnTimerUpdated(string time, UnifiedTimerMode mode)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TimerDisplay.Text = time;
                if (mode == UnifiedTimerMode.Pomodoro)
                    UpdateProgress();
            });
        }

        private void OnStateChanged(PomodoroState state)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateUI();
                if (state == PomodoroState.Idle && SharedTimerService.IsRunning)
                    SharedTimerService.StopCurrent();
            });
        }

        private void OnPhaseChanged(PomodoroPhase phase, int total, int cycle)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateUI();
                UpdateStatsText();
            });
        }

        private void OnWorkPhaseEnded()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PomodoroService.IsBreakConfirmShowing) return;
                PomodoroService.IsBreakConfirmShowing = true;
                try
                {
                    var win = Window.GetWindow(this);
                    if (win == null) return;
                    bool confirmed = ConfirmDialog.Show(win,
                        "番茄时间到！", "是否开始休息？",
                        "开始休息", "跳过");
                    if (confirmed)
                        _pomo.ConfirmBreak();
                    else
                        _pomo.SkipBreak();
                }
                finally
                {
                    PomodoroService.IsBreakConfirmShowing = false;
                }
            }));
        }

        private void OnGlobalEvent(string message)
        {
            if (message != "DayChanged") return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _selectedDate = DateTime.Now;
                _currentMonth = DateTime.Now;
                if (!IsVisible) return;
                LoadTags();
                LoadRecords();
                GenerateCalendar();
                LoadStats();
                DrawGanttChart();
                DrawPieCharts();
                UpdateUI();
            }));
        }

        private void UpdateUI()
        {
            var s = _pomo.State;
            bool isPomo = _pomo.Mode == UnifiedTimerMode.Pomodoro;
            bool active = s != PomodoroState.Idle;

            SimpleModeTab.Style = (Style)FindResource(isPomo ? "SecondaryButtonStyle" : "PrimaryButtonStyle");
            PomoModeTab.Style = (Style)FindResource(isPomo ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

            WorkTab.Visibility = isPomo ? Visibility.Visible : Visibility.Collapsed;
            ShortBreakTab.Visibility = isPomo ? Visibility.Visible : Visibility.Collapsed;
            LongBreakTab.Visibility = isPomo ? Visibility.Visible : Visibility.Collapsed;

            SettingsBtn.Visibility = isPomo ? Visibility.Visible : Visibility.Collapsed;
            ProgressBar.Visibility = isPomo ? Visibility.Visible : Visibility.Collapsed;
            StatsText.Visibility = isPomo ? Visibility.Visible : Visibility.Collapsed;

            if (isPomo)
            {
                WorkTab.Content = $"工作 {_pomo.WorkMinutes:D2}:00";
                ShortBreakTab.Content = $"短休 {_pomo.ShortBreakMinutes:D2}:00";
                LongBreakTab.Content = $"长休 {_pomo.LongBreakMinutes:D2}:00";

                var p = _pomo.Phase;
                WorkTab.Style = (Style)FindResource(p == PomodoroPhase.Work ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
                ShortBreakTab.Style = (Style)FindResource(p == PomodoroPhase.ShortBreak ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
                LongBreakTab.Style = (Style)FindResource(p == PomodoroPhase.LongBreak ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            }

            if (s == PomodoroState.Idle)
                TimerDisplay.Text = _pomo.FormatTime();

            TimerStatusText.Text = s switch
            {
                PomodoroState.Paused => "已暂停",
                PomodoroState.Running when isPomo => "计时中",
                _ => ""
            };

            PauseBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            StopBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            SkipBtn.Visibility = (isPomo && active) ? Visibility.Visible : Visibility.Collapsed;
            Add1Btn.Visibility = (isPomo && s == PomodoroState.Running) ? Visibility.Visible : Visibility.Collapsed;
            Add5Btn.Visibility = (isPomo && s == PomodoroState.Running) ? Visibility.Visible : Visibility.Collapsed;

            PauseBtn.Content = s switch
            {
                PomodoroState.Paused => "▶ 继续",
                _ => "⏸ 暂停"
            };
            PauseBtn.Style = (Style)FindResource(s == PomodoroState.Paused ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

            UpdateStatsText();
        }

        private void UpdateStatsText()
        {
            if (_pomo.Mode != UnifiedTimerMode.Pomodoro)
            {
                StatsText.Text = "";
                return;
            }

            string phaseText = _pomo.Phase switch
            {
                PomodoroPhase.Work => "工作中",
                PomodoroPhase.ShortBreak => "短休息",
                PomodoroPhase.LongBreak => "长休息",
                _ => ""
            };
            if (_pomo.State == PomodoroState.Paused)
                phaseText = "暂停中";

            var parts = new List<string>();
            if (_pomo.TodayCompletions > 0)
                parts.Add($"今日 {_pomo.TodayCompletions} 个");
            if (_pomo.CycleCount > 0)
                parts.Add($"本轮 {_pomo.CycleCount}/{_pomo.BeforeLongBreak} 个");
            if (_pomo.TotalCompletions > 0)
                parts.Add($"总计 {_pomo.TotalCompletions} 个");

            var stats = string.Join("  ", parts);
            StatsText.Text = string.IsNullOrEmpty(phaseText)
                ? stats
                : string.IsNullOrEmpty(stats) ? phaseText : $"{phaseText} · {stats}";
        }

        private void UpdateProgress()
        {
            var total = _pomo.PhaseDurationSeconds;
            if (total > 0)
            {
                var elapsed = total - _pomo.Current.TotalSeconds;
                ProgressBar.Value = Math.Max(0, Math.Min(100, elapsed / total * 100));
            }
        }

        // ========== MODE SWITCH ==========
        private void SimpleModeTab_Click(object sender, RoutedEventArgs e)
        {
            if (_pomo.State != PomodoroState.Idle)
            {
                bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                    "切换模式", "切换会放弃当前进度，确认？", "确认", "取消");
                if (!confirmed) return;
                SharedTimerService.StopCurrent();
                _pomo.Stop();
            }
            _pomo.Mode = UnifiedTimerMode.Simple;
            _pomo.Phase = PomodoroPhase.Work;
            _pomo.Current = TimeSpan.Zero;
            UpdateUI();
            TimerDisplay.Text = "00:00:00";
        }

        private void PomoModeTab_Click(object sender, RoutedEventArgs e)
        {
            if (_pomo.State != PomodoroState.Idle)
            {
                bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                    "切换模式", "切换会放弃当前进度，确认？", "确认", "取消");
                if (!confirmed) return;
                SharedTimerService.StopCurrent();
                _pomo.Stop();
            }
            _pomo.Mode = UnifiedTimerMode.Pomodoro;
            _pomo.Phase = PomodoroPhase.Work;
            _pomo.Current = TimeSpan.FromMinutes(_pomo.WorkMinutes);
            UpdateUI();
            TimerDisplay.Text = _pomo.FormatTime();
        }

        // ========== CONTROL BUTTONS (no start button) ==========
        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            switch (_pomo.State)
            {
                case PomodoroState.Running:
                    if (_pomo.Mode == UnifiedTimerMode.Simple)
                        SharedTimerService.PauseCurrent();
                    _pomo.Pause();
                    break;
                case PomodoroState.Paused:
                    if (_pomo.Mode == UnifiedTimerMode.Simple)
                        SharedTimerService.ResumeCurrent();
                    _pomo.Resume();
                    break;
            }
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                "确认停止", _pomo.Mode == UnifiedTimerMode.Pomodoro
                    ? "要放弃当前番茄/休息吗？" : "要停止计时吗？",
                "停止", "取消");
            if (!confirmed) return;

            if (_pomo.Mode == UnifiedTimerMode.Simple)
            {
                SharedTimerService.StopCurrent();
                _selectedTagId = 0;
                LoadStats();
                DrawGanttChart();
            }
            _pomo.Stop();
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
        {
            _pomo.Skip();
        }

        private void Add1Btn_Click(object sender, RoutedEventArgs e)
        {
            _pomo.AddTime(1);
        }

        private void Add5Btn_Click(object sender, RoutedEventArgs e)
        {
            _pomo.AddTime(5);
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PomodoroSettingsDialog(
                _pomo.WorkMinutes, _pomo.ShortBreakMinutes,
                _pomo.LongBreakMinutes, _pomo.BeforeLongBreak,
                _pomo.AutoStartBreaks, _pomo.AutoStartPomodoros)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
            {
                _pomo.WorkMinutes = dialog.WorkMinutes;
                _pomo.ShortBreakMinutes = dialog.ShortBreakMinutes;
                _pomo.LongBreakMinutes = dialog.LongBreakMinutes;
                _pomo.BeforeLongBreak = dialog.PomodorosBeforeLongBreak;
                _pomo.AutoStartBreaks = dialog.AutoStartBreaks;
                _pomo.AutoStartPomodoros = dialog.AutoStartPomodoros;
                UpdateUI();
            }
        }

        // ========== PHASE TABS (click starts pomodoro if idle) ==========
        private void WorkTab_Click(object sender, RoutedEventArgs e)
        {
            if (_pomo.State != PomodoroState.Idle)
            {
                bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                    "切换阶段", "切换会放弃当前进度，确认？", "确认", "取消");
                if (!confirmed) return;
                _pomo.Stop();
            }
            _pomo.SetPhase(PomodoroPhase.Work);
            _pomo.Current = TimeSpan.FromMinutes(_pomo.WorkMinutes);
            _pomo.Mode = UnifiedTimerMode.Pomodoro;
            TimerDisplay.Text = _pomo.FormatTime();
            _pomo.Start();
        }

        private void ShortBreakTab_Click(object sender, RoutedEventArgs e)
        {
            if (_pomo.State != PomodoroState.Idle)
            {
                bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                    "切换阶段", "切换会放弃当前进度，确认？", "确认", "取消");
                if (!confirmed) return;
                _pomo.Stop();
            }
            _pomo.SetPhase(PomodoroPhase.ShortBreak);
            _pomo.Current = TimeSpan.FromMinutes(_pomo.ShortBreakMinutes);
            _pomo.Mode = UnifiedTimerMode.Pomodoro;
            TimerDisplay.Text = _pomo.FormatTime();
            _pomo.Start();
        }

        private void LongBreakTab_Click(object sender, RoutedEventArgs e)
        {
            if (_pomo.State != PomodoroState.Idle)
            {
                bool confirmed = ConfirmDialog.Show(Window.GetWindow(this),
                    "切换阶段", "切换会放弃当前进度，确认？", "确认", "取消");
                if (!confirmed) return;
                _pomo.Stop();
            }
            _pomo.SetPhase(PomodoroPhase.LongBreak);
            _pomo.Current = TimeSpan.FromMinutes(_pomo.LongBreakMinutes);
            _pomo.Mode = UnifiedTimerMode.Pomodoro;
            TimerDisplay.Text = _pomo.FormatTime();
            _pomo.Start();
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(() =>
            {
                LoadTags();
                LoadRecords();
                LoadStats();
                DrawGanttChart();
                DrawPieCharts();
            });
        }

        // ========== TAGS (TOGGLE: click selected = stop) ==========
        private void LoadTags()
        {
            _allTags = _tagRepo.GetAllTags();

            var panel = new WrapPanel();

            int idx = 0;
            foreach (var tag in _allTags)
            {
                bool isSelected = tag.Id == _selectedTagId && _pomo.State != PomodoroState.Idle
                    && _pomo.Mode == UnifiedTimerMode.Simple;

                var border = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = Cursors.Hand,
                    Tag = tag,
                    Background = isSelected
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color))
                        : (Brush)FindResource("CardBrush"),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color)),
                    BorderThickness = new Thickness(isSelected ? 0 : 1.5),
                    Opacity = 0
                };

                var text = new TextBlock
                {
                    Text = tag.Name,
                    FontSize = 12,
                    Foreground = isSelected
                        ? Brushes.White
                        : (Brush)FindResource("TextBrush")
                };

                border.Child = text;
                border.MouseLeftButtonDown += TagItem_Click;
                border.MouseRightButtonDown += TagItem_RightClick;
                panel.Children.Add(border);

                var delayMs = idx * 50;
                var translateAnim = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    BeginTime = TimeSpan.FromMilliseconds(delayMs)
                };
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    BeginTime = TimeSpan.FromMilliseconds(delayMs)
                };
                var translate = new TranslateTransform(0, 8);
                border.RenderTransform = translate;
                translate.BeginAnimation(TranslateTransform.YProperty, translateAnim);
                border.BeginAnimation(Border.OpacityProperty, fadeAnim);
                idx++;
            }

            TagItemsControl.Items.Clear();
            TagItemsControl.Items.Add(panel);
        }

        private void TagItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is TimeTag tag)
            {
                if (_pomo.State != PomodoroState.Idle && _selectedTagId == tag.Id)
                {
                    SharedTimerService.StopCurrent();
                    _pomo.Stop();
                    _selectedTagId = 0;
                    InsertIdleRecords();
                }
                else
                {
                    if (SharedTimerService.IsRunning)
                    {
                        SharedTimerService.StopCurrent();
                        InsertIdleRecords();
                    }
                    _selectedTagId = tag.Id;
                    _pomo.SelectedTagId = tag.Id;
                    _pomo.SelectedTagName = tag.Name;
                    _pomo.SelectedTagColor = tag.Color;
                    SharedTimerService.StartWithTag(tag.Id);
                    _pomo.Restart();
                }
                LoadTags();
                LoadRecords();
                LoadStats();
                DrawGanttChart();
            }
        }

        private void TagItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is TimeTag tag)
            {
                var menu = new ContextMenu
                {
                    Background = (Brush)FindResource("CardBrush"),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1)
                };

                var editItem = new MenuItem
                {
                    Header = "编辑标签",
                    Foreground = (Brush)FindResource("TextBrush"),
                    Background = Brushes.Transparent
                };
                editItem.Click += (s, ev) => EditTag(tag);
                menu.Items.Add(editItem);

                if (!tag.IsDefault && !tag.IsPreset)
                {
                    var deleteItem = new MenuItem
                    {
                        Header = "删除",
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48)),
                        Background = Brushes.Transparent
                    };
                    deleteItem.Click += (s, ev) =>
                    {
                        if (ConfirmDialog.Show(Window.GetWindow(this), "确认删除", $"确认删除标签 \"{tag.Name}\"?", "删除", "取消"))
                        {
                            _tagRepo.DeleteTag(tag.Id);
                            if (_selectedTagId == tag.Id)
                            {
                                _selectedTagId = _allTags.FirstOrDefault(t => t.Id != tag.Id)?.Id ?? 0;
                            }
                            LoadTags();
                        }
                    };
                    menu.Items.Add(deleteItem);
                }

                menu.PlacementTarget = border;
                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TagEditorDialog();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _tagRepo.InsertTag(dialog.Result);
                LoadTags();
            }
        }

        private void EditTag(TimeTag tag)
        {
            var dialog = new TagEditorDialog(tag);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _tagRepo.UpdateTag(dialog.Result);
                LoadTags();
                LoadRecords();
            }
        }

        // ========== IDLE RECORD AUTO-FILL ==========
        private void InsertIdleRecords()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var records = _recordRepo.GetRecordsByDate(today);
            if (records.Count < 2) return;

            var idleTag = _allTags.FirstOrDefault(t => t.IsDefault);
            if (idleTag == null) return;

            var sorted = records.OrderBy(r => r.StartTime).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var current = sorted[i];
                var next = sorted[i + 1];
                if (current.EndTime.HasValue && current.EndTime.Value < next.StartTime)
                {
                    var gap = next.StartTime - current.EndTime.Value;
                    if (gap.TotalMinutes >= 1)
                    {
                        var idleRecord = new TimeRecord
                        {
                            TagId = idleTag.Id,
                            StartTime = current.EndTime.Value,
                            EndTime = next.StartTime,
                            Date = today
                        };
                        _recordRepo.InsertRecord(idleRecord);
                    }
                }
            }
        }

        // ========== STATS MODE ==========
        private void StatsDay_Click(object sender, RoutedEventArgs e) { _statsMode = "day"; UpdateStatsButtons(); LoadStats(); }
        private void StatsWeek_Click(object sender, RoutedEventArgs e) { _statsMode = "week"; UpdateStatsButtons(); LoadStats(); }
        private void StatsMonth_Click(object sender, RoutedEventArgs e) { _statsMode = "month"; UpdateStatsButtons(); LoadStats(); }
        private void StatsYear_Click(object sender, RoutedEventArgs e) { _statsMode = "year"; UpdateStatsButtons(); LoadStats(); }

        private void UpdateStatsButtons()
        {
            StatsDayBtn.Style = (Style)FindResource(_statsMode == "day" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            StatsWeekBtn.Style = (Style)FindResource(_statsMode == "week" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            StatsMonthBtn.Style = (Style)FindResource(_statsMode == "month" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            StatsYearBtn.Style = (Style)FindResource(_statsMode == "year" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        }

        // ========== STATS ==========
        private void LoadStats()
        {
            TodayStatsPanel.Children.Clear();
            var selectedDate = _selectedDate;
            List<TimeRecord> records;

            if (_statsMode == "day")
            {
                records = _recordRepo.GetRecordsByDate(selectedDate.ToString("yyyy-MM-dd"));
            }
            else if (_statsMode == "week")
            {
                var startOfWeek = TaskService.GetWeekStartForDate(selectedDate);
                records = _recordRepo.GetRecordsByDateRange(startOfWeek.ToString("yyyy-MM-dd"), selectedDate.ToString("yyyy-MM-dd"));
            }
            else if (_statsMode == "month")
            {
                var startOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
                records = _recordRepo.GetRecordsByDateRange(startOfMonth.ToString("yyyy-MM-dd"), selectedDate.ToString("yyyy-MM-dd"));
            }
            else
            {
                var startOfYear = new DateTime(selectedDate.Year, 1, 1);
                records = _recordRepo.GetRecordsByDateRange(startOfYear.ToString("yyyy-MM-dd"), selectedDate.ToString("yyyy-MM-dd"));
            }

            var tagTimes = new Dictionary<int, TimeSpan>();
            foreach (var r in records)
            {
                var dur = (r.EndTime ?? DateTime.Now) - r.StartTime;
                if (!tagTimes.ContainsKey(r.TagId))
                    tagTimes[r.TagId] = TimeSpan.Zero;
                tagTimes[r.TagId] += dur;
            }

            var includedTagIds = TimeStatsHelper.GetIncludedTagIds();
            var filteredTagTimes = new Dictionary<int, TimeSpan>();
            foreach (var kvp in tagTimes)
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == kvp.Key);
                if (tag == null || tag.IsDefault) continue;
                if (includedTagIds.Count > 0 && !includedTagIds.Contains(kvp.Key)) continue;
                filteredTagTimes[kvp.Key] = kvp.Value;
            }
            tagTimes = filteredTagTimes;

            var totalTime = tagTimes.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
            var totalText = new TextBlock
            {
                Text = $"总计 {FormatDuration(totalTime)}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 12, 0)
            };
            TodayStatsPanel.Children.Add(totalText);

            foreach (var kvp in tagTimes.OrderByDescending(k => k.Value))
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == kvp.Key);
                if (tag == null) continue;

                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 10, 4),
                    Cursor = Cursors.Hand,
                    Tag = tag
                };
                panel.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color)),
                    Margin = new Thickness(6, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                panel.Children.Add(new TextBlock
                {
                    Text = $"{tag.Name} {FormatDuration(kvp.Value)}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                panel.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is StackPanel sp && sp.Tag is TimeTag clickedTag)
                    {
                        ShowPieDetail(clickedTag, kvp.Value,
                            totalTime.TotalSeconds > 0 ? kvp.Value.TotalSeconds / totalTime.TotalSeconds * 100 : 0,
                            clickedTag.Color);
                    }
                };
                TodayStatsPanel.Children.Add(panel);
            }

            AnimateTodayStats();
        }

        private void AnimateTodayStats()
        {
            for (int i = 0; i < TodayStatsPanel.Children.Count; i++)
            {
                var child = TodayStatsPanel.Children[i] as FrameworkElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 40);
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

        private string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h{ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m";
            return $"{(int)ts.TotalSeconds}s";
        }

        private string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // ========== GANTT CHART ==========
        private void GanttBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _ganttWidth = e.NewSize.Width - 16;
            GanttCanvas.Width = _ganttWidth > 50 ? _ganttWidth : 400;
            if (IsVisible) DrawGanttChart();
        }

        private void DrawGanttChart()
        {
            GanttCanvas.Children.Clear();
            GanttDateLabel.Text = _selectedDate.ToString("MM-dd");

            var records = _recordRepo.GetRecordsByDate(_selectedDate.ToString("yyyy-MM-dd"));
            if (records.Count == 0)
            {
                var noData = new TextBlock
                {
                    Text = "暂无记录",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                };
                Canvas.SetLeft(noData, 10);
                Canvas.SetTop(noData, 80);
                GanttCanvas.Children.Add(noData);
                return;
            }

            double canvasWidth = _ganttWidth > 50 ? _ganttWidth : 400;
            double rowHeight = 24;
            double headerHeight = 22;
            double leftMargin = 55;

            var tagGroups = records.GroupBy(r => r.TagId).ToList();
            double y = headerHeight;
            double neededHeight = headerHeight + tagGroups.Count * rowHeight + 10;

            for (int h = 0; h < 24; h += 3)
            {
                double x = leftMargin + (h / 24.0) * (canvasWidth - leftMargin);
                var line = new Line
                {
                    X1 = x, Y1 = headerHeight, X2 = x, Y2 = neededHeight,
                    Stroke = (Brush)FindResource("BorderBrush"),
                    StrokeThickness = 0.5
                };
                GanttCanvas.Children.Add(line);

                var label = new TextBlock
                {
                    Text = $"{h:D2}:00",
                    FontSize = 9,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                };
                Canvas.SetLeft(label, x - 15);
                Canvas.SetTop(label, 0);
                GanttCanvas.Children.Add(label);
            }

            foreach (var group in tagGroups)
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == group.Key);
                var color = tag?.Color ?? "#808080";
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

                var tagLabel = new TextBlock
                {
                    Text = tag?.Name ?? "?",
                    FontSize = 9,
                    Foreground = (Brush)FindResource("TextBrush")
                };
                Canvas.SetLeft(tagLabel, 2);
                Canvas.SetTop(tagLabel, y);
                GanttCanvas.Children.Add(tagLabel);

                foreach (var record in group)
                {
                    double startHour = record.StartTime.TimeOfDay.TotalHours;
                    double endHour = record.EndTime?.TimeOfDay.TotalHours ?? DateTime.Now.TimeOfDay.TotalHours;
                    if (endHour < startHour) endHour = 24;

                    double x1 = leftMargin + (startHour / 24.0) * (canvasWidth - leftMargin);
                    double x2 = leftMargin + (endHour / 24.0) * (canvasWidth - leftMargin);
                    double barWidth = Math.Max(2, x2 - x1);

                    var dur = record.EndTime.HasValue ? record.EndTime.Value - record.StartTime : DateTime.Now - record.StartTime;
                    var totalDayRecords = records.Where(r => r.TagId == group.Key).ToList();
                    var totalDayTime = totalDayRecords.Aggregate(TimeSpan.Zero, (a, r) => a + (r.EndTime.HasValue ? r.EndTime.Value - r.StartTime : DateTime.Now - r.StartTime));
                    var allRecordsTime = records.Aggregate(TimeSpan.Zero, (a, r) => a + (r.EndTime.HasValue ? r.EndTime.Value - r.StartTime : DateTime.Now - r.StartTime));
                    var pctOfTag = allRecordsTime.TotalSeconds > 0 ? (dur.TotalSeconds / allRecordsTime.TotalSeconds * 100) : 0;

                    var bar = new Border
                    {
                        Width = barWidth,
                        Height = 16,
                        CornerRadius = new CornerRadius(3),
                        Background = brush,
                        Opacity = record.EndTime.HasValue ? 0.8 : 1.0,
                        Cursor = Cursors.Hand,
                        ToolTip = $"{tag?.Name}\n{record.StartTime:HH:mm} - {record.EndTime?.ToString("HH:mm") ?? "进行中"}\n时长: {FormatDuration(dur)}\n占比: {pctOfTag:F1}%{(string.IsNullOrEmpty(record.Note) ? "" : $"\n备注: {record.Note}")}"
                    };
                    bar.Tag = new GanttBarInfo { Tag = tag, Record = record, Dur = dur, Pct = pctOfTag, Color = color };
                    bar.MouseLeftButtonDown += GanttBar_Click;
                    Canvas.SetLeft(bar, x1);
                    Canvas.SetTop(bar, y + 2);
                    GanttCanvas.Children.Add(bar);
                }

                y += rowHeight;
            }

            GanttCanvas.Height = Math.Max(120, neededHeight);
        }

        // ========== RECORDS ==========
        private void LoadRecords()
        {
            var records = _recordRepo.GetRecordsByDate(_selectedDate.ToString("yyyy-MM-dd"));
            if (_sortAsc)
                records = records.OrderBy(r => r.StartTime).ToList();
            else
                records = records.OrderByDescending(r => r.StartTime).ToList();
            RecordsPanel.Children.Clear();

            if (records.Count == 0)
            {
                var noRecords = new TextBlock
                {
                    Text = "暂无记录",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(4)
                };
                RecordsPanel.Children.Add(noRecords);
                return;
            }

            foreach (var record in records)
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == record.TagId);
                var color = tag?.Color ?? "#808080";
                var tagName = tag?.Name ?? "未知";

                var recordBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = (Brush)FindResource("CardBrush"),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    BorderThickness = new Thickness(3, 0, 0, 0)
                };

                var panel = new StackPanel();

                var header = new TextBlock
                {
                    Text = $"{tagName}  {record.StartTime:HH:mm} - {(record.EndTime?.ToString("HH:mm") ?? "进行中")}",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush")
                };
                panel.Children.Add(header);

                if (record.EndTime.HasValue)
                {
                    var dur = record.EndTime.Value - record.StartTime;
                    var durText = new TextBlock
                    {
                        Text = $"时长: {FormatDuration(dur)}",
                        FontSize = 11,
                        Foreground = (Brush)FindResource("SecondaryTextBrush"),
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    panel.Children.Add(durText);
                }

                if (!string.IsNullOrEmpty(record.Note))
                {
                    var noteText = new TextBlock
                    {
                        Text = $"📝 {record.Note}",
                        FontSize = 11,
                        Foreground = (Brush)FindResource("SecondaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 0, 0)
                    };
                    panel.Children.Add(noteText);
                }

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                var editBtn = new Button
                {
                    Content = "编辑",
                    FontSize = 11,
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = record,
                    Style = (Style)FindResource("SecondaryButtonStyle")
                };
                editBtn.Click += (s, ev) => EditRecord(record);
                buttonsPanel.Children.Add(editBtn);

                var noteBtn = new Button
                {
                    Content = "备注",
                    FontSize = 11,
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = record,
                    Style = (Style)FindResource("SecondaryButtonStyle")
                };
                noteBtn.Click += (s, ev) => EditRecordNote(record);
                buttonsPanel.Children.Add(noteBtn);

                var deleteBtn = new Button
                {
                    Content = "删除",
                    FontSize = 11,
                    Padding = new Thickness(8, 3, 8, 3),
                    Tag = record,
                    Style = (Style)FindResource("SecondaryButtonStyle")
                };
                deleteBtn.Click += (s, ev) =>
                {
                    if (ConfirmDialog.Show(Window.GetWindow(this), "确认删除", "确认删除此记录?", "删除", "取消"))
                    {
                        _recordRepo.DeleteRecord(record.Id);
                        LoadRecords();
                        LoadStats();
                        DrawGanttChart();
                    }
                };
                buttonsPanel.Children.Add(deleteBtn);

                panel.Children.Add(buttonsPanel);
                recordBorder.Child = panel;
                RecordsPanel.Children.Add(recordBorder);
            }

            AnimateRecordCards();
        }

        private void AnimateRecordCards()
        {
            for (int i = 0; i < RecordsPanel.Children.Count; i++)
            {
                var child = RecordsPanel.Children[i] as FrameworkElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 40);
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var slide = new TranslateTransform(0, 12);
                child.RenderTransform = slide;
                var slideAnim = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                slide.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            }
        }

        private void EditRecord(TimeRecord record)
        {
            var dialog = new RecordEditDialog(record, _allTags);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                record.StartTime = dialog.ResultStartTime;
                record.EndTime = dialog.ResultEndTime;
                record.TagId = dialog.ResultTagId;
                record.Note = dialog.ResultNote;
                _recordRepo.UpdateRecord(record);
                LoadRecords();
                LoadStats();
                DrawGanttChart();
            }
        }

        private void EditRecordNote(TimeRecord record)
        {
            var tag = _allTags.FirstOrDefault(t => t.Id == record.TagId);
            var timeRange = $"{record.StartTime:HH:mm} - {(record.EndTime?.ToString("HH:mm") ?? "进行中")}";
            var dialog = new NoteInputDialog(tag?.Name ?? "未知", timeRange, record.Note)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
            {
                record.Note = dialog.ResultNote;
                _recordRepo.UpdateRecord(record);
                LoadRecords();
                LoadStats();
                DrawGanttChart();
            }
        }

        private void ClearRecords_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmDialog.Show(Window.GetWindow(this), "确认清空", $"确认清空 {_selectedDate:yyyy-MM-dd} 的所有记录?", "清空", "取消"))
            {
                _recordRepo.ClearRecordsByDate(_selectedDate.ToString("yyyy-MM-dd"));
                LoadRecords();
                LoadStats();
                DrawGanttChart();
            }
        }

        private void SortToggle_Click(object sender, RoutedEventArgs e)
        {
            _sortAsc = !_sortAsc;
            SortToggleBtn.Content = _sortAsc ? "↑正序" : "↓倒序";
            SortToggleBtn.Style = _sortAsc
                ? (Style)FindResource("PrimaryButtonStyle")
                : (Style)FindResource("SecondaryButtonStyle");
            LoadRecords();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV文件 (*.csv)|*.csv",
                FileName = $"time_records_{DateTime.Now:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var records = _recordRepo.GetAllRecords();
                    var lines = new List<string> { "日期,标签,开始时间,结束时间,时长(分钟),备注" };
                    foreach (var r in records)
                    {
                        var tag = _allTags.FirstOrDefault(t => t.Id == r.TagId);
                        var dur = r.EndTime.HasValue ? (r.EndTime.Value - r.StartTime).TotalMinutes : 0;
                        lines.Add($"{r.Date},{tag?.Name ?? "未知"},{r.StartTime:HH:mm},{r.EndTime?.ToString("HH:mm") ?? ""},{dur:F0},{EscapeCsv(r.Note)}");
                    }
                    System.IO.File.WriteAllText(dialog.FileName, string.Join("\n", lines), System.Text.Encoding.UTF8);
                    ConfirmDialog.Show(Window.GetWindow(this), "提示", "导出成功！", "确定");
                }
                catch (Exception ex)
                {
                    ConfirmDialog.Show(Window.GetWindow(this), "错误", $"导出失败: {ex.Message}", "确定");
                }
            }
        }

        // ========== CALENDAR ==========
        private void GenerateCalendar()
        {
            CalendarGrid.Children.Clear();
            var year = _currentMonth.Year;
            var month = _currentMonth.Month;
            MonthLabel.Text = $"{year}年{month}月";

            var firstDay = new DateTime(year, month, 1);
            int startOffset = (int)firstDay.DayOfWeek;
            int daysInMonth = DateTime.DaysInMonth(year, month);
            var today = DateTime.Today;

            var recordsThisMonth = _recordRepo.GetRecordsByDateRange(
                $"{year}-{month:D2}-01",
                $"{year}-{month:D2}-{daysInMonth:D2}"
            );
            var datesWithRecords = recordsThisMonth.Select(r => r.Date).ToHashSet();

            for (int i = 0; i < startOffset; i++)
            {
                CalendarGrid.Children.Add(new Border());
            }

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var dateStr = date.ToString("yyyy-MM-dd");
                bool isToday = date == today;
                bool isSelected = date.Date == _selectedDate.Date;
                bool hasRecord = datesWithRecords.Contains(dateStr);

                var cell = new Grid { Margin = new Thickness(1) };

                var dayBtn = new Button
                {
                    Content = day.ToString(),
                    FontSize = 12,
                    Tag = date,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(0),
                    MinHeight = 30
                };

                if (isSelected)
                {
                    dayBtn.Background = (Brush)FindResource("PrimaryBrush");
                    dayBtn.Foreground = Brushes.White;
                    dayBtn.FontWeight = FontWeights.Bold;
                    var template = new ControlTemplate(typeof(Button));
                    var borderFactory = new FrameworkElementFactory(typeof(Border));
                    borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
                    var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                    contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                    borderFactory.AppendChild(contentPresenter);
                    template.VisualTree = borderFactory;
                    dayBtn.Template = template;
                }
                else
                {
                    dayBtn.Background = Brushes.Transparent;
                    dayBtn.Foreground = (Brush)FindResource("TextBrush");
                }

                dayBtn.Click += (s, ev) =>
                {
                    _selectedDate = (DateTime)((Button)s).Tag;
                    GenerateCalendar();
                    LoadRecords();
                    DrawGanttChart();
                    LoadStats();
                    DrawPieCharts();
                };

                cell.Children.Add(dayBtn);

                if (isToday && !isSelected)
                {
                    var dot = new Border
                    {
                        Width = 5, Height = 5,
                        CornerRadius = new CornerRadius(2.5),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    cell.Children.Add(dot);
                }

                if (hasRecord && !isSelected)
                {
                    var indicator = new Border
                    {
                        Width = 4, Height = 4,
                        CornerRadius = new CornerRadius(2),
                        Background = (Brush)FindResource("PrimaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 2, 2, 0)
                    };
                    cell.Children.Add(indicator);
                }

                CalendarGrid.Children.Add(cell);
            }

            AnimateCalendarCells();
        }

        private void AnimateCalendarCells()
        {
            for (int i = 0; i < CalendarGrid.Children.Count; i++)
            {
                var child = CalendarGrid.Children[i] as UIElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 15);
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var scale = new ScaleTransform(0.9, 0.9);
                child.RenderTransform = scale;
                child.RenderTransformOrigin = new Point(0.5, 0.5);
                var scaleX = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var scaleY = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }

        // ========== PIE CHARTS ==========
        private void DrawPieCharts()
        {
            DrawPieChart(WeekPieCanvas, GetTagTimesForPeriod(-7));
            DrawPieChart(MonthPieCanvas, GetTagTimesForPeriod(-30));
        }

        private Dictionary<int, TimeSpan> GetTagTimesForPeriod(int days)
        {
            var start = _selectedDate.Date.AddDays(days);
            var records = _recordRepo.GetRecordsByDateRange(start.ToString("yyyy-MM-dd"), _selectedDate.ToString("yyyy-MM-dd"));
            var tagTimes = new Dictionary<int, TimeSpan>();
            foreach (var r in records)
            {
                var dur = (r.EndTime ?? DateTime.Now) - r.StartTime;
                if (!tagTimes.ContainsKey(r.TagId)) tagTimes[r.TagId] = TimeSpan.Zero;
                tagTimes[r.TagId] += dur;
            }

            var includedTagIds = TimeStatsHelper.GetIncludedTagIds();
            var filtered = new Dictionary<int, TimeSpan>();
            foreach (var kvp in tagTimes)
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == kvp.Key);
                if (tag == null || tag.IsDefault) continue;
                if (includedTagIds.Count > 0 && !includedTagIds.Contains(kvp.Key)) continue;
                filtered[kvp.Key] = kvp.Value;
            }
            return filtered;
        }

        private void DrawPieChart(Canvas canvas, Dictionary<int, TimeSpan> tagTimes)
        {
            canvas.Children.Clear();
            var total = tagTimes.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
            if (total.TotalSeconds <= 0)
            {
                var noData = new TextBlock { Text = "暂无", FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush") };
                Canvas.SetLeft(noData, 45); Canvas.SetTop(noData, 55);
                canvas.Children.Add(noData);
                return;
            }

            double cx = 60, cy = 60, r = 50;
            double startAngle = 0;

            var slices = new List<(System.Windows.Shapes.Path path, double targetAngle, double sweepAngle)>();
            foreach (var kv in tagTimes.OrderByDescending(k => k.Value))
            {
                var tag = _allTags.FirstOrDefault(t => t.Id == kv.Key);
                var color = tag?.Color ?? "#808080";
                var sweepAngle = (kv.Value.TotalSeconds / total.TotalSeconds) * 360;
                var pct = (kv.Value.TotalSeconds / total.TotalSeconds * 100);

                var path = CreatePieSlice(cx, cy, r, startAngle, sweepAngle, color);
                path.ToolTip = $"{tag?.Name ?? "未知"}\n时长: {FormatDuration(kv.Value)}\n占比: {pct:F1}%";
                path.Cursor = Cursors.Hand;
                path.Tag = new PieSliceInfo { Tag = tag, Duration = kv.Value, Pct = pct, Color = color };
                path.MouseLeftButtonDown += PieSlice_Click;

                path.Opacity = 0;

                canvas.Children.Add(path);
                slices.Add((path, startAngle, sweepAngle));
                startAngle += sweepAngle;
            }

            for (int i = 0; i < slices.Count; i++)
            {
                var (path, _, _) = slices[i];
                var delay = TimeSpan.FromMilliseconds(i * 80);

                var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                path.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

                var scale = new ScaleTransform(0.8, 0.8, cx, cy);
                path.RenderTransform = scale;
                var scaleXAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
                {
                    BeginTime = delay,
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                var scaleYAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
                {
                    BeginTime = delay,
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            }
        }

        private System.Windows.Shapes.Path CreatePieSlice(double cx, double cy, double r, double startAngle, double sweepAngle, string color)
        {
            var startRad = startAngle * Math.PI / 180;
            var endRad = (startAngle + sweepAngle) * Math.PI / 180;

            var startPoint = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            var endPoint = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));
            var isLargeArc = sweepAngle > 180;

            var figure = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
            figure.Segments.Add(new LineSegment(startPoint, true));
            figure.Segments.Add(new ArcSegment(endPoint, new Size(r, r), 0, isLargeArc, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Stroke = (Brush)FindResource("CardBrush"),
                StrokeThickness = 2
            };
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            GenerateCalendar();
            LoadStats();
            DrawPieCharts();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(1);
            GenerateCalendar();
            LoadStats();
            DrawPieCharts();
        }

        // ========== GANTT BAR CLICK ==========
        private void GanttDetail_Close(object sender, RoutedEventArgs e)
        {
            GanttDetailPopup.IsOpen = false;
        }

        private void DetailTitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.OriginalSource is System.Windows.Controls.Border)
                GanttDetailPopup.IsOpen = false;
        }

        private void GanttBar_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border bar && bar.Tag is GanttBarInfo info)
            {
                ShowGanttDetail(info.Tag, info.Record, info.Dur, info.Pct, info.Color);
            }
        }

        private void ShowGanttDetail(TimeTag tag, TimeRecord record, TimeSpan dur, double pctOfTag, string color)
        {
            _detailTagId = tag?.Id ?? 0;
            _highlightRecordId = record?.Id ?? -1;
            _detailFilter = "day";
            ShowDetailPanel(tag, color, dur, pctOfTag, record);
        }

        private void ShowPieDetail(TimeTag tag, TimeSpan duration, double pct, string color)
        {
            _detailTagId = tag?.Id ?? 0;
            _highlightRecordId = -1;
            _detailFilter = "week";
            ShowDetailPanel(tag, color, duration, pct, null);
        }

        private void ShowDetailPanel(TimeTag tag, string color, TimeSpan currentDur, double currentPct, TimeRecord currentRecord)
        {
            GanttDetailContent.Children.Clear();

            DetailTitlePanel.Children.Clear();
            DetailTitlePanel.Children.Add(new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });
            DetailTitlePanel.Children.Add(new TextBlock
            {
                Text = tag?.Name ?? "未知", FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var tagTimeDay = GetTagTotalTime(tag?.Id ?? 0, -1);
            var tagTimeWeek = GetTagTotalTime(tag?.Id ?? 0, -7);
            var tagTimeMonth = GetTagTotalTime(tag?.Id ?? 0, -30);
            var tagTimeYear = GetTagTotalTime(tag?.Id ?? 0, -365);

            var totalsGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            totalsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var dayLabel = _selectedDate.Date == DateTime.Today ? "今日" : _selectedDate.ToString("M/d");
            AddTotalCell(totalsGrid, 0, 0, dayLabel, FormatDuration(tagTimeDay));
            AddTotalCell(totalsGrid, 0, 1, "本周", FormatDuration(tagTimeWeek));
            AddTotalCell(totalsGrid, 0, 2, "本月", FormatDuration(tagTimeMonth));
            AddTotalCell(totalsGrid, 0, 3, "本年", FormatDuration(tagTimeYear));
            GanttDetailContent.Children.Add(totalsGrid);

            if (currentRecord != null)
            {
                var segGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                segGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                segGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AddDetailRow(segGrid, 0, "时间范围", $"{currentRecord.StartTime:HH:mm} - {currentRecord.EndTime?.ToString("HH:mm") ?? "进行中"}");
                AddDetailRow(segGrid, 1, "时长", FormatDuration(currentDur));
                AddDetailRow(segGrid, 2, "今日占比", $"{currentPct:F1}%");
                AddDetailRow(segGrid, 3, "日期", currentRecord.StartTime.ToString("yyyy-MM-dd"));
                GanttDetailContent.Children.Add(segGrid);
            }

            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
            var filterLabel = new TextBlock
            {
                Text = "记录列表", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            filterPanel.Children.Add(filterLabel);
            foreach (var f in new[] { ("day", "日"), ("week", "周"), ("month", "月"), ("all", "全部") })
            {
                var btn = new Button
                {
                    Content = f.Item2, FontSize = 10, Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 4, 0), Tag = f.Item1,
                    Style = _detailFilter == f.Item1 ? (Style)FindResource("PrimaryButtonStyle") : (Style)FindResource("SecondaryButtonStyle")
                };
                btn.Click += DetailFilter_Click;
                filterPanel.Children.Add(btn);
            }
            GanttDetailContent.Children.Add(filterPanel);

            _detailRecordsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 200 };
            _detailRecordsPanel = new StackPanel();
            _detailRecordsScroll.Content = _detailRecordsPanel;
            GanttDetailContent.Children.Add(_detailRecordsScroll);

            BuildDetailRecordsList(tag);

            GanttDetailPopup.IsOpen = true;
        }

        private void DetailFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filter)
            {
                _detailFilter = filter;
                var tag = _allTags.FirstOrDefault(t => t.Id == _detailTagId);
                if (tag != null)
                {
                    var color = tag.Color ?? "#808080";
                    ShowDetailPanel(tag, color, TimeSpan.Zero, 0, null);
                }
            }
        }

        private void BuildDetailRecordsList(TimeTag tag)
        {
            _detailRecordsPanel.Children.Clear();

            DateTime startDate;
            string periodLabel;
            switch (_detailFilter)
            {
                case "week":
                    startDate = _selectedDate.Date.AddDays(-7);
                    periodLabel = "近一周";
                    break;
                case "month":
                    startDate = _selectedDate.Date.AddDays(-30);
                    periodLabel = "近一月";
                    break;
                case "all":
                    startDate = DateTime.MinValue;
                    periodLabel = "全部";
                    break;
                default:
                    startDate = _selectedDate.Date;
                    periodLabel = _selectedDate.Date == DateTime.Today ? "今日" : _selectedDate.ToString("M月d日");
                    break;
            }

            List<TimeRecord> records;
            if (_detailFilter == "all")
            {
                records = _recordRepo.GetAllRecords()
                    .Where(r => r.TagId == _detailTagId)
                    .OrderByDescending(r => r.StartTime)
                    .ToList();
            }
            else
            {
                records = _recordRepo.GetRecordsByDateRange(startDate.ToString("yyyy-MM-dd"), _selectedDate.Date.ToString("yyyy-MM-dd"))
                    .Where(r => r.TagId == _detailTagId)
                    .OrderByDescending(r => r.StartTime)
                    .ToList();
            }

            if (records.Count == 0)
            {
                _detailRecordsPanel.Children.Add(new TextBlock
                {
                    Text = $"{periodLabel}暂无记录", FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            foreach (var rec in records)
            {
                var recDur = rec.EndTime.HasValue ? rec.EndTime.Value - rec.StartTime : DateTime.Now - rec.StartTime;
                var isHighlighted = rec.Id == _highlightRecordId;

                var recBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = isHighlighted
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag?.Color ?? "#808080")) { Opacity = 0.2 }
                        : (Brush)FindResource("CardBrush"),
                    BorderBrush = isHighlighted
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag?.Color ?? "#808080"))
                        : Brushes.Transparent,
                    BorderThickness = isHighlighted ? new Thickness(2) : new Thickness(0),
                    Tag = rec.Id
                };

                var recGrid = new Grid();
                recGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                recGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                recGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dateText = new TextBlock
                {
                    Text = rec.StartTime.ToString("yyyy-MM-dd"),
                    FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(dateText, 0);

                var timeText = new TextBlock
                {
                    Text = $"{rec.StartTime:HH:mm} - {rec.EndTime?.ToString("HH:mm") ?? "进行中"}",
                    FontSize = 11, Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(timeText, 1);

                var durText = new TextBlock
                {
                    Text = FormatDuration(recDur),
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(durText, 2);

                recGrid.Children.Add(dateText);
                recGrid.Children.Add(timeText);
                recGrid.Children.Add(durText);

                var recContent = new StackPanel();
                recContent.Children.Add(recGrid);
                if (!string.IsNullOrEmpty(rec.Note))
                {
                    recContent.Children.Add(new TextBlock
                    {
                        Text = $"📝 {rec.Note}",
                        FontSize = 10,
                        Foreground = (Brush)FindResource("SecondaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 0, 0)
                    });
                }
                recBorder.Child = recContent;

                if (isHighlighted)
                {
                    _highlightedRecordBorder = recBorder;
                    recBorder.Loaded += (s, ev) =>
                    {
                        recBorder.BringIntoView();
                    };
                }

                _detailRecordsPanel.Children.Add(recBorder);
            }
        }

        private TimeSpan GetTagTotalTime(int tagId, int days)
        {
            var start = _selectedDate.Date.AddDays(days);
            var end = _selectedDate.Date;
            var records = _recordRepo.GetRecordsByDateRange(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));
            TimeSpan total = TimeSpan.Zero;
            foreach (var r in records.Where(r => r.TagId == tagId))
                total += (r.EndTime ?? DateTime.Now) - r.StartTime;
            return total;
        }

        private void AddTotalCell(Grid grid, int row, int col, string label, string value)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4) };
            panel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = value, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, col);
            grid.Children.Add(panel);
        }

        private void AddDetailRow(Grid grid, int row, string label, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelText = new TextBlock
            {
                Text = label, FontSize = 11,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(0, 2, 12, 2)
            };
            var valueText = new TextBlock
            {
                Text = value, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(labelText, row); Grid.SetColumn(labelText, 0);
            Grid.SetRow(valueText, row); Grid.SetColumn(valueText, 1);
            grid.Children.Add(labelText);
            grid.Children.Add(valueText);
        }

        private class GanttBarInfo
        {
            public TimeTag Tag { get; set; }
            public TimeRecord Record { get; set; }
            public TimeSpan Dur { get; set; }
            public double Pct { get; set; }
            public string Color { get; set; }
        }

        // ========== PIE SLICE CLICK ==========
        private void PieSlice_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Path path && path.Tag is PieSliceInfo info)
            {
                ShowPieDetail(info.Tag, info.Duration, info.Pct, info.Color);
            }
        }

        private class PieSliceInfo
        {
            public TimeTag Tag { get; set; }
            public TimeSpan Duration { get; set; }
            public double Pct { get; set; }
            public string Color { get; set; }
        }
    }
}
