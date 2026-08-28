using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    /// <summary>
    /// 自定义模块页：模块列表 + 新建/编辑（含字段定义）+ 记一笔 + 历史/趋势。
    /// 与安卓端 custom_modules.json 数据互通。
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

        public CustomModulesView()
        {
            InitializeComponent();
            Loaded += (s, e) => Reload();
        }

        public void Reload()
        {
            ModulesPanel.Children.Clear();
            var modules = CustomModuleRepository.GetAll();
            if (modules.Count == 0)
            {
                ModulesPanel.Children.Add(new TextBlock
                {
                    Text = "还没有模块。点下方「＋ 新建模块」创建第一个，比如「跑步」记数值 km、「日记」记文本、「喝咖啡」记杯数。",
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }
            foreach (var m in modules) ModulesPanel.Children.Add(BuildModuleCard(m));
        }

        private Border BuildModuleCard(CustomModule m)
        {
            var color = ParseColor(m.ColorHex);

            var iconText = new TextBlock
            {
                Text = ModuleIcons[Math.Min(m.Icon, ModuleIcons.Length - 1)],
                FontSize = 20, VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = m.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush")
            };
            var fieldsText = string.Join("、", m.Fields.Select(f => f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"({f.Unit})")));
            var sub = new TextBlock
            {
                Text = $"字段：{(fieldsText == "" ? "无" : fieldsText)} · {m.Records.Count} 条记录",
                FontSize = 11.5, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap
            };

            var infoCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoCol.Children.Add(title);
            infoCol.Children.Add(sub);

            Button MakeBtn(string content, RoutedEventHandler onClick, bool primary = false)
            {
                var b = new Button
                {
                    Content = content,
                    Style = (Style)FindResource(primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
                    Padding = new Thickness(12, 5, 12, 5), FontSize = 12, Margin = new Thickness(6, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                b.Click += onClick;
                return b;
            }

            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btns.Children.Add(MakeBtn("记一笔", (s, e) => ShowRecordDialog(m), primary: true));
            btns.Children.Add(MakeBtn("历史", (s, e) => ShowHistoryDialog(m)));
            btns.Children.Add(MakeBtn("编辑", (s, e) => ShowEditorDialog(m)));
            btns.Children.Add(MakeBtn("删除", (s2, e2) =>
            {
                if (MessageBox.Show($"确定删除「{m.Name}」及其全部 {m.Records.Count} 条记录吗？", "删除模块",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                CustomModuleRepository.Delete(m.Id);
                Reload();
            }));

            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var iconBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(9),
                Width = 42, Height = 42,
                Child = iconText
            };
            Grid.SetColumn(iconBox, 0);
            Grid.SetColumn(infoCol, 2);
            Grid.SetColumn(btns, 3);
            grid.Children.Add(iconBox);
            grid.Children.Add(infoCol);
            grid.Children.Add(btns);

            return new Border
            {
                Background = (Brush)FindResource("CardBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid
            };
        }

        // ============ 新建 / 编辑模块 ============

        private void AddModule_Click(object sender, RoutedEventArgs e) => ShowEditorDialog(null);

        private void ShowEditorDialog(CustomModule initial)
        {
            var win = MakeDialogWindow(initial == null ? "新建模块" : "编辑模块", 560, 620);

            var root = new StackPanel { Margin = new Thickness(18) };

            var nameBox = new TextBox { Text = initial?.Name ?? "", FontSize = 13, Padding = new Thickness(8, 6, 8, 6) };
            root.Children.Add(FormRow("模块名称", nameBox));

            int iconIdx = initial?.Icon ?? 0;
            var iconPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            string colorHex = initial?.ColorHex ?? "#4F6EF7";
            for (int i = 0; i < ModuleIcons.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Content = ModuleIcons[i], FontSize = 15, Width = 34, Height = 32, Margin = new Thickness(0, 0, 4, 4),
                    Style = (Style)FindResource("SecondaryButtonStyle"), Cursor = Cursors.Hand
                };
                b.Click += (s, e) => iconIdx = idx;
                iconPanel.Children.Add(b);
            }
            root.Children.Add(FormRow("图标（点击选中）", iconPanel));

            var colorBox = new TextBox { Text = colorHex, FontSize = 13, Width = 110, Padding = new Thickness(8, 6, 8, 6) };
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

                    var labelBox = new TextBox { Text = f.Label, FontSize = 12, Padding = new Thickness(6, 5, 6, 5) };
                    labelBox.TextChanged += (s, e) => f.Label = labelBox.Text;
                    Grid.SetColumn(labelBox, 0);

                    var typeCombo = new ComboBox { FontSize = 12, ItemsSource = FieldTypeNames, SelectedIndex = Math.Max(0, Array.IndexOf(FieldTypes, f.Type)) };
                    typeCombo.SelectionChanged += (s, e) => { f.Type = FieldTypes[typeCombo.SelectedIndex]; RenderFields(); };
                    Grid.SetColumn(typeCombo, 2);

                    var unitBox = new TextBox { Text = f.Unit, FontSize = 12, Padding = new Thickness(6, 5, 6, 5) };
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
                            Text = f.Options, FontSize = 12, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 6),
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
                        Key = string.IsNullOrWhiteSpace(f.Options) && f.Type != "select" ? $"f{i + 1}" : $"f{i + 1}",
                        Label = f.Label.Trim(), Type = f.Type,
                        Unit = string.IsNullOrWhiteSpace(f.Unit) ? null : f.Unit.Trim(),
                        Options = string.IsNullOrWhiteSpace(f.Options) ? null : f.Options
                    }).ToList();
                if (validFields.Count == 0) { MessageBox.Show("至少需要一个字段"); return; }
                if (initial == null)
                    CustomModuleRepository.Add(new CustomModule { Name = nameBox.Text.Trim(), ColorHex = colorBox.Text.Trim(), Icon = iconIdx, Fields = validFields });
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

        // ============ 记一笔 ============

        private void ShowRecordDialog(CustomModule m)
        {
            var win = MakeDialogWindow($"记录 · {m.Name}", 520, 560);
            var root = new StackPanel { Margin = new Thickness(18) };
            var values = new Dictionary<string, string>();

            var dateBox = new TextBox { Text = DateTime.Now.ToString("yyyy-MM-dd"), FontSize = 13, Width = 140, Padding = new Thickness(8, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
            root.Children.Add(FormRow("日期", dateBox));

            foreach (var f in m.Fields)
            {
                UIElement input;
                if (f.Type == "bool")
                {
                    var cb = new ComboBox { FontSize = 12, Width = 120, ItemsSource = new[] { "是", "否" }, SelectedIndex = 1 };
                    cb.SelectionChanged += (s, e) => values[f.Key] = cb.SelectedIndex == 0 ? "true" : "false";
                    input = cb;
                }
                else if (f.Type == "select")
                {
                    var opts = (f.Options ?? "").Split(',').Select(o => o.Trim()).Where(o => o != "").ToArray();
                    if (opts.Length == 0) opts = new[] { "选项1", "选项2" };
                    var combo = new ComboBox { FontSize = 12, Width = 160, ItemsSource = opts };
                    combo.SelectionChanged += (s, e) => values[f.Key] = opts[combo.SelectedIndex];
                    input = combo;
                }
                else
                {
                    var tb = new TextBox { FontSize = 13, Width = 160, Padding = new Thickness(8, 6, 8, 6) };
                    TextChangedHandler(f.Key);
                    void TextChangedHandler(string key) => tb.TextChanged += (s, e) => values[key] = tb.Text;
                    input = tb;
                }
                var label = f.Label + (string.IsNullOrEmpty(f.Unit) ? "" : $"（{f.Unit}）");
                root.Children.Add(FormRow(label, input));
            }

            var noteBox = new TextBox { FontSize = 13, Padding = new Thickness(8, 6, 8, 6) };
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

        // ============ 历史 ============

        private void ShowHistoryDialog(CustomModule m)
        {
            var fresh = CustomModuleRepository.GetAll().First(x => x.Id == m.Id);
            var win = MakeDialogWindow($"{fresh.Name} · 历史（{fresh.Records.Count} 条）", 640, 600);
            var root = new StackPanel { Margin = new Thickness(18) };

            var numberField = fresh.Fields.FirstOrDefault(f => f.Type == "number");
            if (numberField != null && fresh.Records.Count(r => r.Values.ContainsKey(numberField.Key) && double.TryParse(r.Values[numberField.Key], out _)) >= 2)
            {
                root.Children.Add(new TextBlock { Text = $"{numberField.Label} 趋势", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 0, 0, 6) });
                root.Children.Add(BuildTrendChart(fresh, numberField));
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

        /// <summary>纯 WPF 画的折线趋势图（不引第三方图表库）</summary>
        private FrameworkElement BuildTrendChart(CustomModule m, CustomModuleField f)
        {
            var points = m.Records
                .Where(r => r.Values.ContainsKey(f.Key) && double.TryParse(r.Values[f.Key], out _))
                .OrderBy(r => r.Date).ThenBy(r => r.Time)
                .Select(r => (Date: r.Date, Value: double.Parse(r.Values[f.Key])))
                .ToList();
            double max = points.Max(p => p.Value) <= 0 ? 1 : points.Max(p => p.Value);
            double min = Math.Min(0, points.Min(p => p.Value));

            var canvas = new Canvas { Height = 130, ClipToBounds = true };
            double w = 520, h = 110, padL = 34;
            // 网格
            for (int g = 0; g <= 3; g++)
            {
                var y = h * g / 3.0;
                canvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = padL, X2 = w, Y1 = y, Y2 = y,
                    Stroke = (Brush)FindResource("BorderBrush"), StrokeThickness = 0.6,
                    StrokeDashArray = new DoubleCollection { 3, 3 }
                });
                var lbl = new TextBlock
                {
                    Text = (max - (max - min) * g / 3.0).ToString("0.#"),
                    FontSize = 9, Foreground = (Brush)FindResource("SecondaryTextBrush")
                };
                Canvas.SetLeft(lbl, 2); Canvas.SetTop(lbl, y - 6);
                canvas.Children.Add(lbl);
            }
            if (points.Count >= 2)
            {
                Func<int, double> x = i => padL + i * ((w - padL) / (points.Count - 1));
                Func<double, double> yOf = v => (1 - (v - min) / (max - min)) * (h - 10);
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(ParseColor(m.ColorHex)),
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                };
                for (int i = 0; i < points.Count; i++)
                    poly.Points.Add(new Point(x(i), yOf(points[i].Value)));
                canvas.Children.Add(poly);
                foreach (var (p, i) in points.Select((p, i) => (p, i)))
                {
                    var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(ParseColor(m.ColorHex)) };
                    Canvas.SetLeft(dot, x(i) - 2.5); Canvas.SetTop(dot, yOf(p.Value) - 2.5);
                    canvas.Children.Add(dot);
                }
                var first = new TextBlock { Text = points.First().Date, FontSize = 9, Foreground = (Brush)FindResource("SecondaryTextBrush") };
                Canvas.SetLeft(first, padL); Canvas.SetTop(first, h + 2);
                var last = new TextBlock { Text = points.Last().Date, FontSize = 9, Foreground = (Brush)FindResource("SecondaryTextBrush") };
                Canvas.SetLeft(last, w - 50); Canvas.SetTop(last, h + 2);
                canvas.Children.Add(first); canvas.Children.Add(last);
            }
            return canvas;
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
    }
}
