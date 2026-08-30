using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Reflection;
using Microsoft.Win32;
using ME.Data;
using ME.Models;
using ME.Services;
using ME.Core;

using Forms = System.Windows.Forms;

namespace ME.Views
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private readonly BackupService _backupService;
        private readonly SettingsRepository _settingsRepo;

        private static readonly List<ColorBallDef> ColorBalls = new List<ColorBallDef>
        {
            new ColorBallDef("#007AFF", "默认蓝"),
            new ColorBallDef("#34C759", "森林绿"),
            new ColorBallDef("#FF3B30", "珊瑚红"),
            new ColorBallDef("#FF9500", "琥珀橙"),
            new ColorBallDef("#5856D6", "靛蓝紫"),
            new ColorBallDef("CUSTOM", "自定义"),
        };

        public SettingsView()
        {
            InitializeComponent();
            _backupService = new BackupService();
            _settingsRepo = new SettingsRepository();
            BackupModePartial.IsChecked = true;
            UpdateBackupPanelVisibility();
            BuildColorBalls();
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
            this.Unloaded += (s, e) => EventAggregator.Instance.Unsubscribe<string>(OnGlobalEvent);
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver != null)
                    VersionText.Text = $"版本 {ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { }
            LoadSyncConfig();
            ApplySettingsCategory(SettingsNav.SelectedIndex);
        }

        // ========== 分类导航（微信设置式：左侧大类 → 右侧一页） ==========

        private (StackPanel panel, string title)[] CategoryPanels => new (StackPanel, string)[]
        {
            (Sec_Appearance, "外观"),
            (Sec_General, "通用"),
            (Sec_Focus, "专注与统计"),
            (Sec_Data, "数据与备份"),
            (Sec_Modules, "自定义模块"),
            (Sec_AI, "AI 分析"),
            (Sec_About, "关于"),
        };

        private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsCardsPanel == null) return; // XAML 尚未加载
            ApplySettingsCategory(SettingsNav.SelectedIndex);
        }

        private void ApplySettingsCategory(int index)
        {
            var secs = CategoryPanels;
            if (index < 0 || index >= secs.Length) index = 0;
            for (int k = 0; k < secs.Length; k++)
                secs[k].panel.Visibility = k == index ? Visibility.Visible : Visibility.Collapsed;
            SettingsPageTitle.Text = secs[index].title;
            AnimateSettingCards();
        }

        // ========== GitHub 云同步 ==========

        private void LoadSyncConfig()
        {
            var c = GitHubSyncService.Load();
            SyncRepoBox.Text = string.IsNullOrWhiteSpace(c.Repo) ? "ME-Data" : c.Repo;
            SyncBranchBox.Text = string.IsNullOrWhiteSpace(c.Branch) ? "main" : c.Branch;
            SyncProxyBox.Text = c.Proxy;
            if (!string.IsNullOrWhiteSpace(c.EncryptedToken))
            {
                try { SyncTokenBox.Password = SecureStore.Decrypt(c.EncryptedToken); } catch { }
            }
            // 启动自动同步开关（赋值会触发 Checked/Unchecked，用标志跳过保存）
            _loadingSyncConfig = true;
            AutoSyncToggle.IsChecked = c.AutoSyncOnStartup;
            _loadingSyncConfig = false;
            var last = "";
            if (!string.IsNullOrEmpty(c.LastPushAt)) last += $"上次上传 {c.LastPushAt}  ";
            if (!string.IsNullOrEmpty(c.LastPullAt)) last += $"上次下载 {c.LastPullAt}  ";
            if (!string.IsNullOrEmpty(c.LastSyncAt)) last += $"上次自动同步 {c.LastSyncAt}";
            SyncLastTimeText.Text = last;
            if (!string.IsNullOrWhiteSpace(GitHubSyncService.LastAutoSyncResult))
                AutoSyncStatusText.Text = $"本次启动：{GitHubSyncService.LastAutoSyncResult}";
            RefreshAccountDisplay();
            // 已登录但没存账号名时后台补拉一次，保证刷新后仍显示登录状态
            if (!string.IsNullOrWhiteSpace(c.EncryptedToken) && string.IsNullOrWhiteSpace(c.AccountName))
            {
                _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await GitHubSyncService.RefreshAccountAsync();
                        RefreshAccountDisplay();
                    }
                    catch { }
                }));
            }
        }

        private void RefreshAccountDisplay()
        {
            var c = GitHubSyncService.Load();
            if (!string.IsNullOrWhiteSpace(c.EncryptedToken))
            {
                SyncAccountText.Text = string.IsNullOrWhiteSpace(c.AccountName)
                    ? "已登录 GitHub（Token 已保存）"
                    : $"已登录：{c.AccountName}";
                SyncLoginBtn.Content = "重新授权";
            }
            else
            {
                SyncAccountText.Text = "未登录 GitHub 账号";
                SyncLoginBtn.Content = "账号授权登录";
            }
        }

        private System.Windows.Threading.DispatcherTimer _loginPollTimer;
        private GitHubSyncService.DeviceFlowSession _loginSession;

        private async void SyncLogin_Click(object sender, RoutedEventArgs e)
        {
            SaveSyncConfig(); // 保存代理设置，登录请求要用
            SyncLoginBtn.IsEnabled = false;
            SyncLoginStatus.Text = "正在请求授权码…";
            try
            {
                _loginSession = await GitHubSyncService.LoginStartAsync();
            }
            catch (Exception ex)
            {
                SyncLoginStatus.Text = "请求失败：" + ex.Message + "（可尝试在上方填写代理）";
                SyncLoginBtn.IsEnabled = true;
                return;
            }
            GitHubSyncService.OpenLoginPage(_loginSession);
            SyncLoginStatus.Text = $"请在打开的网页登录 GitHub 并输入代码 {_loginSession.UserCode}，点 Authorize 后等待…";

            _loginPollTimer?.Stop();
            _loginPollTimer = new System.Windows.Threading.DispatcherTimer
            {
                // 比要求的最小间隔多留 1 秒余量：网络往返时间有抖动，掐得太准会被 GitHub 判定轮询过快（slow_down）
                Interval = TimeSpan.FromSeconds(Math.Max(3, _loginSession.Interval) + 1)
            };
            bool expired = false;
            bool polling = false;   // 上一次轮询请求还没返回时跳过本次 Tick，避免请求堆积
            int netFailures = 0;    // 连续网络失败次数：偶发抖动不打断授权，持续连不上才提示
            _loginPollTimer.Tick += async (s2, e2) =>
            {
                if (_loginSession == null || _loginSession.ExpiresAt < DateTime.Now)
                {
                    if (!expired) { expired = true; _loginPollTimer.Stop(); SyncLoginStatus.Text = "授权超时，请重试"; SyncLoginBtn.IsEnabled = true; }
                    return;
                }
                if (polling) return;
                polling = true;
                string result;
                try { result = await GitHubSyncService.LoginPollAsync(_loginSession); }
                catch (Exception ex)
                {
                    // 网络抖动/超时不打断授权，静默重试到授权码过期；连续失败较久才给出提示
                    netFailures++;
                    if (netFailures == 3)
                        SyncLoginStatus.Text = $"网络不稳定（{ex.Message}），仍在自动重试，授权成功后会自动完成，请稍候…";
                    if (netFailures >= 10)
                    {
                        _loginPollTimer.Stop();
                        SyncLoginStatus.Text = "长时间无法连接 GitHub，请检查网络或代理后重试";
                        SyncLoginBtn.IsEnabled = true;
                    }
                    polling = false;
                    return;
                }
                polling = false;
                // GitHub 要求两次轮询至少间隔 Interval 秒，返回 slow_down 时要求会更宽；
                // 定时器必须跟着放宽，否则之后每次轮询都「过快」，GitHub 一直回 slow_down 而不给
                // token——表现为网页已授权、软件却永远停在等待（本版核心修复）
                _loginPollTimer.Interval = TimeSpan.FromSeconds(Math.Min(60, Math.Max(3, _loginSession.Interval) + 1));
                if (result == null)
                {
                    if (netFailures > 0)
                    {
                        netFailures = 0;
                        SyncLoginStatus.Text = $"网络已恢复，继续等待授权…（代码 {_loginSession.UserCode}）";
                    }
                    return; // 仍在等待
                }
                _loginPollTimer.Stop();
                if (result.StartsWith("!"))
                {
                    SyncLoginStatus.Text = result.TrimStart('!');
                    SyncLoginBtn.IsEnabled = true;
                }
                else
                {
                    try { SyncTokenBox.Password = SecureStore.Decrypt(GitHubSyncService.Load().EncryptedToken); } catch { }
                    SyncLoginBtn.IsEnabled = true;
                    RefreshAccountDisplay();
                    SyncStatusService.RefreshLoginState();
                    SyncLoginStatus.Text = "✓ 授权成功，正在自动创建同步仓库 ME-Data…";
                    // 自动创建私有仓库 ME-Data 并写入配置，用户无需手填仓库名
                    try
                    {
                        var repo = await GitHubSyncService.EnsureDefaultRepoAsync();
                        SyncLoginStatus.Text = $"✓ 授权成功，已配置仓库 {repo}，可直接上传/下载";
                        LoadSyncConfig();
                    }
                    catch (Exception ex)
                    {
                        SyncLoginStatus.Text = "✓ 授权成功，但自动建仓失败：" + ex.Message + "（可手动填仓库名）";
                    }
                }
            };
            _loginPollTimer.Start();
        }

        private void SyncLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定退出 GitHub 账号？将清除已保存的 Token。", "退出登录",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            GitHubSyncService.Logout();
            SyncTokenBox.Password = "";
            SyncLoginStatus.Text = "";
            RefreshAccountDisplay();
            SyncStatusService.RefreshLoginState();
        }

        private void SaveSyncConfig()
        {
            var c = GitHubSyncService.Load();
            // 只填仓库名即可（如 ME-Data），账号前缀在上传/下载时自动补
            c.Repo = string.IsNullOrWhiteSpace(SyncRepoBox.Text) ? "ME-Data" : SyncRepoBox.Text.Trim();
            c.Branch = string.IsNullOrWhiteSpace(SyncBranchBox.Text) ? "main" : SyncBranchBox.Text.Trim();
            c.Proxy = SyncProxyBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(SyncTokenBox.Password))
                c.EncryptedToken = SecureStore.Encrypt(SyncTokenBox.Password.Trim());
            GitHubSyncService.Save(c);
        }

        private async void SyncPush_Click(object sender, RoutedEventArgs e)
        {
            if (SyncRepoBox.Text.Trim() == "" && SyncTokenBox.Password.Trim() == "")
            {
                SyncStatusText.Text = "请先登录 GitHub 账号或填写 Token";
                return;
            }
            SaveSyncConfig();
            SyncPushBtn.IsEnabled = false; SyncPullBtn.IsEnabled = false;
            SyncStatusText.Text = "上传中…";
            try
            {
                var msg = await GitHubSyncService.PushAsync();
                SyncStatusText.Text = msg;
            }
            catch (Exception ex) { SyncStatusText.Text = "上传失败：" + ex.Message; }
            SyncPushBtn.IsEnabled = true; SyncPullBtn.IsEnabled = true;
            LoadSyncConfig();
        }

        private async void SyncPull_Click(object sender, RoutedEventArgs e)
        {
            if (SyncRepoBox.Text.Trim() == "" && SyncTokenBox.Password.Trim() == "")
            {
                SyncStatusText.Text = "请先登录 GitHub 账号或填写 Token";
                return;
            }
            if (MessageBox.Show("下载会用仓库数据覆盖本机数据（本机会先自动备份）。继续？", "云同步下载",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SaveSyncConfig();
            SyncPushBtn.IsEnabled = false; SyncPullBtn.IsEnabled = false;
            SyncStatusText.Text = "下载中…";
            try
            {
                var msg = await GitHubSyncService.PullAsync();
                SyncStatusText.Text = msg;
            }
            catch (Exception ex) { SyncStatusText.Text = "下载失败：" + ex.Message; }
            SyncPushBtn.IsEnabled = true; SyncPullBtn.IsEnabled = true;
            LoadSyncConfig();
        }

        private void AnimateSettingCards()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SettingsCardsPanel is Panel panel)
                {
                    int idx = 0;
                    foreach (var child in panel.Children)
                    {
                        if (child is FrameworkElement el && el.Visibility == Visibility.Visible)
                        {
                            el.Opacity = 0;
                            var delay = TimeSpan.FromMilliseconds(idx * 60);
                            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                            {
                                BeginTime = delay,
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                            };
                            el.BeginAnimation(UIElement.OpacityProperty, fade);
                            var slide = new TranslateTransform(0, 12);
                            el.RenderTransform = slide;
                            var slideAnim = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300))
                            {
                                BeginTime = delay,
                                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                            };
                            slide.BeginAnimation(TranslateTransform.YProperty, slideAnim);
                            idx++;
                        }
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BuildColorBalls()
        {
            ColorBallsPanel.Children.Clear();
            foreach (var def in ColorBalls)
            {
                var ball = new Border
                {
                    Width = 30, Height = 30,
                    CornerRadius = new CornerRadius(15),
                    Margin = new Thickness(0, 0, 10, 0),
                    Cursor = Cursors.Hand,
                    Tag = def.Color,
                    ToolTip = def.Name,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                };

                if (def.Color == "CUSTOM")
                {
                    ball.Background = (SolidColorBrush)FindResource("CardBrush");
                    ball.Child = new TextBlock
                    {
                        Text = "+",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = (SolidColorBrush)FindResource("PrimaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                else
                {
                    ball.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(def.Color));
                }

                ball.MouseLeftButtonDown += ColorBall_Click;
                ColorBallsPanel.Children.Add(ball);
            }
            UpdateColorBallSelection();
        }

        private void UpdateColorBallSelection()
        {
            var currentColor = _settingsRepo.GetValue(SettingsKeys.WindowBorderColor, "#007AFF");
            var isPreset = ColorBalls.Any(b => b.Color == currentColor);
            foreach (var child in ColorBallsPanel.Children)
            {
                if (child is Border ball)
                {
                    var ballColor = ball.Tag as string;
                    bool isSelected;
                    if (ballColor == "CUSTOM")
                        isSelected = !isPreset;
                    else
                        isSelected = ballColor == currentColor;
                    ball.BorderBrush = isSelected
                        ? (SolidColorBrush)FindResource("PrimaryBrush")
                        : Brushes.Transparent;
                    ball.BorderThickness = isSelected ? new Thickness(3) : new Thickness(2);

                    if (ballColor == "CUSTOM" && !isPreset)
                    {
                        try
                        {
                            ball.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(currentColor));
                            if (ball.Child is TextBlock tb) tb.Text = "";
                        }
                        catch { }
                    }
                    else if (ballColor == "CUSTOM")
                    {
                        ball.Background = (SolidColorBrush)FindResource("CardBrush");
                        if (ball.Child is TextBlock tb) tb.Text = "+";
                    }
                }
            }
        }

        private void ColorBall_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border ball)
            {
                var color = ball.Tag as string;
                if (color == "CUSTOM")
                {
                    ShowCustomColorDialog();
                    return;
                }
                _settingsRepo.SetValue(SettingsKeys.WindowBorderColor, color);
                ApplyWindowBorderColor(color);
                UpdateColorBallSelection();
            }
        }

        private void ShowCustomColorDialog()
        {
            var colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = false
            };

            var currentColor = _settingsRepo.GetValue(SettingsKeys.WindowBorderColor, "#007AFF");
            try
            {
                var clr = (Color)ColorConverter.ConvertFromString(currentColor);
                colorDialog.Color = System.Drawing.Color.FromArgb(clr.R, clr.G, clr.B);
            }
            catch { }

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = colorDialog.Color;
                var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                _settingsRepo.SetValue(SettingsKeys.WindowBorderColor, hex);
                ApplyWindowBorderColor(hex);
                UpdateColorBallSelection();
            }
        }

        private void ApplyWindowBorderColor(string colorStr)
        {
            try
            {
                var mainWindow = Window.GetWindow(this) as MainWindow;
                if (mainWindow != null)
                {
                    var border = mainWindow.FindName("WindowBorder") as System.Windows.Controls.Border;
                    if (border != null)
                    {
                        border.BorderThickness = new Thickness(1);
                        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
                    }
                }
            }
            catch { }
        }

        private void LoadSettings()
        {
            var theme = _settingsRepo.GetValue(SettingsKeys.Theme, "Light");
            foreach (ComboBoxItem item in ThemeCombo.Items)
            {
                if (item.Tag?.ToString() == theme)
                {
                    ThemeCombo.SelectedItem = item;
                    break;
                }
            }

            var borderColor = _settingsRepo.GetValue(SettingsKeys.WindowBorderColor, "#007AFF");
            UpdateColorBallSelection();

            AutoStartToggle.IsChecked = _settingsRepo.GetValue(SettingsKeys.AutoStart, "False") == "True";
            MinimizeToTrayToggle.IsChecked = _settingsRepo.GetValue(SettingsKeys.MinimizeToTray, "False") == "True";
            TrayBalloonToggle.IsChecked = _settingsRepo.GetValue(SettingsKeys.TrayBalloonEnabled, "True") == "True";
            SoundToggle.IsChecked = SoundService.IsEnabled();
            FocusSoundToggle.IsChecked = _settingsRepo.GetValue(SettingsKeys.FocusSoundEnabled, "True") == "True";
            LastBackupText.Text = _settingsRepo.GetValue(SettingsKeys.LastBackupDate, "");

            // Floating window
            FloatingWindowToggle.IsChecked = _settingsRepo.GetValue(SettingsKeys.FloatingWindowEnabled, "False") == "True";

            // Week start
            var weekStart = _settingsRepo.GetValue(SettingsKeys.WeekStartDay, "1");
            foreach (ComboBoxItem item in WeekStartCombo.Items)
            {
                if (item.Tag?.ToString() == weekStart)
                {
                    WeekStartCombo.SelectedItem = item;
                    break;
                }
            }

            // Pomodoro auto start
            PomodoroAutoStartToggle.IsChecked = _settingsRepo.GetValue(SettingsKeys.PomodoroAutoStart, "False") == "True";

            // Stats tag selection
            BuildStatsTagsPanel();

            // DeepSeek API Key → 多供应商
            LoadAiProviders();
        }

        private void WeekStart_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (WeekStartCombo.SelectedItem is ComboBoxItem item)
            {
                _settingsRepo.SetValue(SettingsKeys.WeekStartDay, item.Tag?.ToString() ?? "1");
            }
        }

        private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item)
            {
                var theme = item.Tag?.ToString() ?? "Light";
                ThemeService.ApplyTheme(theme);
            }
        }

        private void AutoStartToggle_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = AutoStartToggle.IsChecked == true;
            _settingsRepo.SetValue(SettingsKeys.AutoStart, isEnabled.ToString());
            SetAutoStart(isEnabled);
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                        key.SetValue("GoalMap", System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    else
                        key.DeleteValue("GoalMap", false);
                }
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "错误", $"设置开机启动失败: {ex.Message}", "确定");
            }
        }

        private void TrayToggle_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = MinimizeToTrayToggle.IsChecked == true;
            _settingsRepo.SetValue(SettingsKeys.MinimizeToTray, isEnabled.ToString());
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.SetTrayVisible(isEnabled);
        }

        private void TrayBalloon_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = TrayBalloonToggle.IsChecked == true;
            _settingsRepo.SetValue(SettingsKeys.TrayBalloonEnabled, isEnabled.ToString());
        }

        private void FloatingWindow_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = FloatingWindowToggle.IsChecked == true;
            _settingsRepo.SetValue(SettingsKeys.FloatingWindowEnabled, isEnabled.ToString());
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null)
            {
                if (isEnabled)
                    mainWindow.ShowFloatingWindow();
                else
                    mainWindow.HideFloatingWindow();
            }
        }

        private void SoundToggle_Changed(object sender, RoutedEventArgs e)
        {
            SoundService.SetEnabled(SoundToggle.IsChecked == true);
            _settingsRepo.SetValue(SettingsKeys.SoundEnabled, (SoundToggle.IsChecked == true).ToString());
        }

        private void FocusSound_Changed(object sender, RoutedEventArgs e)
        {
            _settingsRepo.SetValue(SettingsKeys.FocusSoundEnabled, (FocusSoundToggle.IsChecked == true).ToString());
        }

        private void PomodoroAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            _settingsRepo.SetValue(SettingsKeys.PomodoroAutoStart, (PomodoroAutoStartToggle.IsChecked == true).ToString());
        }

        private void BuildStatsTagsPanel()
        {
            StatsTagsPanel.Children.Clear();
            var tagRepo = new TimeTagRepository();
            var tags = tagRepo.GetAllTags();
            var includedIds = TimeStatsHelper.GetIncludedTagIds();

            foreach (var tag in tags)
            {
                var cb = new CheckBox
                {
                    Content = tag.Name,
                    IsChecked = includedIds.Count == 0 || includedIds.Contains(tag.Id),
                    Tag = tag.Id,
                    Margin = new Thickness(0, 0, 12, 6),
                    Foreground = (SolidColorBrush)FindResource("TextBrush"),
                };
                if (!string.IsNullOrEmpty(tag.Color))
                {
                    try
                    {
                        cb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color));
                    }
                    catch { }
                }
                cb.Checked += StatsTag_Changed;
                cb.Unchecked += StatsTag_Changed;
                StatsTagsPanel.Children.Add(cb);
            }
        }

        private void StatsTag_Changed(object sender, RoutedEventArgs e)
        {
            var selectedIds = new List<int>();
            foreach (var child in StatsTagsPanel.Children)
            {
                if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is int id)
                {
                    selectedIds.Add(id);
                }
            }
            _settingsRepo.SetValue(SettingsKeys.StatsIncludedTags, string.Join(",", selectedIds));
        }

        private void StatsTags_SelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var child in StatsTagsPanel.Children)
            {
                if (child is CheckBox cb)
                    cb.IsChecked = true;
            }
        }

        private void StatsTags_DeselectAll(object sender, RoutedEventArgs e)
        {
            foreach (var child in StatsTagsPanel.Children)
            {
                if (child is CheckBox cb)
                    cb.IsChecked = false;
            }
        }

        private void BackupMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateBackupPanelVisibility();
        }

        // ========== 启动自动同步 ==========

        private bool _loadingSyncConfig;

        private void AutoSyncToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSyncConfig || AutoSyncToggle == null) return; // XAML 初始化/回填期不写盘
            var c = GitHubSyncService.Load();
            c.AutoSyncOnStartup = AutoSyncToggle.IsChecked == true;
            GitHubSyncService.Save(c);
        }

        /// <summary>后台自动同步完成后刷新状态显示</summary>
        private void OnGlobalEvent(string message)
        {
            if (message != "SyncStatusChanged") return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var c = GitHubSyncService.Load();
                var last = "";
                if (!string.IsNullOrEmpty(c.LastPushAt)) last += $"上次上传 {c.LastPushAt}  ";
                if (!string.IsNullOrEmpty(c.LastPullAt)) last += $"上次下载 {c.LastPullAt}  ";
                if (!string.IsNullOrEmpty(c.LastSyncAt)) last += $"上次自动同步 {c.LastSyncAt}";
                SyncLastTimeText.Text = last;
                if (!string.IsNullOrWhiteSpace(GitHubSyncService.LastAutoSyncResult))
                    AutoSyncStatusText.Text = $"本次启动：{GitHubSyncService.LastAutoSyncResult}";
            }));
        }

        private void UpdateBackupPanelVisibility()
        {
            if (PartialBackupPanel != null)
                PartialBackupPanel.Visibility = BackupModePartial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            if (BackupModeFull.IsChecked == true)
                BackupAllData();
            else
                BackupPartialData();
        }

        private void BackupAllData()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"me_full_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };
                if (dlg.ShowDialog() == true)
                {
                    var dataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ME", "JsonData");
                    var merged = new Dictionary<string, object>();
                    if (Directory.Exists(dataDir))
                    {
                        foreach (var file in Directory.GetFiles(dataDir, "*.json"))
                        {
                            var json = File.ReadAllText(file);
                            using var doc = JsonDocument.Parse(json);
                            var key = Path.GetFileNameWithoutExtension(file);
                            merged[key] = doc.RootElement;
                        }
                    }
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(merged, options));
                    _settingsRepo.SetValue(SettingsKeys.LastBackupDate, $"上次备份: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    LastBackupText.Text = $"上次备份: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    ConfirmDialog.Show(Window.GetWindow(this), "提示", "备份成功!", "确定");
                }
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "错误", $"备份失败: {ex.Message}", "确定");
            }
        }

        private void BackupPartialData()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"me_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };
                if (dlg.ShowDialog() == true)
                {
                    var dataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ME", "JsonData");
                    var selectedFiles = new List<string>();
                    if (BackupGoals.IsChecked == true) selectedFiles.Add("goals.json");
                    if (BackupTasks.IsChecked == true) selectedFiles.Add("tasks.json");
                    if (BackupFocus.IsChecked == true) selectedFiles.Add("focus_sessions.json");
                    if (BackupSettings.IsChecked == true) selectedFiles.Add("settings.json");
                    if (BackupTags.IsChecked == true) selectedFiles.Add("tags.json");
                    if (BackupTimeRecords.IsChecked == true) selectedFiles.Add("time_records.json");
                    if (BackupTimeTags.IsChecked == true) selectedFiles.Add("time_tags.json");
                    if (BackupHealth.IsChecked == true) selectedFiles.Add("health_records.json");
                    if (BackupMeds.IsChecked == true) selectedFiles.Add("medications.json");
                    if (BackupContainers.IsChecked == true) selectedFiles.Add("water_containers.json");
                    if (BackupExerciseItems.IsChecked == true) selectedFiles.Add("exercise_items.json");

                    var merged = new Dictionary<string, object>();
                    foreach (var file in selectedFiles)
                    {
                        var path = Path.Combine(dataDir, file);
                        if (File.Exists(path))
                        {
                            var json = File.ReadAllText(path);
                            using var doc = JsonDocument.Parse(json);
                            var key = Path.GetFileNameWithoutExtension(file);
                            merged[key] = doc.RootElement;
                        }
                    }
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(merged, options));
                    _settingsRepo.SetValue(SettingsKeys.LastBackupDate, $"上次备份: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    LastBackupText.Text = $"上次备份: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    ConfirmDialog.Show(Window.GetWindow(this), "提示", "备份成功!", "确定");
                }
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "错误", $"备份失败: {ex.Message}", "确定");
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json"
                };
                if (dlg.ShowDialog() == true)
                {
                    var json = File.ReadAllText(dlg.FileName);
                    using var doc = JsonDocument.Parse(json);
                    var dataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ME", "JsonData");
                    Directory.CreateDirectory(dataDir);

                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            var filePath = Path.Combine(dataDir, prop.Name + ".json");
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            File.WriteAllText(filePath, JsonSerializer.Serialize(prop.Value, options));
                        }
                    }
                    else
                    {
                        File.Copy(dlg.FileName, Path.Combine(dataDir, Path.GetFileName(dlg.FileName)), true);
                    }

                    ConfirmDialog.Show(Window.GetWindow(this), "提示", "导入成功! 请重启应用以加载数据。", "确定");
                }
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "错误", $"导入失败: {ex.Message}", "确定");
            }
        }

        private readonly AiProviderRepository _aiProviderRepo = new AiProviderRepository();
        private int _selectedProviderId;

        private void LoadAiProviders()
        {
            var providers = _aiProviderRepo.EnsureDefaultDeepSeek();
            var selId = _selectedProviderId;
            AiProviderCombo.Items.Clear();
            foreach (var p in providers)
            {
                var item = new ComboBoxItem
                {
                    Content = p.IsDefault ? $"{p.Name}（默认）" : p.Name,
                    Tag = p.Id
                };
                AiProviderCombo.Items.Add(item);
            }
            var withKey = providers.Where(p => !string.IsNullOrEmpty(AiProviderRepository.GetApiKey(p))).ToList();
            var sel = providers.Find(p => p.Id == selId)
                ?? (withKey.FirstOrDefault(p => p.IsDefault) ?? withKey.FirstOrDefault())
                ?? providers.Find(p => p.IsDefault)
                ?? providers.FirstOrDefault();
            if (sel != null)
            {
                _selectedProviderId = sel.Id;
                foreach (ComboBoxItem it in AiProviderCombo.Items)
                    if (it.Tag is int id && id == sel.Id) { AiProviderCombo.SelectedItem = it; break; }
            }
            DeepSeekStatusText.Text = sel != null && !string.IsNullOrEmpty(AiProviderRepository.GetApiKey(sel))
                ? $"当前供应商：{sel.Name}（{sel.Model}）"
                : $"当前供应商：{sel?.Name}（未填 API Key）";
            DeepSeekStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        }

        private void AiProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (AiProviderCombo.SelectedItem is ComboBoxItem it && it.Tag is int id)
            {
                _selectedProviderId = id;
                var p = _aiProviderRepo.GetAll().Find(x => x.Id == id);
                DeepSeekStatusText.Text = p != null && !string.IsNullOrEmpty(AiProviderRepository.GetApiKey(p))
                    ? $"当前供应商：{p.Name}（{p.Model}）"
                    : $"当前供应商：{p?.Name}（未填 API Key）";
            }
        }

        private void AddAiProvider_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AiProviderDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadAiProviders();
            }
        }

        private void EditAiProvider_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProviderId <= 0) return;
            var dlg = new AiProviderDialog(_selectedProviderId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadAiProviders();
            }
        }

        private async void TestDeepSeekKey_Click(object sender, RoutedEventArgs e)
        {
            var p = _aiProviderRepo.GetAll().Find(x => x.Id == _selectedProviderId);
            if (p == null)
            {
                DeepSeekStatusText.Text = "请先选择供应商";
                return;
            }
            if (string.IsNullOrWhiteSpace(AiProviderRepository.GetApiKey(p)))
            {
                DeepSeekStatusText.Text = $"供应商「{p.Name}」未填写 API Key";
                return;
            }
            TestDeepSeekKeyBtn.IsEnabled = false;
            DeepSeekStatusText.Text = $"正在测试 {p.Name}…";
            try
            {
                var reply = await LlmService.ChatAsync(p, "你是一个测试助手", "请只回复：连接成功", 0);
                DeepSeekStatusText.Text = $"✅ {reply}（{p.Name} 连接成功）";
                DeepSeekStatusText.Foreground = (Brush)FindResource("AccentGreenBrush");
            }
            catch (Exception ex)
            {
                DeepSeekStatusText.Text = $"❌ 失败：{ex.Message}";
                DeepSeekStatusText.Foreground = (Brush)FindResource("AccentRedBrush");
            }
            finally
            {
                TestDeepSeekKeyBtn.IsEnabled = true;
            }
        }

        private void ImportXiaomi_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择小米运动健康导出文件（zip 或 csv）",
                Filter = "小米导出文件|*.zip;*.csv|压缩包|*.zip|CSV 文件|*.csv"
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var result = XiaomiImportService.ImportFile(dlg.FileName);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"导入完成：睡眠 {result.SleepImported} 条，体重 {result.WeightImported} 条，覆盖已有 {result.Overwritten} 条，跳过 {result.SkippedRows} 行");
                foreach (var m in result.Messages.Take(6))
                    sb.AppendLine("· " + m);
                ConfirmDialog.Show(Window.GetWindow(this), "小米健康导入", sb.ToString(), "确定");
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "导入失败", ex.Message, "确定");
            }
        }

        private class ColorBallDef
        {
            public string Color { get; set; }
            public string Name { get; set; }
            public ColorBallDef(string c, string n) { Color = c; Name = n; }
        }

        // ========== 关于：GitHub + 微信 ==========

        private void OpenGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/nailao946/ME",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "错误", $"无法打开链接: {ex.Message}", "确定");
            }
        }

        private void Feedback_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new FeedbackDialog { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }

        // ========== 关于：版本检测（对比 GitHub Releases 发布的最新版本） ==========

        private string _releaseUrl;

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateBtn.IsEnabled = false;
            CheckUpdateBtn.Content = "检查中…";
            UpdateStatusText.Visibility = Visibility.Collapsed;
            _releaseUrl = null;
            try
            {
                var r = await UpdateCheckService.CheckAsync();
                if (r.Error != null)
                {
                    UpdateStatusText.Text = r.Error;
                    UpdateStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
                    UpdateStatusText.Visibility = Visibility.Visible;
                }
                else if (r.HasUpdate)
                {
                    _releaseUrl = r.ReleaseUrl;
                    UpdateStatusText.Text = $"发现新版本 v{r.LatestVersion}（当前 v{r.CurrentVersion}），点此前往下载";
                    UpdateStatusText.Foreground = (Brush)FindResource("PrimaryBrush");
                    UpdateStatusText.Visibility = Visibility.Visible;
                }
                else
                {
                    UpdateStatusText.Text = $"已是最新版本 v{r.CurrentVersion}";
                    UpdateStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
                    UpdateStatusText.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                CheckUpdateBtn.IsEnabled = true;
                CheckUpdateBtn.Content = "重新检查";
            }
        }

        private void UpdateStatusText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_releaseUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _releaseUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void WechatText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Clipboard.SetText("shuaim888888");
                ConfirmDialog.Show(Window.GetWindow(this), "已复制", "微信号 shuaim888888 已复制到剪贴板", "确定");
            }
            catch { }
        }
    }
}
