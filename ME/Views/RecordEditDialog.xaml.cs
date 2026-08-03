using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ME.Models;

namespace ME.Views
{
    public partial class RecordEditDialog : Window
    {
        public DateTime ResultStartTime { get; private set; }
        public DateTime? ResultEndTime { get; private set; }
        public int ResultTagId { get; private set; }
        public string ResultNote { get; private set; }

        private readonly List<TimeTag> _tags;

        public RecordEditDialog(TimeRecord record, List<TimeTag> tags)
        {
            InitializeComponent();
            _tags = tags.ToList();

            StartTimeBox.Text = record.StartTime.ToString("yyyy-MM-dd HH:mm");
            if (record.EndTime.HasValue)
            {
                EndTimeBox.Text = record.EndTime.Value.ToString("yyyy-MM-dd HH:mm");
                NoEndTimeCheck.IsChecked = false;
            }
            else
            {
                EndTimeBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                NoEndTimeCheck.IsChecked = true;
                EndTimeBox.IsEnabled = false;
            }

            TagComboBox.ItemsSource = _tags;
            TagComboBox.SelectedItem = _tags.FirstOrDefault(t => t.Id == record.TagId);
            NoteBox.Text = record.Note ?? "";
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void NoEndTime_Changed(object sender, RoutedEventArgs e)
        {
            if (EndTimeBox != null)
                EndTimeBox.IsEnabled = NoEndTimeCheck.IsChecked != true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!DateTime.TryParse(StartTimeBox.Text, out var start))
            {
                ConfirmDialog.Show(Window.GetWindow(this), "提示", "开始时间格式不正确", "确定");
                return;
            }

            DateTime? end = null;
            if (NoEndTimeCheck.IsChecked != true)
            {
                if (!DateTime.TryParse(EndTimeBox.Text, out var endVal))
                {
                    ConfirmDialog.Show(Window.GetWindow(this), "提示", "结束时间格式不正确", "确定");
                    return;
                }
                end = endVal;
            }

            ResultStartTime = start;
            ResultEndTime = end;
            ResultTagId = (TagComboBox.SelectedItem as TimeTag)?.Id ?? 0;
            ResultNote = NoteBox.Text?.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
