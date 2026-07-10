using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_APPWINDOW = 0x40000;

        public FloatingWindow()
        {
            InitializeComponent();
            _tagRepo = new TimeTagRepository();
            _settingsRepo = new SettingsRepository();
            _taskRepo = new TaskRepository();
            _taskService = new TaskService();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SharedTimerService.TimerUpdated += OnTimerUpdated;
            SharedTimerService.RunningStateChanged += OnRunningStateChanged;
            SharedTimerService.PausedStateChanged += OnPausedStateChanged;
            ThemeService.ThemeChanged += OnThemeChanged;

            var pomo = SharedPomodoroService.Instance;
            pomo.TimerUpdated += OnTimerUpdated;
            pomo.StateChanged += OnPomoStateChanged;
            pomo.PhaseChanged += OnPomoPhaseChanged;
            pomo.WorkPhaseEnded += OnPomoWorkPhaseEnded;

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
            var pomoInit = SharedPomodoroService.Instance;
            if (pomoInit.Mode == UnifiedTimerMode.Pomodoro && pomoInit.CycleCount > 0)
                UpdateCycleDisplay(pomoInit.CycleCount);
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
            SharedTimerService.PausedStateChanged -= OnPausedStateChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            var pomo = SharedPomodoroService.Instance;
            pomo.TimerUpdated -= OnTimerUpdated;
            pomo.StateChanged -= OnPomoStateChanged;
            pomo.PhaseChanged -= OnPomoPhaseChanged;
            pomo.WorkPhaseEnded -= OnPomoWorkPhaseEnded;
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
            var pomo = SharedPomodoroService.Instance;
            var runningTagId = (SharedTimerService.IsRunning || SharedTimerService.IsPaused) ? SharedTimerService.SelectedTagId : -1;
            var pomoActive = pomo.State != PomodoroState.Idle;

            // ── Stop current (main menu, not submenu) ──
            if (SharedTimerService.IsRunning || SharedTimerService.IsPaused || pomoActive)
            {
                var stopText = pomoActive ? "停止 (番茄钟)" :
                    (SharedTimerService.IsRunning || SharedTimerService.IsPaused) ? $"■ 停止" : "";
                var stopItem = new MenuItem
                {
                    Header = stopText,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"))
                };
                stopItem.Click += (s, ev) =>
                {
                    if (pomoActive)
                    {
                        if (pomo.Mode == UnifiedTimerMode.Simple) SharedTimerService.StopCurrent();
                        pomo.Stop();
                    }
                    if (SharedTimerService.IsRunning || SharedTimerService.IsPaused)
                        SharedTimerService.StopCurrent();
                };
                menu.Items.Add(stopItem);
            }

            // ── 计时器 submenu (tags only) ──
            var timerMenu = new MenuItem { Header = "正计时" };
            timerMenu.Resources[typeof(MenuItem)] = subMenuItemStyle;

            foreach (var tag in tags)
            {
                Color tagColor;
                try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
                catch { tagColor = Color.FromRgb(128, 128, 128); }

                var isRunning = pomo.Mode == UnifiedTimerMode.Simple
                    && pomo.State != PomodoroState.Idle
                    && pomo.SelectedTagId == tag.Id;
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
                var captured = tag;
                item.Click += (s, ev) =>
                {
                    if (pomo.State != PomodoroState.Idle && pomo.SelectedTagId == captured.Id)
                    {
                        SharedTimerService.StopCurrent();
                        pomo.Stop();
                    }
                    else
                    {
                        if (pomo.State != PomodoroState.Idle) pomo.Stop();
                        if (SharedTimerService.IsRunning) SharedTimerService.StopCurrent();
                        pomo.SelectedTagId = captured.Id;
                        pomo.SelectedTagName = captured.Name;
                        pomo.SelectedTagColor = captured.Color;
                        SharedTimerService.StartWithTag(captured.Id);
                        pomo.Start();
                    }
                };
                timerMenu.Items.Add(item);
            }

            menu.Items.Add(timerMenu);

            // Separator
            menu.Items.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                Margin = new Thickness(4, 2, 4, 2)
            });

            // ── Timer Mode switch ──
            var modeItem = new MenuItem
            {
                Header = pomo.Mode == UnifiedTimerMode.Pomodoro ? "切换为正计时" : "切换为番茄模式"
            };
            modeItem.Click += (s, ev) =>
            {
                if (pomo.State != PomodoroState.Idle)
                {
                    SharedTimerService.StopCurrent();
                    pomo.Stop();
                }
                pomo.Mode = pomo.Mode == UnifiedTimerMode.Pomodoro
                    ? UnifiedTimerMode.Simple
                    : UnifiedTimerMode.Pomodoro;
                if (pomo.Mode == UnifiedTimerMode.Simple)
                {
                    pomo.Current = TimeSpan.Zero;
                    TimerText.Text = "00:00:00";
                    ExpTimerText.Text = "00:00:00";
                }
                else
                {
                    pomo.Phase = PomodoroPhase.Work;
                    pomo.Current = TimeSpan.FromMinutes(pomo.WorkMinutes);
                    TimerText.Text = pomo.FormatTime();
                    ExpTimerText.Text = pomo.FormatTime();
                }
            };
            menu.Items.Add(modeItem);

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

            // Separator
            menu.Items.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                Margin = new Thickness(4, 2, 4, 2)
            });

            // Exit
            var exitItem = new MenuItem
            {
                Header = "退出",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48))
            };
            exitItem.Click += (s, ev) =>
            {
                var main = Application.Current.MainWindow;
                if (main != null) main.Close();
            };
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        }

        // ─── Timer events ──────────────────────────────────────────────

        // SharedTimerService callback
        private void OnTimerUpdated(string timeStr, string tagName, string tagColor)
        {
            if (SharedPomodoroService.Instance.Mode == UnifiedTimerMode.Pomodoro) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TimerText.Text = timeStr;
                ExpTimerText.Text = timeStr;
                TagNameText.Text = tagName;
                ExpTagNameText.Text = tagName;
                SetTagDotColor(tagColor);
            }));
        }

        // PomodoroService callback
        private void OnTimerUpdated(string time, UnifiedTimerMode mode)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TimerText.Text = time;
                ExpTimerText.Text = time;
                if (mode == UnifiedTimerMode.Pomodoro)
                {
                    var pomo = SharedPomodoroService.Instance;
                    var tagName = !string.IsNullOrEmpty(pomo.SelectedTagName) && pomo.SelectedTagName != "未计时"
                        ? "🍅-" + pomo.SelectedTagName : "🍅";
                    TagNameText.Text = tagName;
                    ExpTagNameText.Text = tagName;
                    SetTagDotColor(pomo.SelectedTagColor);
                }
            }));
        }

        private void OnPomoPhaseChanged(PomodoroPhase phase, int total, int cycle)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateCycleDisplay(cycle)));
        }

        private void UpdateCycleDisplay(int cycle)
        {
            var pomo = SharedPomodoroService.Instance;
            if (pomo.Mode == UnifiedTimerMode.Pomodoro && cycle > 0)
                ExpCycleText.Text = $"本轮 {cycle}/{pomo.BeforeLongBreak} 个";
            else
                ExpCycleText.Text = "";
        }

        private void OnPomoStateChanged(PomodoroState state)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool active = state != PomodoroState.Idle;
                ExpPauseBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                ExpPauseBtn.Content = state == PomodoroState.Paused ? "▶" : "⏸";
                ExpPauseBtn.ToolTip = state == PomodoroState.Paused ? "继续" : "暂停";
                if (state == PomodoroState.Idle && SharedTimerService.IsRunning)
                    SharedTimerService.StopCurrent();
                if (state == PomodoroState.Idle)
                    ExpCycleText.Text = "";
            }));
        }

        private void OnPomoWorkPhaseEnded()
        {
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

        private void OnRunningStateChanged(bool isRunning)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateDisplay(isRunning)));
        }

        private void ExpPauseBtn_Click(object sender, RoutedEventArgs e)
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

        private void OnPausedStateChanged(bool isPaused)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (isPaused)
                {
                    TagNameText.Text = "暂停中";
                    ExpTagNameText.Text = "暂停中";
                    ExpPauseBtn.Content = "▶";
                    ExpPauseBtn.ToolTip = "继续";
                }
                else if (SharedTimerService.IsRunning)
                {
                    var tag = _tagRepo.GetTagById(SharedTimerService.SelectedTagId);
                    var name = tag?.Name ?? "计时中";
                    TagNameText.Text = name;
                    ExpTagNameText.Text = name;
                    ExpPauseBtn.Content = "⏸";
                    ExpPauseBtn.ToolTip = "暂停";
                }
            }));
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
                ExpPauseBtn.Visibility = Visibility.Visible;
                ExpPauseBtn.Content = "⏸";
                ExpPauseBtn.ToolTip = "暂停";
            }
            else
            {
                TagNameText.Text = "未计时";
                ExpTagNameText.Text = "未计时";
                TimerText.Text = "00:00:00";
                ExpTimerText.Text = "00:00:00";
                SetTagDotColor("#808080");
                ExpPauseBtn.Visibility = Visibility.Collapsed;
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

            var allTasks = _taskRepo.GetAllTasks();
            var mainTasks = new List<TaskItem>();
            var subtasksMap = new Dictionary<int, List<TaskItem>>();
            var today = DateTime.Today;

            foreach (var task in allTasks)
            {
                if (task.IsDeleted || task.IsCompleted) continue;

                if (task.Type == TaskType.Quantitative && task.RecurringPattern.HasValue)
                {
                    if (!_taskService.ShouldShowRecurringTaskOnDate(task, today))
                        continue;
                }
                else if (task.Type == TaskType.Recurring && task.RecurringPattern.HasValue)
                {
                    if (!_taskService.ShouldShowRecurringTaskOnDate(task, today))
                        continue;
                }
                else if (task.Type == TaskType.OneTime || task.Type == TaskType.Periodic)
                {
                    if (task.StartDate.HasValue && task.StartDate.Value.Date > today) continue;
                    if (task.EndDate.HasValue && task.EndDate.Value.Date < today) continue;
                }

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

            if (mainTasks.Count == 0)
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

            foreach (var task in mainTasks)
            {
                var row = CreateTaskRow(task);
                TaskListPanel.Children.Add(row);

                // Add subtasks nested under parent
                if (subtasksMap.ContainsKey(task.Id))
                {
                    foreach (var sub in subtasksMap[task.Id])
                    {
                        var subRow = CreateSubtaskRow(sub);
                        TaskListPanel.Children.Add(subRow);
                    }
                }
            }
        }

        private Border CreateSubtaskRow(TaskItem task)
        {
            Brush textBrush, secondaryBrush;
            try { textBrush = (Brush)FindResource("TextBrush"); secondaryBrush = (Brush)FindResource("SecondaryTextBrush"); }
            catch { textBrush = Brushes.White; secondaryBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178)); }

            bool displayDone = _taskService.IsTaskCompletedForDisplay(task, DateTime.Today);

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(12, 128, 128, 128)),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(24, 0, 0, 3),
                Tag = task.Id
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cb = new CheckBox
            {
                IsChecked = displayDone,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = task.Id
            };
            cb.Checked += TaskCheckBox_Changed;
            cb.Unchecked += TaskCheckBox_Changed;
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            var title = new TextBlock
            {
                Text = task.Title,
                FontSize = 12,
                Foreground = displayDone ? secondaryBrush : textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (displayDone)
                title.TextDecorations.Add(TextDecorations.Strikethrough[0]);
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            border.Child = grid;
            return border;
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

            var pomo = SharedPomodoroService.Instance;
            var isActive = pomo.Mode == UnifiedTimerMode.Simple
                && pomo.State != PomodoroState.Idle
                && pomo.SelectedTagId == tag.Id;
            Color tagColor;
            try { tagColor = (Color)ColorConverter.ConvertFromString(tag.Color); }
            catch { tagColor = Color.FromRgb(128, 128, 128); }

            var chip = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = isActive
                    ? new SolidColorBrush(tagColor)
                    : new SolidColorBrush(Color.FromArgb(30, tagColor.R, tagColor.G, tagColor.B)),
                BorderBrush = isActive
                    ? new SolidColorBrush(tagColor)
                    : new SolidColorBrush(Color.FromArgb(60, tagColor.R, tagColor.G, tagColor.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Tag = tag.Id,
                Child = new TextBlock
                {
                    Text = (isActive ? "● " : "") + tag.Name,
                    FontSize = 11,
                    Foreground = isActive ? Brushes.White : textBrush,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            chip.MouseLeftButtonDown += (s, e) =>
            {
                if (pomo.State != PomodoroState.Idle && pomo.SelectedTagId == tag.Id)
                {
                    SharedTimerService.StopCurrent();
                    pomo.Stop();
                }
                else
                {
                    if (pomo.State != PomodoroState.Idle) pomo.Stop();
                    if (SharedTimerService.IsRunning) SharedTimerService.StopCurrent();
                    pomo.SelectedTagId = tag.Id;
                    pomo.SelectedTagName = tag.Name;
                    pomo.SelectedTagColor = tag.Color;
                    SharedTimerService.StartWithTag(tag.Id);
                    pomo.Start();
                }
                LoadTagChips();
                e.Handled = true;
            };

            return chip;
        }

        private void UpdateTagChipStates()
        {
            var pomo = SharedPomodoroService.Instance;
            Brush textBrush;
            try { textBrush = (Brush)FindResource("TextBrush"); }
            catch { textBrush = Brushes.White; }

            foreach (var child in TagChipsPanel.Children)
            {
                if (child is Border chip && chip.Tag is int tagId)
                {
                    var isActive = pomo.Mode == UnifiedTimerMode.Simple
                        && pomo.State != PomodoroState.Idle
                        && pomo.SelectedTagId == tagId;
                    Color tagColor = Color.FromRgb(128, 128, 128);
                    string tagName = "";
                    try
                    {
                        var tag = _tagRepo.GetTagById(tagId);
                        if (tag != null)
                        {
                            tagColor = (Color)ColorConverter.ConvertFromString(tag.Color);
                            tagName = tag.Name;
                        }
                    }
                    catch { }

                    chip.Background = isActive
                        ? new SolidColorBrush(tagColor)
                        : new SolidColorBrush(Color.FromArgb(30, tagColor.R, tagColor.G, tagColor.B));
                    chip.BorderBrush = isActive
                        ? new SolidColorBrush(tagColor)
                        : new SolidColorBrush(Color.FromArgb(60, tagColor.R, tagColor.G, tagColor.B));
                    if (chip.Child is TextBlock tb)
                    {
                        tb.Text = (isActive ? "● " : "") + tagName;
                        tb.Foreground = isActive ? Brushes.White : textBrush;
                    }
                }
            }
        }
    }
}
