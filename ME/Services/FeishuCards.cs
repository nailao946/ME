using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Models;

namespace ME.Services
{
    /// <summary>
    /// 飞书卡片风格的基础构件，供自定义模块 / 目标 / 任务等页复用：
    /// 大圆角 + 细描边 + 头部信息 + 细分割线 + 图标文字操作区。
    /// </summary>
    public static class FeishuCards
    {
        /// <summary>飞书风卡片：大圆角、细描边、无重投影，内容自己往里放</summary>
        public static Border Card(FrameworkElement host, double padding = 16)
        {
            return new Border
            {
                Style = (Style)host.FindResource("CardStyle"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(padding),
                Margin = new Thickness(0, 0, 0, 10),
                SnapsToDevicePixels = true
            };
        }

        /// <summary>卡片内的细分割线（飞书卡片头部与底部操作区之间那条 1px 线）</summary>
        public static FrameworkElement Divider(FrameworkElement host)
        {
            return new Border
            {
                Height = 1,
                Background = (Brush)host.FindResource("BorderBrush"),
                Margin = new Thickness(0, 12, 0, 10),
                SnapsToDevicePixels = true
            };
        }

        /// <summary>飞书卡片底部动作：小图标 + 文字，整块可点，悬停有淡底</summary>
        public static FrameworkElement Action(FrameworkElement host, string icon, string label, Brush fg, MouseButtonEventHandler onClick)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = icon, FontSize = 13, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Foreground = fg, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            var b = new Border
            {
                Child = sp, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };
            b.MouseEnter += (s, e) => b.Background = (Brush)host.FindResource("NavHoverBrush");
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += onClick;
            return b;
        }

        /// <summary>把一条自定义模块记录渲染成「字段 值 · 字段 值」的单行文本</summary>
        public static string RecordValuesText(CustomModule m, CustomModuleRecord r)
        {
            var vals = string.Join(" · ", r.Values.Select(kv =>
            {
                var f = m.Fields.FirstOrDefault(x => x.Key == kv.Key);
                return $"{(f?.Label ?? kv.Key)} {kv.Value}{(string.IsNullOrEmpty(f?.Unit) ? "" : " " + f.Unit)}";
            }));
            return vals == "" ? (string.IsNullOrEmpty(r.Note) ? "（无字段值）" : r.Note) : vals;
        }
    }
}
