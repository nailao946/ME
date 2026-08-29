using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ME.Services;

namespace ME.Views
{
    /// <summary>
    /// 提意见 / 反馈 Bug 弹窗：标题、类型、详细描述分开填写，
    /// 提交时组装为规范化 Markdown Issue（类型前缀 + 反馈类型段落），复用云同步已绑定的账号。
    /// </summary>
    public partial class FeedbackDialog : Window
    {
        private bool _submitting;
        private string _currentTemplate;

        private const string BugTemplate = "【问题描述】\n\n【复现步骤】\n1. \n2. \n\n【期望结果】\n\n【实际结果】";
        private const string SuggestTemplate = "【建议内容】\n\n【使用场景】";

        public FeedbackDialog()
        {
            InitializeComponent();
            SetTemplate(BugTemplate);
            Loaded += (s, e) => TitleBox.Focus();
        }

        private void Type_Checked(object sender, RoutedEventArgs e)
        {
            if (ContentBox == null) return; // XAML 初始化期间触发
            SetTemplate(BugRadio.IsChecked == true ? BugTemplate
                      : SuggestRadio.IsChecked == true ? SuggestTemplate
                      : null);
        }

        /// <summary>切换类型时，仅在内容为空或仍是上一类型模板原文时替换为新模板，不覆盖用户已写的文字。</summary>
        private void SetTemplate(string tpl)
        {
            var cur = ContentBox.Text ?? "";
            bool replace = string.IsNullOrWhiteSpace(cur)
                || (_currentTemplate != null && cur.Trim() == _currentTemplate.Trim());
            if (replace)
            {
                ContentBox.Text = tpl ?? "";
                _currentTemplate = tpl;
            }
        }

        private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ContentPlaceholder != null)
                ContentPlaceholder.Visibility = string.IsNullOrEmpty(ContentBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (_submitting) return;
            var title = TitleBox.Text.Trim();
            if (title.Length == 0)
            {
                ConfirmDialog.Show(this, "标题为空", "请先填写标题，一句话概括你的反馈。", "确定");
                return;
            }
            var content = ContentBox.Text.Trim();
            if (content.Length == 0)
            {
                ConfirmDialog.Show(this, "内容为空", "请先写下详细描述。", "确定");
                return;
            }
            if (_currentTemplate != null && content == _currentTemplate.Trim())
            {
                ConfirmDialog.Show(this, "内容未填写", "请把模板中的提示文字替换为你的具体反馈。", "确定");
                return;
            }
            _submitting = true;
            SubmitButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            SubmitButton.Content = "提交中…";
            StatusText.Visibility = Visibility.Collapsed;
            try
            {
                var typeName = BugRadio.IsChecked == true ? "Bug 反馈"
                             : SuggestRadio.IsChecked == true ? "功能建议" : "其他";
                var prefix = BugRadio.IsChecked == true ? "[Bug] "
                           : SuggestRadio.IsChecked == true ? "[建议] " : "";
                var sb = new StringBuilder();
                sb.Append("**反馈类型：** ").Append(typeName).Append("\n\n").Append(content);

                var n = await GitHubSyncService.SubmitFeedbackAsync(prefix + title, sb.ToString());
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
