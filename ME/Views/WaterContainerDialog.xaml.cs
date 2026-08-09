using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    public partial class WaterContainerDialog : Window
    {
        private readonly WaterContainerRepository _repo = new WaterContainerRepository();
        private WaterContainer _editing;

        public WaterContainerDialog()
        {
            InitializeComponent();
            RefreshList();
        }

        private void RefreshList()
        {
            ContainerListPanel.Children.Clear();
            var items = _repo.EnsureDefaults();
            foreach (var c in items.OrderBy(c => c.CapacityMl))
            {
                var border = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = Cursors.Hand
                };
                border.MouseLeftButtonDown += (s, e) => StartEdit(c);
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
                delBtn.Click += (s, e) =>
                {
                    e.Handled = true;
                    _repo.Delete(c.Id);
                    if (_editing?.Id == c.Id) ResetEdit();
                    RefreshList();
                };
                dock.Children.Add(delBtn);
                dock.Children.Add(new TextBlock
                {
                    Text = c.IsBuiltIn ? $"{c.Name}（内置）" : c.Name,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                dock.Children.Add(new TextBlock
                {
                    Text = $"{c.CapacityMl:0} ml",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("PrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                border.Child = dock;
                ContainerListPanel.Children.Add(border);
            }
        }

        private void StartEdit(WaterContainer c)
        {
            _editing = c;
            NameBox.Text = c.Name;
            CapacityBox.Text = c.CapacityMl.ToString(CultureInfo.InvariantCulture);
            AddBtn.Visibility = Visibility.Collapsed;
            UpdateBtn.Visibility = Visibility.Visible;
            CancelEditBtn.Visibility = Visibility.Visible;
            MsgText.Text = $"正在编辑：{c.Name}";
            DiameterBox.Clear();
            HeightBox.Clear();
            CalcResultText.Text = "";
        }

        private void ResetEdit()
        {
            _editing = null;
            NameBox.Clear();
            CapacityBox.Clear();
            DiameterBox.Clear();
            HeightBox.Clear();
            CalcResultText.Text = "";
            MsgText.Text = "";
            AddBtn.Visibility = Visibility.Visible;
            UpdateBtn.Visibility = Visibility.Collapsed;
            CancelEditBtn.Visibility = Visibility.Collapsed;
        }

        private bool TryGetMl(out double ml)
        {
            ml = 0;
            if (double.TryParse(CapacityBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0)
            {
                ml = v;
                return true;
            }
            return false;
        }

        private void Calc_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(DiameterBox.Text.Trim(), out var d) || d <= 0 ||
                !double.TryParse(HeightBox.Text.Trim(), out var h) || h <= 0)
            {
                CalcResultText.Text = "请输入有效的直径和高度（cm）";
                CalcResultText.Foreground = (Brush)FindResource("AccentRedBrush");
                return;
            }
            var r = d / 2.0;
            var ml = Math.PI * r * r * h; // 1 cm³ = 1 ml
            CapacityBox.Text = ml.ToString("F0", CultureInfo.InvariantCulture);
            CalcResultText.Text = $"≈ {ml:F0} ml";
            CalcResultText.Foreground = (Brush)FindResource("PrimaryBrush");
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { MsgText.Text = "请输入容器名称"; return; }
            if (!TryGetMl(out var ml)) { MsgText.Text = "请输入有效的容量（ml）"; return; }
            _repo.Insert(new WaterContainer { Name = name, CapacityMl = ml });
            ResetEdit();
            RefreshList();
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_editing == null) return;
            var name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { MsgText.Text = "请输入容器名称"; return; }
            if (!TryGetMl(out var ml)) { MsgText.Text = "请输入有效的容量（ml）"; return; }
            _repo.Update(new WaterContainer { Id = _editing.Id, Name = name, CapacityMl = ml });
            ResetEdit();
            RefreshList();
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            ResetEdit();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
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
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
