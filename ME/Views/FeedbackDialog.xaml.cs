using System;
using System.Windows;
using System.Windows.Input;
using ME.Services;

namespace ME.Views
{
    /// <summary>
    /// 提意见 / 反馈 Bug 弹窗：内容作为 Issue 提交到项目 GitHub 仓库（复用云同步已绑定的账号）。
    /// </summary>
    public partial class FeedbackDialog : Window
    {
        private bool _submitting;

        public FeedbackDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => ContentBox.Focus();
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (_submitting) return;
            var content = ContentBox.Text.Trim();
            if (content.Length == 0)
            {
                ConfirmDialog.Show(this, "内容为空", "请先写下你的建议或遇到的问题。", "确定");
                return;
            }
            _submitting = true;
            SubmitButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            SubmitButton.Content = "提交中…";
            StatusText.Visibility = Visibility.Collapsed;
            try
            {
                var n = await GitHubSyncService.SubmitFeedbackAsync(content);
                ConfirmDialog.Show(this, "提交成功", $"反馈已作为 Issue #{n} 提交到 github.com/nailao946/ME，感谢反馈！", "确定");
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                _submitting = false;
                SubmitButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                SubmitButton.Content = "提交反馈";
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_submitting) DialogResult = false;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_submitting)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }
    }
}
