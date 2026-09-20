using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;
using ME.Services;

namespace ME.Views
{
    public partial class ExpensesView : UserControl
    {
        // 固定支出分类（emoji + 名称）
        private static readonly (string Key, string Emoji)[] ExpenseCats =
        {
            ("餐饮", "🍕"), ("交通", "🚇"), ("购物", "🛍"), ("日用", "🧴"),
            ("居住", "🏠"), ("娱乐", "🎮"), ("医疗", "💊"), ("人情", "🎁"), ("其他", "📦")
        };
        // 固定收入分类
        private static readonly (string Key, string Emoji)[] IncomeCats =
        {
            ("工资", "💰"), ("理财", "📈"), ("红包", "🧧"), ("其他", "📦")
        };

        private static readonly Dictionary<string, string> CatEmoji = BuildCatEmoji();

        private static Dictionary<string, string> BuildCatEmoji()
        {
            var d = new Dictionary<string, string>();
            foreach (var c in ExpenseCats) d[c.Key] = c.Emoji;
            foreach (var c in IncomeCats) d[c.Key] = c.Emoji;
            return d;
        }

        private DateTime _month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        public ExpensesView()
        {
            InitializeComponent();
            Reload();
        }

        private void ExpensesView_Loaded(object sender, RoutedEventArgs e) => Reload();

        // ============ 渲染 ============

        private void Reload()
        {
            var ym = _month.ToString("yyyy-MM");
            MonthLabel.Text = ym;

            var records = ExpenseRepository.GetByMonth(ym);

            // 统计卡
            var income = records.Where(r => r.IsIncome).Sum(r => r.Amount);
            var expense = records.Where(r => !r.IsIncome).Sum(r => r.Amount);
            IncomeText.Text = income.ToString("F2");
            ExpenseText.Text = expense.ToString("F2");
            BalanceText.Text = (income - expense).ToString("F2");

            BuildCategory(records.Where(r => !r.IsIncome).ToList());
            BuildRecords(records);
        }

        private void BuildCategory(List<ExpenseRecord> expenses)
        {
            CategoryPanel.Children.Clear();
            if (expenses.Count == 0) { CategoryEmpty.Visibility = Visibility.Visible; return; }
            CategoryEmpty.Visibility = Visibility.Collapsed;

            var sums = expenses
                .GroupBy(r => r.Category)
                .Select(g => new { Cat = g.Key, Sum = g.Sum(r => r.Amount) })
                .OrderByDescending(x => x.Sum)
                .ToList();
            var max = sums.Max(x => x.Sum);
            var maxBar = 260.0;

            foreach (var s in sums)
            {
                var emoji = CatEmoji.TryGetValue(s.Cat, out var e2) ? e2 : "📦";
                var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
                dp.Children.Add(new TextBlock
                {
                    Text = $"{emoji} {s.Cat}",
                    FontSize = 12.5,
                    Foreground = (Brush)FindResource("TextBrush"),
                    Width = 96,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var fill = new Border
                {
                    Height = 14,
                    Width = Math.Max(4, s.Sum / max * maxBar),
                    Background = (Brush)FindResource("PrimaryBrush"),
                    CornerRadius = new CornerRadius(7),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(fill, Dock.Left);
                dp.Children.Add(fill);
                dp.Children.Add(new TextBlock
                {
                    Text = s.Sum.ToString("F2"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AccentRedBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                CategoryPanel.Children.Add(dp);
            }
        }

        private void BuildRecords(List<ExpenseRecord> records)
        {
            RecordsPanel.Children.Clear();
            if (records.Count == 0) { EmptyText.Visibility = Visibility.Visible; return; }
            EmptyText.Visibility = Visibility.Collapsed;

            var today = DateTime.Now.Date;
            var yesterday = today.AddDays(-1);
            var groups = records.GroupBy(r => r.Date).OrderByDescending(g => g.Key);

            foreach (var g in groups)
            {
                var date = g.Key;
                var header = date == today.ToString("yyyy-MM-dd") ? "今天"
                           : date == yesterday.ToString("yyyy-MM-dd") ? "昨天"
                           : date;
                RecordsPanel.Children.Add(new TextBlock
                {
                    Text = header,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 6, 0, 6)
                });

                foreach (var r in g.OrderBy(x => x.Id))
                {
                    RecordsPanel.Children.Add(BuildRecordRow(r));
                }
            }
        }

        private FrameworkElement BuildRecordRow(ExpenseRecord r)
        {
            var emoji = CatEmoji.TryGetValue(r.Category, out var e2) ? e2 : "📦";
            var row = new Border
            {
                Background = (Brush)FindResource("BackgroundBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock
            {
                Text = $"{emoji} {r.Category}",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush")
            });
            if (!string.IsNullOrWhiteSpace(r.Note))
            {
                left.Children.Add(new TextBlock
                {
                    Text = r.Note,
                    FontSize = 11.5,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            Grid.SetColumn(left, 0);

            var amount = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0),
                Foreground = r.IsIncome ? (Brush)FindResource("AccentGreenBrush") : (Brush)FindResource("AccentRedBrush"),
                Text = (r.IsIncome ? "+" : "−") + r.Amount.ToString("F2")
            };
            Grid.SetColumn(amount, 1);

            var del = new Button
            {
                Content = "🗑",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                FontSize = 12,
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = Cursors.Hand,
                Tag = r.Id,
                VerticalAlignment = VerticalAlignment.Center
            };
            del.Click += (s, e) =>
            {
                if (MessageBox.Show($"删除这笔「{emoji} {r.Category} {(r.IsIncome ? "+" : "−")}{r.Amount:F2}」？",
                        "删除记录", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ExpenseRepository.Delete(r.Id);
                    Reload();
                }
            };
            Grid.SetColumn(del, 2);

            grid.Children.Add(left);
            grid.Children.Add(amount);
            grid.Children.Add(del);
            row.Child = grid;
            return row;
        }

        // ============ 月份导航 ============

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _month = _month.AddMonths(-1);
            Reload();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _month = _month.AddMonths(1);
            Reload();
        }

        // ============ 记一笔弹窗 ============

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var win = MakeDialogWindow("记一笔", 460, 540);
            var root = new StackPanel { Margin = new Thickness(18) };

            root.Children.Add(FormLabel("金额（必填）"));
            var amountBox = new TextBox
            {
                FontSize = 15, Height = 36, Padding = new Thickness(10, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            root.Children.Add(amountBox);

            root.Children.Add(FormLabel("类型"));
            bool isIncome = false;

            // 分类 chips：声明在类型按钮之前，确保 rebuildChips 捕获的变量已明确赋值
            var chipsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            string selectedCat = ExpenseCats[0].Key;
            var chipBorders = new List<Border>();
            void rebuildChips()
            {
                chipsPanel.Children.Clear();
                chipBorders.Clear();
                var cats = isIncome ? IncomeCats : ExpenseCats;
                foreach (var c in cats)
                {
                    var key = c.Key;
                    var chip = Chip($"{c.Emoji} {c.Key}");
                    chip.Tag = key;
                    chip.MouseLeftButtonDown += (s2, ev2) =>
                    {
                        selectedCat = key;
                        foreach (var b in chipBorders)
                            b.Style = (Style)FindResource(b.Tag?.ToString() == selectedCat ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
                    };
                    chipBorders.Add(chip);
                    chipsPanel.Children.Add(chip);
                }
                // 默认选中第一个
                selectedCat = cats[0].Key;
                foreach (var b in chipBorders)
                    b.Style = (Style)FindResource(b.Tag?.ToString() == selectedCat ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            }

            var typePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var expenseBtn = Pill("支出", true);
            var incomeBtn = Pill("收入", false);
            void RefreshType()
            {
                expenseBtn.Style = (Style)FindResource(isIncome ? "SecondaryButtonStyle" : "PrimaryButtonStyle");
                incomeBtn.Style = (Style)FindResource(isIncome ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            }
            expenseBtn.Click += (s, ev) => { isIncome = false; RefreshType(); rebuildChips(); };
            incomeBtn.Click += (s, ev) => { isIncome = true; RefreshType(); rebuildChips(); };
            typePanel.Children.Add(expenseBtn);
            typePanel.Children.Add(incomeBtn);
            root.Children.Add(typePanel);

            root.Children.Add(FormLabel("分类"));
            root.Children.Add(chipsPanel);
            rebuildChips();

            root.Children.Add(FormLabel("日期"));
            var dateBox = new TextBox
            {
                Text = DateTime.Now.ToString("yyyy-MM-dd"),
                FontSize = 13, Height = 34, Width = 160, Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left
            };
            root.Children.Add(dateBox);

            root.Children.Add(FormLabel("备注（可选）"));
            var noteBox = new TextBox
            {
                FontSize = 13, Height = 34, Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            root.Children.Add(noteBox);

            var saveBtn = new Button
            {
                Content = "保存",
                Style = (Style)FindResource("PrimaryButtonStyle"),
                FontSize = 13, Padding = new Thickness(24, 7, 24, 7),
                Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            saveBtn.Click += (s, ev) =>
            {
                if (!double.TryParse(amountBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) || amt <= 0)
                {
                    MessageBox.Show("金额需填一个正数", "记一笔", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var date = NormalizeDate(dateBox.Text.Trim()) ?? DateTime.Now.ToString("yyyy-MM-dd");
                ExpenseRepository.Insert(new ExpenseRecord
                {
                    Date = date,
                    Amount = amt,
                    IsIncome = isIncome,
                    Category = selectedCat,
                    Note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim()
                });
                win.Close();
                Reload();
            };
            root.Children.Add(saveBtn);

            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 弹窗基础件（仿 CustomModulesView.MakeDialogWindow） ============

        private Window MakeDialogWindow(string title, double w, double h)
        {
            var win = new Window
            {
                Title = title, Width = w, Height = h,
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
            };
            var outer = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = (Brush)FindResource("BackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1)
            };
            outer.Effect = new System.Windows.Media.Effects.DropShadowEffect { Opacity = 0.25, BlurRadius = 24, ShadowDepth = 2 };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Border
            {
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Background = (Brush)FindResource("CardBrush")
            };
            var tb = new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            var closeBtn = new Button
            {
                Content = "✕", Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(8, 2, 8, 2), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => win.Close();
            var titleGrid = new Grid();
            titleGrid.Children.Add(tb); titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            Grid.SetRow(titleBar, 0);
            grid.Children.Add(titleBar);

            var contentHost = new Border { Padding = new Thickness(0) };
            Grid.SetRow(contentHost, 1);
            grid.Children.Add(contentHost);

            outer.Child = grid;
            win.Content = outer;
            win.Tag = contentHost;
            win.Resources = this.Resources;
            return win;
        }

        // ============ 小工具 ============

        private static TextBlock FormLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = (Brush)Application.Current.FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 10, 0, 4)
        };

        private static Button Pill(string content, bool selected) => new Button
        {
            Content = content,
            Style = (Style)Application.Current.FindResource(selected ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
            FontSize = 12.5, Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand
        };

        private static Border Chip(string content) => new Border
        {
            Child = new TextBlock
            {
                Text = content, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            },
            Style = (Style)Application.Current.FindResource("SecondaryButtonStyle"),
            Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6), Cursor = Cursors.Hand
        };

        /// <summary>把各种日期写法规整成 yyyy-MM-dd；解析失败返回 null</summary>
        private static string NormalizeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().Replace('/', '-');
            if (s.Length >= 10 && DateTime.TryParse(s.Substring(0, 10), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.ToString("yyyy-MM-dd");
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                return d2.ToString("yyyy-MM-dd");
            return null;
        }
    }
}
