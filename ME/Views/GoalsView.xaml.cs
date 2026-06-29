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
using ME.ViewModels;
using ME.Core;

namespace ME.Views
{
    public partial class GoalsView : System.Windows.Controls.UserControl
    {
        private GoalsViewModel _vm;
        private int? _filterTagId;

        // Drag state
        private bool _isDragging;
        private Point _dragStart;
        private Border _draggedBorder;
        private int _dragSourceIndex;
        private List<Goal> _dragSourceList;
        private StackPanel _dragPanel;
        private StackPanel _dragMainPanel;
        private Border _placeholderBorder;

        public GoalsView()
        {
            InitializeComponent();
            _vm = new GoalsViewModel();
            DataContext = _vm;
            BuildTagFilter();
            LoadGoalsWithSections();
            EventAggregator.Instance.Subscribe<string>(OnTagChanged);
            EventAggregator.Instance.Subscribe<string>(OnTaskCompleted);
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
        }

        private void OnTagChanged(string message)
        {
            if (message == "TagChanged")
            {
                BuildTagFilter();
                LoadGoalsWithSections();
            }
        }

        private void OnTaskCompleted(string message)
        {
            if (message == "TaskCompleted")
            {
                Dispatcher.BeginInvoke(new Action(() => LoadGoalsWithSections()));
            }
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(() =>
            {
                BuildTagFilter();
                LoadGoalsWithSections();
            });
        }

        private void GoalsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible) LoadGoalsWithSections();
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
                LoadGoalsWithSections();
            }
        }

        private void LoadGoalsWithSections()
        {
            _vm.LoadGoals();
            var goals = new List<Goal>();

            foreach (var g in _vm.Goals)
            {
                if (_filterTagId.HasValue && g.TagId != _filterTagId.Value) continue;
                goals.Add(g);
            }

            BuildGoalTree(GoalsPanel, goals);
            AnimateGoalCards();
        }

        private void AnimateGoalCards()
        {
            for (int i = 0; i < GoalsPanel.Children.Count; i++)
            {
                var child = GoalsPanel.Children[i] as FrameworkElement;
                if (child == null) continue;
                child.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 60);
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var slide = new TranslateTransform(0, 10);
                child.RenderTransform = slide;
                var slideAnim = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(350))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                slide.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            }
        }

        private void BuildGoalTree(StackPanel panel, List<Goal> goals)
        {
            panel.Children.Clear();

            foreach (var goal in goals)
            {
                var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                // Goal card
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Cursor = Cursors.SizeAll,
                    Tag = goal
                };
                SetupGoalDragDrop(card, goal, goals, panel);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var progressColor = string.IsNullOrEmpty(goal.TagColor) ? (SolidColorBrush)FindResource("PrimaryBrush")
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(goal.TagColor));

                // Text area
                var textPanel = new StackPanel { IsHitTestVisible = false };

                // Tag badge + Name
                var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
                if (!string.IsNullOrEmpty(goal.TagName))
                {
                    var tagBadge = new Border
                    {
                        CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 8, 0),
                        Background = new SolidColorBrush(string.IsNullOrEmpty(goal.TagColor)
                            ? Color.FromRgb(0, 122, 255)
                            : (Color)ColorConverter.ConvertFromString(goal.TagColor)),
                        Child = new TextBlock { Text = goal.TagName, FontSize = 10, Foreground = Brushes.White }
                    };
                    namePanel.Children.Add(tagBadge);
                }
                namePanel.Children.Add(new TextBlock
                {
                    Text = goal.Name, FontSize = 16, FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)FindResource("TextBrush")
                });
                textPanel.Children.Add(namePanel);

                // Expired label for goal
                bool isGoalExpired = goal.EndDate.HasValue && goal.EndDate.Value.Date < DateTime.Today;
                if (isGoalExpired)
                {
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = "任务已过期", FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48)),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }

                if (!string.IsNullOrEmpty(goal.Description))
                {
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = goal.Description, FontSize = 11,
                        Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                        MaxHeight = 35, TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }

                // Progress + time frame + detail
                var taskService = new TaskService();
                var (progress, detail) = taskService.CalcGoalProgress(goal.Id);
                goal.Progress = progress;

                // Compute completed count and tracking time
                var taskRepo = new TaskRepository();
                var allGoalTasks = taskRepo.GetAllTasks().Where(t => t.GoalId == goal.Id && !t.IsDeleted).ToList();
                int completedCount = 0;
                double totalTrackMinutes = 0;
                var timeRecordRepo = new TimeRecordRepository();
                foreach (var t in allGoalTasks)
                {
                    if (IsTaskCompletedForDisplay(t)) completedCount++;
                    if (t.TimeTagId is int tagId)
                    {
                        var allRecords = timeRecordRepo.GetAllRecords();
                        totalTrackMinutes += allRecords.Where(r => r.TagId == tagId).Sum(r => r.Duration.TotalMinutes);
                    }
                }
                var trackHours = (int)(totalTrackMinutes / 60);
                var trackMins = (int)(totalTrackMinutes % 60);

                // Remaining days
                string remainStr = "";
                if (goal.EndDate.HasValue)
                {
                    var remain = (goal.EndDate.Value.Date - DateTime.Today).Days;
                    remainStr = remain >= 0 ? $"剩余{remain}天" : $"已超{-remain}天";
                }

                var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                infoPanel.Children.Add(new TextBlock { Text = "进度:", FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush") });
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"{progress:F0}%", FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = progressColor, Margin = new Thickness(3, 0, 0, 0)
                });
                if (!string.IsNullOrEmpty(detail))
                {
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $" ({detail})", FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = progressColor
                    });
                }
                if (!string.IsNullOrEmpty(remainStr))
                {
                    var remainColor = remainStr.StartsWith("剩余") ? (SolidColorBrush)FindResource("SecondaryTextBrush")
                        : new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"  {remainStr}", FontSize = 10,
                        Foreground = remainColor, Margin = new Thickness(8, 0, 0, 0)
                    });
                }
                infoPanel.Children.Add(new TextBlock { Text = $"  完成{completedCount}次", FontSize = 10, Foreground = progressColor, Margin = new Thickness(8, 0, 0, 0) });
                if (totalTrackMinutes > 0)
                {
                    infoPanel.Children.Add(new TextBlock { Text = $"  计时{trackHours}h{trackMins:D2}m", FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"), Margin = new Thickness(8, 0, 0, 0) });
                }
                var timeFrame = goal.TimeFrame == GoalTimeFrame.LongTerm ? "长期" : goal.TimeFrame == GoalTimeFrame.Inspiration ? "灵感" : "短期";
                infoPanel.Children.Add(new TextBlock { Text = timeFrame, FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"), Margin = new Thickness(8, 0, 0, 0) });
                if (goal.EndDate.HasValue)
                {
                    var deadlineColor = isGoalExpired
                        ? new SolidColorBrush(Color.FromRgb(255, 59, 48))
                        : (SolidColorBrush)FindResource("SecondaryTextBrush");
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $" 截止:{goal.EndDate.Value:yyyy/MM/dd}", FontSize = 10,
                        Foreground = deadlineColor, Margin = new Thickness(8, 0, 0, 0)
                    });
                }
                textPanel.Children.Add(infoPanel);

                // Progress bar
                var pb = new ProgressBar
                {
                    Value = 0, Maximum = 100, Height = 8,
                    Margin = new Thickness(0, 5, 0, 0),
                    Background = (SolidColorBrush)FindResource("BackgroundBrush"),
                    Foreground = progressColor
                };
                pb.Loaded += (s, e) =>
                {
                    var anim = new DoubleAnimation(0, progress, TimeSpan.FromMilliseconds(600))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                };
                textPanel.Children.Add(pb);

                Grid.SetColumn(textPanel, 0);
                grid.Children.Add(textPanel);

                // Buttons
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                btnPanel.Children.Add(CreateGoalButton("编辑", EditGoal_Click, goal));
                btnPanel.Children.Add(CreateGoalButton("+任务", AddSubtask_Click, goal));
                btnPanel.Children.Add(CreateGoalButton("删除", DeleteGoal_Click, goal));
                Grid.SetColumn(btnPanel, 1);
                grid.Children.Add(btnPanel);

                card.Child = grid;
                wrapper.Children.Add(card);

                // Active subtasks (root-level only, sub-tasks render under parents)
                var rootTasks = goal.Subtasks?.Where(t => !t.ParentTaskId.HasValue).ToList() ?? new List<TaskItem>();
                var activeTasks = rootTasks.Where(t => !IsTaskCompletedForDisplay(t)).ToList();
                var completedTasks = rootTasks.Where(t => IsTaskCompletedForDisplay(t)).ToList();

                if (activeTasks.Count > 0)
                {
                    var activeExpander = new Expander
                    {
                        IsExpanded = true,
                        Margin = new Thickness(24, 0, 0, 0),
                        Header = new TextBlock
                        {
                            Text = $"进行中 ({activeTasks.Count})",
                            FontSize = 11,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                        }
                    };
                    var activePanel = new StackPanel();
                    foreach (var sub in activeTasks)
                    {
                        activePanel.Children.Add(CreateGoalSubtaskCard(sub, goal.TagColor, goal.Subtasks));
                    }
                    activeExpander.Content = activePanel;
                    wrapper.Children.Add(activeExpander);
                }

                // Completed subtasks
                if (completedTasks.Count > 0)
                {
                    var doneExpander = new Expander
                    {
                        IsExpanded = false,
                        Margin = new Thickness(24, 0, 0, 0),
                        Header = new TextBlock
                        {
                            Text = $"已完成 ({completedTasks.Count})",
                            FontSize = 11,
                            Foreground = (SolidColorBrush)FindResource("AccentGreenBrush")
                        }
                    };
                    var donePanel = new StackPanel();
                    foreach (var sub in completedTasks)
                    {
                        donePanel.Children.Add(CreateGoalSubtaskCard(sub, goal.TagColor, goal.Subtasks));
                    }
                    doneExpander.Content = donePanel;
                    wrapper.Children.Add(doneExpander);
                }

                panel.Children.Add(wrapper);
            }
        }

        private bool IsTaskCompletedForDisplay(TaskItem task)
        {
            var ts = new TaskService();
            return ts.IsTaskCompletedForDisplay(task, DateTime.Today);
        }

        private Border CreateGoalSubtaskCard(TaskItem task, string tagColor, List<TaskItem> allGoalTasks = null)
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
            bool isCompletedDisplay = IsTaskCompletedForDisplay(task);

            var outerStack = new StackPanel();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Completion circle
            bool isQuant = task.Type == TaskType.Quantitative && task.QuantitativeTarget.HasValue && task.QuantitativeTarget > 0;
            var circle = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = isQuant ? new CornerRadius(3) : new CornerRadius(9),
                BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                Background = isCompletedDisplay ? progressColor : (isQuant ? progressColor : Brushes.Transparent),
                BorderBrush = isCompletedDisplay ? progressColor : (SolidColorBrush)FindResource("BorderBrush"),
                Child = new TextBlock
                {
                    Text = isCompletedDisplay ? "✓" : (isQuant ? "+" : (isCustomRecurring ? $"{new TaskService().GetCustomRecurringCountOnDate(task.Id, DateTime.Today)}" : "")),
                    Foreground = Brushes.White, FontSize = isCompletedDisplay ? 8 : 10, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            circle.Tag = task;
            circle.MouseLeftButtonDown += (s, e) => SubtaskCircle_Click(s, e);
            Grid.SetColumn(circle, 0);
            grid.Children.Add(circle);

            // Text area
            var textPanel = new StackPanel { IsHitTestVisible = false };
            textPanel.Children.Add(new TextBlock
            {
                Text = task.Title, FontSize = 12,
                Foreground = isCompletedDisplay ? (SolidColorBrush)FindResource("SecondaryTextBrush") : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = isCompletedDisplay ? TextDecorations.Strikethrough : null
            });

            // Expired label
            bool isExpired = !isCompletedDisplay && task.EndDate.HasValue && task.EndDate.Value.Date < DateTime.Today;
            if (isExpired)
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = "任务已过期", FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (!isCompletedDisplay)
            {
                // Description
                if (!string.IsNullOrEmpty(task.Description))
                {
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = task.Description, FontSize = 10,
                        Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
                        MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }

                // Info panel: type + deadline + time tag
                var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                var typeText = task.Type == TaskType.Recurring ? "循环" : task.Type == TaskType.Quantitative ? "量化" : "单次";
                infoPanel.Children.Add(new TextBlock { Text = typeText, FontSize = 10, Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush") });

                // Show time tag if set
                if (task.TimeTagId.HasValue)
                {
                    var tagRepo = new TimeTagRepository();
                    var tag = tagRepo.GetTagById(task.TimeTagId.Value);
                    if (tag != null)
                    {
                        Color timeTagClr;
                        try { timeTagClr = (Color)ColorConverter.ConvertFromString(tag.Color); }
                        catch { timeTagClr = Color.FromRgb(128, 128, 128); }
                        infoPanel.Children.Add(new Border
                        {
                            Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                            Background = new SolidColorBrush(timeTagClr),
                            Margin = new Thickness(6, 0, 4, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        infoPanel.Children.Add(new TextBlock
                        {
                            Text = tag.Name, FontSize = 10,
                            Foreground = new SolidColorBrush(timeTagClr),
                            Margin = new Thickness(0, 0, 6, 0)
                        });
                    }
                }

                // Show elapsed time for tasks with a time tag
                if (task.TimeTagId.HasValue)
                {
                    var trRepo = new TimeRecordRepository();
                    var allRecords = trRepo.GetAllRecords();
                    var tagMins = allRecords.Where(r => r.TagId == task.TimeTagId.Value).Sum(r => r.Duration.TotalMinutes);
                    if (tagMins > 0)
                    {
                        var h = (int)(tagMins / 60);
                        var m = (int)(tagMins % 60);
                        infoPanel.Children.Add(new TextBlock
                        {
                            Text = $"⌚ {h}h{m:D2}m",
                            FontSize = 10,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                            Margin = new Thickness(0, 0, 6, 0)
                        });
                    }
                }
                if (task.EndDate.HasValue)
                {
                    var deadlineColor = isExpired 
                        ? new SolidColorBrush(Color.FromRgb(255, 59, 48))
                        : (SolidColorBrush)FindResource("SecondaryTextBrush");
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $" 截止:{task.EndDate.Value:yyyy/MM/dd}", FontSize = 10,
                        Foreground = deadlineColor, Margin = new Thickness(6, 0, 0, 0)
                    });
                }
                textPanel.Children.Add(infoPanel);
            }

            // Custom recurring progress bar for subtask
            if (isCustomRecurring)
            {
                var taskSvc = new TaskService();
                var current = taskSvc.GetCustomRecurringCountOnDate(task.Id, DateTime.Today);
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
                    var taskService = new TaskService();
                    var today = DateTime.Today;
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    int weekDays = 0;
                    for (int i = 0; i < 7; i++)
                    {
                        var date = startOfWeek.AddDays(i);
                        if (date > today) break;
                        int dayCount = taskService.GetCustomRecurringCountOnDate(task.Id, date);
                        if (dayCount >= target) weekDays++;
                    }
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
            // Quantitative progress bar for subtask
            else if (isQuant)
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

            // Edit/AddSubtask/Delete buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!isCompletedDisplay)
                btnPanel.Children.Add(CreateGoalButton("+子", AddSubtaskToTask_Click, task));
            btnPanel.Children.Add(CreateGoalButton("编辑", EditTaskFromGoal_Click, task));
            btnPanel.Children.Add(CreateGoalButton("删除", DeleteTaskFromGoal_Click, task));
            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            outerStack.Children.Add(grid);
            card.Child = outerStack;

            // Recursively render child tasks
            if (allGoalTasks != null)
            {
                var children = allGoalTasks.Where(t => t.ParentTaskId == task.Id && !t.IsDeleted).ToList();
                if (children.Count > 0)
                {
                    var childMargin = new Thickness(20, 4, 0, 0);
                    foreach (var child in children)
                    {
                        var childCard = CreateGoalSubtaskCard(child, tagColor, allGoalTasks);
                        childCard.Margin = new Thickness(20, 4, 0, 0);
                        outerStack.Children.Add(childCard);
                    }
                }
            }

            return card;
        }

        private Button CreateGoalButton(string content, RoutedEventHandler handler, object tag)
        {
            var btn = new Button
            {
                Content = content,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11, Tag = tag
            };
            btn.Click += handler;
            return btn;
        }

        // ============ DRAG AND DROP FOR GOALS ============
        private void SetupGoalDragDrop(Border card, Goal goal, List<Goal> sourceList, StackPanel mainPanel)
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
                _dragSourceIndex = sourceList.IndexOf(goal);
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
                    Mouse.Capture(_draggedBorder);

                    _placeholderBorder = new Border
                    {
                        Style = (Style)FindResource("CardStyle"),
                        Height = _draggedBorder.ActualHeight, Opacity = 0.3,
                        Margin = new Thickness(0, 0, 0, 12),
                        Background = (SolidColorBrush)FindResource("PrimaryBrush"),
                        IsHitTestVisible = false
                    };
                    if (_dragMainPanel != null && _dragPanel != null)
                    {
                        int srcIdx = _dragMainPanel.Children.IndexOf(_dragPanel);
                        if (srcIdx >= 0)
                            _dragMainPanel.Children.Insert(srcIdx + 1, _placeholderBorder);
                    }
                }

                if (_isDragging && _dragMainPanel != null)
                {
                    // Remove placeholder first, calculate drop index, then re-insert
                    bool hadPlaceholder = _dragMainPanel.Children.Contains(_placeholderBorder);
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
                    if (insertIndex >= 0)
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

                    // Remove placeholder and calculate final drop index
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

                        var repo = new GoalRepository();
                        for (int i = 0; i < _dragSourceList.Count; i++)
                        {
                            _dragSourceList[i].SortOrder = i;
                            repo.UpdateGoal(_dragSourceList[i]);
                        }
                        LoadGoalsWithSections();
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

        private void GoalCircle_Click(object sender, RoutedEventArgs e)
        {
            // Goals are folders — they cannot be completed
        }

        private void AddGoal_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GoalEditDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.ResultGoal != null)
            {
                var repo = new GoalRepository();
                var id = repo.InsertGoal(dialog.ResultGoal);
                dialog.ResultGoal.Id = id;
                dialog.PersistSubtasks();
                LoadGoalsWithSections();
            }
        }

        private void EditGoal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Goal goal)
            {
                var dialog = new GoalEditDialog(goal) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true && dialog.ResultGoal != null)
                {
                    dialog.ResultGoal.Id = goal.Id;
                    var repo = new GoalRepository();
                    repo.UpdateGoal(dialog.ResultGoal);
                    dialog.PersistSubtasks();
                    LoadGoalsWithSections();
                }
            }
        }

        private void AddSubtask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Goal goal)
            {
                var dialog = new TaskEditDialog(isSubtaskMode: true) { Owner = Window.GetWindow(this), Title = "添加任务" };
                if (dialog.ShowDialog() == true && dialog.ResultTask != null)
                {
                    dialog.ResultTask.GoalId = goal.Id;
                    var repo = new TaskRepository();
                    var id = repo.InsertTask(dialog.ResultTask);
                    dialog.ResultTask.Id = id;

                    if (dialog.ResultTask.CountTowardsParent)
                        RecalcGoalProgressFromSubtasks(goal.Id, repo);

                    LoadGoalsWithSections();
                }
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

                    LoadGoalsWithSections();
                }
            }
        }

        private void DeleteGoal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Goal goal)
            {
                var repo = new GoalRepository();
                repo.SoftDeleteGoal(goal.Id);
                LoadGoalsWithSections();
            }
        }

        private void RestoreGoal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Goal goal)
            {
                var repo = new GoalRepository();
                goal.IsArchived = false;
                goal.Progress = 0;
                repo.UpdateGoal(goal);
                SoundService.PlayCompletionSound();
                LoadGoalsWithSections();
            }
        }

        private void ManageTags_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TagEditDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            BuildTagFilter();
        }

        private void SubtaskCircle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TaskItem task)
            {
                var repo2 = new TaskRepository();
                var taskService = new TaskService();

                if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
                {
                    if (task.RecurringPattern == RecurringPattern.Custom && task.RecurringTargetCount.HasValue && task.RecurringTargetCount > 1)
                    {
                        int currentCount = taskService.GetCustomRecurringCountOnDate(task.Id, DateTime.Today);
                        if (currentCount >= task.RecurringTargetCount.Value)
                        {
                            taskService.RemoveCompletion(task.Id, DateTime.Today);
                        }
                        else
                        {
                            taskService.RecordCustomRecurringCompletion(task.Id, DateTime.Today);
                            int count = taskService.GetCustomRecurringCountOnDate(task.Id, DateTime.Today);
                            if (count >= task.RecurringTargetCount.Value)
                            {
                                task.LastCompletedDate = DateTime.Today;
                                repo2.UpdateTask(task);
                            }
                        }
                    }
                    else
                    {
                        bool isCompletedToday = taskService.IsRecurringTaskCompletedOnDate(task, DateTime.Today);
                        if (isCompletedToday)
                            taskService.RemoveCompletion(task.Id, DateTime.Today);
                        else
                            taskService.RecordCompletion(task.Id, DateTime.Today);
                    }
                }
                else if (task.Type == TaskType.Quantitative && task.QuantitativeMode.HasValue)
                {
                    var dialog = new QuantitativeInputDialog(task) { Owner = Window.GetWindow(this) };
                    if (dialog.ShowDialog() == true)
                    {
                        var oldValue = task.QuantitativeCurrent ?? 0;
                        task.QuantitativeCurrent = dialog.NewValue;
                        var delta = task.QuantitativeCurrent.Value - oldValue;
                        bool reachedTarget = task.QuantitativeTarget.HasValue && task.QuantitativeCurrent >= task.QuantitativeTarget.Value;
                        bool isCombined = task.RecurringPattern.HasValue;
                        if (reachedTarget)
                        {
                            task.IsCompleted = true;
                            task.CompletedAt = DateTime.Now;
                        }
                        else if (isCombined)
                        {
                            task.IsCompleted = false;
                            task.CompletedAt = null;
                            var ts2 = new TaskService();
                            double dailyMin = task.QuantitativeDailyMin ?? 0;
                            if (dailyMin > 0 && (task.QuantitativeCurrent ?? 0) >= dailyMin)
                                ts2.RecordCombinedTaskCompletion(task, DateTime.Today);
                            else
                                ts2.RemoveCombinedTaskCompletion(task, DateTime.Today);
                        }
                        else
                        {
                            task.IsCompleted = false;
                            task.CompletedAt = null;
                        }
                        repo2.UpdateTask(task);

                        if (task.CountTowardsParent && task.ParentTaskId.HasValue)
                            SyncParentTaskProgress(task.ParentTaskId.Value, delta, repo2);

                        SoundService.PlayCompletionSound();

                        if (task.GoalId.HasValue)
                            RecalcGoalProgressFromSubtasks(task.GoalId.Value, repo2);

                        EventAggregator.Instance.Publish("TaskCompleted");
                        LoadGoalsWithSections();
                    }
                    return;
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
                LoadGoalsWithSections();
            }
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

                // Also sync to goal if parent task belongs to a goal
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

        private void EditTaskFromGoal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TaskItem task)
            {
                var dialog = new TaskEditDialog(task) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true && dialog.ResultTask != null)
                {
                    dialog.ResultTask.Id = task.Id;
                    var repo = new TaskRepository();
                    repo.UpdateTask(dialog.ResultTask);
                    dialog.PersistSubtasks();
                    LoadGoalsWithSections();
                }
            }
        }

        private void DeleteTaskFromGoal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TaskItem task)
            {
                var repo = new TaskRepository();

                // Subtract from parent task before deleting
                if (task.CountTowardsParent && task.ParentTaskId.HasValue)
                {
                    var delta = -(task.QuantitativeCurrent ?? 0);
                    SyncParentTaskProgress(task.ParentTaskId.Value, delta, repo);
                }

                repo.SoftDeleteTask(task.Id);

                // Recalc goal after delete (deleted task is now excluded from sum)
                if (task.GoalId.HasValue)
                    RecalcGoalProgressFromSubtasks(task.GoalId.Value, repo);

                LoadGoalsWithSections();
            }
        }
    }
}
