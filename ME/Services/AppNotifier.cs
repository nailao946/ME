using System;
using System.Windows.Forms;

namespace ME.Services
{
    /// <summary>
    /// 全局系统托盘通知（气泡）。独立于主窗口的托盘图标，
    /// 仅在弹出通知时短暂显示，通知结束后自动隐藏，不常驻任务栏。
    /// </summary>
    public static class AppNotifier
    {
        private static NotifyIcon _notifyIcon;
        private static System.Windows.Forms.Timer _hideTimer;

        public static void Init()
        {
            try
            {
                if (_notifyIcon != null) return;
                _notifyIcon = new NotifyIcon { Text = "目标地图", Visible = true };

                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "hobby_working_dailyroutine_life_time_management_icon_142245.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    try
                    {
                        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                    catch { }
                }

                _hideTimer = new System.Windows.Forms.Timer { Interval = 12000 };
                _hideTimer.Tick += (s, e) =>
                {
                    _hideTimer.Stop();
                    try { _notifyIcon.Visible = false; } catch { }
                };
            }
            catch { }
        }

        /// <summary>弹出系统托盘气泡通知</summary>
        public static void Show(string title, string text)
        {
            try
            {
                Init();
                if (_notifyIcon == null) return;
                _notifyIcon.Visible = true;
                _notifyIcon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
                if (_hideTimer != null)
                {
                    _hideTimer.Stop();
                    _hideTimer.Start();
                }
            }
            catch { }
        }
    }
}
