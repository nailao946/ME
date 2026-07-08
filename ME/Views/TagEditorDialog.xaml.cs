using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ME.Models;

namespace ME.Views
{
    public partial class TagEditorDialog : Window
    {
        public TimeTag Result { get; private set; }
        private string _selectedColor;
        private readonly bool _isPreset;

        private static readonly List<string> PresetColors = new List<string>
        {
            "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#007AFF",
            "#5856D6", "#AF52DE", "#FF2D55", "#00C7BE", "#8E8E93"
        };

        public TagEditorDialog(TimeTag existing = null)
        {
            InitializeComponent();

            _selectedColor = existing?.Color ?? "#007AFF";
            _isPreset = existing?.IsPreset ?? false;

            NameBox.Text = existing?.Name ?? "新标签";
            NameBox.IsReadOnly = _isPreset;
            ColorBox.Text = _selectedColor;
            NotesBox.Text = existing?.Notes ?? "";

            Result = existing ?? new TimeTag();

            if (_isPreset)
            {
                var note = new TextBlock
                {
                    Text = "预设标签不可编辑名称",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0)),
                    Margin = new Thickness(0, -8, 0, 14)
                };
                var parent = (Panel)NameBox.Parent;
                var idx = parent.Children.IndexOf(NameBox);
                parent.Children.Insert(idx + 1, note);
            }

            BuildColorPalette();
        }

        private void BuildColorPalette()
        {
            foreach (var color in PresetColors)
        {
                var border = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(14),
                    BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
                };

                if (color.Equals(_selectedColor, StringComparison.OrdinalIgnoreCase))
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
                }

                if (_isPreset)
                {
                    border.Cursor = Cursors.Arrow;
                    border.Opacity = 0.6;
                }
                else
                {
                    border.Cursor = Cursors.Hand;
                    var c = color;
                    border.MouseLeftButtonDown += (s, e) => SelectColor(c);
                }
                ColorPalette.Items.Add(border);
            }
        }

        private void SelectColor(string color)
        {
            _selectedColor = color;
            ColorBox.Text = color;

            foreach (var child in ColorPalette.Items)
            {
                if (child is Border b)
                {
                    var bg = b.Background as SolidColorBrush;
                    if (bg != null && ColorToHex(bg.Color).Equals(color.TrimStart('#'), StringComparison.OrdinalIgnoreCase))
                    {
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
                    }
                    else
                    {
                        b.BorderBrush = Brushes.Transparent;
                    }
                }
            }
        }

        private static string ColorToHex(Color c)
        {
            return $"{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private void ColorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(ColorBox.Text);
                _selectedColor = ColorBox.Text;
            }
            catch { }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.OriginalSource is System.Windows.Controls.Border)
                DragMove();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPreset)
            {
                var name = NameBox.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = "未命名标签";
                Result.Name = name;
            }
            Result.Color = _selectedColor;
            Result.Notes = NotesBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
