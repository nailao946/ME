using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    public partial class MedicationEditDialog : Window
    {
        private readonly MedicationRepository _repo = new MedicationRepository();
        private readonly int _editId;

        public MedicationEditDialog(int editId = 0)
        {
            InitializeComponent();
            _editId = editId;

            TypeCombo.ItemsSource = Enum.GetValues(typeof(MedicationType)).Cast<MedicationType>()
                .Select(t => new ComboBoxItem { Content = MedicationRepository.MedicationTypeName(t), Tag = t })
                .ToList();
            TypeCombo.SelectedIndex = 1; // 药片

            UnitCombo.ItemsSource = Enum.GetValues(typeof(MedicationUnit)).Cast<MedicationUnit>()
                .Select(u => new ComboBoxItem { Content = MedicationRepository.MedicationUnitName(u), Tag = u })
                .ToList();
            UnitCombo.SelectedIndex = 1; // 毫克

            FreqCombo.ItemsSource = Enum.GetValues(typeof(MedicationFrequency)).Cast<MedicationFrequency>()
                .Select(f => new ComboBoxItem { Content = MedicationRepository.FrequencyName(f), Tag = f })
                .ToList();
            FreqCombo.SelectedIndex = 0; // 每天

            AddTimeRow("08:00");

            if (_editId > 0)
            {
                TitleText.Text = "编辑用药";
                var m = _repo.GetById(_editId);
                if (m != null) LoadFromModel(m);
            }
            else
            {
                StartDatePicker.SelectedDate = DateTime.Today;
            }
        }

        private void LoadFromModel(MedicationRecord m)
        {
            NameBox.Text = m.Name;
            SelectCombo(TypeCombo, m.Type);
            SpecValueBox.Text = m.SpecValue.ToString(CultureInfo.InvariantCulture);
            SelectCombo(UnitCombo, m.Unit);
            SelectCombo(FreqCombo, m.Frequency);
            UpdateFrequencyPanels();
            EveryNDaysBox.Text = m.FrequencyN.ToString();
            IntervalHoursBox.Text = m.FrequencyN.ToString();
            if (!string.IsNullOrEmpty(m.WeeklyDays))
            {
                var days = m.WeeklyDays.Split(',').Select(s => int.TryParse(s, out var d) ? d : 0).ToHashSet();
                WMon.IsChecked = days.Contains(1);
                WTue.IsChecked = days.Contains(2);
                WWed.IsChecked = days.Contains(3);
                WThu.IsChecked = days.Contains(4);
                WFri.IsChecked = days.Contains(5);
                WSat.IsChecked = days.Contains(6);
                WSun.IsChecked = days.Contains(7);
            }
            TimesPanel.Children.Clear();
            var times = (m.Times ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (times.Length == 0) AddTimeRow("08:00");
            else foreach (var t in times) AddTimeRow(t.Trim());
            StartDatePicker.SelectedDate = m.StartDate;
            EndDatePicker.SelectedDate = m.EndDate;
            NoteBox.Text = m.Note ?? "";
            RemindToggle.IsChecked = m.Remind;
        }

        private static void SelectCombo(ComboBox combo, object value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && Equals(item.Tag, value))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private T GetSelectedTag<T>(ComboBox combo)
        {
            return combo.SelectedItem is ComboBoxItem item && item.Tag is T tag ? tag : default;
        }

        private void FreqCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateFrequencyPanels();
        }

        private void UpdateFrequencyPanels()
        {
            var freq = GetSelectedTag<MedicationFrequency>(FreqCombo);
            EveryNDaysPanel.Visibility = freq == MedicationFrequency.EveryNDays ? Visibility.Visible : Visibility.Collapsed;
            IntervalPanel.Visibility = freq == MedicationFrequency.Interval ? Visibility.Visible : Visibility.Collapsed;
            WeeklyDaysPanel.Visibility = freq == MedicationFrequency.WeeklyDays ? Visibility.Visible : Visibility.Collapsed;
        }

        private void WeeklyDay_Changed(object sender, RoutedEventArgs e)
        {
            // 状态由勾选状态直接读取，无需额外处理
        }

        private void AddTime_Click(object sender, RoutedEventArgs e)
        {
            AddTimeRow("08:00");
        }

        private void AddTimeRow(string time)
        {
            // 每个时间点一行（多行排列，避免一行排不开导致时间显示不全）
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var box = new TextBox
            {
                Style = (Style)FindResource("InputTextBoxStyle"),
                Width = 90,
                Text = time,
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var delBtn = new Button
            {
                Content = "✕ 删除",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Width = 58, Height = 32, FontSize = 10, Padding = new Thickness(0)
            };
            delBtn.Click += (s, ev) => TimesPanel.Children.Remove(row);
            row.Children.Add(box);
            row.Children.Add(delBtn);
            TimesPanel.Children.Add(row);
        }

        private void DigitOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入药名", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double spec = 0;
            double.TryParse(SpecValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out spec);

            var freq = GetSelectedTag<MedicationFrequency>(FreqCombo);
            int freqN = 1;
            if (freq == MedicationFrequency.EveryNDays) int.TryParse(EveryNDaysBox.Text, out freqN);
            else if (freq == MedicationFrequency.Interval) int.TryParse(IntervalHoursBox.Text, out freqN);
            if (freqN <= 0) freqN = 1;

            string weeklyDays = null;
            if (freq == MedicationFrequency.WeeklyDays)
            {
                var days = new List<int>();
                if (WMon.IsChecked == true) days.Add(1);
                if (WTue.IsChecked == true) days.Add(2);
                if (WWed.IsChecked == true) days.Add(3);
                if (WThu.IsChecked == true) days.Add(4);
                if (WFri.IsChecked == true) days.Add(5);
                if (WSat.IsChecked == true) days.Add(6);
                if (WSun.IsChecked == true) days.Add(7);
                if (days.Count == 0)
                {
                    MessageBox.Show("请至少选择一天", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                weeklyDays = string.Join(",", days);
            }

            var times = TimesPanel.Children.OfType<StackPanel>()
                .Select(p => p.Children.OfType<TextBox>().FirstOrDefault()?.Text?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();

            var record = new MedicationRecord
            {
                Name = name,
                Type = GetSelectedTag<MedicationType>(TypeCombo),
                SpecValue = spec,
                Unit = GetSelectedTag<MedicationUnit>(UnitCombo),
                Frequency = freq,
                FrequencyN = freqN,
                WeeklyDays = weeklyDays,
                Times = times.Count > 0 ? string.Join(",", times) : null,
                StartDate = StartDatePicker.SelectedDate,
                EndDate = EndDatePicker.SelectedDate,
                Note = NoteBox.Text?.Trim(),
                Remind = RemindToggle.IsChecked == true
            };

            if (_editId > 0)
            {
                record.Id = _editId;
                _repo.Update(record);
            }
            else
            {
                _repo.Insert(record);
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) DialogResult = false;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
