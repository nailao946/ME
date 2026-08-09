using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ME.Core;
using ME.Data;
using ME.Models;
using ME.Services;

namespace ME.Views
{
    public partial class HealthView : UserControl
    {
        private static readonly string[] MoodEmojis = { "😊", "😐", "😔", "😢" };
        private static readonly string[] MoodNames = { "开心", "平静", "低落", "难过" };
        private static readonly Brush[] MoodColors =
        {
            new SolidColorBrush(Color.FromRgb(52, 199, 89)),
            new SolidColorBrush(Color.FromRgb(90, 200, 250)),
            new SolidColorBrush(Color.FromRgb(255, 159, 10)),
            new SolidColorBrush(Color.FromRgb(90, 90, 110))
        };

        private readonly HealthRepository _repo = new HealthRepository();
        private readonly SettingsRepository _settingsRepo = new SettingsRepository();
        private string _currentTab = "sleep";

        public HealthView()
        {
            InitializeComponent();
            SleepDatePicker.SelectedDate = DateTime.Today;
            WeightDatePicker.SelectedDate = DateTime.Today;
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
            LoadSleep();
            LoadWeight();
            LoadWater();
            LoadMood();
        }

        private void OnGlobalEvent(string message)
        {
            if (message == "DayChanged")
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (this.IsVisible)
                    {
                        SleepDatePicker.SelectedDate = DateTime.Today;
                        WeightDatePicker.SelectedDate = DateTime.Today;
                        LoadSleep();
                        LoadWeight();
                        LoadWater();
                        LoadMood();
                    }
                }));
            }
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (this.IsVisible)
                {
                    LoadSleep();
                    LoadWeight();
                    LoadWater();
                    LoadMood();
                }
            });
        }

        private void HealthView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                LoadSleep();
                LoadWeight();
                LoadWater();
                LoadMood();
            }
        }

        // ============ TAB ============
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            _currentTab = (string)btn.Tag;
            SleepPanel.Visibility = _currentTab == "sleep" ? Visibility.Visible : Visibility.Collapsed;
            WeightPanel.Visibility = _currentTab == "weight" ? Visibility.Visible : Visibility.Collapsed;
            WaterPanel.Visibility = _currentTab == "water" ? Visibility.Visible : Visibility.Collapsed;
            MoodPanel.Visibility = _currentTab == "mood" ? Visibility.Visible : Visibility.Collapsed;

            SleepTabBtn.Style = (Style)FindResource(_currentTab == "sleep" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WeightTabBtn.Style = (Style)FindResource(_currentTab == "weight" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WaterTabBtn.Style = (Style)FindResource(_currentTab == "water" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            MoodTabBtn.Style = (Style)FindResource(_currentTab == "mood" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

            // 面板刚变为可见，按真实宽度重绘图表
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (_currentTab)
                {
                    case "sleep": LoadSleep(); break;
                    case "weight": LoadWeight(); break;
                    case "water": LoadWater(); break;
                    case "mood": LoadMood(); break;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ============ 睡眠 ============
        private static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            var t = (text ?? "").Trim();
            if (TimeSpan.TryParse(t, out var ts)) { time = ts; return true; }
            if (DateTime.TryParse(t, out var dt)) { time = dt.TimeOfDay; return true; }
            return false;
        }

        private static TimeSpan SleepDuration(TimeSpan sleep, TimeSpan wake)
        {
            if (wake > sleep)
                return wake - sleep;
            return TimeSpan.FromHours(24) - sleep + wake;
        }

        private void SaveSleep_Click(object sender, RoutedEventArgs e)
        {
            var date = SleepDatePicker.SelectedDate ?? DateTime.Today;
            if (!TryParseTime(SleepTimeBox.Text, out var sleep) || !TryParseTime(WakeTimeBox.Text, out var wake))
            {
                SleepCalcText.Text = "时间格式不正确，请使用 HH:mm（如 23:00）";
                return;
            }
            if (sleep == wake)
            {
                SleepCalcText.Text = "入睡与起床时间相同，请检查";
                return;
            }
            var dur = SleepDuration(sleep, wake);
            _repo.Upsert(new HealthRecord
            {
                Type = "sleep",
                Date = date.ToString("yyyy-MM-dd"),
                Value = dur.TotalMinutes,
                Detail = $"{sleep:hh\\:mm}|{wake:hh\\:mm}"
            });
            SleepCalcText.Text = $"已保存：{sleep:hh\\:mm} → {wake:hh\\:mm}（{FormatDuration(dur)}）";
            LoadSleep();
        }

        private void ClearSleep_Click(object sender, RoutedEventArgs e)
        {
            var date = SleepDatePicker.SelectedDate ?? DateTime.Today;
            _repo.DeleteByTypeAndDate("sleep", date.ToString("yyyy-MM-dd"));
            SleepCalcText.Text = "已清除当天记录";
            LoadSleep();
        }

        private void LoadSleep()
        {
            var all = _repo.GetByType("sleep");
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");

            // 统计
            var today = all.FirstOrDefault(r => r.Date == todayStr);
            SleepTodayText.Text = today != null ? FormatDuration(TimeSpan.FromMinutes(today.Value)) : "--";

            var weekStart = TaskService.GetWeekStartForDate(DateTime.Today);
            var weekRecords = all.Where(r => string.CompareOrdinal(r.Date, weekStart.ToString("yyyy-MM-dd")) >= 0 &&
                                             string.CompareOrdinal(r.Date, todayStr) <= 0).ToList();
            SleepWeekAvgText.Text = weekRecords.Count > 0
                ? FormatDuration(TimeSpan.FromMinutes(weekRecords.Average(r => r.Value)))
                : "--";

            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var monthRecords = all.Where(r => string.CompareOrdinal(r.Date, monthStart.ToString("yyyy-MM-dd")) >= 0 &&
                                              string.CompareOrdinal(r.Date, todayStr) <= 0).ToList();
            SleepMonthAvgText.Text = monthRecords.Count > 0
                ? FormatDuration(TimeSpan.FromMinutes(monthRecords.Average(r => r.Value)))
                : "--";

            // 近 7 天柱状图
            DrawSleepChart(all);

            // 记录列表（最近 14 条）
            SleepRecordsPanel.Children.Clear();
            var recent = all.OrderByDescending(r => r.Date).Take(14).ToList();
            foreach (var rec in recent)
            {
                var dur = TimeSpan.FromMinutes(rec.Value);
                SleepRecordsPanel.Children.Add(BuildRecordRow(
                    $"{rec.Date}  {rec.Detail ?? ""}",
                    FormatDuration(dur),
                    (s, ev) => { _repo.Delete(rec.Id); LoadSleep(); }));
            }
            if (recent.Count == 0)
                SleepRecordsPanel.Children.Add(BuildEmptyHint("还没有睡眠记录"));
        }

        private void DrawSleepChart(List<HealthRecord> all)
        {
            SleepChartCanvas.Children.Clear();
            var w = SleepChartCanvas.ActualWidth;
            var h = SleepChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 150;
            double maxMinutes = 12 * 60;

            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-(6 - i))).ToList();
            var barW = w / 7 * 0.6;
            var gap = w / 7;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var barBrush = (Brush)FindResource("PrimaryBrush");

            for (int i = 0; i < days.Count; i++)
            {
                var rec = all.FirstOrDefault(r => r.Date == days[i].ToString("yyyy-MM-dd"));
                var minutes = rec?.Value ?? 0;
                var barH = h * 0.75 * Math.Min(minutes / maxMinutes, 1.0);
                var x = i * gap + (gap - barW) / 2;
                var y = h - 20 - barH;
                var rect = new Rectangle
                {
                    Width = barW,
                    Height = barH,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = barBrush,
                    Opacity = 0.9
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                SleepChartCanvas.Children.Add(rect);

                var label = new TextBlock
                {
                    Text = days[i].Day.ToString(),
                    FontSize = 9,
                    Foreground = textBrush
                };
                Canvas.SetLeft(label, x + (barW - 12) / 2);
                Canvas.SetTop(label, h - 16);
                SleepChartCanvas.Children.Add(label);

                if (minutes > 0)
                {
                    var val = new TextBlock
                    {
                        Text = $"{minutes / 60:F0}h",
                        FontSize = 9,
                        Foreground = textBrush
                    };
                    Canvas.SetLeft(val, x + (barW - 16) / 2);
                    Canvas.SetTop(val, y - 14);
                    SleepChartCanvas.Children.Add(val);
                }
            }
            SleepChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 20, X2 = w, Y2 = h - 20, Stroke = axisBrush, StrokeThickness = 1 });
        }

        // ============ 体重 ============
        private void SaveWeight_Click(object sender, RoutedEventArgs e)
        {
            var date = WeightDatePicker.SelectedDate ?? DateTime.Today;
            if (!double.TryParse(WeightBox.Text, out var kg) || kg <= 0)
            {
                BmiText.Text = "请输入有效的体重";
                return;
            }
            double heightCm = 0;
            if (double.TryParse(HeightBox.Text, out var h) && h > 0)
            {
                heightCm = h;
                _settingsRepo.SetValue(SettingsKeys.HealthHeight, heightCm.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                var saved = _settingsRepo.GetValue(SettingsKeys.HealthHeight);
                double.TryParse(saved, out heightCm);
            }

            // 保留旧记录中的身高，避免本次未填身高时抹掉历史 BMI 数据
            string detail = heightCm > 0 ? heightCm.ToString(CultureInfo.InvariantCulture) : null;
            var existing = _repo.GetByTypeAndDate("weight", date.ToString("yyyy-MM-dd"));
            if (detail == null && existing != null)
                detail = existing.Detail;

            _repo.Upsert(new HealthRecord
            {
                Type = "weight",
                Date = date.ToString("yyyy-MM-dd"),
                Value = kg,
                Detail = detail
            });
            BmiText.Text = heightCm > 0 ? $"BMI：{CalcBmi(kg, heightCm):F1}" : "未记录身高，可在录入时填写";
            LoadWeight();
        }

        private void ClearWeight_Click(object sender, RoutedEventArgs e)
        {
            var date = WeightDatePicker.SelectedDate ?? DateTime.Today;
            _repo.DeleteByTypeAndDate("weight", date.ToString("yyyy-MM-dd"));
            BmiText.Text = "已清除当天记录";
            LoadWeight();
        }

        private static double CalcBmi(double kg, double heightCm)
        {
            if (heightCm <= 0) return 0;
            var m = heightCm / 100.0;
            return kg / (m * m);
        }

        private void LoadWeight()
        {
            var all = _repo.GetByType("weight").OrderBy(r => r.Date).ToList();
            double heightCm = 0;
            double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthHeight), out heightCm);

            var latest = all.LastOrDefault();
            WeightLatestText.Text = latest != null ? $"{latest.Value:F1} kg" : "--";
            BmiValueText.Text = latest != null && heightCm > 0 ? $"{CalcBmi(latest.Value, heightCm):F1}" : "--";

            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var monthRecords = all.Where(r => string.CompareOrdinal(r.Date, monthStart.ToString("yyyy-MM-dd")) >= 0).ToList();
            if (monthRecords.Count >= 2)
            {
                var delta = monthRecords.Last().Value - monthRecords.First().Value;
                WeightChangeText.Text = $"{(delta >= 0 ? "+" : "")}{delta:F1} kg";
                WeightChangeText.Foreground = (Brush)FindResource(delta <= 0 ? "AccentGreenBrush" : "AccentRedBrush");
            }
            else
            {
                WeightChangeText.Text = "--";
                WeightChangeText.Foreground = (Brush)FindResource("AccentGreenBrush");
            }

            DrawWeightChart(all);

            WeightRecordsPanel.Children.Clear();
            var recent = all.OrderByDescending(r => r.Date).Take(14).ToList();
            foreach (var rec in recent)
            {
                var bmi = rec.Detail != null && double.TryParse(rec.Detail, out var hc) && hc > 0
                    ? $"  BMI {CalcBmi(rec.Value, hc):F1}" : "";
                WeightRecordsPanel.Children.Add(BuildRecordRow(
                    $"{rec.Date}  {rec.Value:F1} kg{bmi}",
                    null,
                    (s, ev) => { _repo.Delete(rec.Id); LoadWeight(); }));
            }
            if (recent.Count == 0)
                WeightRecordsPanel.Children.Add(BuildEmptyHint("还没有体重记录"));
        }

        private void DrawWeightChart(List<HealthRecord> all)
        {
            WeightChartCanvas.Children.Clear();
            var w = WeightChartCanvas.ActualWidth;
            var h = WeightChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 170;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var lineBrush = (Brush)FindResource("AccentBlueBrush");

            var days = Enumerable.Range(0, 30).Select(i => DateTime.Today.AddDays(-(29 - i))).ToList();
            var values = days.Select(d => all.FirstOrDefault(r => r.Date == d.ToString("yyyy-MM-dd"))?.Value)
                             .ToList();

            WeightChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 20, X2 = w, Y2 = h - 20, Stroke = axisBrush, StrokeThickness = 1 });

            var valid = values.Where(v => v.HasValue).ToList();
            if (valid.Count == 0) return;

            var minV = Math.Min(valid.Min().Value - 1, 40);
            var maxV = Math.Max(valid.Max().Value + 1, minV + 5);
            var range = maxV - minV;
            if (range <= 0) range = 1;

            var points = new PointCollection();
            for (int i = 0; i < days.Count; i++)
            {
                if (!values[i].HasValue) continue;
                var x = (i + 0.5) * w / days.Count;
                var y = h - 20 - (values[i].Value - minV) / range * (h - 40);
                points.Add(new Point(x, y));
            }
            if (points.Count >= 2)
            {
                WeightChartCanvas.Children.Add(new Polyline
                {
                    Points = points,
                    Stroke = lineBrush,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                });
            }
            for (int i = 0; i < days.Count; i++)
            {
                if (!values[i].HasValue) continue;
                var x = (i + 0.5) * w / days.Count;
                var y = h - 20 - (values[i].Value - minV) / range * (h - 40);
                WeightChartCanvas.Children.Add(new Ellipse
                {
                    Width = 5, Height = 5,
                    Fill = lineBrush
                });
                Canvas.SetLeft(WeightChartCanvas.Children[WeightChartCanvas.Children.Count - 1], x - 2.5);
                Canvas.SetTop(WeightChartCanvas.Children[WeightChartCanvas.Children.Count - 1], y - 2.5);
            }

            var lb = new TextBlock { Text = $"{minV:F0}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(lb, 2); Canvas.SetTop(lb, 0);
            WeightChartCanvas.Children.Add(lb);
            var ub = new TextBlock { Text = $"{maxV:F0}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(ub, 2); Canvas.SetTop(ub, h - 34);
            WeightChartCanvas.Children.Add(ub);
        }

        // ============ 喝水 ============
        private void WaterPlus_Click(object sender, RoutedEventArgs e)
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var rec = _repo.GetByTypeAndDate("water", todayStr);
            var count = rec != null ? (int)rec.Value : 0;
            _repo.Upsert(new HealthRecord { Type = "water", Date = todayStr, Value = count + 1 });
            LoadWater();
        }

        private void WaterMinus_Click(object sender, RoutedEventArgs e)
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var rec = _repo.GetByTypeAndDate("water", todayStr);
            var count = rec != null ? (int)rec.Value : 0;
            if (count <= 0) { LoadWater(); return; }
            _repo.Upsert(new HealthRecord { Type = "water", Date = todayStr, Value = count - 1 });
            LoadWater();
        }

        private void WaterGoalBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 仅预览，点击"保存目标"才写入设置
        }

        private void SaveWaterGoal_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(WaterGoalBox.Text, out var goal) && goal > 0 && goal <= 99)
            {
                _settingsRepo.SetValue(SettingsKeys.HealthWaterGoal, goal.ToString());
                LoadWater();
            }
        }

        private void LoadWater()
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            int goal = 8;
            int.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "8"), out goal);
            if (goal <= 0) goal = 8;
            WaterGoalBox.Text = goal.ToString();

            var rec = _repo.GetByTypeAndDate("water", todayStr);
            var count = rec != null ? (int)rec.Value : 0;
            WaterTodayCountText.Text = count.ToString();
            WaterTodayStatText.Text = $"{count} 杯";
            WaterTargetText.Text = $"/ {goal} 杯";
            WaterProgressBar.Maximum = goal;
            WaterProgressBar.Value = Math.Min(count, goal);

            var all = _repo.GetByType("water");
            var weekRecords = all.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
            WaterAvgText.Text = weekRecords.Count > 0
                ? $"{weekRecords.Average(r => r.Value):F1} 杯"
                : "0 杯";

            DrawWaterChart(all);

            WaterRecordsPanel.Children.Clear();
            var recent = all.OrderByDescending(r => r.Date).Take(14).ToList();
            foreach (var r in recent)
            {
                WaterRecordsPanel.Children.Add(BuildRecordRow(
                    $"{r.Date}  {r.Value:F0} 杯",
                    null,
                    (s, ev) => { _repo.Delete(r.Id); LoadWater(); }));
            }
            if (recent.Count == 0)
                WaterRecordsPanel.Children.Add(BuildEmptyHint("还没有喝水记录"));
        }

        private void DrawWaterChart(List<HealthRecord> all)
        {
            WaterChartCanvas.Children.Clear();
            var w = WaterChartCanvas.ActualWidth;
            var h = WaterChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 150;
            int goal = 8;
            int.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "8"), out goal);
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var barBrush = (Brush)FindResource("AccentBlueBrush");

            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-(6 - i))).ToList();
            var gap = w / 7;
            var barW = gap * 0.6;
            double max = Math.Max(goal * 1.2, 8);

            for (int i = 0; i < days.Count; i++)
            {
                var rec = all.FirstOrDefault(r => r.Date == days[i].ToString("yyyy-MM-dd"));
                var count = rec?.Value ?? 0;
                var barH = h * 0.75 * Math.Min(count / max, 1.0);
                var x = i * gap + (gap - barW) / 2;
                var y = h - 20 - barH;
                var rect = new Rectangle
                {
                    Width = barW, Height = barH, RadiusX = 3, RadiusY = 3,
                    Fill = barBrush, Opacity = 0.9
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                WaterChartCanvas.Children.Add(rect);

                var label = new TextBlock { Text = days[i].Day.ToString(), FontSize = 9, Foreground = textBrush };
                Canvas.SetLeft(label, x + (barW - 12) / 2);
                Canvas.SetTop(label, h - 16);
                WaterChartCanvas.Children.Add(label);

                if (count > 0)
                {
                    var val = new TextBlock { Text = $"{count:F0}", FontSize = 9, Foreground = textBrush };
                    Canvas.SetLeft(val, x + (barW - 10) / 2);
                    Canvas.SetTop(val, y - 14);
                    WaterChartCanvas.Children.Add(val);
                }
            }
            WaterChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 20, X2 = w, Y2 = h - 20, Stroke = axisBrush, StrokeThickness = 1 });
        }

        // ============ 心情 ============
        private void Mood_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            var idx = int.Parse((string)btn.Tag);
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            _repo.Upsert(new HealthRecord { Type = "mood", Date = todayStr, Value = idx });
            LoadMood();
        }

        private void ClearMood_Click(object sender, RoutedEventArgs e)
        {
            _repo.DeleteByTypeAndDate("mood", DateTime.Today.ToString("yyyy-MM-dd"));
            LoadMood();
        }

        private void LoadMood()
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var todayRec = _repo.GetByTypeAndDate("mood", todayStr);
            int todayMood = todayRec != null ? (int)todayRec.Value : -1;

            MoodSelectedText.Text = todayMood >= 0 ? $"今天的心情：{MoodEmojis[todayMood]} {MoodNames[todayMood]}" : "";

            var moodBtns = new[] { MoodBtn0, MoodBtn1, MoodBtn2, MoodBtn3 };
            for (int i = 0; i < moodBtns.Length; i++)
            {
                var selected = i == todayMood;
                moodBtns[i].Style = (Style)FindResource(selected ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
                moodBtns[i].Foreground = selected ? Brushes.White : (Brush)FindResource("TextBrush");
            }

            // 近 7 天分布
            MoodDistributionPanel.Children.Clear();
            var all = _repo.GetByType("mood");
            var weekMoods = all.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
            var counts = new int[4];
            foreach (var r in weekMoods)
            {
                var idx = (int)r.Value;
                if (idx >= 0 && idx < 4) counts[idx]++;
            }
            for (int i = 0; i < 4; i++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(new TextBlock
                {
                    Text = MoodEmojis[i],
                    FontSize = 14,
                    Width = 28,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var bar = new Border
                {
                    Width = 120,
                    Height = 12,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
                };
                var fill = new Border
                {
                    Width = weekMoods.Count > 0 ? Math.Max(4, 120.0 * counts[i] / weekMoods.Count) : 4,
                    Height = 12,
                    CornerRadius = new CornerRadius(6),
                    Background = MoodColors[i]
                };
                bar.Child = fill;
                row.Children.Add(bar);
                row.Children.Add(new TextBlock
                {
                    Text = $"{counts[i]} 次",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                MoodDistributionPanel.Children.Add(row);
            }
            if (weekMoods.Count == 0)
                MoodDistributionPanel.Children.Add(BuildEmptyHint("近 7 天还没有心情记录"));

            // 近 30 天时间线
            MoodTimelinePanel.Children.Clear();
            var moodDays = all.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-29).ToString("yyyy-MM-dd")) >= 0)
                              .ToDictionary(r => r.Date, r => (int)r.Value);
            for (int i = 29; i >= 0; i--)
            {
                var d = DateTime.Today.AddDays(-i);
                var key = d.ToString("yyyy-MM-dd");
                var box = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(2),
                    Background = moodDays.TryGetValue(key, out var mi) && mi >= 0 && mi < 4
                        ? MoodColors[mi]
                        : new SolidColorBrush(Color.FromArgb(25, 128, 128, 128)),
                    ToolTip = d.ToString("MM/dd") + (moodDays.TryGetValue(key, out var m2) ? $" {MoodNames[m2]}" : "")
                };
                box.Child = moodDays.TryGetValue(key, out var em) && em >= 0 && em < 4
                    ? new TextBlock { Text = MoodEmojis[em], FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : null;
                MoodTimelinePanel.Children.Add(box);
            }

            // 记录列表
            MoodRecordsPanel.Children.Clear();
            var recent = all.OrderByDescending(r => r.Date).Take(14).ToList();
            foreach (var r in recent)
            {
                var idx = (int)r.Value;
                var moodText = idx >= 0 && idx < 4 ? $"{MoodEmojis[idx]} {MoodNames[idx]}" : "?";
                MoodRecordsPanel.Children.Add(BuildRecordRow(
                    $"{r.Date}  {moodText}",
                    null,
                    (s, ev) => { _repo.Delete(r.Id); LoadMood(); }));
            }
            if (recent.Count == 0)
                MoodRecordsPanel.Children.Add(BuildEmptyHint("还没有心情记录"));
        }

        // ============ 通用 ============
        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h{ts.Minutes:D2}m";
            return $"{ts.Minutes}m";
        }

        private Border BuildRecordRow(string text, string value, RoutedEventHandler deleteClick)
        {
            var border = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var dock = new DockPanel();
            var delBtn = new Button
            {
                Content = "✕",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Width = 26,
                Height = 24,
                FontSize = 10,
                Padding = new Thickness(0),
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(delBtn, Dock.Right);
            delBtn.Click += deleteClick;
            dock.Children.Add(delBtn);
            if (!string.IsNullOrEmpty(value))
            {
                var valueText = new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("PrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(valueText, Dock.Right);
                dock.Children.Add(valueText);
            }
            dock.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            border.Child = dock;
            return border;
        }

        private TextBlock BuildEmptyHint(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(0, 10, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
    }
}
