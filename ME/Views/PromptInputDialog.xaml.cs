using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ME.Views
{
    /// <summary>极简单行输入对话框（用于 AI 生成提示词等一次性输入）</summary>
    public static class PromptInputDialog
    {
        public static string Show(Window owner, string title, string label, string initial = "")
        {
            string result = null;

            var win = new Window
            {
                Title = title,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                Owner = owner,
            };

            var outer = new Border
            {
                Background = (Brush)owner.FindResource("CardBrush"),
                BorderBrush = (Brush)owner.FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(12),
                Padding = new Thickness(18),
                Effect = owner.TryFindResource("CardShadow") as System.Windows.Media.Effects.Effect,
            };

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = title, FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = (Brush)owner.FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12,
                Foreground = (Brush)owner.FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            var box = new TextBox
            {
                Text = initial ?? "", FontSize = 13, Height = 62,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(8, 6, 8, 6),
            };
            root.Children.Add(box);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "取消", Style = (Style)owner.FindResource("SecondaryButtonStyle"), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            var ok = new Button { Content = "开始生成", Style = (Style)owner.FindResource("PrimaryButtonStyle"), Padding = new Thickness(16, 6, 16, 6), Cursor = Cursors.Hand };
            btns.Children.Add(cancel); btns.Children.Add(ok);
            root.Children.Add(btns);

            outer.Child = root;
            win.Content = outer;

            ok.Click += (s, e) => { result = box.Text; win.Close(); };
            cancel.Click += (s, e) => win.Close();
            box.KeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };

            win.ShowDialog();
            return result;
        }
    }
}
