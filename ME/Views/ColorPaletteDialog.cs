using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ME.Views
{
    /// <summary>
    /// 圆形颜料盘选择器（弹窗）：点圆形色块选色，无需输入颜色代码。
    /// 色板与安卓端 ColorPresets 完全一致（24 色精选）。
    /// 用法：var hex = ColorPaletteDialog.Show(ownerWindow, "#4F6EF7"); 取消返回 null。
    /// </summary>
    public static class ColorPaletteDialog
    {
        public static readonly string[] Colors =
        {
            "#E5484D", "#E0603C", "#E07B39", "#E0A93C",
            "#D9B23C", "#A8C03C", "#7CB342", "#2E9E5B",
            "#2BA8A8", "#3AA6B8", "#4FC3F7", "#4A8CF7",
            "#4F6EF7", "#6C5CE7", "#7C5CE0", "#9B59B6",
            "#C25CE0", "#E05C8A", "#E05570", "#B85C5C",
            "#8A8F9E", "#6B7280", "#5A6472", "#3E4756",
        };

        public static string Show(Window owner, string initial)
        {
            string picked = null;
            var win = new Window
            {
                Title = "选择颜色",
                Width = 296,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = FindBrush("BackgroundBrush", Brushes.White),
                WindowStyle = WindowStyle.ToolWindow
            };

            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(new TextBlock
            {
                Text = "点圆形色块选择颜色",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextBrush", Brushes.Black),
                Margin = new Thickness(0, 0, 0, 12)
            });

            var grid = new UniformGrid { Columns = 4 };
            foreach (var hex in Colors)
            {
                var h = hex;
                var selected = string.Equals(initial, hex, StringComparison.OrdinalIgnoreCase);
                var cell = new Grid { Margin = new Thickness(4) };
                var ball = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(17),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    BorderThickness = new Thickness(selected ? 3 : 1),
                    BorderBrush = selected ? FindBrush("TextBrush", Brushes.DimGray) : FindBrush("BorderBrush", Brushes.LightGray),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = hex
                };
                ball.MouseLeftButtonDown += (s, e) => { picked = h; win.DialogResult = true; };
                cell.Children.Add(ball);
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancel = new Button
            {
                Content = "取消",
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            if (win.TryFindResource("SecondaryButtonStyle") is Style s2) cancel.Style = s2;
            cancel.Click += (s, e) => win.DialogResult = false;
            btnRow.Children.Add(cancel);
            stack.Children.Add(btnRow);

            win.Content = stack;
            win.Owner = owner;
            win.ShowDialog();
            return picked;
        }

        private static Brush FindBrush(string key, Brush fallback) =>
            Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }
}
