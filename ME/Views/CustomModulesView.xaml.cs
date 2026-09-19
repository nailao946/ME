using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;
using ME.Services;

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

            // —— 模块主卡片（飞书卡片风格：头部信息 + 最近记录摘要 + 细分割线 + 底部操作区） ——
            var headCard = FeishuCard();
            var headRoot = new StackPanel();

            var headGrid = new Grid();
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
            var fieldsText = string.Join(" · ", m.Fields.Select(f => f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"（{f.Unit}）")));
            titleCol.Children.Add(new TextBlock
            {
                Text = fieldsText == "" ? "暂无字段" : fieldsText,
                FontSize = 11.5, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(titleCol, 1);

            // 记录数徽标（飞书卡片右上角常见的信息胶囊）
            var countBadge = new Border
            {
                CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B)),
                Padding = new Thickness(10, 4, 10, 4)
            };
            countBadge.Child = new TextBlock
            {
                Text = $"{m.Records.Count} 条", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            };
            Grid.SetColumn(countBadge, 2);

            headGrid.Children.Add(iconBox); headGrid.Children.Add(titleCol); headGrid.Children.Add(countBadge);
            headRoot.Children.Add(headGrid);

            // 最近一条记录摘要
            var last = m.Records.OrderByDescending(r => r.Date).ThenByDescending(r => r.Time).FirstOrDefault();
            if (last != null)
            {
                var summary = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 10, 0, 0),
                    Background = (Brush)FindResource("BackgroundBrush")
                };
                var sumSp = new StackPanel();
                sumSp.Children.Add(new TextBlock
                {
                    Text = $"最近记录 · {last.Date} {last.Time}",
                    FontSize = 10.5, Foreground = (Brush)FindResource("SecondaryTextBrush")
                });
                sumSp.Children.Add(new TextBlock
                {
                    Text = RecordValuesText(m, last), FontSize = 12,
                    Foreground = (Brush)FindResource("TextBrush"),
                    Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
                });
                summary.Child = sumSp;
                headRoot.Children.Add(summary);
            }

            headRoot.Children.Add(FeishuDivider());

            // 底部操作区：主操作在左，危险操作靠右
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(FeishuAction("＋", "记一笔", (Brush)FindResource("PrimaryBrush"), (s, e) => ShowRecordDialog(m)));
            actions.Children.Add(FeishuAction("🕘", "全部记录", (Brush)FindResource("SecondaryTextBrush"), (s, e) => ShowHistoryDialog(m)));
            actions.Children.Add(FeishuAction("📚", "资料库", (Brush)FindResource("SecondaryTextBrush"), (s, e) => ShowLibraryDialog(m)));
            actions.Children.Add(FeishuAction("🧾", "导出 CSV", (Brush)FindResource("SecondaryTextBrush"), (s, e) => ExportModule(m)));
            actions.Children.Add(FeishuAction("✎", "编辑", (Brush)FindResource("SecondaryTextBrush"), (s, e) => ShowEditorDialog(m)));
            actions.Children.Add(FeishuAction("🗑", "删除", new SolidColorBrush(Color.FromRgb(255, 59, 48)), (s2, e2) =>
            {
                if (MessageBox.Show($"确定删除「{m.Name}」及其全部 {m.Records.Count} 条记录吗？", "删除模块",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                CustomModuleRepository.Delete(m.Id);
                if (_selectedModuleId == m.Id) _selectedModuleId = 0;
                Reload();
            }));
            headRoot.Children.Add(actions);

            headCard.Child = headRoot;
            root.Children.Add(headCard);

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
                ToolTip = "添加统计组件（数值统计 / 趋势图 / 柱状图 / 分布占比 / 连续打卡 / 最近记录）"
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
                var card = FeishuCard();
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = "最近记录", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 8) });
                foreach (var r in recent) sp.Children.Add(BuildRecordRow(m, r, reloadAfterDelete: false));
                sp.Children.Add(FeishuDivider());
                var moreRow = new StackPanel { Orientation = Orientation.Horizontal };
                moreRow.Children.Add(FeishuAction("🕘", "查看全部记录", (Brush)FindResource("PrimaryBrush"), (s, e) => ShowHistoryDialog(m)));
                moreRow.Children.Add(FeishuAction("＋", "记一笔", (Brush)FindResource("SecondaryTextBrush"), (s, e) => ShowRecordDialog(m)));
                sp.Children.Add(moreRow);
                card.Child = sp;
                root.Children.Add(card);
            }
            return root;
        }

        // ============ 飞书卡片风格基础件（实现下沉到 Services/FeishuCards.cs，供多页复用） ============

        private Border FeishuCard(double padding = 16) => Services.FeishuCards.Card(this, padding);

        private FrameworkElement FeishuDivider() => Services.FeishuCards.Divider(this);

        private FrameworkElement FeishuAction(string icon, string label, Brush fg, MouseButtonEventHandler onClick)
            => Services.FeishuCards.Action(this, icon, label, fg, onClick);

        private static string RecordValuesText(CustomModule m, CustomModuleRecord r)
            => Services.FeishuCards.RecordValuesText(m, r);

        private FrameworkElement BuildStatCell(string label, string value, Color color)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("CardBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12),
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
                "bars" => BuildWidgetBarsBody(m, w, color),
                "recent" => BuildWidgetRecentBody(m, w, color),
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
                "bars" => $"{fname} · 近 {w.Days} 天柱状图",
                "recent" => "最近记录",
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

        /// <summary>柱状图组件：近 N 天每天合计的数值（按天分桶，直观对比每日投入）</summary>
        private FrameworkElement BuildWidgetBarsBody(CustomModule m, CustomDashboardWidget w, Color color)
        {
            var f = m.Fields.FirstOrDefault(x => x.Key == w.FieldKey);
            if (f == null) return WidgetShell(WidgetTitle(m, w), m.ColorHex, new TextBlock { Text = "字段不存在", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });

            var today = DateTime.Today;
            int days = Math.Max(3, w.Days);
            var buckets = new (string Label, double Value)[days];
            for (int i = 0; i < days; i++)
            {
                var d = today.AddDays(-(days - 1 - i));
                buckets[i] = (d.ToString("MM/dd"), 0);
            }
            foreach (var r in m.Records)
            {
                if (!DateTime.TryParse(r.Date, out var rd)) continue;
                int idx = (rd.Date - today.AddDays(-(days - 1))).Days;
                if (idx < 0 || idx >= days) continue;
                if (r.Values.TryGetValue(f.Key, out var v) && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                    buckets[idx].Value += n;
            }
            double max = buckets.Max(b => b.Value);
            var grid = new Grid { Height = 74, VerticalAlignment = VerticalAlignment.Bottom };
            for (int i = 0; i < days; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                double ratio = max <= 0 ? 0 : buckets[i].Value / max;
                var barWrap = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 74 };
                var bar = new Border
                {
                    Height = Math.Max(3, 68 * ratio), VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(2, 2, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(190, color.R, color.G, color.B)),
                    ToolTip = $"{buckets[i].Label}：{buckets[i].Value:0.#}"
                };
                barWrap.Children.Add(bar);
                Grid.SetColumn(barWrap, i);
                grid.Children.Add(barWrap);
            }
            var labels = new TextBlock
            {
                Text = $"{buckets.First().Label} — {buckets.Last().Label}", FontSize = 9,
                Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var sp = new StackPanel();
            sp.Children.Add(grid);
            sp.Children.Add(labels);
            return WidgetShell(WidgetTitle(m, w), m.ColorHex, sp);
        }

        /// <summary>最近记录组件：直接把最新几条记录铺在卡片里，不用点开历史就能看到</summary>
        private FrameworkElement BuildWidgetRecentBody(CustomModule m, CustomDashboardWidget w, Color color)
        {
            var recent = m.Records.OrderByDescending(r => r.Date).ThenByDescending(r => r.Time).Take(5).ToList();
            if (recent.Count == 0)
                return WidgetShell(WidgetTitle(m, w), m.ColorHex, new TextBlock { Text = "暂无记录", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
            var sp = new StackPanel();
            foreach (var r in recent)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(new TextBlock
                {
                    Text = RecordValuesText(m, r), FontSize = 11.5,
                    Foreground = (Brush)FindResource("TextBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{r.Date} {r.Time}", FontSize = 9.5,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                });
                sp.Children.Add(row);
            }
            return WidgetShell(WidgetTitle(m, w), m.ColorHex, sp);
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
            var typeOpts = new[] { ("stat", "数值统计"), ("chart", "趋势图"), ("bars", "柱状图"), ("pie", "分布占比"), ("streak", "连续打卡"), ("recent", "最近记录") };
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

        /// <summary>快速模板：一键填好常用模块的字段（图标/颜色/字段），再按需微调</summary>
        private static readonly (string Name, int Icon, string Color, (string Label, string Type, string Unit, string Options)[] Fields)[] ModulePresets =
        {
            ("跑步", 2, "#FF6B6B", new[] { ("距离", "number", "km", ""), ("路线", "text", "", ""), ("体感", "select", "", "轻松,正常,吃力") }),
            ("喝水", 3, "#5AC8FA", new[] { ("杯数", "number", "杯", "") }),
            ("体重", 1, "#8E8E93", new[] { ("体重", "number", "kg", "") }),
            ("阅读", 6, "#AF52DE", new[] { ("页数", "number", "页", ""), ("状态", "select", "", "想读,在读,读完") }),
            ("日记", 15, "#4F6EF7", new[] { ("标题", "text", "", ""), ("心情", "select", "", "好,中,差") }),
            ("睡眠", 4, "#2E9E5B", new[] { ("时长", "number", "小时", ""), ("入睡", "time", "", "") }),
        };

        private static readonly string[] ColorPresets =
        {
            "#4F6EF7", "#2E9E5B", "#7C5CE0", "#E05C8A", "#E0883C", "#2BA8A8",
            "#FF6B6B", "#5AC8FA", "#AF52DE", "#E0A93C", "#8E8E93", "#1C1C1E"
        };

        private void ShowEditorDialog(CustomModule initial)
        {
            var win = MakeDialogWindow(initial == null ? "新建模块" : "编辑模块", 640, 720);

            var root = new StackPanel { Margin = new Thickness(18) };
            string name = initial?.Name ?? "";
            int iconIdx = initial?.Icon ?? 0;
            string colorHex = initial?.ColorHex ?? "#4F6EF7";

            // —— 实时预览：所见即所得的模块卡头部 ——
            var previewCard = FeishuCard(14);
            root.Children.Add(new TextBlock { Text = "预览", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(previewCard);

            void RenderPreview()
            {
                var c = ParseColor(colorHex);
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.Children.Add(new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B)),
                    Child = new TextBlock
                    {
                        Text = ModuleIcons[Math.Min(iconIdx, ModuleIcons.Length - 1)], FontSize = 19,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                });
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
                col.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(name) ? "模块名称" : name, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
                col.Children.Add(new TextBlock { Text = "记录会显示在这里", FontSize = 10.5, Foreground = (Brush)FindResource("SecondaryTextBrush") });
                Grid.SetColumn(col, 1);
                head.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(34, c.R, c.G, c.B)),
                    Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "0 条", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(c) }
                });
                previewCard.Child = head;
            }

            // —— 快速模板（先只建芯片，点击行为在所有局部变量声明完之后统一挂接） ——
            var presetChips = new List<(Border Chip, (string Name, int Icon, string Color, (string Label, string Type, string Unit, string Options)[] Fields) Preset)>();
            var presetRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var p in ModulePresets)
            {
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 5, 11, 5),
                    Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    Background = (Brush)FindResource("CardBrush"), BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1), ToolTip = "点击套用该模板的字段"
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = ModuleIcons[p.Icon], FontSize = 12, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") });
                chip.Child = sp;
                presetChips.Add((chip, p));
                presetRow.Children.Add(chip);
            }

            // —— 名称 ——
            var nameBox = new TextBox { Text = name, FontSize = 13, Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            nameBox.TextChanged += (s, e) => { name = nameBox.Text; RenderPreview(); };
            root.Children.Add(FormRow("模块名称", nameBox));

            root.Children.Add(new TextBlock { Text = "快速模板", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 10, 0, 2) });
            root.Children.Add(presetRow);

            // —— 图标（带选中态：主题色描边 + 淡底 + 右上角 ✓） ——
            var iconPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var emojiFont = new System.Windows.Media.FontFamily("Segoe UI Emoji");
            FrameworkElement BuildIconButton(int i)
            {
                var c = ParseColor(colorHex);
                bool selected = i == iconIdx;
                var grid = new Grid { Width = 42, Height = 42 };
                grid.Children.Add(new TextBlock
                {
                    Text = ModuleIcons[i], FontSize = 19, FontFamily = emojiFont,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
                if (selected)
                {
                    grid.Children.Add(new Border
                    {
                        Width = 15, Height = 15, CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(c),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 1, 1, 0),
                        Child = new TextBlock
                        {
                            Text = "✓", FontSize = 9, Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                }
                var b = new Border
                {
                    Child = grid, Cursor = Cursors.Hand,
                    CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 6, 6),
                    Background = selected ? new SolidColorBrush(Color.FromArgb(40, c.R, c.G, c.B)) : Brushes.Transparent,
                    BorderBrush = selected ? new SolidColorBrush(c) : (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(selected ? 2 : 1)
                };
                b.MouseEnter += (s2, e2) => { if (i != iconIdx) b.Background = (Brush)FindResource("NavHoverBrush"); };
                b.MouseLeave += (s2, e2) => { if (i != iconIdx) b.Background = Brushes.Transparent; };
                b.MouseLeftButtonDown += (s2, e2) => { iconIdx = i; RenderIcons(); RenderPreview(); };
                return b;
            }
            void RenderIcons()
            {
                iconPanel.Children.Clear();
                for (int i = 0; i < ModuleIcons.Length; i++) iconPanel.Children.Add(BuildIconButton(i));
            }
            RenderIcons();
            root.Children.Add(new TextBlock { Text = "图标（点选后带 ✓ 选中标记）", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 10, 0, 2) });
            root.Children.Add(iconPanel);

            // —— 颜色：预设色球 + 自定义 ——
            var colorRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var colorBtn = new Button { Height = 30, Width = 96, Padding = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("SecondaryButtonStyle"), Content = "自定义…" };
            void RenderColorBtn()
            {
                colorRow.Children.Clear();
                foreach (var hex in ColorPresets)
                {
                    var ball = new Border
                    {
                        Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = new SolidColorBrush(ParseColor(hex)),
                        BorderBrush = string.Equals(hex, colorHex, StringComparison.OrdinalIgnoreCase)
                            ? (Brush)FindResource("TextBrush") : (Brush)FindResource("BorderBrush"),
                        BorderThickness = new Thickness(string.Equals(hex, colorHex, StringComparison.OrdinalIgnoreCase) ? 2 : 1)
                    };
                    var h = hex; // 捕获
                    ball.MouseLeftButtonDown += (s2, e2) => { colorHex = h; RenderColorBtn(); RenderIcons(); RenderPreview(); };
                    colorRow.Children.Add(ball);
                }
                colorRow.Children.Add(colorBtn);
            }
            colorBtn.Click += (s, e) =>
            {
                var picked = ColorPaletteDialog.Show(win, colorHex);
                if (picked != null) { colorHex = picked; RenderColorBtn(); RenderIcons(); RenderPreview(); }
            };
            RenderColorBtn();
            root.Children.Add(new TextBlock { Text = "颜色", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 10, 0, 2) });
            root.Children.Add(colorRow);

            // —— 字段编辑（带排序与类型说明） ——
            var fieldsPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var fields = initial?.Fields.Select(f => new FieldDraft { Label = f.Label, Type = f.Type, Unit = f.Unit ?? "", Options = f.Options ?? "" }).ToList()
                         ?? new List<FieldDraft> { new FieldDraft { Label = "数值", Type = "number" } };

            void MoveField(int idx, int delta)
            {
                int target = idx + delta;
                if (target < 0 || target >= fields.Count) return;
                var f = fields[idx];
                fields.RemoveAt(idx);
                fields.Insert(target, f);
                RenderFields();
            }

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
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });

                    var labelBox = new TextBox { Text = f.Label, FontSize = 12, Height = 30, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
                    labelBox.TextChanged += (s, e) => f.Label = labelBox.Text;
                    Grid.SetColumn(labelBox, 0);

                    var typeCombo = new ComboBox { FontSize = 12, Height = 30, ItemsSource = FieldTypeNames, VerticalContentAlignment = VerticalAlignment.Center, SelectedIndex = Math.Max(0, Array.IndexOf(FieldTypes, f.Type)) };
                    typeCombo.SelectionChanged += (s, e) => { f.Type = FieldTypes[typeCombo.SelectedIndex]; RenderFields(); };
                    Grid.SetColumn(typeCombo, 2);

                    var unitBox = new TextBox { Text = f.Unit, FontSize = 12, Height = 30, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
                    unitBox.TextChanged += (s, e) => f.Unit = unitBox.Text;
                    Grid.SetColumn(unitBox, 4);

                    var ops = new StackPanel { Orientation = Orientation.Horizontal };
                    Button OpBtn(string content, string tip, RoutedEventHandler onClick)
                    {
                        var b = new Button { Content = content, Style = (Style)FindResource("SecondaryButtonStyle"), FontSize = 11, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = tip };
                        b.Click += onClick;
                        return b;
                    }
                    ops.Children.Add(OpBtn("↑", "上移", (s2, e2) => MoveField(idx, -1)));
                    ops.Children.Add(OpBtn("↓", "下移", (s2, e2) => MoveField(idx, 1)));
                    var delBtn = OpBtn("✕", "移除字段", (s2, e2) => { if (fields.Count > 1) { fields.RemoveAt(idx); RenderFields(); } });
                    delBtn.Style = (Style)FindResource("DangerButtonStyle");
                    ops.Children.Add(delBtn);
                    Grid.SetColumn(ops, 6);

                    row.Children.Add(labelBox); row.Children.Add(typeCombo); row.Children.Add(unitBox); row.Children.Add(ops);
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

            // 挂接快速模板（此时 nameBox / fields / RenderXxx 均已就绪）
            foreach (var (chip, p) in presetChips)
            {
                chip.MouseLeftButtonDown += (s, e) =>
                {
                    if (initial != null && MessageBox.Show("套用模板会覆盖当前字段定义，继续吗？", "套用模板", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    name = p.Name;
                    nameBox.Text = p.Name;
                    iconIdx = p.Icon;
                    colorHex = p.Color;
                    fields = p.Fields.Select(f => new FieldDraft { Label = f.Label, Type = f.Type, Unit = f.Unit, Options = f.Options }).ToList();
                    RenderColorBtn(); RenderIcons(); RenderPreview(); RenderFields();
                };
            }

            root.Children.Add(new TextBlock { Text = "字段定义（↑↓ 调整记录时的填写顺序）", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 10, 0, 2) });
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
                    var nm = CustomModuleRepository.Add(new CustomModule { Name = nameBox.Text.Trim(), ColorHex = colorHex, Icon = iconIdx, Fields = validFields });
                    _selectedModuleId = nm.Id;
                }
                else
                {
                    initial.Name = nameBox.Text.Trim();
                    initial.ColorHex = colorHex;
                    initial.Icon = iconIdx;
                    initial.Fields = validFields;
                    CustomModuleRepository.Update(initial);
                }
                win.Close();
                Reload();
            };
            root.Children.Add(saveBtn);

            RenderPreview();
            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 模块资料库（本地 HTML + AI 生成，随云同步互通） ============

        /// <summary>把模块记录导出为 CSV 文件（用户选择保存位置）</summary>
        private void ExportModule(CustomModule m)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"导出「{m.Name}」记录",
                Filter = "CSV 文件|*.csv",
                FileName = $"{m.Name}.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, Services.HtmlLibraryService.BuildDefaultCsv(m), Encoding.UTF8);
                MessageBox.Show($"已导出 {m.Records.Count} 条记录到：\n{dlg.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        /// <summary>资料库：本模块绑定的 HTML 资料页（AI 生成 / 导入本地 HTML / 浏览器打开）</summary>
        private void ShowLibraryDialog(CustomModule m)
        {
            var win = MakeDialogWindow($"资料库 · {m.Name}", 660, 560);
            var root = new StackPanel { Margin = new Thickness(18) };

            root.Children.Add(new TextBlock
            {
                Text = "把模块做成个人资料库：AI 可根据记录生成 HTML 资料页与配套 CSV（离线可开、随云同步互通），也可导入本地已有的 HTML。",
                FontSize = 11.5, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            var list = new StackPanel();

            void RenderList()
            {
                list.Children.Clear();
                var pages = HtmlLibraryRepository.GetFor(m.Id);
                if (pages.Count == 0)
                {
                    list.Children.Add(new TextBlock
                    {
                        Text = "还没有资料页。点下方「AI 生成」或「导入本地 HTML」开始。",
                        FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                        Margin = new Thickness(0, 4, 0, 4)
                    });
                    return;
                }
                foreach (var p in pages)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    info.Children.Add(new TextBlock
                    {
                        Text = p.Title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    var tag = p.Source == "ai" ? "AI 生成" : "本地";
                    info.Children.Add(new TextBlock
                    {
                        Text = $"{tag} · {p.UpdatedAt} · {p.SizeBytes / 1024.0:0.#} KB",
                        FontSize = 10.5, Foreground = (Brush)FindResource("SecondaryTextBrush")
                    });
                    Grid.SetColumn(info, 0);

                    Button Act(string content, string tip, RoutedEventHandler onClick)
                    {
                        var b = new Button
                        {
                            Content = content, Style = (Style)FindResource("SecondaryButtonStyle"),
                            FontSize = 11, Padding = new Thickness(10, 4, 10, 4),
                            Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = tip
                        };
                        b.Click += onClick;
                        return b;
                    }
                    var openBtn = Act("打开", "用默认浏览器打开该资料页", (s2, e2) =>
                    {
                        try
                        {
                            var (htmlPath, _) = HtmlLibraryRepository.Export(p, Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "ME", "HtmlExport"));
                            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
                        }
                        catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
                    });
                    var csvBtn = Act("导出", "把该资料页导出为 .html + .csv 文件", (s2, e2) =>
                    {
                        var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "选择导出目录" };
                        if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                        try
                        {
                            var (hp, cp) = HtmlLibraryRepository.Export(p, fbd.SelectedPath);
                            MessageBox.Show("已导出：\n" + hp + (cp == null ? "" : "\n" + cp), "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message); }
                    });
                    var delBtn = Act("删除", "删除该资料页", (s2, e2) =>
                    {
                        if (MessageBox.Show($"删除资料页「{p.Title}」？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        HtmlLibraryRepository.Delete(p.Id);
                        RenderList();
                    });
                    Grid.SetColumn(openBtn, 1); Grid.SetColumn(csvBtn, 2); Grid.SetColumn(delBtn, 3);
                    row.Children.Add(info); row.Children.Add(openBtn); row.Children.Add(csvBtn); row.Children.Add(delBtn);
                    list.Children.Add(row);
                }
            }
            RenderList();

            root.Children.Add(new Border
            {
                Style = (Style)FindResource("CardStyle"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300, Content = list }
            });

            var ops = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var aiBtn = new Button { Content = "✨ AI 生成资料页", Style = (Style)FindResource("PrimaryButtonStyle"), FontSize = 12.5, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            var importBtn = new Button { Content = "导入本地 HTML", Style = (Style)FindResource("SecondaryButtonStyle"), FontSize = 12.5, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            var exportAllBtn = new Button { Content = "导出记录 CSV", Style = (Style)FindResource("SecondaryButtonStyle"), FontSize = 12.5, Padding = new Thickness(16, 7, 16, 7), Cursor = Cursors.Hand };
            ops.Children.Add(aiBtn); ops.Children.Add(importBtn); ops.Children.Add(exportAllBtn);
            root.Children.Add(ops);

            var status = new TextBlock { FontSize = 11.5, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            root.Children.Add(status);

            importBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择 HTML 文件", Filter = "HTML|*.html;*.htm|所有文件|*.*" };
                if (dlg.ShowDialog() != true) return;
                try { HtmlLibraryService.ImportFile(m.Id, dlg.FileName); RenderList(); }
                catch (Exception ex) { MessageBox.Show("导入失败：" + ex.Message); }
            };
            exportAllBtn.Click += (s, e) => ExportModule(m);

            aiBtn.Click += async (s, e) =>
            {
                var hint = PromptInputDialog.Show(win, "AI 生成资料页", "想让它做成什么样？（可留空，例如：做一页可搜索的跑步记录看板）");
                if (hint == null) return;
                aiBtn.IsEnabled = false;
                status.Text = "正在生成…（大模型可能需要十几秒）";
                try
                {
                    var provider = new AiProviderRepository().GetDefault();
                    await HtmlLibraryService.GenerateWithAiAsync(m, hint, provider);
                    status.Text = "✓ 已生成并保存到资料库（会随云同步同步到安卓端）。";
                    RenderList();
                }
                catch (Exception ex)
                {
                    status.Text = "✗ 生成失败：" + ex.Message;
                }
                aiBtn.IsEnabled = true;
            };

            ((Border)win.Tag).Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            win.ShowDialog();
        }

        // ============ 记一笔（沿用） ============

        private void ShowRecordDialog(CustomModule m)
        {
            // 自适应：高度随字段数量伸缩（少量字段时窗口更紧凑，多字段时封顶并内部滚动）
            int height = Math.Min(780, 190 + m.Fields.Count * 64 + (m.Fields.Any(f => f.Type == "select") ? 30 : 0) + 130);
            var win = MakeDialogWindow($"记录 · {m.Name}", 500, height);
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
