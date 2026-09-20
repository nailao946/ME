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
    /// <summary>
    /// 任务树与任务卡片渲染。
    /// （从 TasksView.xaml.cs 拆分而来，降低单文件体积）
    /// </summary>
    public partial class TasksView : System.Windows.Controls.UserControl
    {
        private void BuildTaskTree(StackPanel panel, List<TaskItem> mainTasks, Dictionary<int, List<TaskItem>> subtasksMap, Dictionary<int, string> tagColorMap, bool isCompleted, List<TimeTag> timeTags)
        {

            foreach (var task in mainTasks)
            {
                var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                string tagColor = GetTagColorForTask(task, tagColorMap);
                string tagName = GetTagNameForTask(task);

                // Main task card (matching GoalsView style)
                var card = CreateTaskCard(task, isCompleted, tagColor, tagName, timeTags);
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
                        var subCard = CreateSubtaskCard(sub, tagColor, subtasks, subtaskPanel, timeTags);
                        subtaskPanel.Children.Add(subCard);
                    }
                    subtaskExpander.Content = subtaskPanel;
                    wrapper.Children.Add(subtaskExpander);
                }

                panel.Children.Add(wrapper);
            }
        }

        private Border CreateTaskCard(TaskItem task, bool isCompleted, string tagColor, string tagName, List<TimeTag> timeTags)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Cursor = isCompleted ? Cursors.Hand : Cursors.SizeAll,
                Tag = task
            };

            // 边框跟随标签颜色（时间标签优先，其次目标标签）
            var timeTag = FindTimeTag(task.TimeTagId, timeTags);
            var borderHex = timeTag?.Color ?? tagColor;
            var borderBrush = TagBorderBrush(borderHex);
            if (borderBrush != null)
            {
                card.BorderBrush = borderBrush;
                card.BorderThickness = new Thickness(1.2);
            }

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
            // 徽章并排：目标标签 + 时间标签（如「考研 数学」任务名）
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
            if (timeTag != null)
            {
                var ttColor = ParseHexColor(timeTag.Color) ?? Color.FromRgb(0, 122, 255);
                namePanel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(ttColor),
                    Child = new TextBlock { Text = timeTag.Name, FontSize = 10, Foreground = Brushes.White }
                });
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = task.Title, FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = isCompleted ? (SolidColorBrush)FindResource("SecondaryTextBrush") : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = isCompleted ? TextDecorations.Strikethrough : null
            });
            textPanel.Children.Add(namePanel);

            // 过去完成的任务显示完成日期
            if (TryGetCompletionDate(task, out var pastDoneDate) && pastDoneDate < _selectedDate.Date)
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = $"完成于 {pastDoneDate:yyyy-MM-dd}", FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

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
                    // 量化每日目标口径提示：今日还需比上一日最终值多 N
                    var dailyHint = QuantDailyHint(task);
                    if (!string.IsNullOrEmpty(dailyHint))
                        infoPanel.Children.Add(new TextBlock
                        {
                            Text = " " + dailyHint, FontSize = 10,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
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
                // 时间标签的累计用时（与目标管理界面口径一致），显示在截止时间前
                if (timeTag != null)
                {
                    var tagMins = new TimeRecordRepository().GetAllRecords()
                        .Where(r => r.TagId == timeTag.Id).Sum(r => r.Duration.TotalMinutes);
                    if (tagMins > 0)
                    {
                        var th = (int)(tagMins / 60);
                        var tm = (int)(tagMins % 60);
                        infoPanel.Children.Add(new TextBlock
                        {
                            Text = $" ⌚ {th}h{tm:D2}m", FontSize = 10,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                            Margin = new Thickness(8, 0, 0, 0)
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
                    Background = ME.Services.ThemeService.Solid("BackgroundBrush"),
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

            // Buttons (icons, matching GoalsView)
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnPanel.Children.Add(CreateTaskButton("\uE946", "打卡详情（哪些天完成/未完成）", TaskDetail_Click, task));
            btnPanel.Children.Add(CreateTaskButton("\uE70F", "编辑", EditTask_Click, task));
            btnPanel.Children.Add(CreateTaskButton("\uE710", "添加子任务", AddSubtaskToTask_Click, task));
            btnPanel.Children.Add(CreateTaskButton("\uE74D", "删除", DeleteTask_Click, task));
            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            card.Child = grid;
            return card;
        }

        private Border CreateSubtaskCard(TaskItem task, string tagColor, List<TaskItem> subtaskList = null, StackPanel subtaskPanel = null, List<TimeTag> timeTags = null)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = task
            };

            // 边框跟随标签颜色（时间标签优先，其次目标标签）
            var subTimeTag = FindTimeTag(task.TimeTagId, timeTags);
            var subBorder = TagBorderBrush(subTimeTag?.Color ?? tagColor);
            if (subBorder != null)
            {
                card.BorderBrush = subBorder;
                card.BorderThickness = new Thickness(1.2);
            }

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
                    Background = ME.Services.ThemeService.Solid("BackgroundBrush"),
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
                    Background = ME.Services.ThemeService.Solid("BackgroundBrush"),
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
            btnPanel.Children.Add(CreateTaskButton("\uE946", "打卡详情（哪些天完成/未完成）", TaskDetail_Click, task));
            btnPanel.Children.Add(CreateTaskButton("\uE70F", "编辑", EditTask_Click, task));
            btnPanel.Children.Add(CreateTaskButton("\uE74D", "删除", DeleteTask_Click, task));
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

        /// <summary>图标小按钮（Segoe MDL2 Assets 字形 + 悬停提示）</summary>
        private Button CreateTaskButton(string glyph, string tooltip, RoutedEventHandler handler, TaskItem task)
        {
            var btn = new Button
            {
                Content = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 0),
                ToolTip = tooltip, Tag = task
            };
            btn.Click += handler;
            return btn;
        }

        private void TaskDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TaskItem task)
            {
                var win = new TaskDetailWindow(task) { Owner = Window.GetWindow(this) };
                win.ShowDialog();
            }
        }

    }
}
