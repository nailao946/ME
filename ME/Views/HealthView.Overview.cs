using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ME.Core;
using ME.Data;
using ME.Models;
using ME.Services;

namespace ME.Views
{
    /// <summary>
    /// 健康总览：概览卡片、人体部位图、部位详情、健康报告导出。
    /// （从 HealthView.xaml.cs 拆分而来，降低单文件体积）
    /// </summary>
    public partial class HealthView : UserControl
    {
        // ============ 总览 ============
        private string _overviewPart = "睡眠";
        // 快捷记录输入件：AddQuickEntry 的 buildInputs 与 trySave 之间共享（闭包捕获字段最简单可靠）
        private TextBox _quickBox1;
        private TextBox _quickBox2;
        private ComboBox _quickCombo;

        private void LoadOverview()
        {
            BuildOverviewCards();
            BuildOverviewInfo();
            DrawBodyFigure();
            ShowBodyPartDetail(_overviewPart);
        }

        /// <summary>总览页右侧"详细信息"：身高/体重/平均睡眠等汇总数据</summary>
        private void BuildOverviewInfo()
        {
            OverviewInfoPanel.Children.Clear();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");

            void AddGroup(string title, params (string label, string value, string brushKey)[] items)
            {
                var group = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("TextBrush"),
                    Margin = new Thickness(0, 0, 0, 6)
                });
                var grid = new UniformGrid { Columns = 2 };
                foreach (var (label, value, brushKey) in items)
                {
                    var cell = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 6, 6)
                    };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock
                    {
                        Text = label,
                        FontSize = 10,
                        Foreground = (Brush)FindResource("SecondaryTextBrush")
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = value,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource(brushKey),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                    cell.Child = sp;
                    grid.Children.Add(cell);
                }
                panel.Children.Add(grid);
                group.Child = panel;
                OverviewInfoPanel.Children.Add(group);
            }

            // ===== 睡眠组 =====
            var sleeps = _repo.GetByType("sleep").Where(r => string.CompareOrdinal(r.Date, todayStr) <= 0).ToList();
            var sleep7 = sleeps.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
            var sleep30 = sleeps.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-29).ToString("yyyy-MM-dd")) >= 0).ToList();
            var sleepItems = new List<(string, string, string)>();
            var sleepRec = sleeps.LastOrDefault();
            if (sleepRec != null)
            {
                sleepItems.Add(("最近睡眠", FormatDuration(TimeSpan.FromMinutes(sleepRec.Value)), "PrimaryBrush"));
                if (!string.IsNullOrEmpty(sleepRec.Detail))
                {
                    var parts = sleepRec.Detail.Split('|');
                    if (parts.Length >= 2)
                        sleepItems.Add(("入睡 / 起床", parts[0] + " / " + parts[1], "SecondaryTextBrush"));
                }
                sleepItems.Add(("记录日期", sleepRec.Date, "SecondaryTextBrush"));
            }
            if (sleep7.Count > 0) sleepItems.Add(("近 7 天平均", FormatDuration(TimeSpan.FromMinutes(sleep7.Average(r => r.Value))), "AccentBlueBrush"));
            if (sleep30.Count > 0) sleepItems.Add(("近 30 天平均", FormatDuration(TimeSpan.FromMinutes(sleep30.Average(r => r.Value))), "AccentGreenBrush"));
            if (sleepItems.Count == 0) sleepItems.Add(("睡眠", "暂无记录", "SecondaryTextBrush"));
            AddGroup("😴 睡眠", sleepItems.ToArray());

            // ===== 身体组 =====
            double h = 0; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthHeight), out h);
            var weights = _repo.GetByType("weight").OrderBy(r => r.Date).ToList();
            var latestW = weights.LastOrDefault();
            var bodyItems = new List<(string, string, string)>
            {
                ("身高", h > 0 ? h.ToString("0.#") + " cm" : "未填写", h > 0 ? "PrimaryBrush" : "SecondaryTextBrush")
            };
            if (latestW != null)
            {
                bodyItems.Add(("最新体重", latestW.Value.ToString("F1") + " kg", "PrimaryBrush"));
                bodyItems.Add(("记录日期", latestW.Date, "SecondaryTextBrush"));
                if (h > 0)
                {
                    var bmi = CalcBmi(latestW.Value, h);
                    string g, brush;
                    if (bmi < 18.5) { g = "偏瘦"; brush = "AccentBlueBrush"; }
                    else if (bmi < 24) { g = "正常"; brush = "AccentGreenBrush"; }
                    else if (bmi < 28) { g = "超重"; brush = "AccentYellowBrush"; }
                    else { g = "肥胖"; brush = "AccentRedBrush"; }
                    bodyItems.Add(("BMI", bmi.ToString("F1") + "（" + g + "）", brush));
                }
            }
            else
            {
                bodyItems.Add(("最新体重", "暂无记录", "SecondaryTextBrush"));
            }
            AddGroup("⚖️ 身体", bodyItems.ToArray());

            // ===== 喝水组 =====
            var waters = _repo.GetByType("water").Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
            double goal = 2000; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "2000"), out goal);
            var waterRec = _repo.GetByTypeAndDate("water", todayStr);
            AddGroup("💧 喝水",
                ("今日喝水", waterRec != null ? waterRec.Value.ToString("F0") + " ml" : "0 ml", waterRec != null ? "AccentBlueBrush" : "SecondaryTextBrush"),
                ("每日目标", goal.ToString("F0") + " ml", "SecondaryTextBrush"),
                ("近 7 天日均", waters.Count > 0 ? waters.Average(r => r.Value).ToString("F0") + " ml" : "暂无数据", "AccentGreenBrush"));

            // ===== 尿酸组 =====
            var uricAll = _repo.GetByType("uric_acid").OrderBy(r => r.Date).ToList();
            var latestUric = uricAll.LastOrDefault();
            if (latestUric != null)
            {
                var (lower, upper) = GetUricRange();
                var (text, brush) = ClassifyUric(latestUric.Value, lower, upper);
                AddGroup("💉 尿酸",
                    ("最新值", latestUric.Value.ToString("F0") + " μmol/L", brush),
                    ("状态", text, brush),
                    ("正常范围", lower.ToString("F0") + " ~ " + upper.ToString("F0"), "SecondaryTextBrush"),
                    ("记录日期", latestUric.Date, "SecondaryTextBrush"));
            }
            else
            {
                AddGroup("💉 尿酸", ("最新值", "暂无记录", "SecondaryTextBrush"));
            }

            // ===== 心情组 =====
            var mood = _repo.GetByTypeAndDate("mood", todayStr);
            var mi = mood != null ? MoodIdxOf(mood.Value) : -1;
            var allMoods = _repo.GetByType("mood");
            var weekMoods = allMoods.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
            var moodItems = new List<(string, string, string)>
            {
                ("今日心情", mi >= 0 && mi < 5 ? MoodEmojis[mi] + " " + MoodNames[mi] : "未记录", mi >= 3 ? "AccentGreenBrush" : "SecondaryTextBrush")
            };
            if (weekMoods.Count > 0)
            {
                var avg = (int)Math.Round(weekMoods.Average(r => MoodIdxOf(r.Value)));
                if (avg < 0 || avg > 4) avg = 2;
                moodItems.Add(("近 7 天平均", MoodEmojis[avg] + " " + MoodNames[avg], "SecondaryTextBrush"));
            }
            AddGroup("😊 心情", moodItems.ToArray());

            // ===== 用药组 =====
            var activeMeds = _medRepo.GetActive();
            AddGroup("💊 用药",
                ("在用药物", activeMeds.Count > 0 ? activeMeds.Count.ToString() + " 种" : "无", activeMeds.Count > 0 ? "AccentYellowBrush" : "SecondaryTextBrush"),
                ("提醒", activeMeds.Any(m => m.Remind) ? "已开启" : "未开启", activeMeds.Any(m => m.Remind) ? "AccentGreenBrush" : "SecondaryTextBrush"));
        }

        private (string title, string value, string sub, string brushKey) GetOverviewCardData(string key)
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            switch (key)
            {
                case "睡眠":
                {
                    var rec = _repo.GetByTypeAndDate("sleep", todayStr);
                    if (rec == null) return (key, "--", "今日未记录", "SecondaryTextBrush");
                    var dur = TimeSpan.FromMinutes(rec.Value);
                    var ok = dur.TotalHours >= 7 && dur.TotalHours <= 9;
                    return (key, FormatDuration(dur), ok ? "睡眠充足" : "建议 7-9 小时", ok ? "AccentGreenBrush" : "AccentYellowBrush");
                }
                case "体重":
                {
                    var all = _repo.GetByType("weight").OrderBy(r => r.Date).ToList();
                    var rec = all.LastOrDefault();
                    if (rec == null) return (key, "--", "暂无记录", "SecondaryTextBrush");
                    double h = 0; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthHeight), out h);
                    if (h <= 0) return (key, $"{rec.Value:F1} kg", "未填身高", "PrimaryBrush");
                    var bmi = CalcBmi(rec.Value, h);
                    string g, brush;
                    if (bmi < 18.5) { g = $"BMI {bmi:F1} 偏瘦"; brush = "AccentBlueBrush"; }
                    else if (bmi < 24) { g = $"BMI {bmi:F1} 正常"; brush = "AccentGreenBrush"; }
                    else if (bmi < 28) { g = $"BMI {bmi:F1} 超重"; brush = "AccentYellowBrush"; }
                    else { g = $"BMI {bmi:F1} 肥胖"; brush = "AccentRedBrush"; }
                    return (key, $"{rec.Value:F1} kg", g, brush);
                }
                case "喝水":
                {
                    var rec = _repo.GetByTypeAndDate("water", todayStr);
                    if (rec == null) return (key, "0 ml", "今日未记录", "SecondaryTextBrush");
                    double goal = 2000; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "2000"), out goal);
                    var pct = goal > 0 ? rec.Value / goal : 0;
                    var brush = pct >= 0.8 ? "AccentGreenBrush" : pct >= 0.5 ? "AccentYellowBrush" : "AccentRedBrush";
                    return (key, $"{rec.Value:F0} ml", $"目标 {goal:F0} ml", brush);
                }
                case "心情":
                {
                    var rec = _repo.GetByTypeAndDate("mood", todayStr);
                    if (rec == null) return (key, "--", "今日未记录", "SecondaryTextBrush");
                    var idx = MoodIdxOf(rec.Value);
                    return (key, MoodEmojis[idx], MoodNames[idx], idx >= 3 ? "AccentGreenBrush" : "AccentYellowBrush");
                }
                case "尿酸":
                {
                    var rec = _repo.GetByTypeAndDate("uric_acid", todayStr);
                    if (rec == null) return (key, "--", "今日未记录", "SecondaryTextBrush");
                    var (lower, upper) = GetUricRange();
                    var (text, brush) = ClassifyUric(rec.Value, lower, upper);
                    return (key, $"{rec.Value:F0}", text, brush);
                }
                case "血压":
                {
                    var bp = _repo.GetByType("blood_pressure").OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).FirstOrDefault();
                    if (bp == null) return (key, "--", "未记录，点左臂可记", "SecondaryTextBrush");
                    double.TryParse(bp.Detail, out var dia);
                    var (text, brush) = ClassifyBp(bp.Value, dia);
                    return (key, $"{bp.Value:F0}/{dia:F0}", $"{bp.Date} {text}", brush);
                }
                case "心率":
                {
                    var hr = _repo.GetByTypeAndDate("heart_rate", todayStr);
                    if (hr == null) return (key, "--", "今日未记录", "SecondaryTextBrush");
                    var (text, brush) = ClassifyHeartRate(hr.Value);
                    return (key, $"{hr.Value:F0} bpm", text, brush);
                }
                case "用药":
                {
                    var meds = _medRepo.GetActive();
                    return (key, meds.Count.ToString(), meds.Count > 0 ? "种在用药物" : "暂无用药", meds.Count > 0 ? "PrimaryBrush" : "SecondaryTextBrush");
                }
                default:
                    return (key, "--", "", "SecondaryTextBrush");
            }
        }

        private void BuildOverviewCards()
        {
            OverviewCardsPanel.Children.Clear();
            foreach (var key in new[] { "睡眠", "血压", "心率", "体重", "喝水", "心情", "尿酸", "用药" })
            {
                var (title, value, sub, brushKey) = GetOverviewCardData(key);
                var emoji = key switch
                {
                    "睡眠" => "😴", "体重" => "⚖️", "喝水" => "💧", "心情" => "😊",
                    "尿酸" => "💉", "用药" => "💊", "血压" => "🩸", "心率" => "❤️", _ => "💊"
                };
                // 强调色低透明度做图标底
                Brush iconBg = new SolidColorBrush(Color.FromArgb(30, 128, 140, 170));
                if (FindResource(brushKey) is SolidColorBrush scb)
                    iconBg = new SolidColorBrush(Color.FromArgb(34, scb.Color.R, scb.Color.G, scb.Color.B));

                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Width = 172,
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(12, 12, 12, 12),
                    Cursor = Cursors.Hand
                };
                var keyCopy = key;
                card.MouseLeftButtonDown += (s, e) => ShowBodyPartDetail(keyCopy);

                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var iconBox = new Border
                {
                    Width = 38, Height = 38, CornerRadius = new CornerRadius(11),
                    Background = iconBg,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBox.Child = new TextBlock
                {
                    Text = emoji,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                rowPanel.Children.Add(iconBox);

                var col = new StackPanel { Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(new TextBlock { Text = key, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
                col.Children.Add(new TextBlock
                {
                    Text = value,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource(brushKey),
                    Margin = new Thickness(0, 1, 0, 0)
                });
                rowPanel.Children.Add(col);

                var panel = new StackPanel();
                panel.Children.Add(rowPanel);
                panel.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontSize = 10,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 6, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                card.Child = panel;
                OverviewCardsPanel.Children.Add(card);
            }
        }

        private void DrawBodyFigure()
        {
            BodyCanvas.Children.Clear();
            var w = BodyCanvas.ActualWidth;
            var h = BodyCanvas.ActualHeight;
            if (w < 50) w = 220;
            if (h < 50) h = 340;

            var cx = w / 2.0;
            var fillBrush = new SolidColorBrush(Color.FromArgb(36, 0, 122, 255));   // 半透明主题蓝
            var hoverFill = new SolidColorBrush(Color.FromArgb(70, 0, 122, 255));
            var borderBrush = new SolidColorBrush(Color.FromRgb(120, 140, 170));

            // 用圆角填充块画简化人体，每块中间放 emoji 图标，直观不抽象
            void AddPart(string emoji, string label, double x, double y, double pw, double ph, double radius, string partKey, string tooltip)
            {
                var rect = new Rectangle
                {
                    Width = pw,
                    Height = ph,
                    RadiusX = radius,
                    RadiusY = radius,
                    Fill = fillBrush,
                    Stroke = borderBrush,
                    StrokeThickness = 1.5,
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.MouseLeftButtonDown += (s, e) => ShowBodyPartDetail(partKey);
                rect.MouseEnter += (s, e) => rect.Fill = hoverFill;
                rect.MouseLeave += (s, e) => rect.Fill = fillBrush;
                BodyCanvas.Children.Add(rect);

                var emojiText = new TextBlock
                {
                    Text = emoji,
                    FontSize = 19,
                    Width = pw,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(emojiText, x);
                Canvas.SetTop(emojiText, y + ph / 2 - 14);
                BodyCanvas.Children.Add(emojiText);
                // 部位名称不画在图上（用户要求：明面上只有图标），解释放悬浮提示与下方详情
            }

            // 头（睡眠/心情）
            AddPart("🧠", "睡眠/心情", cx - 24, 4, 48, 48, 24, "头部", "头部：睡眠 / 心情");
            // 心胸（静息心率）
            AddPart("❤️", "心率", cx - 34, 60, 68, 56, 18, "心胸", "心胸：静息心率");
            // 腹（血糖 / 体重）
            AddPart("🍬", "血糖/体重", cx - 34, 122, 68, 58, 14, "腹部", "腹部：血糖 / 体重");
            // 左臂（血压——血压计袖带绑的位置）
            AddPart("💪", "血压", cx - 74, 76, 30, 104, 15, "左臂", "左臂：血压");
            // 右臂（用药）
            AddPart("💊", "用药", cx + 44, 76, 30, 104, 15, "右臂", "右臂：用药");
            // 左腿（尿酸——痛风常发于下肢关节）
            AddPart("🦵", "尿酸", cx - 34, 188, 30, 116, 15, "左腿", "左腿：尿酸");
            // 右腿（运动 / 久坐）
            AddPart("🏃", "运动/久坐", cx + 4, 188, 30, 116, 15, "右腿", "右腿：运动 / 久坐");
        }

        private void ShowBodyPartDetail(string part)
        {
            _overviewPart = part;
            OverviewDetailPanel.Children.Clear();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");

            void AddRow(string label, string value, string brushKey)
            {
                var row = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var dock = new DockPanel();
                var valueText = new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource(brushKey),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(valueText, Dock.Right);
                dock.Children.Add(valueText);
                dock.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Child = dock;
                OverviewDetailPanel.Children.Add(row);
            }

            OverviewDetailPanel.Children.Add(new TextBlock
            {
                Text = PartTitle(part),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // —— 快捷记录组件：部位详情里直接记一笔，记完原地刷新 ——
            TextBox MiniBox() => new TextBox
            {
                Width = 64, FontSize = 12, Height = 28, Padding = new Thickness(7, 3, 7, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            void AddQuickEntry(string tip, Func<StackPanel> buildInputs, Func<bool> trySave)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = tip, FontSize = 10.5, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 0, 0, 5) });
                var row = buildInputs();
                var saveBtn = new Button { Content = "记录", Style = (Style)FindResource("PrimaryButtonStyle"), FontSize = 11.5, Padding = new Thickness(13, 4, 13, 4), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
                saveBtn.Click += (s, e) =>
                {
                    if (trySave())
                    {
                        BuildOverviewCards();
                        ShowBodyPartDetail(_overviewPart);
                    }
                };
                row.Children.Add(saveBtn);
                sp.Children.Add(row);
                card.Child = sp;
                OverviewDetailPanel.Children.Add(card);
            }

            switch (part)
            {
                case "头部":
                case "睡眠":
                {
                    var rec = _repo.GetByTypeAndDate("sleep", todayStr);
                    AddRow("今日睡眠", rec != null ? FormatDuration(TimeSpan.FromMinutes(rec.Value)) : "未记录", rec != null ? "PrimaryBrush" : "SecondaryTextBrush");
                    var mood = _repo.GetByTypeAndDate("mood", todayStr);
                    var mi = mood != null ? (int)mood.Value : -1;
                    AddRow("今日心情", mi >= 0 && mi < 4 ? $"{MoodEmojis[mi]} {MoodNames[mi]}" : "未记录", mi >= 0 && mi <= 1 ? "AccentGreenBrush" : "SecondaryTextBrush");
                    break;
                }
                case "心胸":
                case "心率":
                {
                    var rec = _repo.GetByTypeAndDate("heart_rate", todayStr);
                    if (rec != null)
                    {
                        var (text, brush) = ClassifyHeartRate(rec.Value);
                        AddRow("今日静息心率", $"{rec.Value:F0} bpm", brush);
                        AddRow("评估", text, brush);
                    }
                    else AddRow("今日静息心率", "未记录", "SecondaryTextBrush");
                    var week = _repo.GetByType("heart_rate").Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
                    AddRow("近 7 天平均", week.Count > 0 ? $"{week.Average(r => r.Value):F0} bpm" : "暂无数据", "SecondaryTextBrush");
                    AddRow("正常范围", "静息 60 ~ 100 bpm", "SecondaryTextBrush");

                    AddQuickEntry("记录静息心率（静坐 5 分钟后测更准）", () =>
                    {
                        var hrBox = MiniBox();
                        _quickBox1 = hrBox;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        row.Children.Add(new TextBlock { Text = "心率", FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                        row.Children.Add(hrBox);
                        row.Children.Add(new TextBlock { Text = "bpm", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
                        return row;
                    }, () =>
                    {
                        if (!double.TryParse(_quickBox1.Text.Trim(), out var bpm) || bpm < 30 || bpm > 220)
                        {
                            MessageBox.Show("请输入 30 ~ 220 之间的心率值"); return false;
                        }
                        var today = DateTime.Today.ToString("yyyy-MM-dd");
                        var exist = _repo.GetByTypeAndDate("heart_rate", today);
                        if (exist != null) { exist.Value = bpm; _repo.Upsert(exist); }
                        else _repo.Insert(new HealthRecord { Type = "heart_rate", Date = today, Value = bpm });
                        return true;
                    });
                    break;
                }
                case "腹部":
                case "血糖":
                case "体重":
                {
                    // 血糖
                    var sugar = _repo.GetByTypeAndDate("blood_sugar", todayStr);
                    if (sugar != null)
                    {
                        var fasting = sugar.Detail != "post";
                        var (text, brush) = ClassifyBloodSugar(sugar.Value, fasting);
                        AddRow($"今日血糖（{(fasting ? "空腹" : "餐后")}）", $"{sugar.Value:F1} mmol/L", brush);
                        AddRow("评估", text, brush);
                    }
                    else AddRow("今日血糖", "未记录", "SecondaryTextBrush");
                    AddRow("参考范围", "空腹 3.9 ~ 6.1｜餐后 < 7.8", "SecondaryTextBrush");

                    AddQuickEntry("记录血糖（测量的时间决定参考范围）", () =>
                    {
                        _quickBox1 = MiniBox();
                        _quickCombo = new ComboBox { FontSize = 11, Height = 28, Margin = new Thickness(8, 0, 0, 0), MinWidth = 74, VerticalContentAlignment = VerticalAlignment.Center };
                        _quickCombo.Items.Add("空腹"); _quickCombo.Items.Add("餐后"); _quickCombo.SelectedIndex = 0;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        row.Children.Add(new TextBlock { Text = "血糖", FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                        row.Children.Add(_quickBox1);
                        row.Children.Add(new TextBlock { Text = "mmol/L", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
                        row.Children.Add(_quickCombo);
                        return row;
                    }, () =>
                    {
                        if (!double.TryParse(_quickBox1.Text.Trim(), out var mmol) || mmol < 1 || mmol > 35)
                        {
                            MessageBox.Show("请输入 1 ~ 35 之间的血糖值（mmol/L）"); return false;
                        }
                        var today = DateTime.Today.ToString("yyyy-MM-dd");
                        var timing = _quickCombo.SelectedIndex == 1 ? "post" : "fasting";
                        var exist = _repo.GetByTypeAndDate("blood_sugar", today);
                        if (exist != null) { exist.Value = mmol; exist.Detail = timing; _repo.Upsert(exist); }
                        else _repo.Insert(new HealthRecord { Type = "blood_sugar", Date = today, Value = mmol, Detail = timing });
                        return true;
                    });

                    // 体重 / BMI
                    var all = _repo.GetByType("weight").OrderBy(r => r.Date).ToList();
                    var rec = all.LastOrDefault();
                    if (rec == null) AddRow("最新体重", "未记录", "SecondaryTextBrush");
                    else
                    {
                        double h = 0; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthHeight), out h);
                        AddRow("最新体重", $"{rec.Value:F1} kg（{rec.Date}）", "PrimaryBrush");
                        if (h > 0)
                        {
                            var bmi = CalcBmi(rec.Value, h);
                            string g, brush;
                            if (bmi < 18.5) { g = "偏瘦"; brush = "AccentBlueBrush"; }
                            else if (bmi < 24) { g = "正常"; brush = "AccentGreenBrush"; }
                            else if (bmi < 28) { g = "超重"; brush = "AccentYellowBrush"; }
                            else { g = "肥胖"; brush = "AccentRedBrush"; }
                            AddRow("BMI", $"{bmi:F1}（{g}）", brush);
                        }

                        AddQuickEntry("记录体重", () =>
                        {
                            _quickBox1 = MiniBox();
                            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                            row.Children.Add(new TextBlock { Text = "体重", FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                            row.Children.Add(_quickBox1);
                            row.Children.Add(new TextBlock { Text = "kg", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
                            return row;
                        }, () =>
                        {
                            if (!double.TryParse(_quickBox1.Text.Trim(), out var kg) || kg < 20 || kg > 300)
                            {
                                MessageBox.Show("请输入 20 ~ 300 之间的体重（kg）"); return false;
                            }
                            var today = DateTime.Today.ToString("yyyy-MM-dd");
                            var exist = _repo.GetByTypeAndDate("weight", today);
                            if (exist != null) { exist.Value = kg; _repo.Upsert(exist); }
                            else _repo.Insert(new HealthRecord { Type = "weight", Date = today, Value = kg, Detail = _settingsRepo.GetValue(SettingsKeys.HealthHeight) });
                            return true;
                        });
                    }
                    break;
                }
                case "左臂":
                case "血压":
                {
                    var bp = _repo.GetByType("blood_pressure").OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).FirstOrDefault();
                    if (bp != null)
                    {
                        double.TryParse(bp.Detail, out var dia);
                        var (text, brush) = ClassifyBp(bp.Value, dia);
                        AddRow($"血压（{bp.Date}）", $"{bp.Value:F0}/{dia:F0} mmHg", brush);
                        AddRow("评估", text, brush);
                        var week = _repo.GetByType("blood_pressure").Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
                        if (week.Count > 1)
                            AddRow("近 7 天平均", $"{week.Average(r => r.Value):F0}/{week.Average(r => { double.TryParse(r.Detail, out var d2); return d2; }):F0} mmHg", "SecondaryTextBrush");
                    }
                    else AddRow("血压", "未记录", "SecondaryTextBrush");
                    AddRow("参考范围", "< 120/80 正常｜≥ 140/90 偏高", "SecondaryTextBrush");

                    AddQuickEntry("记录血压（静坐 5 分钟后测，袖带与心脏同高）", () =>
                    {
                        _quickBox1 = MiniBox();
                        _quickBox2 = MiniBox();
                        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        row.Children.Add(new TextBlock { Text = "收缩压", FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                        row.Children.Add(_quickBox1);
                        row.Children.Add(new TextBlock { Text = "/", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                        row.Children.Add(new TextBlock { Text = "舒张压", FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                        row.Children.Add(_quickBox2);
                        row.Children.Add(new TextBlock { Text = "mmHg", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
                        return row;
                    }, () =>
                    {
                        var okSys = double.TryParse(_quickBox1.Text.Trim(), out var sys);
                        var okDia = double.TryParse(_quickBox2.Text.Trim(), out var dia);
                        if (!okSys || !okDia || sys < 60 || sys > 260 || dia < 40 || dia > 180 || dia >= sys)
                        {
                            MessageBox.Show("请输入合理的血压：收缩压 60~260，舒张压 40~180，且收缩压 > 舒张压"); return false;
                        }
                        var today = DateTime.Today.ToString("yyyy-MM-dd");
                        var exist = _repo.GetByTypeAndDate("blood_pressure", today);
                        if (exist != null) { exist.Value = sys; exist.Detail = dia.ToString("F0"); _repo.Upsert(exist); }
                        else _repo.Insert(new HealthRecord { Type = "blood_pressure", Date = today, Value = sys, Detail = dia.ToString("F0") });
                        return true;
                    });
                    break;
                }
                case "喝水":
                {
                    var rec = _repo.GetByTypeAndDate("water", todayStr);
                    double goal = 2000; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "2000"), out goal);
                    AddRow("今日喝水", rec != null ? $"{rec.Value:F0} ml" : "0 ml", rec != null ? "AccentBlueBrush" : "SecondaryTextBrush");
                    AddRow("目标", $"{goal:F0} ml", "SecondaryTextBrush");
                    var all = _repo.GetByType("water");
                    var week = all.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
                    AddRow("近 7 天日均", week.Count > 0 ? $"{week.Average(r => r.Value):F0} ml" : "暂无数据", "SecondaryTextBrush");
                    break;
                }
                case "右臂":
                case "用药":
                {
                    var meds = _medRepo.GetAll();
                    if (meds.Count == 0) { AddRow("用药", "暂无用药记录", "SecondaryTextBrush"); break; }
                    AddRow("在用药物", $"{_medRepo.GetActive().Count} 种", "PrimaryBrush");
                    foreach (var m in _medRepo.GetActive())
                        AddRow(m.Name, FormatMedicationSummary(m), "AccentBlueBrush");
                    break;
                }
                case "左腿":
                case "尿酸":
                {
                    var rec = _repo.GetByTypeAndDate("uric_acid", todayStr);
                    if (rec == null) { AddRow("今日尿酸", "未记录", "SecondaryTextBrush"); break; }
                    var (lower, upper) = GetUricRange();
                    var (text, brush) = ClassifyUric(rec.Value, lower, upper);
                    AddRow("今日尿酸", $"{rec.Value:F0} μmol/L", brush);
                    AddRow("正常范围", $"{lower:F0} ~ {upper:F0}", "SecondaryTextBrush");
                    break;
                }
                case "右腿":
                case "运动":
                case "久坐":
                {
                    var sed = _repo.GetByTypeAndDate("sedentary", todayStr);
                    AddRow("今日久坐记录", sed != null ? $"{sed.Value:F0} 次" : "0 次", sed != null && sed.Value >= 4 ? "AccentYellowBrush" : "AccentGreenBrush");
                    AddRow("建议", "每坐 1 小时起身活动 3~5 分钟", "SecondaryTextBrush");
                    AddRow("详细数据", "见「锻炼」标签页（运动/久坐）", "SecondaryTextBrush");
                    break;
                }
                case "心情":
                {
                    var mood2 = _repo.GetByTypeAndDate("mood", todayStr);
                    var mi2 = mood2 != null ? (int)mood2.Value : -1;
                    AddRow("今日心情", mi2 >= 0 && mi2 < 4 ? $"{MoodEmojis[mi2]} {MoodNames[mi2]}" : "未记录", mi2 >= 0 && mi2 <= 1 ? "AccentGreenBrush" : "SecondaryTextBrush");
                    var allMoods = _repo.GetByType("mood");
                    var weekMoods = allMoods.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
                    if (weekMoods.Count > 0)
                    {
                        var avg = weekMoods.Average(r => r.Value);
                        var idx = (int)Math.Round(avg);
                        if (idx < 0 || idx > 3) idx = 1;
                        AddRow("近 7 天平均", $"{MoodEmojis[idx]} {MoodNames[idx]}", "SecondaryTextBrush");
                    }
                    break;
                }
                default:
                    AddRow("信息", "点击人体部位或上方卡片查看对应数据", "SecondaryTextBrush");
                    break;
            }
        }

        /// <summary>部位显示标题（带 emoji 与对应指标说明）</summary>
        private static string PartTitle(string part) => part switch
        {
            "头部" => "🧠 头部 · 睡眠 / 心情",
            "心胸" => "❤️ 心胸 · 静息心率",
            "腹部" => "🍬 腹部 · 血糖 / 体重",
            "左臂" => "💪 左臂 · 血压",
            "右臂" => "💊 右臂 · 用药",
            "左腿" => "🦵 左腿 · 尿酸",
            "右腿" => "🏃 右腿 · 运动 / 久坐",
            "睡眠" => "😴 睡眠", "体重" => "⚖️ 体重", "喝水" => "💧 喝水",
            "心情" => "😊 心情", "尿酸" => "💉 尿酸", "用药" => "💊 用药",
            "血压" => "💪 血压", "心率" => "❤️ 心率", "血糖" => "🍬 血糖", "运动" => "🏃 运动 / 久坐",
            _ => part
        };

        /// <summary>血压分级：≥140/90 偏高，≥130/85 临界，<90/60 偏低</summary>
        private static (string Text, string Brush) ClassifyBp(double sys, double dia)
        {
            if (sys >= 140 || dia >= 90) return ("偏高（建议关注）", "AccentRedBrush");
            if (sys >= 130 || dia >= 85) return ("临界偏高", "AccentYellowBrush");
            if (sys < 90 || dia < 60) return ("偏低", "AccentBlueBrush");
            return ("正常", "AccentGreenBrush");
        }

        /// <summary>静息心率分级：60~100 正常</summary>
        private static (string Text, string Brush) ClassifyHeartRate(double bpm)
        {
            if (bpm < 60) return ("偏缓（运动员可属正常）", "AccentBlueBrush");
            if (bpm > 100) return ("偏快", "AccentYellowBrush");
            return ("正常", "AccentGreenBrush");
        }

        /// <summary>血糖分级：空腹 3.9~6.1 正常；餐后 <7.8 正常</summary>
        private static (string Text, string Brush) ClassifyBloodSugar(double mmol, bool fasting)
        {
            if (fasting)
            {
                if (mmol < 3.9) return ("偏低", "AccentBlueBrush");
                if (mmol <= 6.1) return ("正常", "AccentGreenBrush");
                if (mmol <= 7.0) return ("临界偏高", "AccentYellowBrush");
                return ("偏高（建议关注）", "AccentRedBrush");
            }
            if (mmol < 7.8) return ("正常", "AccentGreenBrush");
            if (mmol <= 11.1) return ("临界偏高", "AccentYellowBrush");
            return ("偏高（建议关注）", "AccentRedBrush");
        }

        private void ExportReport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出健康报告",
                Filter = "HTML 文件|*.html",
                FileName = $"健康报告_{DateTime.Today:yyyyMMdd}.html"
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var html = BuildHealthReportHtml();
                File.WriteAllText(dlg.FileName, html, Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(Window.GetWindow(this), "导出失败", ex.Message, "确定");
            }
        }

        private string BuildHealthReportHtml()
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>健康报告</title>");
            sb.AppendLine("<style>body{font-family:'Microsoft YaHei',sans-serif;padding:24px;color:#1C1C1E}h1{font-size:22px}table{border-collapse:collapse;width:100%;margin:10px 0}th,td{border:1px solid #ccc;padding:6px 10px;font-size:13px;text-align:left}th{background:#f2f2f7}.ok{color:#34C759;font-weight:bold}.warn{color:#FF9500;font-weight:bold}.bad{color:#FF3B30;font-weight:bold}.sec{font-size:16px;font-weight:bold;margin:20px 0 6px;border-bottom:2px solid #007AFF;padding-bottom:4px}</style></head><body>");
            sb.AppendLine($"<h1>健康报告</h1><p>生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}</p>");

            sb.AppendLine("<div class=\"sec\">今日概况</div><table>");
            foreach (var key in new[] { "睡眠", "血压", "心率", "体重", "喝水", "心情", "尿酸", "用药" })
            {
                var (title, value, sub, brushKey) = GetOverviewCardData(key);
                var cls = brushKey == "AccentGreenBrush" ? "ok" : brushKey == "AccentYellowBrush" ? "warn" : brushKey == "AccentRedBrush" ? "bad" : "";
                sb.AppendLine($"<tr><td>{title}</td><td>{value}</td><td>{sub}</td></tr>");
            }
            sb.AppendLine("</table>");

            // 用药信息
            sb.AppendLine("<div class=\"sec\">用药记录</div>");
            var meds = _medRepo.GetAll();
            if (meds.Count == 0)
            {
                sb.AppendLine("<p>暂无用药记录</p>");
            }
            else
            {
                sb.AppendLine("<table><tr><th>药名</th><th>类型</th><th>规格</th><th>频率</th><th>用药时间</th><th>持续时间</th><th>备注</th></tr>");
                foreach (var m in meds)
                {
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(m.Name)}</td><td>{MedicationRepository.MedicationTypeName(m.Type)}</td><td>{FormatSpec(m)}</td><td>{FormatFrequency(m)}</td><td>{FormatTimes(m)}</td><td>{FormatDuration(m)}</td><td>{System.Net.WebUtility.HtmlEncode(m.Note ?? "")}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            // 最近睡眠/体重趋势摘要
            sb.AppendLine("<div class=\"sec\">近 14 天体重记录</div>");
            var weights = _repo.GetByType("weight").OrderByDescending(r => r.Date).Take(14).ToList();
            if (weights.Count == 0) sb.AppendLine("<p>暂无体重记录</p>");
            else
            {
                sb.AppendLine("<table><tr><th>日期</th><th>体重(kg)</th></tr>");
                foreach (var r in weights) sb.AppendLine($"<tr><td>{r.Date}</td><td>{r.Value:F1}</td></tr>");
                sb.AppendLine("</table>");
            }

            sb.AppendLine("<p style=\"margin-top:30px;color:#8E8E93;font-size:11px\">本报告由 ME 生成，仅供健康参考，不构成医疗建议。</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

    }
}
