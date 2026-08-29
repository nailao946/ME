using System;
using System.Windows;
using System.Windows.Forms;

namespace ME.Services
{
    /// <summary>
    /// 全局系统托盘通知（气泡）。单图标模式：优先借用主窗口的托盘图标弹出气泡，
    /// 因此任务栏始终只有一个 ME 图标；仅当主图标不可用（未开启最小化到托盘）时，
    /// 临时创建一个图标弹完通知后自动隐藏，不常驻。
    /// </summary>
    public static class AppNotifier
    {
        private static NotifyIcon _tempIcon;
        private static System.Windows.Forms.Timer _hideTimer;

        /// <summary>初始化（单图标模式无需预先创建常驻图标，延迟到首次通知）</summary>
        public static void Init()
        {
            // 不再创建独立常驻托盘图标 —— 避免与主窗口图标同时出现两个。
        }

        /// <summary>弹出系统托盘气泡通知</summary>
        public static void Show(string title, string text)
        {
            try
            {
                // 1) 优先借用主窗口的托盘图标（单一图标）
                if (System.Windows.Application.Current?.MainWindow is MainWindow mw && mw.TryShowBalloon(title, text))
                    return;

                // 2) 兜底：临时图标，弹出后 12 秒自动隐藏
                EnsureTempIcon();
                _tempIcon.Visible = true;
                _tempIcon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
                if (_hideTimer != null)
                {
                    _hideTimer.Stop();
                    _hideTimer.Start();
                }
            }
            catch { }
        }

        private static void EnsureTempIcon()
        {
            if (_tempIcon != null) return;
            _tempIcon = new NotifyIcon { Text = "ME" };

            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "hobby_working_dailyroutine_life_time_management_icon_142245.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _tempIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                try
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    _tempIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
                catch { }
            }

            _hideTimer = new System.Windows.Forms.Timer { Interval = 12000 };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                try { _tempIcon.Visible = false; } catch { }
            };
        }
    }
}
