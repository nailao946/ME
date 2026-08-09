using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ME.Core;
using ME.Services;
using ME.Views;

namespace ME
{
    public partial class App : Application
    {
        private DispatcherTimer _dayTimer;
        private DateTime _lastCheckedDate;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            Data.DatabaseHelper.Initialize();
            ThemeService.Initialize();
            try { IdleTimeService.BackfillAllDates(); } catch { }
            StartDayWatcher();
        }

        /// <summary>
        /// 所有窗口/对话框统一打开动画（淡入 + 轻微缩放），MainWindow/FloatingWindow 已有自带动画。
        /// </summary>
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Window window)) return;
            if (window is MainWindow || window is FloatingWindow) return;
            if (window.Opacity < 1.0) return; // 已播放过动画

            window.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            window.BeginAnimation(Window.OpacityProperty, fade);

            if (window.AllowsTransparency)
            {
                // 渲染完成后（ContentRendered）再设置 RenderTransform：
                // Window.CoerceRenderTransform 要求 AllowsTransparency 已生效，
                // Loaded 阶段分层窗口可能尚未就绪，直接设置会抛 InvalidOperationException。
                // 一次性订阅，执行后退订避免累积。
                EventHandler handler = null;
                handler = (s2, e2) =>
                {
                    window.ContentRendered -= handler;
                    ApplyWindowScaleIn(window);
                };
                window.ContentRendered += handler;
            }
        }

        private static void ApplyWindowScaleIn(Window window)
        {
            try
            {
                var st = new ScaleTransform(0.96, 0.96);
                window.RenderTransform = st;
                window.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            }
            catch (InvalidOperationException)
            {
                // 窗口不允许变换时退化为纯淡入
                window.RenderTransform = null;
            }
        }

        private void StartDayWatcher()
        {
            _lastCheckedDate = DateTime.Today;
            _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _dayTimer.Tick += (s, ev) =>
            {
                var today = DateTime.Today;
                if (today == _lastCheckedDate) return;
                var yesterday = _lastCheckedDate;
                _lastCheckedDate = today;
                try { IdleTimeService.EnsureIdleRecords(yesterday); } catch { }
                try { IdleTimeService.EnsureIdleRecords(today); } catch { }
                try { SharedPomodoroService.Instance.RefreshToday(); } catch { }
                EventAggregator.Instance.Publish("DayChanged");
            };
            _dayTimer.Start();
        }
    }
}
