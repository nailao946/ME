using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    public partial class TaskEditDialog : Window
    {
        public TaskItem ResultTask { get; private set; }
        public bool IsEditMode { get; private set; }
        public bool IsSubtaskMode { get; internal set; }
        public ObservableCollection<TaskItem> Subtasks { get; } = new ObservableCollection<TaskItem>();
        private int _editTaskId;
        private int? _editParentTaskId;
        private int? _editGoalId;
        private string _selfUid;
        private List<string> _blockedByUids = new List<string>();
        private Dictionary<string, string> _blockedByName = new Dictionary<string, string>();
        private StackPanel _blockedBySection;
        private ComboBox _prereqCombo;
        private WrapPanel _blockedByChips;

        public new string Title
        {
            get => DialogTitle?.Text ?? "";
            set { if (DialogTitle != null) DialogTitle.Text = value; }
        }

        public TaskEditDialog()
        {
            InitializeComponent();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today.AddDays(7);
            SubtaskList.ItemsSource = Subtasks;
            
            // Apply styles to ListBoxes
            WeekDayListBox.ItemContainerStyle = (Style)FindResource("MacListBoxItemStyle");
            MonthDayListBox.ItemContainerStyle = (Style)FindResource("MacListBoxItemStyle");

            // Load time tags
            LoadTimeTags();

            // Prerequisite (blocked-by) section
            BuildBlockedBySection();
        }

        private void LoadTimeTags()
        {
            TimeTagCombo.Items.Clear();
            TimeTagCombo.Items.Add(new ComboBoxItem { Content = "无", Tag = (int?)null, IsSelected = true });
            var tagRepo = new TimeTagRepository();
            foreach (var tag in tagRepo.GetAllTags())
            {
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }
                var item = new ComboBoxItem();
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Border
                {
                    Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(tagColor),
                    Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
                });
                sp.Children.Add(new TextBlock { Text = tag.Name, FontSize = 13 });
                item.Content = sp;
                item.Tag = (int?)tag.Id;
                TimeTagCombo.Items.Add(item);
            }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) DragMove();
        }

        public TaskEditDialog(bool isSubtaskMode) : this()
        {
            IsSubtaskMode = isSubtaskMode;
            if (isSubtaskMode)
            {
                CountTowardsParentPanel.Visibility = Visibility.Visible;
            }
        }

        public TaskEditDialog(TaskItem existingTask) : this()
        {
            if (existingTask != null)
            {
                IsEditMode = true;
                _editTaskId = existingTask.Id;
                _editParentTaskId = existingTask.ParentTaskId;
                _editGoalId = existingTask.GoalId;
                DialogTitle.Text = "编辑任务";
                TaskNameBox.Text = existingTask.Title;
                TaskDescBox.Text = existingTask.Description;
                StartDatePicker.SelectedDate = existingTask.StartDate;
                EndDatePicker.SelectedDate = existingTask.EndDate;

                // Show subtask section for editing
                SubtaskSection.Visibility = Visibility.Visible;

                // Load existing subtasks
                var taskRepo = new TaskRepository();
                var allTasks = taskRepo.GetAllTasks();
                foreach (var t in allTasks)
                {
                    if (t.ParentTaskId == existingTask.Id && !t.IsDeleted)
                        Subtasks.Add(t);
                }

                if (existingTask.Type == TaskType.Recurring)
                {
                    TaskTypeCombo.SelectedIndex = 1;
                    // Set repeat mode based on pattern
                    if (existingTask.RecurringPattern.HasValue)
                    {
                        switch (existingTask.RecurringPattern.Value)
                        {
                            case RecurringPattern.Daily:
                                RepeatModeCombo.SelectedIndex = 1; // Daily
                                break;
                            case RecurringPattern.Weekday:
                                RepeatModeCombo.SelectedIndex = 2; // Weekday
                                break;
                            case RecurringPattern.Weekend:
                                RepeatModeCombo.SelectedIndex = 3; // Weekend
                                break;
                            case RecurringPattern.Weekly:
                                RepeatModeCombo.SelectedIndex = 4; // Weekly
                                // Load selected days
                                if (!string.IsNullOrEmpty(existingTask.RecurringDaysOfWeek))
                                {
                                    var days = existingTask.RecurringDaysOfWeek.Split(',');
                                    foreach (var day in days)
                                    {
                                        if (int.TryParse(day, out int d))
                                        {
                                            // Select the corresponding ListBoxItem
                                            foreach (ListBoxItem item in WeekDayListBox.Items)
                                            {
                                                if (item.Tag.ToString() == d.ToString())
                                                {
                                                    item.IsSelected = true;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                            case RecurringPattern.Monthly:
                                RepeatModeCombo.SelectedIndex = 5; // Monthly
                                if (existingTask.IsLastDayOfMonth)
                                {
                                    // Select "最后一天"
                                    foreach (ListBoxItem item in MonthDayListBox.Items)
                                    {
                                        if (item.Tag.ToString() == "32")
                                        {
                                            item.IsSelected = true;
                                            break;
                                        }
                                    }
                                }
                                else if (existingTask.RecurringDayOfMonth.HasValue)
                                {
                                    foreach (ListBoxItem item in MonthDayListBox.Items)
                                    {
                                        if (item.Tag.ToString() == existingTask.RecurringDayOfMonth.Value.ToString())
                                        {
                                            item.IsSelected = true;
                                            break;
                                        }
                                    }
                                }
                                break;
                            case RecurringPattern.Interval:
                                RepeatModeCombo.SelectedIndex = 6; // Interval
                                IntervalDaysBox.Text = (existingTask.RecurringInterval ?? 1).ToString();
                                break;
                            case RecurringPattern.Custom:
                                RepeatModeCombo.SelectedIndex = 7; // Custom
                                CustomTimesPerDayBox.Text = (existingTask.RecurringTimesPerDay ?? 1).ToString();
                                CustomDaysPerWeekBox.Text = (existingTask.RecurringTimesPerWeek ?? 7).ToString();
                                break;
                        }
                    }
                }
                else if (existingTask.Type == TaskType.Quantitative)
                {
                    UseQuantitativeCheck.IsChecked = true;
                    QuantStartBox.Text = (existingTask.QuantitativeStart ?? 0).ToString();
                    QuantTargetBox.Text = (existingTask.QuantitativeTarget ?? 0).ToString();
                    QuantUnitBox.Text = existingTask.QuantitativeUnit ?? "";
                    QuantDailyMinBox.Text = (existingTask.QuantitativeDailyMin ?? 0).ToString();
                    QuantModeCombo.SelectedIndex = existingTask.QuantitativeMode == QuantitativeMode.Accumulate ? 0 : 1;
                    // Preserve actual current progress from stored task
                    ResultTask = existingTask;

                    // Also load recurring section for combined tasks
                    if (existingTask.RecurringPattern.HasValue)
                    {
                        TaskTypeCombo.SelectedIndex = 1;
                        var pattern = existingTask.RecurringPattern.Value;
                        switch (pattern)
                        {
                            case RecurringPattern.Daily:
                                RepeatModeCombo.SelectedIndex = 1;
                                break;
                            case RecurringPattern.Weekday:
                                RepeatModeCombo.SelectedIndex = 2;
                                break;
                            case RecurringPattern.Weekend:
                                RepeatModeCombo.SelectedIndex = 3;
                                break;
                            case RecurringPattern.Weekly:
                                RepeatModeCombo.SelectedIndex = 4;
                                if (!string.IsNullOrEmpty(existingTask.RecurringDaysOfWeek))
                                {
                                    var days = existingTask.RecurringDaysOfWeek.Split(',');
                                    foreach (var day in days)
                                    {
                                        if (int.TryParse(day, out int d))
                                        {
                                            foreach (ListBoxItem item in WeekDayListBox.Items)
                                            {
                                                if (item.Tag.ToString() == d.ToString())
                                                {
                                                    item.IsSelected = true;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                            case RecurringPattern.Monthly:
                                RepeatModeCombo.SelectedIndex = 5;
                                if (existingTask.IsLastDayOfMonth)
                                {
                                    foreach (ListBoxItem item in MonthDayListBox.Items)
                                    {
                                        if (item.Tag.ToString() == "32")
                                        {
                                            item.IsSelected = true;
                                            break;
                                        }
                                    }
                                }
                                else if (existingTask.RecurringDayOfMonth.HasValue)
                                {
                                    foreach (ListBoxItem item in MonthDayListBox.Items)
                                    {
                                        if (item.Tag.ToString() == existingTask.RecurringDayOfMonth.Value.ToString())
                                        {
                                            item.IsSelected = true;
                                            break;
                                        }
                                    }
                                }
                                break;
                            case RecurringPattern.Interval:
                                RepeatModeCombo.SelectedIndex = 6;
                                IntervalDaysBox.Text = (existingTask.RecurringInterval ?? 1).ToString();
                                break;
                            case RecurringPattern.Custom:
                                RepeatModeCombo.SelectedIndex = 7;
                                CustomTimesPerDayBox.Text = (existingTask.RecurringTimesPerDay ?? 1).ToString();
                                CustomDaysPerWeekBox.Text = (existingTask.RecurringTimesPerWeek ?? 7).ToString();
                                break;
                        }
                    }
                }

                // Show CountTowardsParent toggle for subtasks
                if (existingTask.ParentTaskId.HasValue || existingTask.GoalId.HasValue)
                {
                    CountTowardsParentPanel.Visibility = Visibility.Visible;
                    CountTowardsParentCheck.IsChecked = existingTask.CountTowardsParent;
                }

                // Load time tag selection
                if (existingTask.TimeTagId.HasValue)
                {
                    foreach (ComboBoxItem item in TimeTagCombo.Items)
                    {
                        if (item.Tag is int tagId && tagId == existingTask.TimeTagId.Value)
                        {
                            item.IsSelected = true;
                            break;
                        }
                    }
                }

                // Load blocked-by prerequisites
                if (!string.IsNullOrEmpty(existingTask.Uid)) _selfUid = existingTask.Uid;
                var blocked = existingTask.BlockedBy ?? new List<string>();
                if (blocked.Any())
                {
                    var repo = new TaskRepository();
                    var all = repo.GetAllTasks();
                    foreach (var uid in blocked)
                    {
                        if (string.IsNullOrEmpty(uid)) continue;
                        _blockedByUids.Add(uid);
                        var t = all.Find(x => x.Uid == uid);
                        _blockedByName[uid] = t != null ? (t.Title ?? "(未命名)") : uid;
                    }
                    RenderBlockedChips();
                }
                LoadPrereqPicker();
            }
        }

        private void TaskTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RepeatOptionsPanel != null)
            {
                RepeatOptionsPanel.Visibility = TaskTypeCombo.SelectedIndex == 1
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RepeatModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WeeklyDaysPanel == null || MonthlyDayPanel == null || IntervalPanel == null || CustomFreqPanel == null) return;

            // Hide all panels first
            WeeklyDaysPanel.Visibility = Visibility.Collapsed;
            MonthlyDayPanel.Visibility = Visibility.Collapsed;
            IntervalPanel.Visibility = Visibility.Collapsed;
            CustomFreqPanel.Visibility = Visibility.Collapsed;

            // Show relevant panel based on selection
            switch (RepeatModeCombo.SelectedIndex)
            {
                case 4: // Weekly
                    WeeklyDaysPanel.Visibility = Visibility.Visible;
                    break;
                case 5: // Monthly
                    MonthlyDayPanel.Visibility = Visibility.Visible;
                    break;
                case 6: // Interval
                    IntervalPanel.Visibility = Visibility.Visible;
                    break;
                case 7: // Custom
                    CustomFreqPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void UseQuantitativeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (QuantitativePanel != null)
            {
                var isQuant = UseQuantitativeCheck.IsChecked == true;
                QuantitativePanel.Visibility = isQuant ? Visibility.Visible : Visibility.Collapsed;
                // Resize dialog: compact when not quantitative, taller when it is
                if (isQuant)
                {
                    ContentScroller.MaxHeight = 580;
                    SizeToContent = SizeToContent.Height;
                }
                else
                {
                    ContentScroller.MaxHeight = 380;
                    SizeToContent = SizeToContent.Height;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TaskNameBox.Text))
            {
                ConfirmDialog.Show(this, "提示", "请输入任务名称", "确定");
                TaskNameBox.Focus();
                return;
            }

            if (ResultTask == null)
                ResultTask = new TaskItem();

            ResultTask.Title = TaskNameBox.Text.Trim();
            ResultTask.Description = TaskDescBox.Text.Trim();
            ResultTask.StartDate = StartDatePicker.SelectedDate;
            ResultTask.EndDate = EndDatePicker.SelectedDate;
            ResultTask.Type = TaskTypeCombo.SelectedIndex == 0 ? TaskType.OneTime : TaskType.Recurring;
            ResultTask.UpdatedAt = DateTime.Now;

            if (IsEditMode)
            {
                ResultTask.Id = _editTaskId;
                ResultTask.ParentTaskId = _editParentTaskId;
                ResultTask.GoalId = _editGoalId;
                // Preserve original CreatedAt so sorting order doesn't change
                var original = new TaskRepository().GetTaskById(_editTaskId);
                if (original != null)
                    ResultTask.CreatedAt = original.CreatedAt;
            }
            else
            {
                ResultTask.CreatedAt = DateTime.Now;
            }

            // Repeat settings
            if (TaskTypeCombo.SelectedIndex == 1) // Recurring
            {
                switch (RepeatModeCombo.SelectedIndex)
                {
                    case 0: // No repeat
                        ResultTask.RecurringPattern = null;
                        break;
                    case 1: // Daily
                        ResultTask.RecurringPattern = RecurringPattern.Daily;
                        break;
                    case 2: // Weekday
                        ResultTask.RecurringPattern = RecurringPattern.Weekday;
                        break;
                    case 3: // Weekend
                        ResultTask.RecurringPattern = RecurringPattern.Weekend;
                        break;
                    case 4: // Weekly
                        ResultTask.RecurringPattern = RecurringPattern.Weekly;
                        var selectedWeekDays = new List<int>();
                        foreach (ListBoxItem item in WeekDayListBox.SelectedItems)
                        {
                            if (int.TryParse(item.Tag.ToString(), out int d))
                                selectedWeekDays.Add(d);
                        }
                        selectedWeekDays.Sort();
                        ResultTask.RecurringDaysOfWeek = string.Join(",", selectedWeekDays);
                        break;
                    case 5: // Monthly
                        ResultTask.RecurringPattern = RecurringPattern.Monthly;
                        if (MonthDayListBox.SelectedItem is ListBoxItem selectedMonthItem)
                        {
                            int dayTag = int.Parse(selectedMonthItem.Tag.ToString());
                            if (dayTag == 32)
                            {
                                ResultTask.IsLastDayOfMonth = true;
                                ResultTask.RecurringDayOfMonth = null;
                            }
                            else
                            {
                                ResultTask.IsLastDayOfMonth = false;
                                ResultTask.RecurringDayOfMonth = dayTag;
                            }
                        }
                        break;
                    case 6: // Interval
                        ResultTask.RecurringPattern = RecurringPattern.Interval;
                        if (int.TryParse(IntervalDaysBox.Text, out int days))
                            ResultTask.RecurringInterval = days;
                        break;
                    case 7: // Custom
                        ResultTask.RecurringPattern = RecurringPattern.Custom;
                        if (int.TryParse(CustomTimesPerDayBox.Text, out int timesPerDay))
                            ResultTask.RecurringTimesPerDay = timesPerDay;
                        if (int.TryParse(CustomDaysPerWeekBox.Text, out int daysPerWeek))
                            ResultTask.RecurringTimesPerWeek = daysPerWeek;
                        ResultTask.RecurringTargetCount = timesPerDay;
                        ResultTask.RecurringCurrentCount = 0;
                        break;
                }
            }

            // Quantitative settings
            if (UseQuantitativeCheck.IsChecked == true)
            {
                ResultTask.Type = TaskType.Quantitative;
                ResultTask.QuantitativeMode = QuantModeCombo.SelectedIndex == 0
                    ? QuantitativeMode.Accumulate : QuantitativeMode.Update;

                if (double.TryParse(QuantStartBox.Text, out double start))
                    ResultTask.QuantitativeStart = start;
                if (double.TryParse(QuantTargetBox.Text, out double target))
                    ResultTask.QuantitativeTarget = target;
                ResultTask.QuantitativeUnit = QuantUnitBox.Text.Trim();
                if (double.TryParse(QuantDailyMinBox.Text, out double dailyMin) && dailyMin > 0)
                    ResultTask.QuantitativeDailyMin = dailyMin;
                else
                    ResultTask.QuantitativeDailyMin = null;

                // Show initial progress (display only; completion record tracks daily check-in)
                if (ResultTask.QuantitativeCurrent == null || ResultTask.QuantitativeCurrent < (ResultTask.QuantitativeStart ?? 0))
                    ResultTask.QuantitativeCurrent = ResultTask.QuantitativeStart;
            }

            // CountTowardsParent for subtasks
            if (CountTowardsParentPanel.Visibility == Visibility.Visible)
            {
                ResultTask.CountTowardsParent = CountTowardsParentCheck.IsChecked == true;
            }

            // Time tag association
            if (TimeTagCombo.SelectedItem is ComboBoxItem tagItem && tagItem.Tag is int selectedTagId)
                ResultTask.TimeTagId = selectedTagId;
            else
                ResultTask.TimeTagId = null;

            // Prerequisites (blocked-by)
            ResultTask.BlockedBy = new List<string>(_blockedByUids);

            DialogResult = true;
            Close();
        }

        private void AddSubtask_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TaskEditDialog(isSubtaskMode: true) { Owner = this, Title = "添加子任务" };
            if (dialog.ShowDialog() == true && dialog.ResultTask != null)
            {
                Subtasks.Add(dialog.ResultTask);
            }
        }

        private void EditSubtask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
            {
                var dialog = new TaskEditDialog(task) { Owner = this, Title = "编辑子任务" };
                dialog.IsSubtaskMode = true;
                dialog.CountTowardsParentPanel.Visibility = Visibility.Visible;
                if (dialog.ShowDialog() == true && dialog.ResultTask != null)
                {
                    var index = Subtasks.IndexOf(task);
                    if (index >= 0)
                    {
                        dialog.ResultTask.Id = task.Id;
                        Subtasks[index] = dialog.ResultTask;
                    }
                }
            }
        }

        private void RemoveSubtask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TaskItem item)
            {
                if (item.Id > 0)
                {
                    var repo = new TaskRepository();

                    // Subtract from parent task before deleting
                    if (item.CountTowardsParent && item.ParentTaskId.HasValue)
                    {
                        var parent = repo.GetAllTasks().Find(t => t.Id == item.ParentTaskId.Value && !t.IsDeleted);
                        if (parent != null && parent.Type == TaskType.Quantitative)
                        {
                            parent.QuantitativeCurrent = (parent.QuantitativeCurrent ?? 0) - (item.QuantitativeCurrent ?? 0);
                            repo.UpdateTask(parent);
                        }
                    }

                    repo.SoftDeleteTask(item.Id);
                }
                Subtasks.Remove(item);
            }
        }

        public void PersistSubtasks()
        {
            if (ResultTask == null || ResultTask.Id <= 0) return;
            var taskRepo = new TaskRepository();

            foreach (var subtask in Subtasks)
            {
                subtask.ParentTaskId = ResultTask.Id;
                if (subtask.Id > 0)
                    taskRepo.UpdateTask(subtask);
                else
                    taskRepo.InsertTask(subtask);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #region 前置依赖 (BlockedBy)

        private void BuildBlockedBySection()
        {
            _blockedBySection = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            _blockedBySection.Children.Add(new TextBlock
            {
                Text = "前置依赖",
                FontSize = 12,
                Foreground = GetBrush("SecondaryTextBrush"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var addBtn = new Button
            {
                Content = "＋ 添加前置",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            addBtn.Click += AddPrereq_Click;
            _blockedBySection.Children.Add(addBtn);

            _prereqCombo = new ComboBox
            {
                Height = 36,
                FontSize = 13,
                Style = (Style)FindResource("MacComboBoxStyle"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            _prereqCombo.SelectionChanged += PrereqCombo_SelectionChanged;
            _blockedBySection.Children.Add(_prereqCombo);

            _blockedByChips = new WrapPanel();
            _blockedBySection.Children.Add(_blockedByChips);

            var content = ContentScroller.Content as StackPanel;
            var dateGrid = FindDateRangeGrid();
            int idx = dateGrid != null ? content.Children.IndexOf(dateGrid) : content.Children.Count - 1;
            content.Children.Insert(idx + 1, _blockedBySection);

            LoadPrereqPicker();
        }

        private FrameworkElement FindDateRangeGrid()
        {
            var content = ContentScroller.Content as StackPanel;
            if (content == null) return null;
            foreach (var child in content.Children)
            {
                if (child is Grid g &&
                    g.Children.OfType<StackPanel>().Any(sp => sp.Children.Contains(StartDatePicker)))
                    return g;
            }
            return null;
        }

        private void LoadPrereqPicker()
        {
            if (_prereqCombo == null) return;
            _prereqCombo.Items.Clear();
            _prereqCombo.Items.Add(new ComboBoxItem { Content = "选择前置任务…", Tag = (TaskItem)null, IsSelected = true });

            var repo = new TaskRepository();
            foreach (var t in repo.GetAllTasks())
            {
                if (t.IsDeleted) continue;
                if (string.IsNullOrEmpty(t.Uid)) continue;                       // (无Uid) 跳过
                if (!string.IsNullOrEmpty(_selfUid) && t.Uid == _selfUid) continue;
                if (_blockedByUids.Contains(t.Uid)) continue;
                _prereqCombo.Items.Add(new ComboBoxItem { Content = t.Title ?? "(未命名)", Tag = t });
            }
        }

        private void AddPrereq_Click(object sender, RoutedEventArgs e)
        {
            if (_prereqCombo != null) _prereqCombo.IsDropDownOpen = true;
        }

        private void PrereqCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_prereqCombo?.SelectedItem is ComboBoxItem item && item.Tag is TaskItem sel)
            {
                if (!string.IsNullOrEmpty(sel.Uid) && !_blockedByUids.Contains(sel.Uid))
                {
                    _blockedByUids.Add(sel.Uid);
                    if (!_blockedByName.ContainsKey(sel.Uid))
                        _blockedByName[sel.Uid] = sel.Title ?? "(未命名)";
                    RenderBlockedChips();
                    LoadPrereqPicker();
                }
                _prereqCombo.SelectedIndex = 0;
            }
        }

        private void RenderBlockedChips()
        {
            if (_blockedByChips == null) return;
            _blockedByChips.Children.Clear();
            foreach (var uid in _blockedByUids)
            {
                var name = _blockedByName.ContainsKey(uid) ? _blockedByName[uid] : uid;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Background = GetBrush("CardBrush"),
                    BorderBrush = GetBrush("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Foreground = GetBrush("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var x = new Button
                {
                    Content = "✕",
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Padding = new Thickness(6, 0, 6, 0),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                var localUid = uid;
                x.Click += (s, ev) =>
                {
                    _blockedByUids.Remove(localUid);
                    _blockedByName.Remove(localUid);
                    RenderBlockedChips();
                    LoadPrereqPicker();
                };
                sp.Children.Add(x);
                chip.Child = sp;
                _blockedByChips.Children.Add(chip);
            }
        }

        #endregion

        private Brush GetBrush(string key) => (Brush)FindResource(key);
    }
}