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
        private bool _loadingUric;

        public HealthView()
        {
            InitializeComponent();
            SleepDatePicker.SelectedDate = DateTime.Today;
            WeightDatePicker.SelectedDate = DateTime.Today;
            UricAcidDatePicker.SelectedDate = DateTime.Today;
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
            LoadSleep();
            LoadWeight();
            LoadWater();
            LoadMood();
            LoadUricAcid();
            LoadCompare();
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
                        LoadUricAcid();
                        LoadCompare();
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
                    LoadUricAcid();
                    LoadCompare();
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
                LoadUricAcid();
                LoadCompare();
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
            UricAcidPanel.Visibility = _currentTab == "uric_acid" ? Visibility.Visible : Visibility.Collapsed;
            ComparePanel.Visibility = _currentTab == "compare" ? Visibility.Visible : Visibility.Collapsed;

            SleepTabBtn.Style = (Style)FindResource(_currentTab == "sleep" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WeightTabBtn.Style = (Style)FindResource(_currentTab == "weight" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WaterTabBtn.Style = (Style)FindResource(_currentTab == "water" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            MoodTabBtn.Style = (Style)FindResource(_currentTab == "mood" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            UricAcidTabBtn.Style = (Style)FindResource(_currentTab == "uric_acid" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            CompareTabBtn.Style = (Style)FindResource(_currentTab == "compare" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

            // 面板刚变为可见，按真实宽度重绘图表
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (_currentTab)
                {
                    case "sleep": LoadSleep(); break;
                    case "weight": LoadWeight(); break;
                    case "water": LoadWater(); break;
                    case "mood": LoadMood(); break;
                    case "uric_acid": LoadUricAcid(); break;
                    case "compare": LoadCompare(); break;
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
        private readonly WaterContainerRepository _containerRepo = new WaterContainerRepository();
        private string _waterPeriod = "week";

        /// <summary>旧版本按"杯"记录（默认 250ml/杯），首次启动迁移为 ml</summary>
        private void EnsureWaterMigrated()
        {
            var migrated = _settingsRepo.GetValue(SettingsKeys.HealthWaterMigrated, "0");
            if (migrated == "1") return;
            // 必须基于全部记录保存，避免覆盖掉 sleep/weight/mood 等其它类型
            var all = _repo.GetAll();
            var waterRecords = all.Where(r => r.Type == "water").ToList();
            bool changed = false;
            foreach (var r in waterRecords)
            {
                // 旧杯数：正整数且 <= 50 杯；已迁移的 ml 值通常 >= 100
                if (r.Value > 0 && r.Value <= 50 && r.Value % 1 == 0)
                {
                    r.Value = Math.Round(r.Value * 250);
                    changed = true;
                }
            }
            if (changed)
            {
                // all 的元素即磁盘反序列化对象引用，直接全量保存
                JsonStore.Save("health_records", all);
            }
            _settingsRepo.SetValue(SettingsKeys.HealthWaterMigrated, "1");
        }

        private void WaterContainerCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateWaterStepText();
        }

        private void UpdateWaterStepText()
        {
            var c = GetSelectedContainer();
            WaterStepText.Text = c != null ? $"每次 +{c.CapacityMl:0} ml" : "请先添加容器";
        }

        private WaterContainer GetSelectedContainer()
        {
            if (WaterContainerCombo.SelectedItem is WaterContainer c) return c;
            if (WaterContainerCombo.Items.Count > 0)
            {
                // 未显式选中时自动选中第一个，保证 +1/-1 始终可用
                WaterContainerCombo.SelectedIndex = 0;
                return WaterContainerCombo.SelectedItem as WaterContainer;
            }
            var items = _containerRepo.EnsureDefaults();
            return items.FirstOrDefault();
        }

        private void ManageContainers_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WaterContainerDialog { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
            ReloadWaterContainers();
        }

        private void ReloadWaterContainers()
        {
            var items = _containerRepo.EnsureDefaults();
            var selId = (WaterContainerCombo.SelectedItem as WaterContainer)?.Id ?? 0;
            WaterContainerCombo.Items.Clear();
            foreach (var c in items)
                WaterContainerCombo.Items.Add(c);
            var sel = items.FirstOrDefault(c => c.Id == selId) ?? items.FirstOrDefault();
            WaterContainerCombo.SelectedItem = sel;
            UpdateWaterStepText();
        }

        private void WaterPeriod_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            _waterPeriod = (string)btn.Tag;
            WaterPeriodTodayBtn.Style = (Style)FindResource(_waterPeriod == "today" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WaterPeriodWeekBtn.Style = (Style)FindResource(_waterPeriod == "week" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WaterPeriodMonthBtn.Style = (Style)FindResource(_waterPeriod == "month" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            LoadWater();
        }

        private void WaterPlus_Click(object sender, RoutedEventArgs e)
        {
            var container = GetSelectedContainer();
            if (container == null) return;
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var rec = _repo.GetByTypeAndDate("water", todayStr);
            var ml = rec != null ? rec.Value : 0;
            _repo.Upsert(new HealthRecord { Type = "water", Date = todayStr, Value = ml + container.CapacityMl });
            LoadWater();
        }

        private void WaterMinus_Click(object sender, RoutedEventArgs e)
        {
            var container = GetSelectedContainer();
            if (container == null) return;
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var rec = _repo.GetByTypeAndDate("water", todayStr);
            var ml = rec != null ? rec.Value : 0;
            if (ml <= 0) { LoadWater(); return; }
            _repo.Upsert(new HealthRecord { Type = "water", Date = todayStr, Value = Math.Max(0, ml - container.CapacityMl) });
            LoadWater();
        }

        private void SaveWaterGoal_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(WaterGoalBox.Text, out var goal) && goal > 0 && goal <= 20000)
            {
                _settingsRepo.SetValue(SettingsKeys.HealthWaterGoal, goal.ToString(CultureInfo.InvariantCulture));
                LoadWater();
            }
            else
            {
                WaterTargetText.Text = "目标无效";
            }
        }

        private void LoadWater()
        {
            EnsureWaterMigrated();
            if (WaterContainerCombo.Items.Count == 0)
                ReloadWaterContainers();
            else
                UpdateWaterStepText();

            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            double goal = 2000;
            double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "2000"), NumberStyles.Any, CultureInfo.InvariantCulture, out goal);
            if (goal <= 0) goal = 2000;
            WaterGoalBox.Text = goal.ToString("F0");

            var rec = _repo.GetByTypeAndDate("water", todayStr);
            var todayMl = rec != null ? rec.Value : 0;
            WaterTodayCountText.Text = todayMl.ToString("F0");
            WaterTargetText.Text = $"/ {goal:F0} ml";
            WaterProgressBar.Maximum = goal;
            WaterProgressBar.Value = Math.Min(todayMl, goal);

            // 周期统计
            var all = _repo.GetByType("water");
            DateTime start;
            if (_waterPeriod == "today")
            {
                start = DateTime.Today;
                WaterPeriodLabel.Text = "今日";
                WaterChartTitle.Text = "今日喝水";
            }
            else if (_waterPeriod == "week")
            {
                start = TaskService.GetWeekStartForDate(DateTime.Today);
                WaterPeriodLabel.Text = "本周平均";
                WaterChartTitle.Text = "本周喝水";
            }
            else
            {
                start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                WaterPeriodLabel.Text = "本月平均";
                WaterChartTitle.Text = "本月喝水";
            }

            var startStr = start.ToString("yyyy-MM-dd");
            var records = all.Where(r => string.CompareOrdinal(r.Date, startStr) >= 0 &&
                                         string.CompareOrdinal(r.Date, todayStr) <= 0).ToList();
            WaterAvgText.Text = records.Count > 0 ? $"{records.Average(r => r.Value):F0} ml" : "0 ml";
            WaterTotalText.Text = $"{records.Sum(r => r.Value):F0} ml";

            int totalDays = _waterPeriod == "today" ? 1 : (DateTime.Today - start).Days + 1;
            var metDays = records.Count(r => r.Value >= goal);
            WaterRateText.Text = $"{metDays * 100.0 / totalDays:F0}%";

            DrawWaterChart(all, start, _waterPeriod == "today" ? 1 : 0);

            WaterRecordsPanel.Children.Clear();
            var recent = all.OrderByDescending(r => r.Date).Take(14).ToList();
            foreach (var r in recent)
            {
                WaterRecordsPanel.Children.Add(BuildRecordRow(
                    $"{r.Date}  {r.Value:F0} ml",
                    null,
                    (s, ev) => { _repo.Delete(r.Id); LoadWater(); }));
            }
            if (recent.Count == 0)
                WaterRecordsPanel.Children.Add(BuildEmptyHint("还没有喝水记录"));
        }

        private void DrawWaterChart(List<HealthRecord> all, DateTime startDate, int isToday)
        {
            WaterChartCanvas.Children.Clear();
            var w = WaterChartCanvas.ActualWidth;
            var h = WaterChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 150;
            double goal = 2000;
            double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthWaterGoal, "2000"), NumberStyles.Any, CultureInfo.InvariantCulture, out goal);
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var barBrush = (Brush)FindResource("AccentBlueBrush");

            List<DateTime> days;
            if (_waterPeriod == "today")
            {
                days = new List<DateTime> { DateTime.Today };
            }
            else if (_waterPeriod == "week")
            {
                var ws = TaskService.GetWeekStartForDate(DateTime.Today);
                days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i)).Where(d => d <= DateTime.Today).ToList();
            }
            else
            {
                var ms = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                days = Enumerable.Range(0, DateTime.Today.Day).Select(i => ms.AddDays(i)).ToList();
            }

            var gap = w / days.Count;
            var barW = Math.Max(6, gap * 0.6);
            double max = Math.Max(goal * 1.2, 500);

            for (int i = 0; i < days.Count; i++)
            {
                var rec = all.FirstOrDefault(r => r.Date == days[i].ToString("yyyy-MM-dd"));
                var ml = rec?.Value ?? 0;
                var barH = h * 0.75 * Math.Min(ml / max, 1.0);
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

                if (ml > 0 && gap > 30)
                {
                    var val = new TextBlock { Text = $"{ml:F0}", FontSize = 9, Foreground = textBrush };
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

        // ============ 尿酸 ============
        private (double lower, double upper) GetUricRange()
        {
            var gender = _settingsRepo.GetValue(SettingsKeys.HealthGender, "male");
            return gender == "female" ? (89, 357) : (149, 416);
        }

        private void UricGenderCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingUric) return;
            if (UricGenderCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _settingsRepo.SetValue(SettingsKeys.HealthGender, tag);
                LoadUricAcid();
            }
        }

        private void SaveUricAcid_Click(object sender, RoutedEventArgs e)
        {
            var date = UricAcidDatePicker.SelectedDate ?? DateTime.Today;
            if (!double.TryParse(UricAcidBox.Text, out var v) || v <= 0)
            {
                UricAcidHint.Text = "请输入有效的尿酸值（μmol/L）";
                return;
            }
            _repo.Upsert(new HealthRecord { Type = "uric_acid", Date = date.ToString("yyyy-MM-dd"), Value = v });
            UricAcidHint.Text = $"已保存：{v:F0} μmol/L";
            LoadUricAcid();
        }

        private void ClearUricAcid_Click(object sender, RoutedEventArgs e)
        {
            var date = UricAcidDatePicker.SelectedDate ?? DateTime.Today;
            _repo.DeleteByTypeAndDate("uric_acid", date.ToString("yyyy-MM-dd"));
            UricAcidHint.Text = "已清除当天记录";
            LoadUricAcid();
        }

        private void LoadUricAcid()
        {
            var gender = _settingsRepo.GetValue(SettingsKeys.HealthGender, "male");
            if (!_loadingUric)
            {
                _loadingUric = true;
                foreach (ComboBoxItem it in UricGenderCombo.Items)
                {
                    if (it.Tag is string g && g == gender)
                    {
                        if (!Equals(UricGenderCombo.SelectedItem, it))
                            UricGenderCombo.SelectedItem = it;
                        break;
                    }
                }
                _loadingUric = false;
            }
            var all = _repo.GetByType("uric_acid").OrderBy(r => r.Date).ToList();
            var (lower, upper) = GetUricRange();
            UricRangeText.Text = $"正常范围：{lower:F0} ~ {upper:F0} μmol/L（参考线）";

            var latest = all.LastOrDefault();
            if (latest != null)
            {
                UricLatestText.Text = $"最新：{latest.Value:F0} μmol/L";
                var (text, brush) = ClassifyUric(latest.Value, lower, upper);
                UricLatestText.Text += $"　{text}";
                UricLatestText.Foreground = (Brush)FindResource(brush);
            }
            else
            {
                UricLatestText.Text = "--";
                UricLatestText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            }

            DrawUricChart(all, lower, upper);

            UricRecordsPanel.Children.Clear();
            var recent = all.OrderByDescending(r => r.Date).Take(14).ToList();
            foreach (var r in recent)
            {
                var (text, brush) = ClassifyUric(r.Value, lower, upper);
                UricRecordsPanel.Children.Add(BuildRecordRow(
                    $"{r.Date}  {r.Value:F0} μmol/L",
                    text,
                    (s, ev) => { _repo.Delete(r.Id); LoadUricAcid(); },
                    brush));
            }
            if (recent.Count == 0)
                UricRecordsPanel.Children.Add(BuildEmptyHint("还没有尿酸记录"));
        }

        private (string text, string brushKey) ClassifyUric(double v, double lower, double upper)
        {
            if (v < lower) return ("偏低", "AccentYellowBrush");
            if (v > upper) return ("偏高", "AccentRedBrush");
            return ("正常", "AccentGreenBrush");
        }

        private void DrawUricChart(List<HealthRecord> all, double lower, double upper)
        {
            UricChartCanvas.Children.Clear();
            var w = UricChartCanvas.ActualWidth;
            var h = UricChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 170;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var lineBrush = (Brush)FindResource("PrimaryBrush");

            var days = Enumerable.Range(0, 30).Select(i => DateTime.Today.AddDays(-(29 - i))).ToList();
            var values = days.Select(d => all.FirstOrDefault(r => r.Date == d.ToString("yyyy-MM-dd"))?.Value).ToList();
            var vals = values.Where(v => v.HasValue).Select(v => v.Value).ToList();

            var minV = Math.Min(lower, vals.Count > 0 ? vals.Min() : lower) - 20;
            var maxV = Math.Max(upper, vals.Count > 0 ? vals.Max() : upper) + 20;
            var range = maxV - minV;
            if (range <= 0) range = 1;

            double Y(double v) => h - 20 - (v - minV) / range * (h - 40);

            // 参考线：上限（红虚线）、下限（绿虚线）
            var upLine = new Line { X1 = 0, X2 = w, Y1 = Y(upper), Y2 = Y(upper), Stroke = new SolidColorBrush(Color.FromRgb(255, 59, 48)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, Opacity = 0.7 };
            UricChartCanvas.Children.Add(upLine);
            var upLabel = new TextBlock { Text = $"上限 {upper:F0}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(upLabel, 2); Canvas.SetTop(upLabel, Y(upper) - 14);
            UricChartCanvas.Children.Add(upLabel);
            var lowLine = new Line { X1 = 0, X2 = w, Y1 = Y(lower), Y2 = Y(lower), Stroke = new SolidColorBrush(Color.FromRgb(52, 199, 89)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, Opacity = 0.7 };
            UricChartCanvas.Children.Add(lowLine);
            var lowLabel = new TextBlock { Text = $"下限 {lower:F0}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(lowLabel, 2); Canvas.SetTop(lowLabel, Y(lower) - 14);
            UricChartCanvas.Children.Add(lowLabel);

            // 数据点 + 连线
            var pts = new List<Point>();
            for (int i = 0; i < days.Count; i++)
            {
                if (!values[i].HasValue) continue;
                var x = (i + 0.5) * w / days.Count;
                var y = Y(values[i].Value);
                pts.Add(new Point(x, y));
                UricChartCanvas.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = lineBrush });
                Canvas.SetLeft(UricChartCanvas.Children[UricChartCanvas.Children.Count - 1], x - 2.5);
                Canvas.SetTop(UricChartCanvas.Children[UricChartCanvas.Children.Count - 1], y - 2.5);
            }
            for (int i = 1; i < pts.Count; i++)
            {
                UricChartCanvas.Children.Add(new Line { X1 = pts[i - 1].X, Y1 = pts[i - 1].Y, X2 = pts[i].X, Y2 = pts[i].Y, Stroke = lineBrush, StrokeThickness = 1.5, Opacity = 0.8 });
            }

            var lb = new TextBlock { Text = $"{minV:F0}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(lb, 2); Canvas.SetTop(lb, 0);
            UricChartCanvas.Children.Add(lb);
            var ub = new TextBlock { Text = $"{maxV:F0}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(ub, 2); Canvas.SetTop(ub, h - 34);
            UricChartCanvas.Children.Add(ub);
        }

        // ============ 对比 + AI 分析 ============
        private int _compareDays = 30;

        private void CompareRange_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            _compareDays = int.Parse((string)btn.Tag);
            CmpRange7Btn.Style = (Style)FindResource(_compareDays == 7 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            CmpRange30Btn.Style = (Style)FindResource(_compareDays == 30 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            LoadCompare();
        }

        private void CompareParam_Changed(object sender, RoutedEventArgs e)
        {
            LoadCompare();
        }

        private List<(string key, string name, string emoji)> GetSelectedCompareParams()
        {
            var list = new List<(string, string, string)>();
            if (CmpWater.IsChecked == true) list.Add(("water", "喝水(ml)", "💧"));
            if (CmpSleep.IsChecked == true) list.Add(("sleep", "睡眠(h)", "😴"));
            if (CmpWeight.IsChecked == true) list.Add(("weight", "体重(kg)", "⚖️"));
            if (CmpUric.IsChecked == true) list.Add(("uric_acid", "尿酸(μmol/L)", "🩸"));
            if (CmpMood.IsChecked == true) list.Add(("mood", "心情(0好-3差)", "😊"));
            return list;
        }

        private void LoadCompare()
        {
            var all = _repo.GetAll();
            var days = Enumerable.Range(0, _compareDays).Select(i => DateTime.Today.AddDays(-(_compareDays - 1 - i))).ToList();
            var selected = GetSelectedCompareParams();

            // 每个参数按天取值
            var series = new List<(string key, string name, string emoji, List<double?> values)>();
            foreach (var p in selected)
            {
                var values = days.Select(d =>
                {
                    var rec = all.FirstOrDefault(r => r.Type == p.key && r.Date == d.ToString("yyyy-MM-dd"));
                    return rec != null ? (double?)rec.Value : null;
                }).ToList();
                series.Add((p.key, p.name, p.emoji, values));
            }

            DrawCompareChart(days, series);
            BuildCompareLegend(series);
        }

        private void DrawCompareChart(List<DateTime> days, List<(string key, string name, string emoji, List<double?> values)> series)
        {
            CompareChartCanvas.Children.Clear();
            var w = CompareChartCanvas.ActualWidth;
            var h = CompareChartCanvas.ActualHeight;
            if (w < 50) w = 700;
            if (h < 50) h = 200;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");

            var colors = new[]
            {
                (Brush)FindResource("PrimaryBrush"),
                (Brush)FindResource("AccentGreenBrush"),
                (Brush)FindResource("AccentBlueBrush"),
                (Brush)FindResource("AccentRedBrush"),
                (Brush)FindResource("AccentYellowBrush")
            };

            for (int s = 0; s < series.Count; s++)
            {
                var vals = series[s].values.Where(v => v.HasValue).Select(v => v.Value).ToList();
                if (vals.Count == 0) continue;
                var minV = vals.Min();
                var maxV = vals.Max();
                var range = maxV - minV;
                if (range < 1e-9) range = 1;

                // 按连续非空段分段画线，避免跨缺口误导性连线
                int segStart = -1;
                for (int i = 0; i <= days.Count; i++)
                {
                    bool has = i < days.Count && series[s].values[i].HasValue;
                    if (has && segStart < 0) segStart = i;
                    if (!has && segStart >= 0)
                    {
                        DrawCompareSegment(colors[s % colors.Length], days, series[s].values, segStart, i - 1, w, h, minV, range);
                        segStart = -1;
                    }
                }
            }

            // 日期标签（首/中/尾）
            if (days.Count > 0)
            {
                var idxs = new[] { 0, days.Count / 2, days.Count - 1 };
                foreach (var i in idxs)
                {
                    var lbl = new TextBlock { Text = days[i].ToString("MM/dd"), FontSize = 9, Foreground = textBrush };
                    var x = (i + 0.5) * w / days.Count;
                    Canvas.SetLeft(lbl, Math.Max(0, Math.Min(w - 40, x - 16)));
                    Canvas.SetTop(lbl, h - 16);
                    CompareChartCanvas.Children.Add(lbl);
                }
            }
            CompareChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 20, X2 = w, Y2 = h - 20, Stroke = axisBrush, StrokeThickness = 1 });
        }

        private void DrawCompareSegment(Brush brush, List<DateTime> days, List<double?> values, int start, int end, double w, double h, double minV, double range)
        {
            double Norm(double v) => (v - minV) / range * 100.0;

            var pts = new List<Point>();
            for (int i = start; i <= end; i++)
            {
                if (!values[i].HasValue) continue;
                var x = (i + 0.5) * w / days.Count;
                var y = h - 20 - Norm(values[i].Value) / 100.0 * (h - 40);
                pts.Add(new Point(x, y));
            }
            for (int i = 1; i < pts.Count; i++)
            {
                CompareChartCanvas.Children.Add(new Line
                {
                    X1 = pts[i - 1].X, Y1 = pts[i - 1].Y, X2 = pts[i].X, Y2 = pts[i].Y,
                    Stroke = brush, StrokeThickness = 2, Opacity = 0.85
                });
            }
            foreach (var p in pts)
            {
                CompareChartCanvas.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = brush });
                Canvas.SetLeft(CompareChartCanvas.Children[CompareChartCanvas.Children.Count - 1], p.X - 2);
                Canvas.SetTop(CompareChartCanvas.Children[CompareChartCanvas.Children.Count - 1], p.Y - 2);
            }
        }

        private void BuildCompareLegend(List<(string key, string name, string emoji, List<double?> values)> series)
        {
            CompareLegendPanel.Children.Clear();
            var colors = new[]
            {
                (Brush)FindResource("PrimaryBrush"),
                (Brush)FindResource("AccentGreenBrush"),
                (Brush)FindResource("AccentBlueBrush"),
                (Brush)FindResource("AccentRedBrush"),
                (Brush)FindResource("AccentYellowBrush")
            };
            for (int i = 0; i < series.Count; i++)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4) };
                item.Children.Add(new Border { Width = 14, Height = 4, CornerRadius = new CornerRadius(2), Background = colors[i % colors.Length], VerticalAlignment = VerticalAlignment.Center });
                item.Children.Add(new TextBlock
                {
                    Text = $"{series[i].emoji} {series[i].name}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextBrush"),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                CompareLegendPanel.Children.Add(item);
            }
        }

        private async void AiAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = SecureStore.Decrypt(_settingsRepo.GetValue(SettingsKeys.DeepSeekApiKey, ""));
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AiStatusText.Text = "未配置 DeepSeek API Key，请到 设置 → AI 分析 中填写";
                return;
            }
            var selected = GetSelectedCompareParams();
            if (selected.Count < 2)
            {
                AiStatusText.Text = "请至少勾选 2 个参数再分析";
                return;
            }
            AiAnalyzeBtn.IsEnabled = false;
            AiStatusText.Text = "正在请求 DeepSeek 分析…";
            AiResultText.Text = "";
            try
            {
                var dataText = BuildCompareDataText(selected);
                var system = "你是一名健康数据分析助手。用户会提供若干健康指标按日期的数据（数值越大代表量越多；心情 0=开心、3=难过）。" +
                             "请分析这些指标之间可能存在的相关性、趋势规律，给出可执行的健康建议。用简体中文回答，分点列出，不超过 400 字。";
                var result = await DeepSeekService.ChatAsync(apiKey, system, dataText);
                AiResultText.Text = result;
                AiStatusText.Text = "分析完成";
            }
            catch (Exception ex)
            {
                AiStatusText.Text = $"分析失败：{ex.Message}";
            }
            finally
            {
                AiAnalyzeBtn.IsEnabled = true;
            }
        }

        private string BuildCompareDataText(List<(string key, string name, string emoji)> selected)
        {
            var all = _repo.GetAll();
            var days = Enumerable.Range(0, _compareDays).Select(i => DateTime.Today.AddDays(-(_compareDays - 1 - i))).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"以下为近 {_compareDays} 天健康数据（日期, " + string.Join(", ", selected.Select(p => p.name)) + "）：");
            foreach (var d in days)
            {
                var dateStr = d.ToString("yyyy-MM-dd");
                var parts = new List<string>();
                foreach (var p in selected)
                {
                    var rec = all.FirstOrDefault(r => r.Type == p.key && r.Date == dateStr);
                    parts.Add(rec != null ? rec.Value.ToString("F1", CultureInfo.InvariantCulture) : "无记录");
                }
                sb.AppendLine(dateStr + ": " + string.Join(", ", parts));
            }
            return sb.ToString();
        }

        // ============ 通用 ============
        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h{ts.Minutes:D2}m";
            return $"{ts.Minutes}m";
        }

        private Border BuildRecordRow(string text, string value, RoutedEventHandler deleteClick, string brushKey = null)
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
                    Foreground = (Brush)FindResource(brushKey ?? "PrimaryBrush"),
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
