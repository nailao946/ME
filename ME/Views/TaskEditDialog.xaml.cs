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
    }
}