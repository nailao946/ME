using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    public partial class AiProviderDialog : Window
    {
        private readonly AiProviderRepository _repo = new AiProviderRepository();
        private readonly int _editId;

        public AiProviderDialog(int editId = 0)
        {
            InitializeComponent();
            _editId = editId;
            FormatCombo.SelectedIndex = 0;

            if (_editId > 0)
            {
                TitleText.Text = "编辑 AI 供应商";
                DeleteBtn.Visibility = Visibility.Visible;
                var p = _repo.GetAll().Find(x => x.Id == _editId);
                if (p != null)
                {
                    NameBox.Text = p.Name;
                    BaseUrlBox.Text = p.BaseUrl;
                    ModelBox.Text = p.Model;
                    SetDefaultCheck.IsChecked = p.IsDefault;
                    if (!string.IsNullOrEmpty(p.EncryptedApiKey))
                        HintText.Text = "已保存 API Key（可留空表示不修改）";
                    var fmt = p.ApiFormat == AiApiFormat.Anthropic ? "Anthropic" : "OpenAI";
                    foreach (ComboBoxItem it in FormatCombo.Items)
                        if (it.Tag?.ToString() == fmt) { FormatCombo.SelectedItem = it; break; }
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { HintText.Text = "请输入供应商名称"; return; }
            var baseUrl = BaseUrlBox.Text?.Trim();
            if (string.IsNullOrEmpty(baseUrl)) { HintText.Text = "请输入请求地址"; return; }
            var model = ModelBox.Text?.Trim();
            if (string.IsNullOrEmpty(model)) { HintText.Text = "请输入模型名称"; return; }

            var provider = new AiProvider
            {
                Id = _editId,
                Name = name,
                BaseUrl = baseUrl,
                Model = model,
                ApiFormat = FormatCombo.SelectedItem is ComboBoxItem it && it.Tag?.ToString() == "Anthropic"
                    ? AiApiFormat.Anthropic : AiApiFormat.OpenAI,
                IsDefault = SetDefaultCheck.IsChecked == true
            };
            var key = ApiKeyBox.Password?.Trim();
            if (!string.IsNullOrEmpty(key))
                provider.EncryptedApiKey = Services.SecureStore.Encrypt(key);

            if (_editId > 0)
                _repo.Update(provider);
            else
                _repo.Insert(provider);

            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_editId > 0)
            {
                _repo.Delete(_editId);
                DialogResult = true;
            }
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
