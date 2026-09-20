using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Models;

namespace ME.Views
{
    public partial class QuantitativeInputDialog : Window
    {
        public double NewValue { get; private set; }
        private readonly TaskItem _task;
        private DatePicker _datePicker;
        public DateTime? SelectedDate { get; internal set; }

        public QuantitativeInputDialog(TaskItem task)
        {
            InitializeComponent();
            _task = task;

            TaskTitleText.Text = task.Title;
            var current = task.QuantitativeCurrent ?? task.QuantitativeStart ?? 0;
            var target = task.QuantitativeTarget ?? 0;
            var unit = task.QuantitativeUnit ?? "";

            NewValue = current;

            if (task.QuantitativeMode == QuantitativeMode.Accumulate)
            {
                ModeText.Text = $"模式：求和（累加）| 单位：{unit}";
                InputLabel.Text = $"输入要增加的数值（{unit}）";
            }
            else
            {
                ModeText.Text = $"模式：更新（覆盖）| 单位：{unit}";
                InputLabel.Text = $"输入新的数值（{unit}）";
            }

            CurrentValueText.Text = $"{current} {unit}";
            TargetValueText.Text = $"{target} {unit}";

            var progress = target > 0 ? Math.Min(current / target * 100, 100) : 0;
            ProgressBar.Value = progress;
            ProgressText.Text = $"{progress:F1}%";

            ValueInput.Focus();
            BuildDateRow();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (_datePicker == null || _datePicker.SelectedDate == null ||
                _datePicker.SelectedDate.Value.Date > DateTime.Today ||
                _datePicker.SelectedDate.Value.Date < DateTime.Today.AddDays(-30))
            {
                ConfirmDialog.Show(this, "提示", "请选择今天或过去 30 天内的日期", "确定");
                return;
            }

            if (!double.TryParse(ValueInput.Text, out double input))
            {
                ConfirmDialog.Show(this, "提示", "请输入有效数值", "确定");
                return;
            }

            var current = _task.QuantitativeCurrent ?? _task.QuantitativeStart ?? 0;

            if (_task.QuantitativeMode == QuantitativeMode.Accumulate)
            {
                NewValue = current + input;
            }
            else
            {
                NewValue = input;
            }

            SelectedDate = _datePicker?.SelectedDate;

            DialogResult = true;
            Close();
        }

        private void BuildDateRow()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock
            {
                Text = "日期",
                FontSize = 12,
                Foreground = GetBrush("SecondaryTextBrush"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            _datePicker = new DatePicker
            {
                SelectedDate = DateTime.Today,
                DisplayDateStart = DateTime.Today.AddDays(-30),
                DisplayDateEnd = DateTime.Today,
                FontSize = 13,
                Height = 36,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _datePicker.Background = GetBrush("CardBrush");
            _datePicker.Foreground = GetBrush("TextBrush");
            _datePicker.BorderBrush = GetBrush("BorderBrush");
            panel.Children.Add(_datePicker);

            var sv = FindContentScroller();
            if (sv?.Content is StackPanel sp)
            {
                int idx = sp.Children.IndexOf(InputLabel);
                if (idx < 0) idx = sp.Children.Count;
                sp.Children.Insert(idx, panel);
            }
        }

        private ScrollViewer FindContentScroller()
        {
            if (this.Content is Border border && border.Child is Grid grid)
            {
                foreach (var c in grid.Children)
                    if (c is ScrollViewer sv) return sv;
            }
            return null;
        }

        private Brush GetBrush(string key) => (Brush)FindResource(key);

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm_Click(sender, e);
            }
        }
    }
}
