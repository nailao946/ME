using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ME.Data;
using ME.Models;
using ME.Services;
using ME.ViewModels;
using ME.Views;
using Forms = System.Windows.Forms;

namespace ME
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _vm;
        private GoalsView _goalsView;
        private TasksView _tasksView;
        private CalendarView _calendarView;
        private ReviewView _reviewView;
        private SettingsView _settingsView;
        private CustomModulesView _customModulesView;
        private TimeTrackView _timeTrackView;
        private HealthView _healthView;
        private UserControl _currentView;
        private Forms.NotifyIcon _notifyIcon;
        private bool _isDarkTheme;
        private FloatingWindow _floatingWindow;
        private Forms.ToolStripMenuItem _floatingMenuItem;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainWindowViewModel();
            DataContext = _vm;

            // AllowsTransparency 窗口无法用 WindowChrome，用 WM_NCHITTEST 钩子实现可靠边缘拉伸
            SourceInitialized += (s, e) =>
            {
                _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                _hwndSource?.AddHook(WindowProc);
            };

            _isDarkTheme = ThemeService.IsDarkMode();
            UpdateThemeButton();
            SetupTrayIcon();
            ApplyWindowBorderColor();
            InitFloatingWindow();

            // 启动自动同步（设置里可关；后台运行不阻塞启动，完成后通过事件刷新设置页状态）
            _ = GitHubSyncService.AutoSyncOnStartupAsync();

            ThemeService.ThemeChanged += (theme) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _isDarkTheme = ThemeService.IsDarkMode();
                    UpdateThemeButton();
                    ApplyWindowBorderColor();
                    RebuildTrayMenu();
                });
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeService.Initialize();
            UpdateView(0);
        }

        private void UpdateThemeButton()
        {
            ThemeToggleBtn.Content = _isDarkTheme ? "☀️" : "🌙";
        }

        private void ApplyWindowBorderColor()
        {
            try
            {
                var settingsRepo = new SettingsRepository();
                var colorStr = settingsRepo.GetValue(SettingsKeys.WindowBorderColor, "#007AFF");
                if (WindowBorder != null)
                {
                    if (colorStr == "NONE")
                    {
                        WindowBorder.BorderThickness = new Thickness(0);
                    }
                    else
                    {
                        WindowBorder.BorderThickness = new Thickness(1);
                        WindowBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr));
                    }
                }
            }
            catch { }
        }

        // ========== CUSTOM CHROME ==========
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            var newTheme = _isDarkTheme ? "Light" : "Dark";
            ThemeService.ApplyTheme(newTheme);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            AnimateWindowState(WindowState.Minimized);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_notifyIcon != null && _notifyIcon.Visible)
            {
                AnimateHide();
                var settingsRepo = new SettingsRepository();
                var showBalloon = settingsRepo.GetValue(SettingsKeys.TrayBalloonEnabled, "True");
                if (showBalloon == "True")
                {
                    _notifyIcon.ShowBalloonTip(2000, "目标地图", "已最小化到系统托盘", Forms.ToolTipIcon.Info);
                }
            }
            else
            {
                var fadeAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeAnim.Completed += (s2, e2) => Application.Current.Shutdown();
                BeginAnimation(OpacityProperty, fadeAnim);
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        // ========== MANUAL WINDOW RESIZE (AllowsTransparency) ==========
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
        private const uint WM_NCLBUTTONDOWN = 0xA1;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int ResizeEdge = 6;

        private System.Windows.Interop.HwndSource _hwndSource;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        /// <summary>WM_NCHITTEST：窗口边缘 10px 返回系统拉伸区域，保证 AllowsTransparency 窗口也能自由拉伸</summary>
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
            {
                var pt = new POINT
                {
                    X = (short)((long)lParam & 0xFFFF),
                    Y = (short)(((long)lParam >> 16) & 0xFFFF)
                };
                ScreenToClient(hwnd, ref pt);
                var w = ActualWidth;
                var h = ActualHeight;
                int ht = HTCLIENT;
                if (pt.X < ResizeEdge) ht = pt.Y < ResizeEdge ? HTTOPLEFT : pt.Y > h - ResizeEdge ? HTBOTTOMLEFT : HTLEFT;
                else if (pt.X > w - ResizeEdge) ht = pt.Y < ResizeEdge ? HTTOPRIGHT : pt.Y > h - ResizeEdge ? HTBOTTOMRIGHT : HTRIGHT;
                else if (pt.Y < ResizeEdge) ht = HTTOP;
                else if (pt.Y > h - ResizeEdge) ht = HTBOTTOM;
                if (ht != HTCLIENT)
                {
                    handled = true;
                    return (IntPtr)ht;
                }
            }
            return IntPtr.Zero;
        }

        private void ResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            var w = ActualWidth;
            var h = ActualHeight;
            const int edge = ResizeEdge;
            int ht = 0;
            if (pos.X < edge) ht = pos.Y < edge ? HTTOPLEFT : pos.Y > h - edge ? HTBOTTOMLEFT : HTLEFT;
            else if (pos.X > w - edge) ht = pos.Y < edge ? HTTOPRIGHT : pos.Y > h - edge ? HTBOTTOMRIGHT : HTRIGHT;
            else if (pos.Y < edge) ht = HTTOP;
            else if (pos.Y > h - edge) ht = HTBOTTOM;
            if (ht != 0)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)ht, IntPtr.Zero);
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (WindowState == WindowState.Maximized) return;
            var pos = e.GetPosition(this);
            var w = ActualWidth;
            var h = ActualHeight;
            const int edge = ResizeEdge;
            if (pos.X < edge) Cursor = pos.Y < edge ? Cursors.SizeNWSE : pos.Y > h - edge ? Cursors.SizeNESW : Cursors.SizeWE;
            else if (pos.X > w - edge) Cursor = pos.Y < edge ? Cursors.SizeNESW : pos.Y > h - edge ? Cursors.SizeNWSE : Cursors.SizeWE;
            else if (pos.Y < edge) Cursor = Cursors.SizeNS;
            else if (pos.Y > h - edge) Cursor = Cursors.SizeNS;
            else Cursor = Cursors.Arrow;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var bw = WindowBorder.ActualWidth;
            var bh = WindowBorder.ActualHeight;
            if (bw > 0 && bh > 0)
            {
                var radius = WindowState == WindowState.Maximized ? 0.0 : 12.0;
                WindowClip.Rect = new Rect(0, 0, bw, bh);
                WindowClip.RadiusX = radius;
                WindowClip.RadiusY = radius;
                WindowBorder.CornerRadius = new CornerRadius(radius);
                WindowBorder.Margin = new Thickness(0);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
                return;
            }
            DragMove();
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
        }

        // ========== WINDOW ANIMATIONS ==========
        private void AnimateWindowState(WindowState targetState)
        {
            var transform = WindowBorder.RenderTransform as ScaleTransform;
            if (transform == null)
            {
                transform = new ScaleTransform(1, 1, 0.5, 0.5);
                WindowBorder.RenderTransform = transform;
            }

            var fadeAnim = new DoubleAnimation(1, 0.85, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleX = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleY = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeAnim.Completed += (s, e) =>
            {
                WindowState = targetState;
                var fadeBack = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var scaleBackX = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var scaleBackY = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeBack);
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleBackX);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleBackY);
            };

            BeginAnimation(OpacityProperty, fadeAnim);
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private void AnimateHide()
        {
            var transform = WindowBorder.RenderTransform as ScaleTransform;
            if (transform == null)
            {
                transform = new ScaleTransform(1, 1, 0.5, 0.5);
                WindowBorder.RenderTransform = transform;
            }

            var fadeAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleX = new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleY = new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeAnim.Completed += (s, e) =>
            {
                Hide();
                Opacity = 1;
                transform.ScaleX = 1;
                transform.ScaleY = 1;
            };

            BeginAnimation(OpacityProperty, fadeAnim);
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        public void ShowWithAnimation()
        {
            Show();
            var transform = WindowBorder.RenderTransform as ScaleTransform;
            if (transform == null)
            {
                transform = new ScaleTransform(1, 1, 0.5, 0.5);
                WindowBorder.RenderTransform = transform;
            }

            var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleX = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleY = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeAnim);
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        // ========== TRAY ICON ==========
        /// <summary>供 AppNotifier 借用主窗口托盘图标弹气泡（单图标模式）。主图标未启用时返回 false。</summary>
        public bool TryShowBalloon(string title, string text)
        {
            if (_notifyIcon == null || !_notifyIcon.Visible) return false;
            try
            {
                _notifyIcon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SetupTrayIcon()
        {
            try
            {
                _notifyIcon = new Forms.NotifyIcon();
                _notifyIcon.Text = "目标地图";

                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hobby_working_dailyroutine_life_time_management_icon_142245.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    try
                    {
                        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                    catch
                    {
                        _notifyIcon.Icon = SystemIcons.Application;
                    }
                }

                RebuildTrayMenu();

                _notifyIcon.DoubleClick += (s, ev) => { ShowWithAnimation(); WindowState = WindowState.Normal; Activate(); };

                var settingsRepo = new SettingsRepository();
                var minimizeToTray = settingsRepo.GetValue(SettingsKeys.MinimizeToTray, "False");
                _notifyIcon.Visible = minimizeToTray == "True";
            }
            catch
            {
            }
        }

        private void RebuildTrayMenu()
        {
            var isDark = ThemeService.IsDarkMode();
            var menu = new Forms.ContextMenuStrip();
            menu.Renderer = new ToolStripThemeRenderer(isDark);
            menu.Padding = new System.Windows.Forms.Padding(4);
            menu.Font = new System.Drawing.Font("Segoe UI", 10f);

            var showItem = new Forms.ToolStripMenuItem("显示主窗口");
            showItem.Click += (s, ev) => { Show(); WindowState = WindowState.Normal; Activate(); };
            menu.Items.Add(showItem);

            _floatingMenuItem = new Forms.ToolStripMenuItem("显示悬浮窗");
            _floatingMenuItem.Click += (s, ev) => ToggleFloatingWindow();
            menu.Items.Add(_floatingMenuItem);

            menu.Items.Add(new Forms.ToolStripSeparator());

            var exitItem = new Forms.ToolStripMenuItem("退出");
            exitItem.Click += (s, ev) => { _notifyIcon.Visible = false; CloseFloatingWindowPermanent(); Application.Current.Shutdown(); };
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        public void SetTrayVisible(bool visible)
        {
            if (_notifyIcon != null)
                _notifyIcon.Visible = visible;
        }

        // ========== FLOATING WINDOW ==========
        private void InitFloatingWindow()
        {
            var settingsRepo = new SettingsRepository();
            var enabled = settingsRepo.GetValue(SettingsKeys.FloatingWindowEnabled, "False");
            if (enabled == "True")
            {
                ShowFloatingWindow();
            }
        }

        public void ShowFloatingWindow()
        {
            if (_floatingWindow == null)
            {
                _floatingWindow = new FloatingWindow();
                _floatingWindow.Closed += (s, ev) => _floatingWindow = null;
            }
            _floatingWindow.Show();
            if (_floatingMenuItem != null)
                _floatingMenuItem.Text = "隐藏悬浮窗";
        }

        public void HideFloatingWindow()
        {
            _floatingWindow?.Hide();
            if (_floatingMenuItem != null)
                _floatingMenuItem.Text = "显示悬浮窗";
        }

        public void ToggleFloatingWindow()
        {
            if (_floatingWindow != null && _floatingWindow.IsVisible)
            {
                HideFloatingWindow();
            }
            else
            {
                ShowFloatingWindow();
            }
        }

        private void CloseFloatingWindowPermanent()
        {
            if (_floatingWindow != null)
            {
                _floatingWindow.ClosePermanent();
                _floatingWindow = null;
            }
        }

        // ========== NAVIGATION ==========
        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedIndex >= 0)
            {
                UpdateView(NavList.SelectedIndex);
            }
        }

        private void UpdateView(int index)
        {
            if (_currentView != null)
                _currentView.Visibility = Visibility.Collapsed;

            switch (index)
            {
                case 0: ShowView(ref _tasksView, () => new TasksView(), "任务列表"); break;
                case 1: ShowView(ref _goalsView, () => new GoalsView(), "目标管理"); break;
                case 2: ShowView(ref _calendarView, () => new CalendarView(), "日历视图"); break;
                case 3: ShowView(ref _reviewView, () => new ReviewView(), "定期盘点"); break;
                case 4: ShowView(ref _timeTrackView, () => new TimeTrackView(), "时间追踪"); break;
                case 5: ShowView(ref _healthView, () => new HealthView(), "健康"); break;
                case 6: ShowView(ref _customModulesView, () => new CustomModulesView(), "自定义模块"); break;
                case 7: ShowView(ref _settingsView, () => new SettingsView(), "设置"); break;
            }
        }

        private void ShowView<T>(ref T view, Func<T> create, string title) where T : UserControl
        {
            if (view == null)
            {
                view = create();
                view.Visibility = Visibility.Collapsed;
                view.Opacity = 0;
                ContentGrid.Children.Add(view);
            }

            if (_currentView != null && _currentView != view)
            {
                var oldView = _currentView;
                // Slide out + fade out
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                var slideOut = new DoubleAnimation(0, -12, TimeSpan.FromSeconds(0.15))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                var oldTransform = oldView.RenderTransform as TranslateTransform ?? new TranslateTransform();
                oldView.RenderTransform = oldTransform;

                fadeOut.Completed += (s, e) =>
                {
                    oldView.Visibility = Visibility.Collapsed;
                    oldView.Opacity = 1;
                    oldView.BeginAnimation(UIElement.OpacityProperty, null);
                    oldTransform.BeginAnimation(TranslateTransform.YProperty, null);
                    oldTransform.Y = 0;
                };
                oldView.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                oldTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
            }

            // Slide in + fade in
            view.Visibility = Visibility.Visible;
            view.Opacity = 0;
            var transform = view.RenderTransform as TranslateTransform ?? new TranslateTransform();
            view.RenderTransform = transform;
            transform.Y = 16;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideIn = new DoubleAnimation(16, 0, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            view.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.YProperty, slideIn);

            _currentView = view;
            TitleText.Text = title;
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            if (WindowBorder != null)
                ApplyWindowBorderColor();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (WindowBorder != null)
                WindowBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(60, 128, 128, 128));
        }

        protected override void OnClosed(EventArgs e)
        {
            _hwndSource?.RemoveHook(WindowProc);
            SharedTimerService.StopCurrent();
            CloseFloatingWindowPermanent();
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
