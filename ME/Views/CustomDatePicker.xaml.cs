using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ME.Views
{
    public partial class CustomDatePicker : UserControl
    {
        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(CustomDatePicker),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

        public DateTime? SelectedDate
        {
            get => (DateTime?)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        /// <summary>选择日期变化时触发（含用户选择与代码赋值）</summary>
        public event EventHandler SelectedDateChanged;

        private DateTime _displayMonth;
        private bool _suppressUpdate;

        public CustomDatePicker()
        {
            InitializeComponent();
            _displayMonth = DateTime.Today;
            Loaded += (s, e) => { if (!SelectedDate.HasValue) _displayMonth = DateTime.Today; else _displayMonth = SelectedDate.Value; BuildCalendar(); UpdateDateText(); };
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CustomDatePicker picker && !picker._suppressUpdate)
            {
                var date = e.NewValue as DateTime?;
                if (date.HasValue) picker._displayMonth = date.Value;
                picker.UpdateDateText();
                picker.BuildCalendar();
                picker.SelectedDateChanged?.Invoke(picker, EventArgs.Empty);
            }
        }

        private void UpdateDateText()
        {
            DateText.Text = SelectedDate.HasValue ? SelectedDate.Value.ToString("yyyy-MM-dd") : "选择日期";
            DateText.Foreground = SelectedDate.HasValue
                ? (Brush)FindResource("TextBrush")
                : (Brush)FindResource("SecondaryTextBrush");
        }

        private void TogglePopup(object sender, MouseButtonEventArgs e)
        {
            if (CalendarPopup.IsOpen)
            {
                ClosePopup();
                return;
            }
            CalendarPopup.IsOpen = true;
            BuildCalendar();
            // 不再用“点击主窗口任意处即关闭”（会导致刚打开/点边缘就消失），
            // 改为：再点输入框收起 / 选择日期或今天收起 / 窗口失活收起 / Esc 收起
            var win = Window.GetWindow(this);
            if (win != null)
            {
                win.Deactivated -= Window_Deactivated;
                win.Deactivated += Window_Deactivated;
                win.PreviewKeyDown -= Window_PreviewKeyDown;
                win.PreviewKeyDown += Window_PreviewKeyDown;
            }
        }

        private void ClosePopup()
        {
            if (!CalendarPopup.IsOpen) return;
            var win = Window.GetWindow(this);
            if (win != null)
            {
                win.Deactivated -= Window_Deactivated;
                win.PreviewKeyDown -= Window_PreviewKeyDown;
            }
            CalendarPopup.IsOpen = false;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            ClosePopup();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && CalendarPopup.IsOpen)
            {
                ClosePopup();
                e.Handled = true;
            }
        }

        private void Popup_GotMouseCapture(object sender, MouseEventArgs e)
        {
            // Keep popup open while interacting
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayMonth = _displayMonth.AddMonths(-1);
            BuildCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayMonth = _displayMonth.AddMonths(1);
            BuildCalendar();
        }

        private void Today_Click(object sender, RoutedEventArgs e)
        {
            _suppressUpdate = true;
            SelectedDate = DateTime.Today;
            _displayMonth = DateTime.Today;
            _suppressUpdate = false;
            BuildCalendar();
            UpdateDateText();
            ClosePopup();
        }

        private void BuildCalendar()
        {
            DaysGrid.Children.Clear();
            MonthYearLabel.Text = _displayMonth.ToString("yyyy年 M月");

            var firstDay = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            var startOffset = (int)firstDay.DayOfWeek;
            var daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);

            // Empty cells for offset
            for (int i = 0; i < startOffset; i++)
            {
                DaysGrid.Children.Add(new Border { Height = 36 });
            }

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
                var isToday = date == DateTime.Today;
                var isSelected = SelectedDate.HasValue && date == SelectedDate.Value.Date;

                var btn = new Button
                {
                    Content = day.ToString(),
                    FontSize = 13,
                    Padding = new Thickness(0),
                    Height = 36,
                    Width = 36,
                    Tag = date,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                if (isSelected)
                {
                    btn.Background = (Brush)FindResource("PrimaryBrush");
                    btn.Foreground = Brushes.White;
                    btn.FontWeight = FontWeights.Bold;
                }
                else if (isToday)
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = (Brush)FindResource("PrimaryBrush");
                    btn.BorderBrush = (Brush)FindResource("PrimaryBrush");
                    btn.BorderThickness = new Thickness(1);
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = (Brush)FindResource("TextBrush");
                }

                // Override template for rounded look
                btn.Template = CreateDayButtonTemplate(btn, isSelected, isToday);
                btn.Click += DayButton_Click;
                DaysGrid.Children.Add(btn);
            }
        }

        private ControlTemplate CreateDayButtonTemplate(Button btn, bool isSelected, bool isToday)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;

            // Hover trigger
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            if (!isSelected)
            {
                trigger.Setters.Add(new Setter(Button.BackgroundProperty, (Brush)FindResource("NavHoverBrush")));
            }
            template.Triggers.Add(trigger);

            return template;
        }

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DateTime date)
            {
                _suppressUpdate = true;
                SelectedDate = date;
                _suppressUpdate = false;
                UpdateDateText();
                ClosePopup();
            }
        }
    }
}
