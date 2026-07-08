using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    public partial class GoalEditDialog : Window
    {
        public Goal ResultGoal { get; private set; }
        public ObservableCollection<TaskItem> Subtasks { get; } = new ObservableCollection<TaskItem>();
        private int? _selectedTagId;
        private List<GoalTag> _allTags;
        private int _editGoalId;

        public GoalEditDialog()
        {
            InitializeComponent();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today.AddYears(1);
            SubtaskList.ItemsSource = Subtasks;
            LoadTags();
        }

        public GoalEditDialog(Goal existingGoal) : this()
        {
            if (existingGoal != null)
            {
                _editGoalId = existingGoal.Id;
                DialogTitle.Text = "编辑目标";
                GoalNameBox.Text = existingGoal.Name;
                GoalDescBox.Text = existingGoal.Description;
                StartDatePicker.SelectedDate = existingGoal.StartDate;
                EndDatePicker.SelectedDate = existingGoal.EndDate;
                TimeFrameCombo.SelectedIndex = (int)existingGoal.TimeFrame;
                _selectedTagId = existingGoal.TagId;
                RefreshTagSelection();

                // Load quantitative settings
                if (existingGoal.QuantitativeMode.HasValue)
                {
                    UseQuantitativeCheck.IsChecked = true;
                    QuantStartBox.Text = (existingGoal.QuantitativeStart ?? 0).ToString();
                    QuantTargetBox.Text = (existingGoal.QuantitativeTarget ?? 0).ToString();
                    QuantUnitBox.Text = existingGoal.QuantitativeUnit ?? "";
                    QuantModeCombo.SelectedIndex = existingGoal.QuantitativeMode == QuantitativeMode.Accumulate ? 0 : 1;
                }

                // Load existing subtasks
                var taskRepo = new TaskRepository();
                var childTasks = taskRepo.GetTasksByGoalId(existingGoal.Id);
                foreach (var t in childTasks)
                    Subtasks.Add(t);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void UseQuantitativeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (QuantitativePanel != null)
            {
                var isQuant = UseQuantitativeCheck.IsChecked == true;
                QuantitativePanel.Visibility = isQuant ? Visibility.Visible : Visibility.Collapsed;
                if (isQuant)
                {
                    ContentScroller.MaxHeight = 600;
                    SizeToContent = SizeToContent.Height;
                }
                else
                {
                    ContentScroller.MaxHeight = 400;
                    SizeToContent = SizeToContent.Height;
                }
            }
        }

        private void LoadTags()
        {
            var repo = new TagRepository();
            _allTags = repo.GetAllTags();
            BuildTagButtons();
        }

        private void BuildTagButtons()
        {
            TagPanel.Children.Clear();
            TagPanel.Children.Add(CreateTagButton("无标签", "#8E8E93", null));
            foreach (var tag in _allTags)
                TagPanel.Children.Add(CreateTagButton(tag.Name, tag.Color, tag.Id));
        }

        private System.Windows.Controls.Border CreateTagButton(string name, string color, int? tagId)
        {
            var border = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(14),
                Height = 28,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Tag = tagId,
                Padding = new Thickness(12, 0, 12, 0),
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = name, FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White
                }
            };

            try
            {
                var c = (Color)ColorConverter.ConvertFromString(color);
                border.Background = new SolidColorBrush(c);
                border.BorderBrush = _selectedTagId == tagId ? Brushes.White : Brushes.Transparent;
            }
            catch
            {
                border.Background = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                border.BorderBrush = _selectedTagId == tagId ? Brushes.White : Brushes.Transparent;
            }

            border.MouseLeftButtonDown += (s, e) =>
            {
                _selectedTagId = tagId;
                RefreshTagSelection();
            };

            return border;
        }

        private void RefreshTagSelection()
        {
            foreach (var child in TagPanel.Children)
            {
                if (child is System.Windows.Controls.Border border)
                {
                    var isSelected = Equals(border.Tag, _selectedTagId);
                    border.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
                    border.BorderBrush = isSelected ? Brushes.White : Brushes.Transparent;
                }
            }
        }

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TagEditDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultTag != null)
            {
                var repo = new TagRepository();
                var id = repo.InsertTag(dialog.ResultTag);
                dialog.ResultTag.Id = id;
                _allTags.Add(dialog.ResultTag);
                BuildTagButtons();
            }
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
                dialog.CountTowardsParentCheck.IsChecked = task.CountTowardsParent;
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
                // If it has an ID, delete from repository
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GoalNameBox.Text))
            {
                ConfirmDialog.Show(this, "提示", "请输入目标名称", "确定");
                GoalNameBox.Focus();
                return;
            }

            ResultGoal = new Goal
            {
                Name = GoalNameBox.Text.Trim(),
                Description = GoalDescBox.Text.Trim(),
                StartDate = StartDatePicker.SelectedDate,
                EndDate = EndDatePicker.SelectedDate,
                TimeFrame = (GoalTimeFrame)TimeFrameCombo.SelectedIndex,
                TagId = _selectedTagId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Progress = 0
            };

            // Quantitative settings
            if (UseQuantitativeCheck.IsChecked == true)
            {
                ResultGoal.QuantitativeMode = QuantModeCombo.SelectedIndex == 0
                    ? QuantitativeMode.Accumulate : QuantitativeMode.Update;
                if (double.TryParse(QuantStartBox.Text, out double start))
                    ResultGoal.QuantitativeStart = start;
                if (double.TryParse(QuantTargetBox.Text, out double target))
                    ResultGoal.QuantitativeTarget = target;
                ResultGoal.QuantitativeUnit = QuantUnitBox.Text.Trim();
            }

            if (_selectedTagId.HasValue)
            {
                var tag = _allTags.Find(t => t.Id == _selectedTagId.Value);
                if (tag != null)
                    ResultGoal.Color = MapHexToGoalColor(tag.Color);
            }

            DialogResult = true;
            Close();
        }

        public void PersistSubtasks()
        {
            if (ResultGoal == null || ResultGoal.Id <= 0) return;
            var taskRepo = new TaskRepository();

            foreach (var subtask in Subtasks)
            {
                if (subtask.Id > 0)
                {
                    // Existing task - update
                    subtask.GoalId = ResultGoal.Id;
                    taskRepo.UpdateTask(subtask);
                }
                else
                {
                    // New task - insert
                    subtask.GoalId = ResultGoal.Id;
                    taskRepo.InsertTask(subtask);
                }
            }
        }

        private GoalColor MapHexToGoalColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return GoalColor.Blue;
            switch (hex.ToUpper())
            {
                case "#FF3B30": return GoalColor.Red;
                case "#34C759": return GoalColor.Green;
                case "#007AFF": return GoalColor.Blue;
                case "#FF2D55": return GoalColor.Pink;
                case "#8E8E93": return GoalColor.Gray;
                case "#FFCC00": return GoalColor.Yellow;
                default: return GoalColor.Blue;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class SubtaskItem
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
