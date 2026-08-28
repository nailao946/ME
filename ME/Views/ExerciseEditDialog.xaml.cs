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
    public partial class ExerciseEditDialog : Window
    {
        private readonly ExerciseRepository _repo = new ExerciseRepository();
        private readonly int _editId;

        public ExerciseEditDialog(int editId = 0)
        {
            InitializeComponent();
            _editId = editId;

            // 分类：已有分类 + 可输入自定义
            CategoryCombo.ItemsSource = _repo.GetCategories();

            FreqCombo.ItemsSource = new[]
            {
                new ComboBoxItem { Content = "每日", Tag = "daily" },
                new ComboBoxItem { Content = "隔日", Tag = "every_other_day" },
                new ComboBoxItem { Content = "每周指定几天", Tag = "weekly_days" }
            }.ToList();
            FreqCombo.SelectedIndex = 0;

            UnitCombo.ItemsSource = new[]
            {
                new ComboBoxItem { Content = "次", Tag = "次" },
                new ComboBoxItem { Content = "分钟", Tag = "分钟" },
                new ComboBoxItem { Content = "千卡", Tag = "千卡" }
            }.ToList();
            UnitCombo.SelectedIndex = 0;

            if (_editId > 0)
            {
                TitleText.Text = "编辑锻炼项目";
                var m = _repo.GetById(_editId);
                if (m != null) LoadFromModel(m);
            }
        }

        private void LoadFromModel(ExerciseItem m)
        {
            NameBox.Text = m.Name;
            SelectComboByTag(FreqCombo, m.Frequency);
            SelectComboByTag(UnitCombo, m.Unit);
            TargetValueBox.Text = m.TargetValue.ToString(CultureInfo.InvariantCulture);
            UpdateWeeklyPanel();
            if (!string.IsNullOrEmpty(m.Category))
            {
                CategoryCombo.Text = m.Category.Trim();
            }
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
            NoteBox.Text = m.Note ?? "";
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && Equals(item.Tag?.ToString(), tag))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private string GetSelectedTag(ComboBox combo)
        {
            return combo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null;
        }

        private void FreqCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateWeeklyPanel();
        }

        private void UpdateWeeklyPanel()
        {
            WeeklyDaysPanel.Visibility = GetSelectedTag(FreqCombo) == "weekly_days" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void WeeklyDay_Changed(object sender, RoutedEventArgs e)
        {
            // 状态由勾选状态直接读取
        }

        private void Decimal_PreviewTextInput(object sender, TextCompositionEventArgs e)
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入项目名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!double.TryParse(TargetValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var target) || target <= 0)
            {
                MessageBox.Show("请输入大于 0 的目标量", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var freq = GetSelectedTag(FreqCombo);
            string weeklyDays = null;
            if (freq == "weekly_days")
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

            var item = new ExerciseItem
            {
                Name = name,
                TargetValue = target,
                Unit = GetSelectedTag(UnitCombo) ?? "次",
                Frequency = freq ?? "daily",
                WeeklyDays = weeklyDays,
                Category = CategoryCombo.Text?.Trim(),
                Note = NoteBox.Text?.Trim()
            };

            if (_editId > 0)
            {
                item.Id = _editId;
                _repo.Update(item);
            }
            else
            {
                _repo.Insert(item);
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
