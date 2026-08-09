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
        private readonly MedicationRepository _medRepo = new MedicationRepository();
        private readonly ExerciseRepository _exerciseRepo = new ExerciseRepository();
        private readonly AiProviderRepository _aiProviderRepo = new AiProviderRepository();
        private string _currentTab = "sleep";
        private bool _loadingUric;
        private string _aiSystemPrompt = AiPromptDialog.DefaultAiSystemPrompt;

        public HealthView()
        {
            InitializeComponent();
            _compareInitializing = false; // XAML 加载完成，控件已全部创建
            _aiSystemPrompt = _settingsRepo.GetValue(SettingsKeys.AiSystemPrompt, AiPromptDialog.DefaultAiSystemPrompt);
            SleepDatePicker.SelectedDate = DateTime.Today;
            WeightDatePicker.SelectedDate = DateTime.Today;
            UricAcidDatePicker.SelectedDate = DateTime.Today;
            UricHourBox.Text = DateTime.Now.Hour.ToString("D2");
            UricMinBox.Text = DateTime.Now.Minute.ToString("D2");
            WeightFromPicker.SelectedDateChanged += (s, ev) => OnWeightRangePicked();
            WeightToPicker.SelectedDateChanged += (s, ev) => OnWeightRangePicked();
            SleepDatePicker.SelectedDateChanged += (s, ev) => LoadSleep(); // 切换日期回填该天入睡/起床
            UricAcidDatePicker.SelectedDateChanged += (s, ev) => LoadUricAcid(); // 切换日期回填该天测量时间
            UricTargetBox.Text = _settingsRepo.GetValue(SettingsKeys.UricTarget, "");
            ThemeService.ThemeChanged += OnThemeChanged;
            this.Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
            EventAggregator.Instance.Subscribe<string>(OnGlobalEvent);
            LoadSleep();
            LoadWeight();
            LoadWater();
            LoadMood();
            LoadUricAcid();
            LoadExercise();
            LoadMedications();
            LoadCompare();
            LoadOverview();
            AiPromptPreview.Text = _aiSystemPrompt.Length > 14 ? _aiSystemPrompt.Substring(0, 14) + "…" : _aiSystemPrompt;
            AiPromptPreview.ToolTip = _aiSystemPrompt;
        }

        private void OnWeightRangePicked()
        {
            if (!_weightRangeInit) return; // 初始赋值阶段，EnsureWeightRange 会处理
            // 与当前值相同（初始化赋值）时不重复刷新
            if (WeightFromPicker.SelectedDate == _weightFrom && WeightToPicker.SelectedDate == _weightTo) return;
            _weightFrom = WeightFromPicker.SelectedDate;
            _weightTo = WeightToPicker.SelectedDate;
            UpdateWeightRangeButtons(-1); // 手动选择日期，取消快捷按钮高亮
            LoadWeight();
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
                        LoadExercise();
                        LoadMedications();
                        LoadCompare();
                        LoadOverview();
                    }
                }));
            }
            else if (message == "HealthDataChanged")
            {
                // 悬浮窗久坐 +1 等外部写入健康数据时刷新
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (this.IsVisible && _currentTab == "exercise") LoadExercise();
                    LoadOverview();
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
                    LoadExercise();
                    LoadMedications();
                    LoadCompare();
                    LoadOverview();
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
                LoadMedications();
                LoadCompare();
                LoadOverview();
            }
        }

        // ============ TAB ============
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            _currentTab = (string)btn.Tag;
            OverviewPanel.Visibility = _currentTab == "overview" ? Visibility.Visible : Visibility.Collapsed;
            SleepPanel.Visibility = _currentTab == "sleep" ? Visibility.Visible : Visibility.Collapsed;
            WeightPanel.Visibility = _currentTab == "weight" ? Visibility.Visible : Visibility.Collapsed;
            WaterPanel.Visibility = _currentTab == "water" ? Visibility.Visible : Visibility.Collapsed;
            MoodPanel.Visibility = _currentTab == "mood" ? Visibility.Visible : Visibility.Collapsed;
            UricAcidPanel.Visibility = _currentTab == "uric_acid" ? Visibility.Visible : Visibility.Collapsed;
            ExercisePanel.Visibility = _currentTab == "exercise" ? Visibility.Visible : Visibility.Collapsed;
            MedicationPanel.Visibility = _currentTab == "medication" ? Visibility.Visible : Visibility.Collapsed;
            ComparePanel.Visibility = _currentTab == "compare" ? Visibility.Visible : Visibility.Collapsed;

            OverviewTabBtn.Style = (Style)FindResource(_currentTab == "overview" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            SleepTabBtn.Style = (Style)FindResource(_currentTab == "sleep" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WeightTabBtn.Style = (Style)FindResource(_currentTab == "weight" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WaterTabBtn.Style = (Style)FindResource(_currentTab == "water" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            MoodTabBtn.Style = (Style)FindResource(_currentTab == "mood" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            UricAcidTabBtn.Style = (Style)FindResource(_currentTab == "uric_acid" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            ExerciseTabBtn.Style = (Style)FindResource(_currentTab == "exercise" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            MedicationTabBtn.Style = (Style)FindResource(_currentTab == "medication" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            CompareTabBtn.Style = (Style)FindResource(_currentTab == "compare" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

            // 面板刚变为可见，按真实宽度重绘图表
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (_currentTab)
                {
                    case "overview": LoadOverview(); break;
                    case "sleep": LoadSleep(); break;
                    case "weight": LoadWeight(); break;
                    case "water": LoadWater(); break;
                    case "mood": LoadMood(); break;
                    case "uric_acid": LoadUricAcid(); break;
                    case "exercise": LoadExercise(); break;
                    case "medication": LoadMedications(); break;
                    case "compare": LoadCompare(); break;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ============ 睡眠 ============
        private void DigitOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (var c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>允许数字和小数点（尿酸值可带小数）</summary>
        private void UricValue_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (var c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private static bool TryGetTime(TextBox hourBox, TextBox minBox, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (!int.TryParse(hourBox.Text, out var h) || !int.TryParse(minBox.Text, out var m))
                return false;
            if (h < 0 || h > 23 || m < 0 || m > 59)
                return false;
            time = new TimeSpan(h, m, 0);
            return true;
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
            if (!TryGetTime(SleepHourBox, SleepMinBox, out var sleep) ||
                !TryGetTime(WakeHourBox, WakeMinBox, out var wake))
            {
                SleepCalcText.Text = "请填写有效的时间（时 0-23，分 0-59）";
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

            // 回填所选日期（默认今天）的入睡/起床时间到输入框；无记录则重置默认，避免误存上一天的值
            var selDateStr = (SleepDatePicker.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            var selRec = all.FirstOrDefault(r => r.Date == selDateStr);
            bool filled = false;
            if (selRec != null && !string.IsNullOrEmpty(selRec.Detail))
            {
                var parts = selRec.Detail.Split('|');
                if (parts.Length == 2 && TimeSpan.TryParse(parts[0], out var sl) && TimeSpan.TryParse(parts[1], out var wk))
                {
                    SleepHourBox.Text = sl.Hours.ToString("D2");
                    SleepMinBox.Text = sl.Minutes.ToString("D2");
                    WakeHourBox.Text = wk.Hours.ToString("D2");
                    WakeMinBox.Text = wk.Minutes.ToString("D2");
                    filled = true;
                }
            }
            if (!filled)
            {
                SleepHourBox.Text = "23";
                SleepMinBox.Text = "00";
                WakeHourBox.Text = "07";
                WakeMinBox.Text = "00";
            }

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

            // 身体成分分析（中国成人标准：偏瘦<18.5 / 正常18.5-24 / 超重24-28 / 肥胖≥28）
            UpdateBodyComposition(latest, heightCm);

            // 趋势图按所选范围
            EnsureWeightRange();
            var from = _weightFrom;
            if (!from.HasValue)
            {
                // "全部"：从最早记录到最近
                from = all.Count > 0
                    ? DateTime.ParseExact(all.First().Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : DateTime.Today.AddDays(-29);
            }
            var to = _weightTo ?? DateTime.Today;
            var fromVal = from.Value;
            var inRange = all.Where(r => string.CompareOrdinal(r.Date, fromVal.ToString("yyyy-MM-dd")) >= 0 &&
                                         string.CompareOrdinal(r.Date, to.ToString("yyyy-MM-dd")) <= 0).ToList();
            WeightChartTitle.Text = $"体重趋势（{fromVal:yyyy/MM/dd} ~ {to:yyyy/MM/dd}）";
            DrawWeightChart(all, fromVal, to);

            // 较 N 天前变化
            if (inRange.Count >= 2)
            {
                var firstDate = DateTime.ParseExact(inRange.First().Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var lastDate = DateTime.ParseExact(inRange.Last().Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var daysBetween = (lastDate - firstDate).Days;
                var delta = inRange.Last().Value - inRange.First().Value;
                WeightDeltaText.Text = daysBetween > 0
                    ? $"较 {daysBetween} 天前 {(delta >= 0 ? "+" : "")}{delta:F1} kg"
                    : "";
                WeightDeltaText.Foreground = (Brush)FindResource(delta <= 0 ? "AccentGreenBrush" : "AccentRedBrush");
            }
            else
            {
                WeightDeltaText.Text = "";
            }

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

        private DateTime? _weightFrom;
        private DateTime? _weightTo;
        private bool _weightRangeInit;

        private void EnsureWeightRange()
        {
            if (_weightRangeInit) return;
            _weightRangeInit = true;
            _weightFrom = DateTime.Today.AddDays(-29);
            _weightTo = DateTime.Today;
            WeightFromPicker.SelectedDate = _weightFrom;
            WeightToPicker.SelectedDate = _weightTo;
            UpdateWeightRangeButtons(30);
        }

        private void WeightRange_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            var days = int.Parse((string)btn.Tag);
            var to = DateTime.Today;
            var from = days > 0 ? to.AddDays(-(days - 1)) : DateTime.MinValue;
            _weightFrom = from == DateTime.MinValue ? (DateTime?)null : from;
            _weightTo = to;
            if (_weightFrom.HasValue) WeightFromPicker.SelectedDate = _weightFrom;
            else WeightFromPicker.SelectedDate = null;
            WeightToPicker.SelectedDate = to;
            UpdateWeightRangeButtons(days);
            LoadWeight();
        }

        private void UpdateWeightRangeButtons(int days)
        {
            WRange7Btn.Style = (Style)FindResource(days == 7 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WRange30Btn.Style = (Style)FindResource(days == 30 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WRange90Btn.Style = (Style)FindResource(days == 90 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WRange365Btn.Style = (Style)FindResource(days == 365 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            WRangeAllBtn.Style = (Style)FindResource(days == 0 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        }

        private void UpdateBodyComposition(HealthRecord latest, double heightCm)
        {
            if (latest == null || heightCm <= 0)
            {
                BodyCompGradeText.Text = "记录体重并填写身高后，可查看身体成分分析";
                BodyCompGradeText.Foreground = (Brush)FindResource("SecondaryTextBrush");
                BodyCompSuggestionText.Text = "";
                BodyCompTargetText.Text = "";
                return;
            }

            var bmi = CalcBmi(latest.Value, heightCm);
            string grade, gradeBrush;
            if (bmi < 18.5) { grade = "偏瘦（体重过低）"; gradeBrush = "AccentBlueBrush"; }
            else if (bmi < 24) { grade = "正常"; gradeBrush = "AccentGreenBrush"; }
            else if (bmi < 28) { grade = "超重"; gradeBrush = "AccentYellowBrush"; }
            else { grade = "肥胖"; gradeBrush = "AccentRedBrush"; }

            BodyCompGradeText.Text = $"当前 BMI {bmi:F1} → {grade}";
            BodyCompGradeText.Foreground = (Brush)FindResource(gradeBrush);

            var low = 18.5 * heightCm / 100.0 * heightCm / 100.0;
            var high = 23.9 * heightCm / 100.0 * heightCm / 100.0;
            BodyCompSuggestionText.Text = $"建议体重范围：{low:F1} ~ {high:F1} kg（BMI 18.5~23.9）";
            BodyCompTargetText.Text = latest.Value < low
                ? $"距建议体重下限还差 {low - latest.Value:F1} kg"
                : latest.Value > high
                    ? $"比建议体重上限多 {latest.Value - high:F1} kg"
                    : "体重处于健康范围，继续保持";

            // 标记 BMI 在区间条上的位置（映射 14~32 → 0~1）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var pos = (bmi - 14.0) / (32.0 - 14.0);
                pos = Math.Max(0, Math.Min(1, pos));
                var host = (FrameworkElement)BmiMarker.Parent;
                if (host != null && host.ActualWidth > 0)
                {
                    var x = pos * host.ActualWidth;
                    BmiMarker.Margin = new Thickness(Math.Max(0, x - 1.5), 0, 0, 0);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void DrawWeightChart(List<HealthRecord> all, DateTime from, DateTime to)
        {
            WeightChartCanvas.Children.Clear();
            var w = WeightChartCanvas.ActualWidth;
            var h = WeightChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 170;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var lineBrush = (Brush)FindResource("AccentBlueBrush");

            var days = new List<DateTime>();
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
                days.Add(d);
            if (days.Count > 366) days = days.Where((_, i) => i % (days.Count / 366 + 1) == 0).ToList();
            if (days.Count == 0) return;

            var values = days.Select(d => all.FirstOrDefault(r => r.Date == d.ToString("yyyy-MM-dd"))?.Value)
                             .ToList();

            WeightChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 20, X2 = w, Y2 = h - 20, Stroke = axisBrush, StrokeThickness = 1 });

            var valid = values.Where(v => v.HasValue).ToList();
            if (valid.Count == 0)
            {
                WeightChartCanvas.Children.Add(new TextBlock
                {
                    Text = "该时间段暂无体重数据",
                    FontSize = 11,
                    Foreground = textBrush
                });
                Canvas.SetLeft(WeightChartCanvas.Children[WeightChartCanvas.Children.Count - 1], w / 2 - 60);
                Canvas.SetTop(WeightChartCanvas.Children[WeightChartCanvas.Children.Count - 1], h / 2 - 8);
                return;
            }

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
            // 测量时间（默认当前时间），存入 Detail 供记录列表显示
            var timeStr = "00:00";
            if (int.TryParse(UricHourBox.Text, out var hh) && int.TryParse(UricMinBox.Text, out var mm)
                && hh >= 0 && hh <= 23 && mm >= 0 && mm <= 59)
            {
                timeStr = $"{hh:D2}:{mm:D2}";
            }
            else
            {
                UricAcidHint.Text = "测量时间无效（时 0-23，分 0-59）";
                return;
            }
            _repo.Upsert(new HealthRecord { Type = "uric_acid", Date = date.ToString("yyyy-MM-dd"), Value = v, Detail = timeStr });
            UricAcidHint.Text = $"已保存：{v:F0} μmol/L（{timeStr}）";
            LoadUricAcid();
        }

        private void SaveUricTarget_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(UricTargetBox.Text, out var target) && target > 0 && target < 1200)
            {
                _settingsRepo.SetValue(SettingsKeys.UricTarget, target.ToString(CultureInfo.InvariantCulture));
                LoadUricAcid();
                UricAcidHint.Text = $"目标已设为 {target:F0} μmol/L";
            }
            else
            {
                UricTargetBox.Text = "";
                _settingsRepo.SetValue(SettingsKeys.UricTarget, "");
                LoadUricAcid();
                UricAcidHint.Text = "已清除目标值";
            }
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

            // 切换日期时回填该天的测量时间；无记录则重置为当前时间，避免把旧时刻误存到新日期
            var selUricDate = (UricAcidDatePicker.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            var selRec = all.FirstOrDefault(r => r.Date == selUricDate);
            if (selRec != null && !string.IsNullOrEmpty(selRec.Detail) && selRec.Detail.Contains(":"))
            {
                var tp = selRec.Detail.Split(':');
                if (tp.Length == 2 && int.TryParse(tp[0], out var hh2) && int.TryParse(tp[1], out var mm2))
                {
                    UricHourBox.Text = hh2.ToString("D2");
                    UricMinBox.Text = mm2.ToString("D2");
                }
                else
                {
                    UricHourBox.Text = DateTime.Now.Hour.ToString("D2");
                    UricMinBox.Text = DateTime.Now.Minute.ToString("D2");
                }
            }
            else
            {
                UricHourBox.Text = DateTime.Now.Hour.ToString("D2");
                UricMinBox.Text = DateTime.Now.Minute.ToString("D2");
            }

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
                var dateText = string.IsNullOrEmpty(r.Detail) ? r.Date : $"{r.Date} {r.Detail}";
                UricRecordsPanel.Children.Add(BuildRecordRow(
                    $"{dateText}  {r.Value:F0} μmol/L",
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

            double target = 0;
            double.TryParse(_settingsRepo.GetValue(SettingsKeys.UricTarget, ""), NumberStyles.Any, CultureInfo.InvariantCulture, out target);

            var minV = Math.Min(lower, vals.Count > 0 ? vals.Min() : lower) - 20;
            var maxV = Math.Max(upper, vals.Count > 0 ? vals.Max() : upper) + 20;
            if (target > 0) { minV = Math.Min(minV, target - 20); maxV = Math.Max(maxV, target + 20); }
            var range = maxV - minV;
            if (range <= 0) range = 1;

            double Y(double v) => h - 20 - (v - minV) / range * (h - 40);

            // 目标线（黄色点线）
            if (target > 0)
            {
                var targetLine = new Line { X1 = 0, X2 = w, Y1 = Y(target), Y2 = Y(target), Stroke = new SolidColorBrush(Color.FromRgb(255, 204, 0)), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 6, 3 }, Opacity = 0.9 };
                UricChartCanvas.Children.Add(targetLine);
                var targetLabel = new TextBlock { Text = $"目标 {target:F0}", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(255, 204, 0)) };
                Canvas.SetLeft(targetLabel, 2); Canvas.SetTop(targetLabel, Math.Max(0, Y(target) - 14));
                UricChartCanvas.Children.Add(targetLabel);
            }

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
        private bool _compareInitializing = true;

        private void CompareRange_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            _compareDays = int.Parse((string)btn.Tag);
            CmpRange7Btn.Style = (Style)FindResource(_compareDays == 7 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            CmpRange30Btn.Style = (Style)FindResource(_compareDays == 30 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            CmpRangeAllBtn.Style = (Style)FindResource(_compareDays == 0 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
            LoadCompare();
        }

        private void CompareParam_Changed(object sender, RoutedEventArgs e)
        {
            if (_compareInitializing) return; // XAML 顺序加载中，控件尚未全部创建
            LoadCompare();
        }

        private List<(string key, string name, string emoji)> GetSelectedCompareParams()
        {
            var list = new List<(string, string, string)>();
            if (CmpWater.IsChecked == true) list.Add(("water", "喝水(ml)", "💧"));
            if (CmpSleep.IsChecked == true) list.Add(("sleep", "睡眠(h)", "😴"));
            if (CmpWeight.IsChecked == true) list.Add(("weight", "体重(kg)", "⚖️"));
            if (CmpUric.IsChecked == true) list.Add(("uric_acid", "尿酸(μmol/L)", "💉"));
            if (CmpMood.IsChecked == true) list.Add(("mood", "心情(0好-3差)", "😊"));
            return list;
        }

        private void LoadCompare()
        {
            var all = _repo.GetAll();
            var days = _compareDays > 0
                ? Enumerable.Range(0, _compareDays).Select(i => DateTime.Today.AddDays(-(_compareDays - 1 - i))).ToList()
                : BuildAllDateRange(all);
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

            DrawCompareCharts(days, series);
            BuildCompareLegend(series);
        }

        /// <summary>"全部"时间范围：从最早记录日期到今天（无记录时回退近 30 天）</summary>
        private List<DateTime> BuildAllDateRange(List<HealthRecord> all)
        {
            var dates = all
                .Select(r => DateTime.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (DateTime?)d : null)
                .Where(d => d.HasValue)
                .Select(d => d.Value.Date)
                .ToList();
            if (dates.Count == 0)
                return Enumerable.Range(0, 30).Select(i => DateTime.Today.AddDays(-(29 - i))).ToList();
            var min = dates.Min();
            var max = dates.Max();
            var list = new List<DateTime>();
            for (var d = min; d <= max; d = d.AddDays(1)) list.Add(d);
            return list;
        }

        private void DrawCompareCharts(List<DateTime> days, List<(string key, string name, string emoji, List<double?> values)> series)
        {
            CompareChartsPanel.Children.Clear();
            var w = CompareChartsPanel.ActualWidth;
            if (w < 50) w = 700;

            var colors = new[]
            {
                (Brush)FindResource("PrimaryBrush"),
                (Brush)FindResource("AccentGreenBrush"),
                (Brush)FindResource("AccentBlueBrush"),
                (Brush)FindResource("AccentRedBrush"),
                (Brush)FindResource("AccentYellowBrush")
            };
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var axisBrush = (Brush)FindResource("BorderBrush");

            foreach (var p in series)
            {
                var idx = series.IndexOf(p);
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 6),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                var panel = new StackPanel();

                // 标题行：emoji + 名称 + 数值范围
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                titleRow.Children.Add(new TextBlock
                {
                    Text = $"{p.emoji} {p.name}",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var vals = p.values.Where(v => v.HasValue).Select(v => v.Value).ToList();
                titleRow.Children.Add(new TextBlock
                {
                    Text = vals.Count > 0 ? $"　{vals.Min():F1} ~ {vals.Max():F1}" : "　暂无数据",
                    FontSize = 10,
                    Foreground = textBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                panel.Children.Add(titleRow);

                var canvas = new Canvas { Height = 100, Margin = new Thickness(0, 6, 0, 0) };
                panel.Children.Add(canvas);
                card.Child = panel;
                CompareChartsPanel.Children.Add(card);

                // 空数据占位
                if (vals.Count == 0)
                {
                    canvas.Children.Add(new TextBlock
                    {
                        Text = "该时间段暂无记录",
                        FontSize = 11,
                        Foreground = textBrush,
                        Margin = new Thickness(12, 30, 0, 0)
                    });
                    continue;
                }

                // 数据点过少提示（1~2 条折线不成形，仍显示数据点）
                if (vals.Count < 3)
                {
                    canvas.Children.Add(new TextBlock
                    {
                        Text = $"该时间段仅 {vals.Count} 天有记录（显示为数据点，多记录几天即可看到折线）",
                        FontSize = 10,
                        Foreground = textBrush,
                        Margin = new Thickness(12, 2, 0, 0)
                    });
                }

                // 每个参数按各自数据范围绘制（不归一化到 0-100%，直观显示各自走势）
                var minV = vals.Min();
                var maxV = vals.Max();
                var range = maxV - minV;
                if (range < 1e-9) range = Math.Max(Math.Abs(maxV) * 0.1, 1);
                minV -= range * 0.1;
                maxV += range * 0.1;
                range = maxV - minV;

                double Y(double v) => 84 - (v - minV) / range * 70;

                // 网格线（3 条）
                for (int g = 1; g <= 3; g++)
                {
                    var gy = 84 - 70 * g / 4.0;
                    canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = gy, Y2 = gy, Stroke = axisBrush, StrokeThickness = 0.5, Opacity = 0.4 });
                }

                // 折线（按连续段）
                int segStart = -1;
                for (int i = 0; i <= days.Count; i++)
                {
                    bool has = i < days.Count && p.values[i].HasValue;
                    if (has && segStart < 0) segStart = i;
                    if (!has && segStart >= 0)
                    {
                        DrawCompareSegment(canvas, colors[idx % colors.Length], days, p.values, segStart, i - 1, w, minV, range, p.name, Y);
                        segStart = -1;
                    }
                }

                // Y 轴刻度（顶部 max、底部 min）
                canvas.Children.Add(new TextBlock { Text = $"{maxV:F1}", FontSize = 8, Foreground = textBrush });
                Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], 2);
                Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], 0);
                canvas.Children.Add(new TextBlock { Text = $"{minV:F1}", FontSize = 8, Foreground = textBrush });
                Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], 2);
                Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], 78);

                // 日期标签（首/中/尾，天数少时去重）+ 底线
                if (days.Count > 0)
                {
                    var rawIdxs = days.Count > 2 ? new[] { 0, days.Count / 2, days.Count - 1 } : new[] { 0, days.Count - 1 };
                    var idxs = rawIdxs.Distinct().ToArray();
                    foreach (var i in idxs)
                    {
                        var lbl = new TextBlock { Text = days[i].ToString("MM/dd"), FontSize = 8, Foreground = textBrush };
                        var x = (i + 0.5) * w / days.Count;
                        Canvas.SetLeft(lbl, Math.Max(0, Math.Min(w - 40, x - 14)));
                        Canvas.SetTop(lbl, 84);
                        canvas.Children.Add(lbl);
                    }
                }
                canvas.Children.Add(new Line { X1 = 0, Y1 = 84, X2 = w, Y2 = 84, Stroke = axisBrush, StrokeThickness = 1 });
            }

            if (series.Count == 0)
            {
                CompareChartsPanel.Children.Add(new TextBlock
                {
                    Text = "请勾选至少 1 个参数查看趋势",
                    FontSize = 12,
                    Foreground = textBrush,
                    Margin = new Thickness(8, 16, 0, 8)
                });
            }
        }

        private void DrawCompareSegment(Canvas canvas, Brush brush, List<DateTime> days, List<double?> values, int start, int end, double w, double minV, double range, string seriesName, Func<double, double> Y)
        {
            var pts = new List<Point>();
            for (int i = start; i <= end; i++)
            {
                if (!values[i].HasValue) continue;
                var x = (i + 0.5) * w / days.Count;
                pts.Add(new Point(x, Y(values[i].Value)));
            }
            for (int i = 1; i < pts.Count; i++)
            {
                canvas.Children.Add(new Line
                {
                    X1 = pts[i - 1].X, Y1 = pts[i - 1].Y, X2 = pts[i].X, Y2 = pts[i].Y,
                    Stroke = brush, StrokeThickness = 2, Opacity = 0.9
                });
            }
            var isSingle = pts.Count == 1;
            var dotSize = isSingle ? 10.0 : 6.0;
            for (int i = 0; i < pts.Count; i++)
            {
                var dot = new Ellipse { Width = dotSize, Height = dotSize, Fill = brush, Stroke = Brushes.White, StrokeThickness = 1.2 };
                int realIdx = start;
                int count = -1;
                for (int j = start; j <= end; j++)
                {
                    if (values[j].HasValue)
                    {
                        count++;
                        if (count == i) { realIdx = j; break; }
                    }
                }
                dot.ToolTip = $"{seriesName}\n{days[realIdx]:yyyy-MM-dd}  值 {values[realIdx]:F1}";
                canvas.Children.Add(dot);
                Canvas.SetLeft(dot, pts[i].X - dotSize / 2);
                Canvas.SetTop(dot, pts[i].Y - dotSize / 2);
                // 单数据点：在点上方标出数值，避免"看不到折线"
                if (isSingle)
                {
                    canvas.Children.Add(new TextBlock
                    {
                        Text = $"{values[realIdx]:F1}",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = brush
                    });
                    Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], pts[i].X - 10);
                    Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], pts[i].Y - 24);
                }
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
            var provider = _aiProviderRepo.GetDefault();
            if (provider == null)
            {
                AiStatusText.Text = "未配置 AI 供应商，请到 设置 → AI 分析 中添加";
                return;
            }
            if (string.IsNullOrWhiteSpace(AiProviderRepository.GetApiKey(provider)))
            {
                AiStatusText.Text = $"当前供应商「{provider.Name}」未填写 API Key，请到 设置 → AI 分析 中填写，或切换其它已填 Key 的供应商";
                return;
            }
            var selected = GetSelectedCompareParams();
            if (selected.Count < 2 && AiScopeSelected.IsChecked == true)
            {
                AiStatusText.Text = "请至少勾选 2 个参数再分析（或切换为发送全部数据）";
                return;
            }
            AiAnalyzeBtn.IsEnabled = false;
            AiStatusText.Text = $"正在请求 {provider.Name} 分析…";
            AiResultText.Text = "";
            try
            {
                var dataText = BuildCompareDataText(selected);
                var result = await LlmService.ChatAsync(provider, _aiSystemPrompt, dataText);
                AiResultText.Text = result;
                AiStatusText.Text = $"分析完成（{provider.Name}）";
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

        private void ShowAiPrompt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AiPromptDialog(_aiSystemPrompt) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                _aiSystemPrompt = dlg.Prompt;
                _settingsRepo.SetValue(SettingsKeys.AiSystemPrompt, _aiSystemPrompt);
                AiPromptPreview.Text = _aiSystemPrompt.Length > 14 ? _aiSystemPrompt.Substring(0, 14) + "…" : _aiSystemPrompt;
                AiPromptPreview.ToolTip = _aiSystemPrompt;
            }
        }

        private string BuildCompareDataText(List<(string key, string name, string emoji)> selected)
        {
            var all = _repo.GetAll();
            var days = _compareDays > 0
                ? Enumerable.Range(0, _compareDays).Select(i => DateTime.Today.AddDays(-(_compareDays - 1 - i))).ToList()
                : BuildAllDateRange(all);
            var sb = new System.Text.StringBuilder();

            // 模式一：发送全部数据（含用药、锻炼、久坐等）
            if (AiScopeAll.IsChecked == true)
            {
                sb.AppendLine($"以下为全部健康数据（共 {days.Count} 天；心情 0=开心、1=平静、2=低落、3=难过）：");
                foreach (var d in days)
                {
                    var dateStr = d.ToString("yyyy-MM-dd");
                    var parts = all.Where(r => r.Date == dateStr).Select(DescribeHealthRecord).ToList();
                    sb.AppendLine(dateStr + (parts.Count > 0 ? ": " + string.Join("，", parts) : ": （无记录）"));
                }

                var meds = _medRepo.GetAll();
                sb.AppendLine();
                sb.AppendLine(meds.Count > 0 ? "用药记录：" : "用药记录：无");
                foreach (var m in meds)
                {
                    sb.AppendLine($"- {m.Name}（{MedicationRepository.MedicationTypeName(m.Type)}，{FormatSpec(m)}，{FormatFrequency(m)}，时间 {FormatTimes(m)}，{FormatDuration(m)}）");
                }

                var items = _exerciseRepo.GetAll();
                sb.AppendLine();
                sb.AppendLine(items.Count > 0 ? "锻炼项目：" : "锻炼项目：无");
                foreach (var it in items)
                {
                    sb.AppendLine($"- {it.Name}：目标 {ExerciseRepository.TargetText(it)}，频率 {ExerciseRepository.FrequencyName(it.Frequency)}");
                }
                return sb.ToString();
            }

            // 模式二：仅发送上方勾选的参数（逐日）
            var rangeLabel = _compareDays > 0 ? $"近 {_compareDays} 天" : "全部记录";
            sb.AppendLine($"以下为{rangeLabel}健康数据（日期, " + string.Join(", ", selected.Select(p => p.name)) + "）：");
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

        /// <summary>把一条健康记录转成便于 AI 阅读的文字</summary>
        private string DescribeHealthRecord(HealthRecord rec)
        {
            switch (rec.Type)
            {
                case "sleep":
                {
                    var dur = FormatDuration(TimeSpan.FromMinutes(rec.Value));
                    var detail = string.Empty;
                    if (!string.IsNullOrEmpty(rec.Detail))
                    {
                        var parts = rec.Detail.Split('|');
                        if (parts.Length >= 2)
                            detail = $"（入睡{parts[0]}，起床{parts[1]}）";
                    }
                    return $"睡眠={dur}{detail}";
                }
                case "weight": return $"体重={rec.Value:F1}kg";
                case "water": return $"喝水={rec.Value:F0}ml";
                case "mood":
                {
                    var idx = (int)rec.Value;
                    var name = idx >= 0 && idx < MoodNames.Length ? MoodNames[idx] : "未知";
                    return $"心情={idx}({name})";
                }
                case "uric_acid": return $"尿酸={rec.Value:F0}μmol/L";
                case "sedentary": return $"久坐活动={rec.Value:F0}次";
                case "exercise":
                {
                    var item = _exerciseRepo.GetById(int.TryParse(rec.Detail, out var itemId) ? itemId : 0);
                    var name = item != null ? item.Name : "锻炼";
                    var unit = item != null && !string.IsNullOrEmpty(item.Unit) ? item.Unit : "次";
                    return $"{name}={rec.Value:0.##}{unit}";
                }
                default: return $"{rec.Type}={rec.Value:F0}";
            }
        }

        // ============ 总览 ============
        private string _overviewPart = "睡眠";

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
            var mi = mood != null ? (int)mood.Value : -1;
            var allMoods = _repo.GetByType("mood");
            var weekMoods = allMoods.Where(r => string.CompareOrdinal(r.Date, DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd")) >= 0).ToList();
            var moodItems = new List<(string, string, string)>
            {
                ("今日心情", mi >= 0 && mi < 4 ? MoodEmojis[mi] + " " + MoodNames[mi] : "未记录", mi >= 0 && mi <= 1 ? "AccentGreenBrush" : "SecondaryTextBrush")
            };
            if (weekMoods.Count > 0)
            {
                var avg = (int)Math.Round(weekMoods.Average(r => r.Value));
                if (avg < 0 || avg > 3) avg = 1;
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
                    var idx = (int)rec.Value;
                    if (idx < 0 || idx >= 4) return (key, "--", "未知", "SecondaryTextBrush");
                    return (key, MoodEmojis[idx], MoodNames[idx], idx <= 1 ? "AccentGreenBrush" : "AccentYellowBrush");
                }
                case "尿酸":
                {
                    var rec = _repo.GetByTypeAndDate("uric_acid", todayStr);
                    if (rec == null) return (key, "--", "今日未记录", "SecondaryTextBrush");
                    var (lower, upper) = GetUricRange();
                    var (text, brush) = ClassifyUric(rec.Value, lower, upper);
                    return (key, $"{rec.Value:F0}", text, brush);
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
            foreach (var key in new[] { "睡眠", "体重", "喝水", "心情", "尿酸", "用药" })
            {
                var (title, value, sub, brushKey) = GetOverviewCardData(key);
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Width = 150,
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(12, 10, 12, 10),
                    Cursor = Cursors.Hand
                };
                var keyCopy = key;
                card.MouseLeftButtonDown += (s, e) => ShowBodyPartDetail(keyCopy);
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = key, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
                panel.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource(brushKey), Margin = new Thickness(0, 2, 0, 0) });
                panel.Children.Add(new TextBlock { Text = sub, FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
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
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(emojiText, x + pw / 2 - 9);
                Canvas.SetTop(emojiText, y + ph / 2 - 16);
                BodyCanvas.Children.Add(emojiText);

                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(lbl, x + pw / 2 - 22);
                Canvas.SetTop(lbl, y + ph / 2 + 3);
                BodyCanvas.Children.Add(lbl);
            }

            // 头（睡眠/心情）
            AddPart("🧠", "睡眠/心情", cx - 26, 4, 52, 52, 26, "头部", "头部：睡眠 / 心情");
            // 躯干（体重）
            AddPart("🩺", "体重", cx - 38, 66, 76, 130, 22, "躯干", "躯干：体重（尿酸在腿部）");
            // 左臂（喝水）
            AddPart("💧", "喝水", cx - 76, 82, 32, 100, 16, "喝水", "左臂：喝水");
            // 右臂（用药）
            AddPart("💊", "用药", cx + 44, 82, 32, 100, 16, "用药", "右臂：用药");
            // 左腿（尿酸）
            AddPart("💉", "尿酸", cx - 38, 204, 32, 100, 16, "尿酸", "左腿：尿酸");
            // 右腿（体重）
            AddPart("⚖️", "体重", cx + 6, 204, 32, 100, 16, "体重", "右腿：体重");
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
                Text = part,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

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
                case "躯干":
                case "体重":
                case "右腿":
                {
                    var all = _repo.GetByType("weight").OrderBy(r => r.Date).ToList();
                    var rec = all.LastOrDefault();
                    if (rec == null) { AddRow("最新体重", "未记录", "SecondaryTextBrush"); break; }
                    double h = 0; double.TryParse(_settingsRepo.GetValue(SettingsKeys.HealthHeight), out h);
                    AddRow("最新体重", $"{rec.Value:F1} kg", "PrimaryBrush");
                    AddRow("记录日期", rec.Date, "SecondaryTextBrush");
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
                    break;
                }
                case "左臂":
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
                default:
                    AddRow("信息", "点击人体部位查看对应数据", "SecondaryTextBrush");
                    break;
            }
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
            foreach (var key in new[] { "睡眠", "体重", "喝水", "心情", "尿酸", "用药" })
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

            sb.AppendLine("<p style=\"margin-top:30px;color:#8E8E93;font-size:11px\">本报告由目标地图生成，仅供健康参考，不构成医疗建议。</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // ============ 锻炼 ============
        private List<ExerciseItem> _exerciseItems = new List<ExerciseItem>();

        private void LoadExercise()
        {
            LoadExerciseItems();
            LoadExerciseToday();
            DrawExerciseChart();
            LoadExerciseRecords();
            LoadSedentary();
        }

        // ============ 久坐活动 ============
        private void SedentaryPlus_Click(object sender, RoutedEventArgs e)
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var rec = _repo.GetByTypeAndDate("sedentary", todayStr);
            if (rec != null)
            {
                rec.Value += 1;
                _repo.Upsert(rec);
            }
            else
            {
                _repo.Insert(new HealthRecord
                {
                    Type = "sedentary",
                    Date = todayStr,
                    Value = 1
                });
            }
            LoadSedentary();
        }

        private void LoadSedentary()
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var rec = _repo.GetByTypeAndDate("sedentary", todayStr);
            SedentaryTodayText.Text = rec != null ? $"{rec.Value:0} 次" : "0 次";

            // 近 7 天柱状图
            SedentaryChartCanvas.Children.Clear();
            var w = SedentaryChartCanvas.ActualWidth;
            var h = SedentaryChartCanvas.ActualHeight;
            if (w < 50) w = 300;
            if (h < 50) h = 90;

            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-(6 - i))).ToList();
            var all = _repo.GetByType("sedentary").ToList();
            var maxV = days
                .Select(d => all.FirstOrDefault(r => r.Date == d.ToString("yyyy-MM-dd"))?.Value ?? 0)
                .DefaultIfEmpty(0).Max();
            if (maxV <= 0) maxV = 1;

            var barW = w / 7 * 0.6;
            var gap = w / 7;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var barBrush = (Brush)FindResource("AccentGreenBrush");

            for (int i = 0; i < days.Count; i++)
            {
                var v = all.FirstOrDefault(r => r.Date == days[i].ToString("yyyy-MM-dd"))?.Value ?? 0;
                var barH = (h - 18) * Math.Min(v / maxV, 1.0);
                var x = i * gap + (gap - barW) / 2;
                var y = h - 18 - barH;
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
                SedentaryChartCanvas.Children.Add(rect);

                var label = new TextBlock
                {
                    Text = days[i].Day.ToString(),
                    FontSize = 9,
                    Foreground = textBrush
                };
                Canvas.SetLeft(label, x + (barW - 12) / 2);
                Canvas.SetTop(label, h - 14);
                SedentaryChartCanvas.Children.Add(label);

                if (v > 0)
                {
                    var val = new TextBlock
                    {
                        Text = $"{v:0}",
                        FontSize = 9,
                        Foreground = textBrush
                    };
                    Canvas.SetLeft(val, x + (barW - 10) / 2);
                    Canvas.SetTop(val, y - 13);
                    SedentaryChartCanvas.Children.Add(val);
                }
            }
            SedentaryChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 18, X2 = w, Y2 = h - 18, Stroke = axisBrush, StrokeThickness = 1 });
        }

        private void LoadExerciseItems()
        {
            _exerciseItems = _exerciseRepo.GetAll();
            // 下拉框：记住原选中项目
            int prevId = 0;
            if (ExerciseItemCombo.SelectedItem is ComboBoxItem prevSel &&
                int.TryParse(prevSel.Tag?.ToString(), out var pv)) prevId = pv;

            ExerciseItemCombo.Items.Clear();
            if (_exerciseItems.Count == 0)
            {
                ExerciseItemCombo.Items.Add(new ComboBoxItem { Content = "（请先新建项目）", Tag = "0" });
                ExerciseItemCombo.SelectedIndex = 0;
            }
            else
            {
                foreach (var it in _exerciseItems)
                    ExerciseItemCombo.Items.Add(new ComboBoxItem { Content = it.Name, Tag = it.Id });
                var idx = _exerciseItems.FindIndex(i => i.Id == prevId);
                ExerciseItemCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
            UpdateExerciseUnit();

            // 项目列表卡片
            ExerciseItemsPanel.Children.Clear();
            if (_exerciseItems.Count == 0)
            {
                ExerciseItemsPanel.Children.Add(BuildEmptyHint("还没有锻炼项目，点击「＋ 新建项目」创建"));
                return;
            }
            foreach (var it in _exerciseItems)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var dock = new DockPanel();
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(btnPanel, Dock.Right);
                var editBtn = new Button
                {
                    Content = "✎",
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Width = 28, Height = 24, FontSize = 10, Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                var id = it.Id;
                editBtn.Click += (s, ev) => EditExerciseItem(id);
                var delBtn = new Button
                {
                    Content = "✕",
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Width = 28, Height = 24, FontSize = 10, Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                delBtn.Click += (s, ev) =>
                {
                    _exerciseRepo.Delete(id);
                    LoadExercise();
                };
                btnPanel.Children.Add(editBtn);
                btnPanel.Children.Add(delBtn);
                dock.Children.Add(btnPanel);
                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = it.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush")
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"目标 {ExerciseRepository.TargetText(it)} · {ExerciseRepository.FrequencyDesc(it)}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                dock.Children.Add(info);
                card.Child = dock;
                ExerciseItemsPanel.Children.Add(card);
            }
        }

        private void ExerciseItemCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateExerciseUnit();
        }

        private void UpdateExerciseUnit()
        {
            ExerciseUnitText.Text = "";
            ExerciseHintText.Text = "";
            if (ExerciseItemCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var id))
            {
                var it = _exerciseItems.FirstOrDefault(x => x.Id == id);
                if (it != null)
                {
                    ExerciseUnitText.Text = it.Unit;
                    ExerciseHintText.Text = $"目标 {ExerciseRepository.TargetText(it)} · {ExerciseRepository.FrequencyDesc(it)}；同一天可多次记录，自动累加";
                }
            }
        }

        private void RecordExercise_Click(object sender, RoutedEventArgs e)
        {
            if (ExerciseItemCombo.SelectedItem is not ComboBoxItem item ||
                !int.TryParse(item.Tag?.ToString(), out var itemId) || itemId <= 0)
            {
                MessageBox.Show("请先选择锻炼项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!double.TryParse(ExerciseValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                MessageBox.Show("请输入大于 0 的本次量", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _repo.Insert(new HealthRecord
            {
                Type = "exercise",
                Date = DateTime.Today.ToString("yyyy-MM-dd"),
                Value = value,
                Detail = itemId.ToString()
            });
            ExerciseValueBox.Text = "1";
            LoadExercise();
        }

        private void AddExerciseItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ExerciseEditDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) LoadExercise();
        }

        private void EditExerciseItem(int id)
        {
            var dlg = new ExerciseEditDialog(id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) LoadExercise();
        }

        /// <summary>今日各项目达标情况</summary>
        private void LoadExerciseToday()
        {
            ExerciseTodayPanel.Children.Clear();
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            var yesterdayStr = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
            var records = _repo.GetByType("exercise").ToList();

            if (_exerciseItems.Count == 0)
            {
                ExerciseTodayPanel.Children.Add(BuildEmptyHint("暂无锻炼项目"));
                return;
            }
            foreach (var it in _exerciseItems)
            {
                var todaySum = records.Where(r => r.Date == todayStr && r.Detail == it.Id.ToString()).Sum(r => r.Value);
                var yesterdaySum = records.Where(r => r.Date == yesterdayStr && r.Detail == it.Id.ToString()).Sum(r => r.Value);
                var due = ExerciseRepository.IsDueToday(it, yesterdaySum >= it.TargetValue);
                var reached = todaySum >= it.TargetValue;

                string status;
                string brushKey;
                if (reached)
                {
                    status = "✓ 已达标";
                    brushKey = "AccentGreenBrush";
                }
                else if (!due)
                {
                    status = "今日休息";
                    brushKey = "SecondaryTextBrush";
                }
                else
                {
                    status = $"还差 {it.TargetValue - todaySum:0.##} {it.Unit}";
                    brushKey = "AccentYellowBrush";
                }

                var row = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var dock = new DockPanel();
                var statusText = new TextBlock
                {
                    Text = status,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource(brushKey),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(statusText, Dock.Right);
                dock.Children.Add(statusText);
                dock.Children.Add(new TextBlock
                {
                    Text = $"{it.Name}  {todaySum:0.##} / {it.TargetValue:0.##} {it.Unit}",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Child = dock;
                ExerciseTodayPanel.Children.Add(row);
            }
        }

        /// <summary>近 7 天锻炼量柱状图（所有项目合计）</summary>
        private void DrawExerciseChart()
        {
            ExerciseChartCanvas.Children.Clear();
            var w = ExerciseChartCanvas.ActualWidth;
            var h = ExerciseChartCanvas.ActualHeight;
            if (w < 50) w = 500;
            if (h < 50) h = 150;

            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-(6 - i))).ToList();
            var all = _repo.GetByType("exercise").ToList();
            var maxTotal = days
                .Select(d => all.Where(r => r.Date == d.ToString("yyyy-MM-dd")).Sum(r => r.Value))
                .DefaultIfEmpty(0).Max();
            if (maxTotal <= 0) maxTotal = 1;

            var barW = w / 7 * 0.6;
            var gap = w / 7;
            var axisBrush = (Brush)FindResource("BorderBrush");
            var textBrush = (Brush)FindResource("SecondaryTextBrush");
            var barBrush = (Brush)FindResource("PrimaryBrush");

            for (int i = 0; i < days.Count; i++)
            {
                var total = all.Where(r => r.Date == days[i].ToString("yyyy-MM-dd")).Sum(r => r.Value);
                var barH = h * 0.75 * Math.Min(total / maxTotal, 1.0);
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
                ExerciseChartCanvas.Children.Add(rect);

                var label = new TextBlock
                {
                    Text = days[i].Day.ToString(),
                    FontSize = 9,
                    Foreground = textBrush
                };
                Canvas.SetLeft(label, x + (barW - 12) / 2);
                Canvas.SetTop(label, h - 16);
                ExerciseChartCanvas.Children.Add(label);

                if (total > 0)
                {
                    var val = new TextBlock
                    {
                        Text = $"{total:0.##}",
                        FontSize = 9,
                        Foreground = textBrush
                    };
                    Canvas.SetLeft(val, x + (barW - 18) / 2);
                    Canvas.SetTop(val, y - 14);
                    ExerciseChartCanvas.Children.Add(val);
                }
            }
            ExerciseChartCanvas.Children.Add(new Line { X1 = 0, Y1 = h - 20, X2 = w, Y2 = h - 20, Stroke = axisBrush, StrokeThickness = 1 });
        }

        /// <summary>锻炼历史记录（最近 30 条）</summary>
        private void LoadExerciseRecords()
        {
            ExerciseRecordsPanel.Children.Clear();
            var all = _repo.GetByType("exercise")
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
                .Take(30).ToList();
            if (all.Count == 0)
            {
                ExerciseRecordsPanel.Children.Add(BuildEmptyHint("还没有锻炼记录"));
                return;
            }
            foreach (var r in all)
            {
                var it = _exerciseItems.FirstOrDefault(x => x.Id.ToString() == r.Detail);
                var name = it != null ? it.Name : "已删除项目";
                var unit = it != null ? it.Unit : "次";
                var row = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var dock = new DockPanel();
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(btnPanel, Dock.Right);
                var rid = r.Id;
                var delBtn = new Button
                {
                    Content = "✕",
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Width = 26, Height = 22, FontSize = 9, Padding = new Thickness(0)
                };
                delBtn.Click += (s, ev) =>
                {
                    _repo.Delete(rid);
                    LoadExercise();
                };
                btnPanel.Children.Add(delBtn);
                dock.Children.Add(btnPanel);
                var valueText = new TextBlock
                {
                    Text = $"{r.Value:0.##} {unit}",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("PrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(valueText, Dock.Right);
                dock.Children.Add(valueText);
                dock.Children.Add(new TextBlock
                {
                    Text = $"{r.Date}  {name}",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Child = dock;
                ExerciseRecordsPanel.Children.Add(row);
            }
        }

        // ============ 用药 ============
        private string FormatMedicationSummary(MedicationRecord m)
        {
            var parts = new List<string>
            {
                m.SpecValue > 0 ? $"{m.SpecValue:0.##} {MedicationRepository.MedicationUnitAbbr(m.Unit)}" : MedicationRepository.MedicationUnitName(m.Unit),
                MedicationRepository.FrequencyName(m.Frequency)
            };
            if (!string.IsNullOrEmpty(m.Times))
                parts.Add(string.Join("/", m.Times.Split(',')));
            return string.Join(" · ", parts);
        }

        private string FormatSpec(MedicationRecord m)
        {
            return m.SpecValue > 0 ? $"{m.SpecValue:0.##} {MedicationRepository.MedicationUnitName(m.Unit)}" : MedicationRepository.MedicationUnitName(m.Unit);
        }

        private string FormatFrequency(MedicationRecord m)
        {
            switch (m.Frequency)
            {
                case MedicationFrequency.Daily: return "每天";
                case MedicationFrequency.EveryNDays: return $"每隔 {m.FrequencyN} 天";
                case MedicationFrequency.WeeklyDays:
                {
                    var names = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
                    return string.Join("、", (m.WeeklyDays ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out var d) && d >= 1 && d <= 7 ? names[d - 1] : s));
                }
                case MedicationFrequency.Interval: return $"每 {m.FrequencyN} 小时";
                default: return "按需";
            }
        }

        private string FormatTimes(MedicationRecord m)
        {
            if (string.IsNullOrEmpty(m.Times)) return "--";
            return string.Join("、", m.Times.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
        }

        private string FormatDuration(MedicationRecord m)
        {
            if (!m.StartDate.HasValue && !m.EndDate.HasValue) return "长期";
            var s = m.StartDate.HasValue ? m.StartDate.Value.ToString("yyyy-MM-dd") : "？";
            var e = m.EndDate.HasValue ? m.EndDate.Value.ToString("yyyy-MM-dd") : "至今";
            return $"{s} ~ {e}";
        }

        private void LoadMedications()
        {
            MedicationRecordsPanel.Children.Clear();
            var meds = _medRepo.GetAll();
            if (meds.Count == 0)
            {
                MedicationRecordsPanel.Children.Add(BuildEmptyHint("还没有用药记录，点击右上角 + 添加"));
                return;
            }
            foreach (var m in meds)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var dock = new DockPanel();
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(btnPanel, Dock.Right);
                var editBtn = new Button
                {
                    Content = "✎",
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Width = 28, Height = 24, FontSize = 10, Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                var mid = m.Id;
                editBtn.Click += (s, ev) => EditMedication(mid);
                var delBtn = new Button
                {
                    Content = "✕",
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Width = 28, Height = 24, FontSize = 10, Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                delBtn.Click += (s, ev) =>
                {
                    _medRepo.Delete(mid);
                    LoadMedications();
                    LoadOverview();
                };
                btnPanel.Children.Add(editBtn);
                btnPanel.Children.Add(delBtn);
                dock.Children.Add(btnPanel);
                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = m.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush")
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{MedicationRepository.MedicationTypeName(m.Type)} · {FormatSpec(m)} · {FormatFrequency(m)} · {FormatTimes(m)} · {FormatDuration(m)}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrEmpty(m.Note))
                {
                    info.Children.Add(new TextBlock
                    {
                        Text = m.Note,
                        FontSize = 10,
                        Foreground = (Brush)FindResource("SecondaryTextBrush"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                dock.Children.Add(info);
                card.Child = dock;
                MedicationRecordsPanel.Children.Add(card);
            }
        }

        private void AddMedication_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new MedicationEditDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadMedications();
                LoadOverview();
            }
        }

        private void EditMedication(int id)
        {
            var dlg = new MedicationEditDialog(id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadMedications();
                LoadOverview();
            }
        }

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
