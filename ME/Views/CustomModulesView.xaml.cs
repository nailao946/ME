using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    /// <summary>
    /// 自定义模块页：模块切换 + 仪表盘（内置统计 + 用户可增删改的统计组件）+ 记一笔 + 历史。
    /// 数据与安卓端 custom_modules.json 互通；仪表盘组件配置存 custom_dashboards.json（安卓忽略）。
    /// </summary>
    public partial class CustomModulesView : UserControl
    {
        /// <summary>图标集（与安卓 ModuleIconList 同一顺序，存索引）</summary>
        public static readonly string[] ModuleIcons =
        {
            "❤️", "🏋️", "🏃", "💧", "🌙", "😊", "📖", "🎓", "💼", "🏠", "🛒", "☕", "🧘", "🎵", "🐾", "📚"
        };
        private static readonly string[] FieldTypeNames = { "数值", "文本", "时间", "是否", "单选" };
        private static readonly string[] FieldTypes = { "number", "text", "time", "bool", "select" };
        private static readonly string[] PiePalette = { "#4F6EF7", "#34C759", "#FF9500", "#AF52DE", "#FF2D55", "#8E8E93" };

        private List<CustomModule> _modules = new();
        private int _selectedModuleId;

        public CustomModulesView()
        {
            InitializeComponent();
            Loaded += (s, e) => Reload();
        }

        public void Reload()
        {
            _modules = CustomModuleRepository.GetAll();
            if (_selectedModuleId == 0 || _modules.All(m => m.Id != _selectedModuleId))
                _selectedModuleId = _modules.Count > 0 ? _modules[0].Id : 0;
            RenderChips();
            RenderDashboard();
        }

        // ============ 模块 chips ============

        private void RenderChips()
        {
            ModuleChips.Children.Clear();
            if (_modules.Count == 0) return;
            foreach (var m in _modules)
            {
                var chip = BuildModuleChip(m);
                ModuleChips.Children.Add(chip);
            }
        }

        private FrameworkElement BuildModuleChip(CustomModule m)
        {
            bool selected = m.Id == _selectedModuleId;
            var color = ParseColor(m.ColorHex);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = ModuleIcons[Math.Min(m.Icon, ModuleIcons.Length - 1)], FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock
            {
                Text = m.Name, FontSize = 12.5, Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal
            });
            var border = new Border
            {
                Child = content,
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Background = selected ? new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B)) : Brushes.Transparent,
                BorderBrush = selected ? new SolidColorBrush(color) : (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = m.Name
            };
            border.MouseLeftButtonDown += (s, e) => { _selectedModuleId = m.Id; RenderChips(); RenderDashboard(); };
            return border;
        }

        // ============ 仪表盘 ============

        private void RenderDashboard()
        {
            DashboardHost.Children.Clear();
            if (_modules.Count == 0)
            {
                DashboardHost.Children.Add(new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Child = new TextBlock
                    {
                        Text = "还没有模块。点右上角「＋ 新建模块」创建第一个，比如「跑步」记数值 km、「喝水」记杯数、「日记」记文本。",
                        Foreground = (Brush)FindResource("SecondaryTextBrush"),
                        FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6)
                    }
                });
                return;
            }
            var m = _modules.First(x => x.Id == _selectedModuleId);
            var fresh = CustomModuleRepository.GetAll().First(x => x.Id == m.Id); // 拿最新记录数
            DashboardHost.Children.Add(BuildDashboard(fresh));
        }

        private FrameworkElement BuildDashboard(CustomModule m)
        {
            var color = ParseColor(m.ColorHex);
            var root = new StackPanel();

            // —— 模块标题行 ——
            var headGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBox = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)),
                Child = new TextBlock
                {
                    Text = ModuleIcons[Math.Min(m.Icon, ModuleIcons.Length - 1)],
                    FontSize = 21, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconBox, 0);

            var titleCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            titleCol.Children.Add(new TextBlock { Text = m.Name, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
            var fieldsText = string.Join("、", m.Fields.Select(f => f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"({f.Unit})")));
            titleCol.Children.Add(new TextBlock
            {
                Text = $"{(fieldsText == "" ? "无字段" : fieldsText)} · 共 {m.Records.Count} 条记录",
                FontSize = 11.5, Foreground = (Brush)FindResource("SecondaryTextBrush")
            });
            Grid.SetColumn(titleCol, 1);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button MakeBtn(string content, RoutedEventHandler onClick, bool primary = false)
            {
                var b = new Button
                {
                    Content = content,
                    Style = (Style)FindResource(primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
                    Padding = new Thickness(13, 6, 13, 6), FontSize = 12, Margin = new Thickness(6, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                b.Click += onClick;
                return b;
            }
            btns.Children.Add(MakeBtn("＋ 记一笔", (s, e) => ShowRecordDialog(m), primary: true));
            btns.Children.Add(MakeBtn("全部记录", (s, e) => ShowHistoryDialog(m)));
            btns.Children.Add(MakeBtn("编辑", (s, e) => ShowEditorDialog(m)));
            btns.Children.Add(MakeBtn("删除", (s2, e2) =>
            {
                if (MessageBox.Show($"确定删除「{m.Name}」及其全部 {m.Records.Count} 条记录吗？", "删除模块",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                CustomModuleRepository.Delete(m.Id);
                if (_selectedModuleId == m.Id) _selectedModuleId = 0;
                Reload();
            }));
            Grid.SetColumn(btns, 2);

            headGrid.Children.Add(iconBox); headGrid.Children.Add(titleCol); headGrid.Children.Add(btns);
            root.Children.Add(headGrid);

            // —— 内置统计行（今日 / 本周 / 全部 / 连续） ——
            var builtins = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
            builtins.Children.Add(BuildStatCell("今日", m.Records.Count(r => InRange(r, "today")).ToString(), color));
            builtins.Children.Add(BuildStatCell("本周", m.Records.Count(r => InRange(r, "week")).ToString(), color));
            builtins.Children.Add(BuildStatCell("累计", m.Records.Count.ToString(), color));
            builtins.Children.Add(BuildStatCell("连续天数", CalcStreak(m).ToString() + " 天", color));
            root.Children.Add(builtins);

            // —— 用户组件区 ——
            var widgets = CustomDashboardRepository.GetFor(m.Id);
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var w in widgets)
            {
                var card = BuildWidgetCard(m, w);
                if (card != null) wrap.Children.Add(card);
            }
            // 「添加组件」幽灵卡
            var addCard = new Border
            {
                Width = 232, MinHeight = 120, CornerRadius = new CornerRadius(12),
                BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1, 1, 1, 1),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 10),
                Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                ToolTip = "添加统计组件（数值统计 / 趋势图 / 分布占比 / 连续打卡）"
            };
            addCard.Child = new TextBlock
            {
                Text = "＋ 添加组件", FontSize = 12.5,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            addCard.MouseEnter += (s, e) => addCard.Background = (Brush)FindResource("NavHoverBrush");
            addCard.MouseLeave += (s, e) => addCard.Background = Brushes.Transparent;
            addCard.MouseLeftButtonDown += (s, e) => ShowWidgetEditorDialog(m, null);
            wrap.Children.Add(addCard);
            root.Children.Add(wrap);

            // —— 最近记录 ——
            var recent = m.Records.OrderByDescending(r => r.Date).ThenByDescending(r => r.Time).Take(6).ToList();
            if (recent.Count > 0)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"), Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(14)
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = "最近记录", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 8) });
                foreach (var r in recent) sp.Children.Add(BuildRecordRow(m, r, reloadAfterDelete: false));
                var more = new TextBlock
                {
                    Text = "查看全部记录 →", FontSize = 11.5, Cursor = Cursors.Hand, Margin = new Thickness(0, 6, 0, 0),
                    Foreground = (Brush)FindResource("PrimaryBrush")
                };
                more.MouseLeftButtonDown += (s, e) => ShowHistoryDialog(m);
                sp.Children.Add(more);
                card.Child = sp;
                root.Children.Add(card);
            }
            return root;
        }

        private FrameworkElement BuildStatCell(string label, string value, Color color)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("CardBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 0), MinWidth = 110
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
            sp.Children.Add(new TextBlock
            {
                Text = value, FontSize = 21, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 2, 0, 0)
            });
            card.Child = sp;
            return card;
        }

        private FrameworkElement BuildRecordRow(CustomModule m, CustomModuleRecord r, bool reloadAfterDelete)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var vals = string.Join(" · ", r.Values.Select(kv =>
            {
                var f = m.Fields.FirstOrDefault(x => x.Key == kv.Key);
                return $"{(f?.Label ?? kv.Key)}: {kv.Value}{(string.IsNullOrEmpty(f?.Unit) ? "" : " " + f.Unit)}";
            }));
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = vals == "" ? (string.IsNullOrEmpty(r.Note) ? "（无字段值）" : r.Note) : vals,
                FontSize = 12.5, Foreground = (Brush)FindResource("TextBrush"), TextWrapping = TextWrapping.Wrap
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{r.Date} {r.Time}{(string.IsNullOrEmpty(r.Note) || vals == "" ? "" : " · " + r.Note)}",
                FontSize = 10.5, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 1, 0, 0)
            });
            Grid.SetColumn(info, 0);

            var delBtn = new Button { Content = "✕", Style = (Style)FindResource("SecondaryButtonStyle"), FontSize = 11, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "删除该记录" };
            delBtn.Click += (s, e) =>
            {
                if (MessageBox.Show("删除这条记录？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                CustomModuleRepository.DeleteRecord(m.Id, r.Id);
                Reload();
                if (reloadAfterDelete) { /* 历史弹窗自行刷新 */ }
            };
            Grid.SetColumn(delBtn, 1);
            row.Children.Add(info); row.Children.Add(delBtn);
            return row;
        }

        // ============ 统计组件卡片 ============

        private FrameworkElement BuildWidgetCard(CustomModule m, CustomDashboardWidget w)
        {
            var color = ParseColor(m.ColorHex);
            var card = new Border
            {
                Width = 232, MinHeight = 120, CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("CardBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(12), Cursor = Cursors.Hand,
                ToolTip = "点击编辑组件"
            };
            card.MouseLeftButtonDown += (s, e) => ShowWidgetEditorDialog(m, w);

            FrameworkElement body = w.Type switch
            {
                "chart" => BuildWidgetChartBody(m, w, color),
                "pie" => BuildWidgetPieBody(m, w, color),
                "streak" => BuildWidgetStreakBody(m, w, color),
                _ => BuildWidgetStatBody(m, w, color)
            };
            card.Child = body;
            return card;
        }

        private string WidgetTitle(CustomModule m, CustomDashboardWidget w)
        {
            var f = m.Fields.FirstOrDefault(x => x.Key == w.FieldKey);
            var fname = f?.Label ?? "记录数";
            return w.Type switch
            {
                "chart" => $"{fname} · 近 {w.Days} 天趋势",
                "pie" => $"{fname} · 分布",
                "streak" => f == null ? "连续打卡" : $"{fname} · 连续",
                _ => $"{AggName(w.Agg)}{RangeName(w.Range)} · {fname}"
            };
        }

        private FrameworkElement WidgetShell(string title, string colorHex, UIElement content, FrameworkElement legend = null)
        {
            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            head.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Fill = new SolidColorBrush(ParseColor(colorHex)),
                VerticalAlignment = VerticalAlignment.Center
            });
            var t = new TextBlock
            {
                Text = title, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            head.Children.Add(t);
            sp.Children.Add(head);
            sp.Children.Add(content);
            if (legend != null) sp.Children.Add(legend);
            return sp;
        }

        private FrameworkElement BuildWidgetStatBody(CustomModule m, CustomDashboardWidget w, Color color)
        {
            var f = m.Fields.FirstOrDefault(x => x.Key == w.FieldKey);
            var rs = m.Records.Where(r => InRange(r, w.Range)).ToList();
            string display;
            if (w.Agg == "count" || f == null)
                display = rs.Count.ToString();
            else
            {
                var nums = rs.Where(r => r.Values.ContainsKey(f.Key) && double.TryParse(r.Values[f.Key], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                             .Select(r => double.Parse(r.Values[f.Key], NumberStyles.Any, CultureInfo.InvariantCulture)).ToList();
                display = (w.Agg, nums.Count) switch
                {
                    (_, 0) => "—",
                    ("sum", _) => nums.Sum().ToString("0.#"),
                    ("avg", _) => nums.Average().ToString("0.#"),
                    ("max", _) => nums.Max().ToString("0.#"),
                    ("min", _) => nums.Min().ToString("0.#"),
                    ("latest", _) => rs.Where(r => r.Values.ContainsKey(f.Key) && double.TryParse(r.Values[f.Key], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                                       .OrderBy(r => r.Date).ThenBy(r => r.Time).Last().Values[f.Key],
                    _ => nums.Count.ToString()
                };
            }
            if (f != null && !string.IsNullOrEmpty(f.Unit) && w.Agg != "latest" && display != "—")
                display += $" {f.Unit}";
            var val = new TextBlock
            {
                Text = display, FontSize = 26, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center
            };
            return WidgetShell(WidgetTitle(m, w), m.ColorHex, val);
        }

        private FrameworkElement BuildWidgetChartBody(CustomModule m, CustomDashboardWidget w, Color color)
        {
            var f = m.Fields.FirstOrDefault(x => x.Key == w.FieldKey);
            if (f == null) return WidgetShell(WidgetTitle(m, w), m.ColorHex, new TextBlock { Text = "字段不存在", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
            var canvas = BuildTrendCanvas(m, f, w.Days, 200, 74);
            return WidgetShell(WidgetTitle(m, w), m.ColorHex, canvas);
        }

        private FrameworkElement BuildWidgetPieBody(CustomModule m, CustomDashboardWidget w, Color color)
        {
            var f = m.Fields.FirstOrDefault(x => x.Key == w.FieldKey);
            if (f == null) return WidgetShell(WidgetTitle(m, w), m.ColorHex, new TextBlock { Text = "字段不存在", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
            var rs = m.Records.Where(r => InRange(r, w.Range) && r.Values.ContainsKey(f.Key)).ToList();
            if (rs.Count == 0)
                return WidgetShell(WidgetTitle(m, w), m.ColorHex, new TextBlock { Text = "暂无数据", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });

            var groups = rs.GroupBy(r => r.Values[f.Key]).OrderByDescending(g => g.Count()).ToList();
            var legend = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var donut = new DonutChartDrawable(groups.Select((g, i) => (g.Count() * 1.0 / rs.Count, PiePalette[i % PiePalette.Length])).ToList(), 92);

            int shown = 0;
            foreach (var g in groups.Take(4))
            {
                shown++;
                var li = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                li.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(ParseColor(PiePalette[(shown - 1) % PiePalette.Length])), VerticalAlignment = VerticalAlignment.Center });
                li.Children.Add(new TextBlock
                {
                    Text = $" {g.Key}  {g.Count()}（{g.Count() * 100 / rs.Count}%）",
                    FontSize = 10.5, Foreground = (Brush)FindResource("SecondaryTextBrush")
                });
                legend.Children.Add(li);
            }
            return WidgetShell(WidgetTitle(m, w), m.ColorHex, donut, legend);
        }

        private FrameworkElement BuildWidgetStreakBody(CustomModule m, CustomDashboardWidget w, Color color)
        {
            int streak;
            if (string.IsNullOrEmpty(w.FieldKey))
                streak = CalcStreak(m);
            else
            {
                var days = m.Records.Where(r => r.Values.TryGetValue(w.FieldKey, out var v) && (v == "true" || v == "是"))
                    .Select(r => r.Date).Distinct().OrderByDescending(d => d).ToList();
                streak = CountConsecutive(days);
            }
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = streak.ToString(), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = " 天", FontSize = 13, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 0, 4) });
            return WidgetShell(WidgetTitle(m, w), m.ColorHex, sp);
        }

        // ============ 组件编辑对话框 ============

        private void ShowWidgetEditorDialog(CustomModule m, CustomDashboardWidget existing)
        {
            var win = MakeDialogWindow(existing == null ? "添加组件" : "编辑组件", 480, 430);
            var root = new StackPanel { Margin = new Thickness(18) };
            var widgets = CustomDashboardRepository.GetFor(m.Id);

            var draft = existing == null
                ? new CustomDashboardWidget { Range = "all", Days = 30, Agg = "sum" }
                : new CustomDashboardWidget { Id = existing.Id, Type = existing.Type, FieldKey = existing.FieldKey, Agg = existing.Agg, Range = existing.Range, Days = existing.Days };

            // 先声明全部控件（类型按钮的 Click 会引用它们）
            var fieldLabel = new TextBlock { Text = "统计字段", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 12, 0, 6) };
            var fieldCombo = new ComboBox { FontSize = 12.5, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200 };
            var aggLabel = new TextBlock { Text = "聚合方式", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 12, 0, 6) };
            var aggCombo = new ComboBox { FontSize = 12.5, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200, ItemsSource = new[] { "求和 sum", "平均 avg", "最大 max", "最小 min", "最新 latest", "记录数 count" } };
            var rangeLabel = new TextBlock { Text = "统计范围", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 12, 0, 6) };
            var rangeCombo = new ComboBox { FontSize = 12.5, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200, ItemsSource = new[] { "今天", "本周", "本月", "全部" } };
            var daysLabel = new TextBlock { Text = "趋势天数", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 12, 0, 6) };
            var daysCombo = new ComboBox { FontSize = 12.5, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200, ItemsSource = new[] { "近 7 天", "近 30 天" } };

            root.Children.Add(new TextBlock { Text = "组件类型", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 6) });
            var typePanel = new UniformGrid { Columns = 4 };
            var typeOpts = new[] { ("stat", "数值统计"), ("chart", "趋势图"), ("pie", "分布占比"), ("streak", "连续打卡") };
            var typeBtns = new List<Button>();
            foreach (var (tp, name) in typeOpts)
            {
                var b = new Button
                {
                    Content = name, FontSize = 12, Height = 36, Padding = new Thickness(4), Margin = new Thickness(2, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                b.Click += (s, e) => { draft.Type = tp; RefreshTypeUi(); };
                typeBtns.Add(b);
                typePanel.Children.Add(b);
            }
            root.Children.Add(typePanel);

            root.Children.Add(fieldLabel);
            root.Children.Add(fieldCombo);
            root.Children.Add(aggLabel);
            root.Children.Add(aggCombo);
            root.Children.Add(rangeLabel);
            root.Children.Add(rangeCombo);
            root.Children.Add(daysLabel);
            root.Children.Add(daysCombo);

            aggCombo.SelectedIndex = draft.Agg switch { "sum" => 0, "avg" => 1, "max" => 2, "min" => 3, "latest" => 4, _ => 5 };
            aggCombo.SelectionChanged += (s, e) => draft.Agg = new[] { "sum", "avg", "max", "min", "latest", "count" }[aggCombo.SelectedIndex];
            rangeCombo.SelectedIndex = draft.Range switch { "today" => 0, "week" => 1, "month" => 2, _ => 3 };
            rangeCombo.SelectionChanged += (s, e) => draft.Range = new[] { "today", "week", "month", "all" }[rangeCombo.SelectedIndex];
            daysCombo.SelectedIndex = draft.Days <= 7 ? 0 : 1;
            daysCombo.SelectionChanged += (s, e) => draft.Days = daysCombo.SelectedIndex == 0 ? 7 : 30;
            fieldCombo.SelectionChanged += (s, e) => { if (fieldCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag) draft.FieldKey = tag; };

            void RefreshTypeUi()
            {
                foreach (var b in typeBtns)
                    b.Style = (Style)FindResource(typeOpts[typeBtns.IndexOf(b)].Item1 == draft.Type ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

                // 字段候选
                fieldCombo.Items.Clear();
                var candidates = new List<(string key, string label)>();
                if (draft.Type == "stat")
                {
                    candidates.Add(("", "记录数"));
                    candidates.AddRange(m.Fields.Where(f => f.Type == "number").Select(f => (f.Key, f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"（{f.Unit}）"))));
                }
                else if (draft.Type == "chart")
                    candidates.AddRange(m.Fields.Where(f => f.Type == "number").Select(f => (f.Key, f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"（{f.Unit}）"))));
                else if (draft.Type == "pie")
                    candidates.AddRange(m.Fields.Where(f => f.Type == "select" || f.Type == "bool" || f.Type == "text").Select(f => (f.Key, f.Label)));
                else // streak
                {
                    candidates.Add(("", "任意记录"));
                    candidates.AddRange(m.Fields.Where(f => f.Type == "bool").Select(f => (f.Key, f.Label)));
                }
                if (candidates.Count == 0) candidates.Add(("", "（暂无合适字段）"));
                foreach (var (k, lb) in candidates) fieldCombo.Items.Add(new ComboBoxItem { Content = lb, Tag = k });
                var idx = candidates.FindIndex(c => c.key == draft.FieldKey);
                fieldCombo.SelectedIndex = idx >= 0 ? idx : 0;
                draft.FieldKey = (string)((ComboBoxItem)fieldCombo.SelectedItem).Tag;

                bool isStat = draft.Type == "stat";
                aggLabel.Visibility = aggCombo.Visibility = isStat ? Visibility.Visible : Visibility.Collapsed;
                daysLabel.Visibility = daysCombo.Visibility = draft.Type == "chart" ? Visibility.Visible : Visibility.Collapsed;
                rangeLabel.Visibility = rangeCombo.Visibility = draft.Type == "stat" || draft.Type == "pie" ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshTypeUi();

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            if (existing != null)
            {
                var delBtn = new Button { Content = "删除组件", Style = (Style)FindResource("DangerButtonStyle"), Padding = new Thickness(14, 7, 14, 7), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
                delBtn.Click += (s, e) =>
                {
                    widgets.RemoveAll(x => x.Id == existing.Id);
                    CustomDashboardRepository.SaveFor(m.Id, widgets);
                    win.Close(); Reload();
                };
                btnRow.Children.Add(delBtn);
            }
            var saveBtn = new Button { Content = "保存组件", Style = (Style)FindResource("PrimaryButtonStyle"), Padding = new Thickness(20, 7, 20, 7), FontSize = 12.5, Cursor = Cursors.Hand };
            saveBtn.Click += (s, e) =>
            {
                if (draft.Type == "chart" && m.Fields.All(f => f.Key != draft.FieldKey)) { MessageBox.Show("该类型需要先有对应类型的字段（如数值字段）"); return; }
                if (existing == null) widgets.Add(draft);
                else
                {
                    var i = widgets.FindIndex(x => x.Id == existing.Id);
                    if (i >= 0) widgets[i] = draft;
                }
                CustomDashboardRepository.SaveFor(m.Id, widgets);
                win.Close(); Reload();
            };
            btnRow.Children.Add(saveBtn);
            root.Children.Add(btnRow);

            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 统计工具 ============

        private static bool InRange(CustomModuleRecord r, string range)
        {
            if (!DateTime.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return range == "all";
            var today = DateTime.Today;
            return range switch
            {
                "today" => d == today,
                "week" => IsSameWeek(d, today),
                "month" => d.Year == today.Year && d.Month == today.Month,
                _ => true
            };
        }

        private static bool IsSameWeek(DateTime a, DateTime b)
        {
            // 周一为一周开始
            static int BackToMonday(DateTime d) => ((int)d.DayOfWeek + 6) % 7;
            var ma = a.AddDays(-BackToMonday(a)).Date;
            var mb = b.AddDays(-BackToMonday(b)).Date;
            return ma == mb;
        }

        private static int CountConsecutive(List<string> datesDesc)
        {
            if (datesDesc.Count == 0) return 0;
            var set = new HashSet<string>(datesDesc);
            var start = DateTime.Today;
            if (!set.Contains(start.ToString("yyyy-MM-dd")))
            {
                start = start.AddDays(-1);
                if (!set.Contains(start.ToString("yyyy-MM-dd"))) return 0;
            }
            int n = 0;
            while (set.Contains(start.ToString("yyyy-MM-dd"))) { n++; start = start.AddDays(-1); }
            return n;
        }

        private static int CalcStreak(CustomModule m) =>
            CountConsecutive(m.Records.Select(r => r.Date).Distinct().OrderByDescending(d => d).ToList());

        private static string AggName(string a) => a switch { "sum" => "求和", "avg" => "平均", "max" => "最大", "min" => "最小", "latest" => "最新", _ => "记录数" };
        private static string RangeName(string r) => r switch { "today" => "（今日）", "week" => "（本周）", "month" => "（本月）", _ => "" };

        /// <summary>环形图（纯 WPF Path 弧段）</summary>
        private class DonutChartDrawable : FrameworkElement
        {
            private readonly List<(double frac, string color)> _segs;
            private readonly double _size;
            public DonutChartDrawable(List<(double, string)> segs, double size)
            {
                _segs = segs; _size = size; Width = size; Height = size;
            }
            protected override void OnRender(DrawingContext dc)
            {
                var total = _segs.Sum(s => s.frac);
                if (total <= 0) return;
                var c = new Point(_size / 2, _size / 2);
                var rOut = _size / 2 - 2;
                var rIn = rOut * 0.62;
                double start = -90; // 从正上方开始
                var brushCache = new Dictionary<string, Brush>();
                foreach (var (frac, color) in _segs)
                {
                    var sweep = 360.0 * frac / total;
                    var end = start + sweep;
                    Brush br;
                    if (!brushCache.TryGetValue(color, out br))
                    {
                        br = new SolidColorBrush(ParseColorStatic(color)); br.Freeze();
                        brushCache[color] = br;
                    }
                    if (_segs.Count == 1)
                    {
                        dc.DrawEllipse(null, new Pen(br, rOut - rIn), c, (rOut + rIn) / 2, (rOut + rIn) / 2);
                    }
                    else
                    {
                        var geo = RingSegment(c, rIn, rOut, start, Math.Max(0.5, sweep - 1.2));
                        dc.DrawGeometry(br, null, geo);
                    }
                    start = end;
                }
            }
            private static Geometry RingSegment(Point c, double rIn, double rOut, double a0, double a1)
            {
                Point P(double r, double aDeg)
                {
                    var rad = aDeg * Math.PI / 180;
                    return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
                }
                var large = (a1 - a0) > 180 ? 1 : 0;
                var fig = new PathFigure { StartPoint = P(rOut, a0) };
                fig.Segments.Add(new ArcSegment(P(rOut, a1), new Size(rOut, rOut), 0, large == 1, SweepDirection.Clockwise, true));
                fig.Segments.Add(new LineSegment(P(rIn, a1), true));
                fig.Segments.Add(new ArcSegment(P(rIn, a0), new Size(rIn, rIn), 0, large == 1, SweepDirection.Counterclockwise, true));
                fig.IsClosed = true;
                return new PathGeometry(new[] { fig });
            }
        }

        /// <summary>趋势折线画布（可指定尺寸）</summary>
        private FrameworkElement BuildTrendCanvas(CustomModule m, CustomModuleField f, int days, double w, double h)
        {
            var since = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
            var points = m.Records
                .Where(r => string.Compare(r.Date, since) >= 0 && r.Values.ContainsKey(f.Key) && double.TryParse(r.Values[f.Key], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .OrderBy(r => r.Date).ThenBy(r => r.Time)
                .Select(r => (Date: r.Date, Value: double.Parse(r.Values[f.Key], NumberStyles.Any, CultureInfo.InvariantCulture)))
                .ToList();
            if (points.Count == 0)
                return new TextBlock { Text = "该范围暂无数据", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 20, 0, 20), HorizontalAlignment = HorizontalAlignment.Center };

            double max = points.Max(p => p.Value) <= 0 ? 1 : points.Max(p => p.Value);
            double min = Math.Min(0, points.Min(p => p.Value));
            if (max - min < 0.0001) max = min + 1;

            var canvas = new Canvas { Height = h, ClipToBounds = true };
            double padL = 4, padB = 12;
            var col = new SolidColorBrush(ParseColor(m.ColorHex));
            var gridBrush = (Brush)FindResource("BorderBrush");
            for (int g = 0; g <= 2; g++)
            {
                var y = (h - padB) * g / 2.0;
                canvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = padL, X2 = w, Y1 = y, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 0.6,
                    StrokeDashArray = new DoubleCollection { 3, 3 }
                });
            }
            if (points.Count == 1)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = col };
                Canvas.SetLeft(dot, w / 2 - 3.5); Canvas.SetTop(dot, (h - padB) / 2 - 3.5);
                canvas.Children.Add(dot);
                var lbl = new TextBlock { Text = $"{points[0].Date}：{points[0].Value:0.#}", FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush") };
                Canvas.SetLeft(lbl, w / 2 - 30); Canvas.SetTop(lbl, h - padB + 2);
                canvas.Children.Add(lbl);
                return canvas;
            }
            Func<int, double> x = i => padL + i * ((w - padL - 4) / (points.Count - 1));
            Func<double, double> yOf = v => (1 - (v - min) / (max - min)) * (h - padB - 4) + 2;
            var poly = new System.Windows.Shapes.Polyline
            {
                Stroke = col, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round
            };
            for (int i = 0; i < points.Count; i++) poly.Points.Add(new Point(x(i), yOf(points[i].Value)));
            canvas.Children.Add(poly);
            foreach (var (p, i) in points.Select((p, i) => (p, i)))
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = col };
                Canvas.SetLeft(dot, x(i) - 2.5); Canvas.SetTop(dot, yOf(p.Value) - 2.5);
                canvas.Children.Add(dot);
            }
            var first = new TextBlock { Text = points.First().Date.Substring(5), FontSize = 9, Foreground = (Brush)FindResource("SecondaryTextBrush") };
            Canvas.SetLeft(first, padL); Canvas.SetTop(first, h - padB + 1);
            var last = new TextBlock { Text = points.Last().Date.Substring(5), FontSize = 9, Foreground = (Brush)FindResource("SecondaryTextBrush") };
            Canvas.SetLeft(last, w - 34); Canvas.SetTop(last, h - padB + 1);
            canvas.Children.Add(first); canvas.Children.Add(last);
            return canvas;
        }

        // ============ 新建 / 编辑模块（沿用原逻辑） ============

        private void AddModule_Click(object sender, RoutedEventArgs e) => ShowEditorDialog(null);

        private void ShowEditorDialog(CustomModule initial)
        {
            var win = MakeDialogWindow(initial == null ? "新建模块" : "编辑模块", 560, 620);

            var root = new StackPanel { Margin = new Thickness(18) };

            var nameBox = new TextBox { Text = initial?.Name ?? "", FontSize = 13, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            root.Children.Add(FormRow("模块名称", nameBox));

            int iconIdx = initial?.Icon ?? 0;
            var iconPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            string colorHex = initial?.ColorHex ?? "#4F6EF7";
            for (int i = 0; i < ModuleIcons.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Content = ModuleIcons[i], FontSize = 16, Width = 40, Height = 36,
                    Padding = new Thickness(0),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    Margin = new Thickness(0, 0, 4, 4),
                    Style = (Style)FindResource("SecondaryButtonStyle"), Cursor = Cursors.Hand
                };
                b.Click += (s, e) => iconIdx = idx;
                iconPanel.Children.Add(b);
            }
            root.Children.Add(FormRow("图标（点击选中）", iconPanel));

            var colorBox = new TextBox { Text = colorHex, FontSize = 13, Height = 34, Width = 130, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            root.Children.Add(FormRow("颜色（#RRGGBB）", colorBox));

            // 字段编辑
            var fieldsPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var fields = initial?.Fields.Select(f => new FieldDraft { Label = f.Label, Type = f.Type, Unit = f.Unit ?? "", Options = f.Options ?? "" }).ToList()
                         ?? new List<FieldDraft> { new FieldDraft { Label = "数值", Type = "number" } };

            void RenderFields()
            {
                fieldsPanel.Children.Clear();
                for (int i = 0; i < fields.Count; i++)
                {
                    int idx = i;
                    var f = fields[i];
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

                    var labelBox = new TextBox { Text = f.Label, FontSize = 12, Height = 30, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
                    labelBox.TextChanged += (s, e) => f.Label = labelBox.Text;
                    Grid.SetColumn(labelBox, 0);

                    var typeCombo = new ComboBox { FontSize = 12, Height = 30, ItemsSource = FieldTypeNames, VerticalContentAlignment = VerticalAlignment.Center, SelectedIndex = Math.Max(0, Array.IndexOf(FieldTypes, f.Type)) };
                    typeCombo.SelectionChanged += (s, e) => { f.Type = FieldTypes[typeCombo.SelectedIndex]; RenderFields(); };
                    Grid.SetColumn(typeCombo, 2);

                    var unitBox = new TextBox { Text = f.Unit, FontSize = 12, Height = 30, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
                    unitBox.TextChanged += (s, e) => f.Unit = unitBox.Text;
                    Grid.SetColumn(unitBox, 4);

                    var delBtn = new Button { Content = "移除", Style = (Style)FindResource("DangerButtonStyle"), FontSize = 11, Padding = new Thickness(6, 4, 6, 4) };
                    delBtn.Click += (s, e) => { if (fields.Count > 1) { fields.RemoveAt(idx); RenderFields(); } };
                    Grid.SetColumn(delBtn, 6);

                    row.Children.Add(labelBox); row.Children.Add(typeCombo); row.Children.Add(unitBox); row.Children.Add(delBtn);
                    fieldsPanel.Children.Add(row);

                    if (f.Type == "select")
                    {
                        var optBox = new TextBox
                        {
                            Text = f.Options, FontSize = 12, Height = 30, Padding = new Thickness(6, 3, 6, 3),
                            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6),
                            Tag = "候选值，逗号分隔，如：好,中,差"
                        };
                        optBox.TextChanged += (s, e) => f.Options = optBox.Text;
                        fieldsPanel.Children.Add(optBox);
                    }
                }
            }
            RenderFields();

            root.Children.Add(new TextBlock { Text = "字段定义", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 10, 0, 4) });
            root.Children.Add(fieldsPanel);
            var addFieldBtn = new Button { Content = "＋ 添加字段", Style = (Style)FindResource("SecondaryButtonStyle"), FontSize = 12, Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            addFieldBtn.Click += (s, e) => { fields.Add(new FieldDraft { Label = "", Type = "number" }); RenderFields(); };
            root.Children.Add(addFieldBtn);

            var saveBtn = new Button { Content = "保存", Style = (Style)FindResource("PrimaryButtonStyle"), FontSize = 13, Padding = new Thickness(24, 7, 24, 7), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand };
            saveBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text)) { MessageBox.Show("请填写模块名称"); return; }
                var validFields = fields.Where(f => !string.IsNullOrWhiteSpace(f.Label))
                    .Select((f, i) => new CustomModuleField
                    {
                        Key = $"f{i + 1}",
                        Label = f.Label.Trim(), Type = f.Type,
                        Unit = string.IsNullOrWhiteSpace(f.Unit) ? null : f.Unit.Trim(),
                        Options = string.IsNullOrWhiteSpace(f.Options) ? null : f.Options
                    }).ToList();
                if (validFields.Count == 0) { MessageBox.Show("至少需要一个字段"); return; }
                if (initial == null)
                {
                    var nm = CustomModuleRepository.Add(new CustomModule { Name = nameBox.Text.Trim(), ColorHex = colorBox.Text.Trim(), Icon = iconIdx, Fields = validFields });
                    _selectedModuleId = nm.Id;
                }
                else
                {
                    initial.Name = nameBox.Text.Trim();
                    initial.ColorHex = colorBox.Text.Trim();
                    initial.Icon = iconIdx;
                    initial.Fields = validFields;
                    CustomModuleRepository.Update(initial);
                }
                win.Close();
                Reload();
            };
            root.Children.Add(saveBtn);

            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 记一笔（沿用） ============

        private void ShowRecordDialog(CustomModule m)
        {
            var win = MakeDialogWindow($"记录 · {m.Name}", 520, 560);
            var root = new StackPanel { Margin = new Thickness(18) };
            var values = new Dictionary<string, string>();

            var dateBox = new TextBox { Text = DateTime.Now.ToString("yyyy-MM-dd"), FontSize = 13, Height = 34, Width = 150, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            root.Children.Add(FormRow("日期", dateBox));

            foreach (var f in m.Fields)
            {
                UIElement input;
                if (f.Type == "bool")
                {
                    var cb = new ComboBox { FontSize = 12, Height = 32, Width = 120, ItemsSource = new[] { "是", "否" }, VerticalContentAlignment = VerticalAlignment.Center, SelectedIndex = 1 };
                    cb.SelectionChanged += (s, e) => values[f.Key] = cb.SelectedIndex == 0 ? "true" : "false";
                    input = cb;
                }
                else if (f.Type == "select")
                {
                    var opts = (f.Options ?? "").Split(',').Select(o => o.Trim()).Where(o => o != "").ToArray();
                    if (opts.Length == 0) opts = new[] { "选项1", "选项2" };
                    var combo = new ComboBox { FontSize = 12, Height = 32, Width = 160, ItemsSource = opts, VerticalContentAlignment = VerticalAlignment.Center };
                    combo.SelectionChanged += (s, e) => values[f.Key] = opts[combo.SelectedIndex];
                    input = combo;
                }
                else
                {
                    var tb = new TextBox { FontSize = 13, Height = 34, Width = 160, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
                    TextChangedHandler(f.Key);
                    void TextChangedHandler(string key) => tb.TextChanged += (s, e) => values[key] = tb.Text;
                    input = tb;
                }
                var label = f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"（{f.Unit}）");
                root.Children.Add(FormRow(label, input));
            }

            var noteBox = new TextBox { FontSize = 13, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            root.Children.Add(FormRow("备注（可选）", noteBox));

            var saveBtn = new Button { Content = "保存记录", Style = (Style)FindResource("PrimaryButtonStyle"), FontSize = 13, Padding = new Thickness(24, 7, 24, 7), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand };
            saveBtn.Click += (s, e) =>
            {
                CustomModuleRepository.AddRecord(m.Id, new CustomModuleRecord
                {
                    Date = dateBox.Text.Trim(),
                    Time = DateTime.Now.ToString("HH:mm"),
                    Values = new Dictionary<string, string>(values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))),
                    Note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim()
                });
                win.Close();
                Reload();
            };
            root.Children.Add(saveBtn);

            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 历史（沿用 + 图表更新） ============

        private void ShowHistoryDialog(CustomModule m)
        {
            var fresh = CustomModuleRepository.GetAll().First(x => x.Id == m.Id);
            var win = MakeDialogWindow($"{fresh.Name} · 全部记录（{fresh.Records.Count} 条）", 640, 600);
            var root = new StackPanel { Margin = new Thickness(18) };

            var numberField = fresh.Fields.FirstOrDefault(f => f.Type == "number");
            if (numberField != null && fresh.Records.Count(r => r.Values.ContainsKey(numberField.Key) && double.TryParse(r.Values[numberField.Key], out _)) >= 2)
            {
                root.Children.Add(new TextBlock { Text = $"{numberField.Label} 趋势", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 6) });
                root.Children.Add(BuildTrendCanvas(fresh, numberField, 365, 520, 130));
                root.Children.Add(new TextBlock { Text = " ", FontSize = 6 });
            }

            foreach (var r in fresh.Records.OrderByDescending(r => r.Date).ThenByDescending(r => r.Time).Take(100))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var vals = string.Join(" · ", r.Values.Select(kv =>
                {
                    var f = fresh.Fields.FirstOrDefault(x => x.Key == kv.Key);
                    return $"{(f?.Label ?? kv.Key)}: {kv.Value}{(string.IsNullOrEmpty(f?.Unit) ? "" : " " + f.Unit)}";
                }));
                var info = new TextBlock
                {
                    Text = $"{r.Date} {r.Time}    {vals}{(string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note)}",
                    FontSize = 12, Foreground = (Brush)FindResource("TextBrush"),
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(info, 0);

                var delBtn = new Button { Content = "✕", Style = (Style)FindResource("SecondaryButtonStyle"), FontSize = 11, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
                delBtn.Click += (s, e) =>
                {
                    CustomModuleRepository.DeleteRecord(fresh.Id, r.Id);
                    win.Close();
                    Reload();
                    ShowHistoryDialog(fresh);
                };
                Grid.SetColumn(delBtn, 1);
                row.Children.Add(info); row.Children.Add(delBtn);
                root.Children.Add(row);
            }
            if (fresh.Records.Count == 0)
                root.Children.Add(new TextBlock { Text = "还没有记录", Foreground = (Brush)FindResource("SecondaryTextBrush"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 20) });

            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 通用 ============

        private class FieldDraft { public string Label; public string Type; public string Unit = ""; public string Options = ""; }

        private Grid FormRow(string label, UIElement input)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock { Text = label, FontSize = 12.5, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(input, 1);
            g.Children.Add(lbl); g.Children.Add(input);
            return g;
        }

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
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Border
            {
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Background = (Brush)FindResource("CardBrush")
            };
            var tb = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            var closeBtn = new Button { Content = "✕", Style = (Style)FindResource("SecondaryButtonStyle"), Padding = new Thickness(8, 2, 8, 2), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand };
            closeBtn.Click += (s, e) => win.Close();
            var titleGrid = new Grid();
            titleGrid.Children.Add(tb); titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            Grid.SetRow(titleBar, 0);
            rootGrid.Children.Add(titleBar);

            var contentHost = new Border { Padding = new Thickness(0) };
            Grid.SetRow(contentHost, 1);
            rootGrid.Children.Add(contentHost);

            outer.Child = rootGrid;
            win.Content = outer;
            win.Tag = contentHost; // 对话框内容放进这里：((Border)win.Tag).Child = ...
            win.Resources = this.Resources;
            return win;
        }

        private static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(0x4F, 0x6E, 0xF7); }
        }

        private static Color ParseColorStatic(string hex) => ParseColor(hex);
    }
}
