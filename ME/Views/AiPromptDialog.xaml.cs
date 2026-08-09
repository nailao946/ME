using System.Windows;
using System.Windows.Input;

namespace ME.Views
{
    public partial class AiPromptDialog : Window
    {
        /// <summary>内置默认提示词（可被用户修改并持久化，恢复默认时回到该值）</summary>
        public const string DefaultAiSystemPrompt =
            "你是一名健康数据分析助手。用户会提供若干健康指标按日期的数据（数值越大代表量越多；心情 0=开心、3=难过）。" +
            "请分析这些指标之间可能存在的相关性、趋势规律，给出可执行的健康建议。用简体中文回答，分点列出，不超过 400 字。";

        /// <summary>编辑后的提示词（DialogResult=true 时有效）</summary>
        public string Prompt => PromptBox.Text.Trim();

        public AiPromptDialog(string initialPrompt)
        {
            InitializeComponent();
            PromptBox.Text = string.IsNullOrWhiteSpace(initialPrompt) ? DefaultAiSystemPrompt : initialPrompt;
            PromptBox.Focus();
            PromptBox.CaretIndex = 0;
            Loaded += (s, e) => PromptBox.Focus();
        }

        private void RestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            PromptBox.Text = DefaultAiSystemPrompt;
            PromptBox.Focus();
            PromptBox.CaretIndex = PromptBox.Text.Length;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PromptBox.Text))
            {
                ConfirmDialog.Show(this, "提示词不能为空", "请填写提示词后再保存，或点击恢复默认提示词。", "确定");
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }
    }
}
