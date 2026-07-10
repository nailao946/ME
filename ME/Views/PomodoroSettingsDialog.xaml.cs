using System;
using System.Windows;
using System.Windows.Input;

namespace ME.Views
{
    public partial class PomodoroSettingsDialog : Window
    {
        public int WorkMinutes { get; private set; }
        public int ShortBreakMinutes { get; private set; }
        public int LongBreakMinutes { get; private set; }
        public int PomodorosBeforeLongBreak { get; private set; }
        public bool AutoStartBreaks { get; private set; }
        public bool AutoStartPomodoros { get; private set; }

        public PomodoroSettingsDialog(int work, int shortBreak, int longBreak, int beforeLong,
            bool autoBreak = true, bool autoWork = false)
        {
            InitializeComponent();
            WorkMinutesBox.Text = work.ToString();
            ShortBreakBox.Text = shortBreak.ToString();
            LongBreakBox.Text = longBreak.ToString();
            BeforeLongBox.Text = beforeLong.ToString();
            AutoBreakCheck.IsChecked = autoBreak;
            AutoWorkCheck.IsChecked = autoWork;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(WorkMinutesBox.Text.Trim(), out int w) || w <= 0 ||
                !int.TryParse(ShortBreakBox.Text.Trim(), out int sb) || sb <= 0 ||
                !int.TryParse(LongBreakBox.Text.Trim(), out int lb) || lb <= 0 ||
                !int.TryParse(BeforeLongBox.Text.Trim(), out int bl) || bl <= 0)
            {
                var msg = new ConfirmDialog("提示", "请输入有效的正整数", "确定") { Owner = this };
                msg.CancelBtn.Visibility = Visibility.Collapsed;
                msg.ShowDialog();
                return;
            }

            WorkMinutes = w;
            ShortBreakMinutes = sb;
            LongBreakMinutes = lb;
            PomodorosBeforeLongBreak = bl;
            AutoStartBreaks = AutoBreakCheck.IsChecked == true;
            AutoStartPomodoros = AutoWorkCheck.IsChecked == true;
            DialogResult = true;
        }
    }
}
