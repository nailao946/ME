using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ME.Data;
using ME.Models;
using ME.Services;
using ME.Core;

namespace ME.Views
{
    public partial class FloatingWindow : Window
    {
        private readonly TimeTagRepository _tagRepo;
        private readonly SettingsRepository _settingsRepo;
        private readonly TaskRepository _taskRepo;
        private readonly TaskService _taskService;
        private bool _isClosingFromCode;
        private bool _isExpanded;

        // Drag state
        private bool _isDragging;
        private Point _dragStartPoint;
        private const double DragThreshold = 3.0;

        // Edge snap
        private const double SnapThreshold = 20.0;

        // Expand direction
        private enum ExpandDir { RightDown, LeftDown, RightUp, LeftUp }
        private ExpandDir _expandDir = ExpandDir.RightDown;
        private double _pillLeft, _pillTop;

        public FloatingWindow()
        {
            InitializeComponent();
            _tagRepo = new TimeTagRepository();
            _settingsRepo = new SettingsRepository();
            _taskRepo = new TaskRepository();
            _taskService = new TaskService();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SharedTimerService.TimerUpdated += OnTimerUpdated;
            SharedTimerService.RunningStateChanged += OnRunningStateChanged;
            ThemeService.ThemeChanged += OnThemeChanged;

            // Restore position
            var left = _settingsRepo.GetValue("FloatingWindowLeft", "");
            var top = _settingsRepo.GetValue("FloatingWindowTop", "");
            if (double.TryParse(left, out var l) && double.TryParse(top, out var t))
            {
                Left = l;
                Top = t;
            }
            else
            {
                Left = SystemParameters.PrimaryScreenWidth - 200;
                Top = SystemParameters.PrimaryScreenHeight - 120;
            }

            UpdateDisplay(SharedTimerService.IsRunning);
            LoadTagChips();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosingFromCode)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            SharedTimerService.TimerUpdated -= OnTimerUpdated;
            SharedTimerService.RunningStateChanged -= OnRunningStateChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            SavePosition();
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateDisplay(SharedTimerService.IsRunning);
                LoadTagChips();
                if (_isExpanded) LoadTaskList();
            }));
        }

        public void ClosePermanent()
        {
            _isClosingFromCode = true;
            Close();
        }

        private void SavePosition()
        {
            _settingsRepo.SetValue("FloatingWindowLeft", Left.ToString("F0"));
            _settingsRepo.SetValue("FloatingWindowTop", Top.ToString("F0"));
        }

        // ─── Expand / Collapse ─────────────────────────────────────────

        private void ToggleExpand()
        {
            if (_isExpanded)
                Collapse();
            else
                Expand();
        }

        private void Expand()
        {
            _isExpanded = true;

            // Save pill position
            _pillLeft = Left;
            _pillTop = Top;
            var pillWidth = ActualWidth;
            var pillHeight = ActualHeight;

            // Determine expand direction based on screen position
            var screen = SystemParameters.WorkArea;
            var centerX = Left + pillWidth / 2;
            var centerY = Top + pillHeight / 2;
            bool goLeft = centerX > screen.Left + screen.Width / 2;
            bool goUp = centerY > screen.Top + screen.Height / 2;

            _expandDir = goLeft
                ? (goUp ? ExpandDir.LeftUp : ExpandDir.LeftDown)
                : (goUp ? ExpandDir.RightUp : ExpandDir.RightDown);

            // Swap panels
            CollapsedPanel.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            LoadTaskList();
            StartDotPulse();

            // Set expanded size
            SizeToContent = SizeToContent.Manual;
            Width = 280;
            Height = 420;

            // Adjust position so expanded panel opens in the right direction
            switch (_expandDir)
            {
                case ExpandDir.LeftDown:
                    Left = _pillLeft + pillWidth - 280;
                    break;
                case ExpandDir.RightUp:
                    Top = _pillTop + pillHeight - 420;
                    break;
                case ExpandDir.LeftUp:
                    Left = _pillLeft + pillWidth - 280;
                    Top = _pillTop + pillHeight - 420;
                    break;
            }

            // Set scale center for animation origin (scale from the pill's corner)
            switch (_expandDir)
            {
                case ExpandDir.RightDown:
                    ContentScale.CenterX = 0; ContentScale.CenterY = 0;
                    break;
                case ExpandDir.LeftDown:
                    ContentScale.CenterX = 280; ContentScale.CenterY = 0;
                    break;
                case ExpandDir.RightUp:
                    ContentScale.CenterX = 0; ContentScale.CenterY = 420;
                    break;
                case ExpandDir.LeftUp:
                    ContentScale.CenterX = 280; ContentScale.CenterY = 420;
                    break;
            }

            // Animate scale in
            ContentScale.ScaleX = 0.3;
            ContentScale.ScaleY = 0.3;
            var scaleAnim = new DoubleAnimation(0.3, 1, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        private void Collapse()
        {
            var scaleAnim = new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(0.2))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            scaleAnim.Completed += (s, e) =>
            {
                _isExpanded = false;
                ExpandedPanel.Visibility = Visibility.Collapsed;
                CollapsedPanel.Visibility = Visibility.Visible;

                // Reset scale
                ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ContentScale.ScaleX = 1;
                ContentScale.ScaleY = 1;
                ContentScale.CenterX = 0;
                ContentScale.CenterY = 0;

                // Restore pill size
                SizeToContent = SizeToContent.WidthAndHeight;
                Width = double.NaN;
                Height = double.NaN;

                // Restore pill position so it stays in the same spot
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Left = _pillLeft;
                    Top = _pillTop;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        // ─── Pill click → expand ───────────────────────────────────────

        private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Begin drag tracking
            _isDragging = false;
            _dragStartPoint = e.GetPosition(this);
            MouseMove += Pill_MouseMove;
            MouseLeftButtonUp += Pill_MouseLeftButtonUp;
            CaptureMouse();
            e.Handled = true;
        }

        private void Pill_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (!_isDragging)
            {
                if (Math.Abs(pos.X - _dragStartPoint.X) > DragThreshold ||
                    Math.Abs(pos.Y - _dragStartPoint.Y) > DragThreshold)
                {
                    _isDragging = true;
                }
            }

            if (_isDragging)
            {
                var screenPos = PointToScreen(pos);
                Left = screenPos.X - _dragStartPoint.X;
                Top = screenPos.Y - _dragStartPoint.Y;
            }
        }

        private void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            MouseMove -= Pill_MouseMove;
            MouseLeftButtonUp -= Pill_MouseLeftButtonUp;
            ReleaseMouseCapture();

            if (_isDragging)
            {
                _isDragging = false;
                SnapToEdge();
            }
            else
            {
                // It was a click, not a drag → expand
                ToggleExpand();
            }
        }

        // ─── Header click → collapse (from expanded state) ─────────────

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Collapse();
            e.Handled = true;
        }

        // ─── Edge snapping ─────────────────────────────────────────────

        private void SnapToEdge()
        {
            var screen = SystemParameters.WorkArea;
            var w = ActualWidth;
            var h = ActualHeight;

            if (Left < screen.Left + SnapThreshold)
                Left = screen.Left;
            else if (Left + w > screen.Right - SnapThreshold)
                Left = screen.Right - w;

            if (Top < screen.Top + SnapThreshold)
                Top = screen.Top;
            else if (Top + h > screen.Bottom - SnapThreshold)
                Top = screen.Bottom - h;
        }

        // ─── Right-click context menu ──────────────────────────────────

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowContextMenu();
        }

        private void ShowContextMenu()
        {
            Brush cardBrush, textBrush, primaryBrush, hoverBrush;
            try { cardBrush = (Brush)FindResource("CardBrush"); }
            catch { cardBrush = new SolidColorBrush(Color.FromRgb(44, 44, 46)); }
            try { textBrush = (Brush)FindResource("TextBrush"); }
            catch { textBrush = Brushes.White; }
            try { primaryBrush = (Brush)FindResource("PrimaryBrush"); }
            catch { primaryBrush = new SolidColorBrush(Color.FromRgb(0, 122, 255)); }
            hoverBrush = new SolidColorBrush(Color.FromArgb(30, 0, 122, 255));

            var menuItemStyle = new Style(typeof(MenuItem));
            menuItemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, cardBrush));
            menuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, textBrush));
            menuItemStyle.Setters.Add(new Setter(MenuItem.BorderBrushProperty, Brushes.Transparent));
            menuItemStyle.Setters.Add(new Setter(MenuItem.BorderThicknessProperty, new Thickness(1)));
            menuItemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 6, 10, 6)));
            var trigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            trigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, hoverBrush));
            trigger.Setters.Add(new Setter(MenuItem.ForegroundProperty, textBrush));
            trigger.Setters.Add(new Setter(MenuItem.BorderBrushProperty, primaryBrush));
            menuItemStyle.Triggers.Add(trigger);

            var subMenuItemStyle = new Style(typeof(MenuItem));
            subMenuItemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, cardBrush));
            subMenuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, textBrush));
            subMenuItemStyle.Setters.Add(new Setter(MenuItem.BorderBrushProperty, Brushes.Transparent));
            subMenuItemStyle.Setters.Add(new Setter(MenuItem.BorderThicknessProperty, new Thickness(1)));
            subMenuItemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 5, 10, 5)));
            var subTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            subTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, hoverBrush));
            subTrigger.Setters.Add(new Setter(MenuItem.ForegroundProperty, textBrush));
            subTrigger.Setters.Add(new Setter(MenuItem.BorderBrushProperty, primaryBrush));
            subMenuItemStyle.Triggers.Add(subTrigger);

            var menu = new ContextMenu
            {
                Background = cardBrush,
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                Foreground = textBrush,
                Padding = new Thickness(4),
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                PlacementTarget = this
            };
            menu.Resources[typeof(MenuItem)] = menuItemStyle;

            var tags = _tagRepo.GetAllTags();
            var runningTagId = SharedTimerService.IsRunning ? SharedTimerService.SelectedTagId : -1;

            // ── Stop current (main menu, not submenu) ──
            if (SharedTimerService.IsRunning)
            {
                var currentTag = _tagRepo.GetTagById(runningTagId);
                var stopItem = new MenuItem
                {
                    Header = $"■ 停止 [{currentTag?.Name ?? "未知"}]",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"))
                };
                stopItem.Click += (s, ev) => SharedTimerService.StopCurrent();
                menu.Items.Add(stopItem);
            }

            // ── 计时器 submenu (tags only) ──
            var timerMenu = new MenuItem { Header = "计时器" };
            timerMenu.Resources[typeof(MenuItem)] = subMenuItemStyle;

            foreach (var tag in tags)
            {
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }

                var isRunning = SharedTimerService.IsRunning && runningTagId == tag.Id;
                var item = new MenuItem
                {
                    Header = (isRunning ? "● " : "") + tag.Name,
                    Icon = new Border
                    {
                        Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(tagColor),
                        Margin = new Thickness(0, 0, 6, 0)
                    }
                };
                var capturedTagId = tag.Id;
                item.Click += (s, ev) =>
                {
                    if (SharedTimerService.IsRunning && SharedTimerService.SelectedTagId == capturedTagId)
                        SharedTimerService.StopCurrent();
                    else
                        SharedTimerService.StartWithTag(capturedTagId);
                };
                timerMenu.Items.Add(item);
            }

            menu.Items.Add(timerMenu);

            // Show main window
            var showItem = new MenuItem { Header = "显示主窗口" };
            showItem.Click += (s, ev) =>
            {
                var main = Application.Current.MainWindow;
                if (main != null)
                {
                    ((MainWindow)main).ShowWithAnimation();
                    main.WindowState = WindowState.Normal;
                    main.Activate();
                }
            };
            menu.Items.Add(showItem);

            // Hide
            var hideItem = new MenuItem { Header = "隐藏悬浮窗" };
            hideItem.Click += (s, ev) => Hide();
            menu.Items.Add(hideItem);

            menu.IsOpen = true;
        }

        // ─── Timer events ──────────────────────────────────────────────

        private void OnTimerUpdated(string timeStr, string tagName, string tagColor)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TimerText.Text = timeStr;
                ExpTimerText.Text = timeStr;
                TagNameText.Text = tagName;
                ExpTagNameText.Text = tagName;
                SetTagDotColor(tagColor);
            }));
        }

        private void OnRunningStateChanged(bool isRunning)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateDisplay(isRunning)));
        }

        private void UpdateDisplay(bool isRunning)
        {
            if (isRunning)
            {
                var tag = _tagRepo.GetTagById(SharedTimerService.SelectedTagId);
                var name = tag?.Name ?? "计时中";
                var color = tag?.Color ?? "#808080";
                TagNameText.Text = name;
                ExpTagNameText.Text = name;
                TimerText.Text = "00:00:00";
                ExpTimerText.Text = "00:00:00";
                SetTagDotColor(color);
            }
            else
            {
                TagNameText.Text = "未计时";
                ExpTagNameText.Text = "未计时";
                TimerText.Text = "00:00:00";
                ExpTimerText.Text = "00:00:00";
                SetTagDotColor("#808080");
            }
            if (_isExpanded) LoadTaskList();
            UpdateTagChipStates();
        }

        private void SetTagDotColor(string colorStr)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorStr);
                var brush = new SolidColorBrush(color);
                TagDot.Background = brush;
                ExpTagDot.Background = brush;
                TagDotGlow.Color = color;
            }
            catch { }
        }

        // ─── Dot pulse animation ───────────────────────────────────────

        private void StartDotPulse()
        {
            if (!SharedTimerService.IsRunning) return;
            var anim = new DoubleAnimation(0, 6, TimeSpan.FromSeconds(0.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var opacityAnim = new DoubleAnimation(0.5, 0, TimeSpan.FromSeconds(0.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            TagDotGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, anim);
            TagDotGlow.BeginAnimation(DropShadowEffect.OpacityProperty, opacityAnim);
        }

        // ─── Task list ─────────────────────────────────────────────────

        private void LoadTaskList()
        {
            TaskListPanel.Children.Clear();

            var tasks = _taskRepo.GetTodayTasks();
            if (tasks.Count == 0)
            {
                TaskListPanel.Children.Add(new TextBlock
                {
                    Text = "今天没有待办任务",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                });
                return;
            }

            foreach (var task in tasks)
            {
                var row = CreateTaskRow(task);
                TaskListPanel.Children.Add(row);
            }
        }

        private Border CreateTaskRow(TaskItem task)
        {
            Brush textBrush, secondaryBrush, cardBrush;
            try
            {
                textBrush = (Brush)FindResource("TextBrush");
                secondaryBrush = (Brush)FindResource("SecondaryTextBrush");
                cardBrush = (Brush)FindResource("CardBrush");
            }
            catch
            {
                textBrush = Brushes.White;
                secondaryBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178));
                cardBrush = new SolidColorBrush(Color.FromRgb(44, 44, 46));
            }

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                Tag = task.Id
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            // Checkbox
            bool displayDone = _taskService.IsTaskCompletedForDisplay(task, DateTime.Today);
            var cb = new CheckBox
            {
                IsChecked = displayDone,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Tag = task.Id
            };
            cb.Checked += TaskCheckBox_Changed;
            cb.Unchecked += TaskCheckBox_Changed;
            Grid.SetColumn(cb, 0);

            // Title
            var title = new TextBlock
            {
                Text = task.Title,
                FontSize = 13,
                Foreground = displayDone ? secondaryBrush : textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (displayDone)
                title.TextDecorations.Add(TextDecorations.Strikethrough[0]);
            Grid.SetColumn(title, 1);

            // Task type badge
            var badge = CreateTaskBadge(task);
            if (badge != null)
            {
                Grid.SetColumn(badge, 2);
                grid.Children.Add(badge);
            }

            grid.Children.Add(cb);
            grid.Children.Add(title);

            border.Child = grid;

            // Double-click to start timer for this task's tag
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    // Find the task's associated tag (if any)
                    // For now, just toggle expand back
                    Collapse();
                }
            };

            return border;
        }

        private Border CreateTaskBadge(TaskItem task)
        {
            string text;
            Color color;

            switch (task.Type)
            {
                case TaskType.Recurring:
                    var count = _taskService.GetCustomRecurringCountOnDate(task.Id, DateTime.Today);
                    var target = task.RecurringTargetCount ?? 1;
                    text = $"{count}/{target}";
                    color = Color.FromRgb(0, 122, 255); // blue
                    break;
                case TaskType.Quantitative:
                    var cur = task.QuantitativeCurrent ?? 0;
                    var tgt = task.QuantitativeTarget ?? 0;
                    text = $"{cur}/{tgt}{task.QuantitativeUnit ?? ""}";
                    color = Color.FromRgb(52, 199, 89); // green
                    break;
                case TaskType.Periodic:
                    text = "定期";
                    color = Color.FromRgb(255, 149, 0); // orange
                    break;
                default:
                    return null;
            }

            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private void TaskCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is int taskId)
            {
                var task = _taskRepo.GetTaskById(taskId);
                if (task == null) return;

                if (cb.IsChecked == true)
                {
                    if (task.Type == TaskType.Quantitative && task.QuantitativeMode.HasValue)
                    {
                        task.QuantitativeCurrent = (task.QuantitativeCurrent ?? 0) + 1;
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
                            // Record recurring completion so other views see today's progress
                            _taskService.RecordCombinedTaskCompletion(task, DateTime.Today);
                        }
                        else if (!isCombined && task.QuantitativeDailyMin.HasValue && (task.QuantitativeCurrent ?? 0) >= task.QuantitativeDailyMin.Value)
                        {
                            task.IsCompleted = true;
                            task.CompletedAt = DateTime.Now;
                        }
                        else
                        {
                            task.IsCompleted = false;
                            task.CompletedAt = null;
                            cb.IsChecked = false;
                        }
                    }
                    else
                    {
                        task.IsCompleted = true;
                        task.CompletedAt = DateTime.Now;
                        if (task.Type == TaskType.Recurring)
                        {
                            _taskService.RecordCustomRecurringCompletion(task.Id, DateTime.Today);
                        }
                    }
                }
                else
                {
                    if (task.Type == TaskType.Quantitative && task.QuantitativeMode.HasValue)
                    {
                        task.QuantitativeCurrent = Math.Max(0, (task.QuantitativeCurrent ?? 0) - 1);
                        task.IsCompleted = false;
                        task.CompletedAt = null;
                        if (task.RecurringPattern.HasValue)
                            _taskService.RemoveCombinedTaskCompletion(task, DateTime.Today);
                    }
                    else
                    {
                        task.IsCompleted = false;
                        task.CompletedAt = null;
                        if (task.Type == TaskType.Recurring)
                            _taskService.RemoveCompletion(task.Id, DateTime.Today);
                    }
                }

                _taskRepo.UpdateTask(task);

                if (task.GoalId.HasValue)
                {
                    var repo2 = new TaskRepository();
                    var goalRepo = new GoalRepository();
                    var goal = goalRepo.GetAllGoals().Find(g => g.Id == task.GoalId.Value && !g.IsDeleted);
                    if (goal != null)
                    {
                        var ts = new TaskService();
                        var (progress, _) = ts.CalcGoalProgress(task.GoalId.Value);
                        goal.Progress = progress;
                        goalRepo.UpdateGoal(goal);
                    }
                }

                SoundService.PlayCompletionSound();
                EventAggregator.Instance.Publish("TaskCompleted");
                LoadTaskList();
            }
        }

        // ─── Tag chips ─────────────────────────────────────────────────

        private void LoadTagChips()
        {
            TagChipsPanel.Children.Clear();
            var tags = _tagRepo.GetAllTags();

            foreach (var tag in tags)
            {
                var chip = CreateTagChip(tag);
                TagChipsPanel.Children.Add(chip);
            }
        }

        private Border CreateTagChip(TimeTag tag)
        {
            Brush textBrush;
            try { textBrush = (Brush)FindResource("TextBrush"); }
            catch { textBrush = Brushes.White; }

            var isRunning = SharedTimerService.IsRunning && SharedTimerService.SelectedTagId == tag.Id;
            Color tagColor;
            try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
            catch { tagColor = Color.FromRgb(128, 128, 128); }

            var chip = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = isRunning
                    ? new SolidColorBrush(tagColor)
                    : new SolidColorBrush(Color.FromArgb(30, tagColor.R, tagColor.G, tagColor.B)),
                BorderBrush = isRunning
                    ? new SolidColorBrush(tagColor)
                    : new SolidColorBrush(Color.FromArgb(60, tagColor.R, tagColor.G, tagColor.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Tag = tag.Id,
                Child = new TextBlock
                {
                    Text = (isRunning ? "● " : "") + tag.Name,
                    FontSize = 11,
                    Foreground = isRunning ? Brushes.White : textBrush,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            chip.MouseLeftButtonDown += (s, e) =>
            {
                if (SharedTimerService.IsRunning && SharedTimerService.SelectedTagId == tag.Id)
                {
                    SharedTimerService.StopCurrent();
                }
                else
                {
                    SharedTimerService.StartWithTag(tag.Id);
                }
                LoadTagChips();
                e.Handled = true;
            };

            return chip;
        }

        private void UpdateTagChipStates()
        {
            if (_isExpanded) LoadTagChips();
        }
    }
}
