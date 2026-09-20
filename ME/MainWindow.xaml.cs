using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private Views.ExpensesView _expensesView;
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
                RegisterGlobalHotkey();
                ApplyAcrylic(); // 启动即按当前主题开/关 DWM 亚克力模糊
            };

            _isDarkTheme = ThemeService.IsDarkMode();
            UpdateThemeButton();
            SetupTrayIcon();
            ApplyWindowBorderColor();
            InitFloatingWindow();

            // 启动自动同步（设置里可关；后台运行不阻塞启动，完成后通过事件刷新设置页状态）
            _ = GitHubSyncService.AutoSyncOnStartupAsync();

            // 左下角云同步状态球：所有同步入口都经 SyncStatusService 登记结果
            SyncStatusService.StateChanged += OnSyncStatusChanged;
            SyncStatusService.RefreshLoginState();

            ThemeService.ThemeChanged += (theme) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _isDarkTheme = ThemeService.IsDarkMode();
                    UpdateThemeButton();
                    ApplyWindowBorderColor();
                    ApplyAcrylic();
                    RebuildTrayMenu();
                    // 缓存复用的页面里，代码后台上过色的元素不会自己变（DynamicResource 管不到它们），
                    // 主题真正变化时把当前页按新主题重建一次，字体/配色立刻全对
                    RefreshCurrentView();
                });
            };
        }

        /// <summary>毛玻璃主题：开 DWM 亚克力模糊（Gradient/Transparent 最明显）；普通主题关闭</summary>
        private void ApplyAcrylic()
        {
            bool on = ThemeService.IsGlass && ThemeService.GlassMode != "Image";
            if (!on)
            {
                WindowAcrylic.Apply(this, false, default);
                return;
            }
            bool dark = ThemeService.IsDarkMode();
            double op = ThemeService.GlassOpacity / 100.0;
            var tint = dark
                ? System.Windows.Media.Color.FromArgb((byte)(op * 170), 0x14, 0x14, 0x16)
                : System.Windows.Media.Color.FromArgb((byte)(op * 150), 0xF2, 0xF2, 0xF7);
            WindowAcrylic.Apply(this, true, tint);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeService.Initialize();
            UpdateView(0);
        }

        // ========== 云同步状态球（左下角） ==========
        private DoubleAnimation _syncBreathe;
        private int _toastSeq;

        private void OnSyncStatusChanged()
        {
            Dispatcher.BeginInvoke(new Action(UpdateSyncBall));
        }

        private void UpdateSyncBall()
        {
            var st = SyncStatusService.State;
            bool breathe = false;
            System.Windows.Media.Color c; string label;
            switch (st)
            {
                case SyncBallState.Running: c = System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E); label = "同步中…"; breathe = true; break;
                case SyncBallState.Success: c = System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E); label = "已同步"; break;
                case SyncBallState.Failed: c = System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44); label = "同步失败"; break;
                case SyncBallState.NotConfigured: c = System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF); label = "未绑定"; break;
                default: c = System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF); label = "云同步"; break;
            }
            SetBreathe(breathe);
            SyncDot.Fill = new SolidColorBrush(c);
            SyncBallText.Text = label;
            SyncBallBtn.ToolTip = st == SyncBallState.NotConfigured
                ? "尚未绑定 GitHub，点击去设置登录"
                : st == SyncBallState.Running
                    ? "正在同步…"
                    : (string.IsNullOrWhiteSpace(SyncStatusService.Message)
                        ? "点击立即云同步（先上传后下载，自动比较新旧，只传有变化的部分）"
                        : SyncStatusService.Message + "\n点击重新同步");
            if (SyncStatusService.ToastPending && (st == SyncBallState.Success || st == SyncBallState.Failed))
            {
                SyncStatusService.ConsumeToast();
                ShowSyncToast(SyncStatusService.Message, st == SyncBallState.Success);
            }
        }

        /** 同步中：状态球呼吸闪烁（透明度往复），结束即停 */
        private void SetBreathe(bool on)
        {
            if (on && _syncBreathe == null)
            {
                _syncBreathe = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(650))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                SyncDot.BeginAnimation(UIElement.OpacityProperty, _syncBreathe);
            }
            else if (!on && _syncBreathe != null)
            {
                _syncBreathe = null;
                SyncDot.BeginAnimation(UIElement.OpacityProperty, null);
            }
        }

        /** 左下角轻提示（HUD）：浮现后停留约 2.8 秒自动淡出，不遮挡不抢占界面 */
        private void ShowSyncToast(string msg, bool ok)
        {
            SyncToastDot.Fill = new SolidColorBrush(ok ? System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E) : System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            SyncToastText.Text = msg;
            SyncToast.Visibility = Visibility.Visible;
            int seq = ++_toastSeq;
            var sb = new Storyboard();
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)) { BeginTime = TimeSpan.FromMilliseconds(2800) };
            foreach (var a in new[] { fadeIn, fadeOut })
            {
                Storyboard.SetTarget(a, SyncToast);
                Storyboard.SetTargetProperty(a, new PropertyPath("Opacity"));
                sb.Children.Add(a);
            }
            sb.Completed += (s, e) => { if (seq == _toastSeq) SyncToast.Visibility = Visibility.Collapsed; };
            sb.Begin(SyncToast);
        }

        private async void SyncBall_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var c = GitHubSyncService.Load();
            if (string.IsNullOrWhiteSpace(c.EncryptedToken))
            {
                ShowSyncToast("尚未绑定 GitHub，先到「设置 → 数据与备份」登录", false);
                int idx = 7;
                foreach (var n in _vm.NavItems)
                    if (n.Name == "设置") { idx = n.ViewIndex; break; }
                NavList.SelectedIndex = idx;
                return;
            }
            if (SyncStatusService.State == SyncBallState.Running) return;
            await GitHubSyncService.SyncAsync(toast: true);
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
            // 只切深浅，保留当前风格（普通/毛玻璃）——之前传旧值会把毛玻璃打回普通
            var target = ThemeService.IsDarkMode() ? "Light" : "Dark";
            new SettingsRepository().SetValue(ThemeService.Keys.Tone, target);
            ThemeService.ApplyTheme();
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
                    _notifyIcon.ShowBalloonTip(2000, "ME", "已最小化到系统托盘", Forms.ToolTipIcon.Info);
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
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
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
            // 全局快捷键（Ctrl+Alt+M 显隐主窗口）
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_SHOW)
            {
                Dispatcher.BeginInvoke(new Action(ToggleMainWindowVisibility));
                handled = true;
                return IntPtr.Zero;
            }
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
                _notifyIcon.Text = "ME";

                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ME.ico");
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

                // 右键弹出菜单前重建：模块/主题等变化后无需重启即可反映到菜单
                _notifyIcon.MouseUp += (s, ev) =>
                {
                    if (ev.Button == Forms.MouseButtons.Right) RebuildTrayMenu();
                };

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
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Padding = new System.Windows.Forms.Padding(6, 6, 6, 6);
            menu.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5f);

            // —— 品牌抬头（不可点，只作分组标题） ——
            var brand = new Forms.ToolStripMenuItem("ME · 个人管理系统")
            {
                Enabled = false,
                ForeColor = System.Drawing.Color.DimGray,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5f, System.Drawing.FontStyle.Bold),
            };
            brand.MouseEnter += (s, ev) => brand.BackColor = System.Drawing.Color.Transparent;
            menu.Items.Add(brand);

            var showItem = new Forms.ToolStripMenuItem("显示主窗口      ");
            showItem.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5f, System.Drawing.FontStyle.Bold);
            showItem.Click += (s, ev) => { Show(); WindowState = WindowState.Normal; Activate(); };
            menu.Items.Add(showItem);

            // —— 主题子菜单：普通（浅/深）与 毛玻璃（浅/深） ——
            var themeItem = new Forms.ToolStripMenuItem("主题");
            void ThemeLeaf(string label, bool glass, bool? dark)
            {
                var leaf = new Forms.ToolStripMenuItem(label) { Checked = IsCurrentThemeChoice(glass, dark) };
                leaf.Click += (s, ev) =>
                {
                    var repo = new SettingsRepository();
                    repo.SetValue(ThemeService.Keys.Style, glass ? "Glass" : "Normal");
                    if (dark.HasValue) repo.SetValue(ThemeService.Keys.Tone, dark.Value ? "Dark" : "Light");
                    else repo.SetValue(ThemeService.Keys.Tone, "System");
                    ThemeService.ApplyTheme();
                    RebuildTrayMenu();
                };
                themeItem.DropDownItems.Add(leaf);
            }
            ThemeLeaf("普通 · 浅色", false, false);
            ThemeLeaf("普通 · 深色", false, true);
            ThemeLeaf("普通 · 跟随系统", false, null);
            themeItem.DropDownItems.Add(new Forms.ToolStripSeparator());
            ThemeLeaf("毛玻璃 · 浅色", true, false);
            ThemeLeaf("毛玻璃 · 深色", true, true);
            ThemeLeaf("毛玻璃 · 跟随系统", true, null);
            menu.Items.Add(themeItem);

            _floatingMenuItem = new Forms.ToolStripMenuItem("显示悬浮窗")
            {
                Checked = _floatingWindow != null && _floatingWindow.IsVisible,
            };
            _floatingMenuItem.Click += (s, ev) => { ToggleFloatingWindow(); RebuildTrayMenu(); };
            menu.Items.Add(_floatingMenuItem);

            menu.Items.Add(new Forms.ToolStripSeparator());

            // —— 快捷入口 ——
            var quickTitle = new Forms.ToolStripMenuItem("快捷入口") { Enabled = false };
            quickTitle.MouseEnter += (s, ev) => quickTitle.BackColor = System.Drawing.Color.Transparent;
            menu.Items.Add(quickTitle);

            // 快速记一笔：列出全部自定义模块，点击直接弹出该模块的记一笔弹窗
            var quickRecord = new Forms.ToolStripMenuItem("快速记一笔 ▸");
            var modules = _customModulesView?.CurrentModules
                          ?? (System.Collections.Generic.IReadOnlyList<ME.Models.CustomModule>)
                              ME.Data.CustomModuleRepository.GetAll().AsReadOnly();
            if (modules.Count == 0)
            {
                var none = new Forms.ToolStripMenuItem("（还没有自定义模块）") { Enabled = false };
                none.MouseEnter += (s2, ev2) => none.BackColor = System.Drawing.Color.Transparent;
                quickRecord.DropDownItems.Add(none);
            }
            else
            {
                foreach (var m in modules.Take(15))
                {
                    var moduleId = m.Id;
                    var item = new Forms.ToolStripMenuItem($"{ME.Services.FeishuCards.IconOf(m.Icon)} {m.Name}");
                    item.Click += (s2, ev2) =>
                    {
                        Show(); WindowState = WindowState.Normal; Activate();
                        UpdateView(6);
                        _customModulesView?.TryOpenRecordDialog(moduleId);
                    };
                    quickRecord.DropDownItems.Add(item);
                }
            }
            menu.Items.Add(quickRecord);

            void Quick(string label, int viewIndex)
            {
                var it = new Forms.ToolStripMenuItem(label);
                it.Click += (s, ev) => { Show(); WindowState = WindowState.Normal; Activate(); UpdateView(viewIndex); };
                menu.Items.Add(it);
            }
            Quick("任务列表", 0);
            Quick("日历视图", 2);
            Quick("自定义模块", 6);
            Quick("记账", 8);
            Quick("设置", 7);

            menu.Items.Add(new Forms.ToolStripSeparator());

            // —— 提醒声音（可勾选） ——
            var soundRepo = new SettingsRepository();
            var soundItem = new Forms.ToolStripMenuItem("完成提示音")
            {
                Checked = soundRepo.GetValue(SettingsKeys.SoundEnabled, "True") == "True",
            };
            soundItem.Click += (s, ev) =>
            {
                var now = !(soundRepo.GetValue(SettingsKeys.SoundEnabled, "True") == "True");
                soundRepo.SetValue(SettingsKeys.SoundEnabled, now.ToString());
                soundItem.Checked = now;
            };
            menu.Items.Add(soundItem);

            // —— 立即同步 ——
            var syncItem = new Forms.ToolStripMenuItem("立即同步云端");
            syncItem.Click += (s, ev) =>
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var msg = await GitHubSyncService.SyncAsync(toast: true);
                    AppNotifier.Show("云同步", msg);
                });
            };
            menu.Items.Add(syncItem);

            menu.Items.Add(new Forms.ToolStripSeparator());

            var exitItem = new Forms.ToolStripMenuItem("退出")
            {
                ForeColor = System.Drawing.Color.Firebrick,
            };
            exitItem.Click += (s, ev) => { _notifyIcon.Visible = false; CloseFloatingWindowPermanent(); Application.Current.Shutdown(); };
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        /// <summary>托盘主题菜单的勾选状态：风格 + 深浅 都匹配才算选中</summary>
        private static bool IsCurrentThemeChoice(bool glass, bool? dark)
        {
            if (ThemeService.IsGlass != glass) return false;
            if (!dark.HasValue) return new SettingsRepository().GetValue(ThemeService.Keys.Tone, "") == "System";
            return ThemeService.IsDarkMode() == dark.Value
                && new SettingsRepository().GetValue(ThemeService.Keys.Tone, "") != "System";
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
        private int _currentViewIndex;

        /// <summary>主题变化后把当前页按新主题重建（丢弃缓存实例），代码后台上色的元素立刻换色</summary>
        private void RefreshCurrentView()
        {
            var idx = _currentViewIndex;
            switch (idx)
            {
                case 0: _tasksView = null; break;
                case 1: _goalsView = null; break;
                case 2: _calendarView = null; break;
                case 3: _reviewView = null; break;
                case 4: _timeTrackView = null; break;
                case 5: _healthView = null; break;
                case 6: _customModulesView = null; break;
                case 7: _settingsView = null; break;
                case 8: _expensesView = null; break;
                default: return;
            }
            if (_currentView != null)
            {
                ContentGrid.Children.Remove(_currentView);
                _currentView = null;
            }
            UpdateView(idx);
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedIndex >= 0)
            {
                UpdateView(NavList.SelectedIndex);
            }
        }

        private void UpdateView(int index)
        {
            _currentViewIndex = index;
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

        // ========== GLOBAL HOTKEY (Ctrl+Alt+M 显隐主窗口) ==========
        private const int HOTKEY_ID_SHOW = 0xB00B;
        private const int WM_HOTKEY = 0x0312;
        private bool _hotkeyHiddenByHotkey;

        /// <summary>注册全局快捷键（设置里可关）：Ctrl+Alt+M 在任何界面显隐主窗口</summary>
        private void RegisterGlobalHotkey()
        {
            try
            {
                var enabled = new SettingsRepository().GetValue(SettingsKeys.GlobalHotkeyEnabled, "False") == "True";
                if (!enabled || _hwndSource == null) return;
                const uint MOD_CONTROL = 0x0002, MOD_ALT = 0x0001;
                if (RegisterHotKey(_hwndSource.Handle, HOTKEY_ID_SHOW, MOD_CONTROL | MOD_ALT, (uint)'M'))
                    _hotkeyRegistered = true;
            }
            catch { }
        }

        private bool _hotkeyRegistered;

        private void UnregisterGlobalHotkey()
        {
            try
            {
                if (_hotkeyRegistered && _hwndSource != null)
                    UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID_SHOW);
                _hotkeyRegistered = false;
            }
            catch { }
        }

        /// <summary>开关切换后重注册全局快捷键（设置页调用）</summary>
        public void ApplyGlobalHotkeySetting()
        {
            UnregisterGlobalHotkey();
            RegisterGlobalHotkey();
        }

        /// <summary>WM_HOTKEY：显示/隐藏主窗口（隐藏时最小化到托盘，若开了托盘）</summary>
        private void ToggleMainWindowVisibility()
        {
            if (WindowState == WindowState.Minimized || !IsVisible || _hotkeyHiddenByHotkey)
            {
                _hotkeyHiddenByHotkey = false;
                ShowWithAnimation();
                WindowState = WindowState.Normal;
                Activate();
            }
            else
            {
                _hotkeyHiddenByHotkey = true;
                Hide();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 退出前自动上传（可关）：改完忘传会让另一台设备下载到旧数据，这里兜底一次
            try { GitHubSyncService.TryPushBeforeExit(); } catch { }
            UnregisterGlobalHotkey();
            _hwndSource?.RemoveHook(WindowProc);
            SharedTimerService.StopCurrent();
            CloseFloatingWindowPermanent();
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
