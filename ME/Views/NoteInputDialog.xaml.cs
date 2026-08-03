using System.Windows;
using System.Windows.Input;

namespace ME.Views
{
    public partial class NoteInputDialog : Window
    {
        public string ResultNote { get; private set; }

        public NoteInputDialog(string tagName, string timeRange, string currentNote)
        {
            InitializeComponent();
            RecordTimeText.Text = $"{tagName}  {timeRange}";
            NoteInput.Text = currentNote ?? "";
            NoteInput.Focus();
            NoteInput.CaretIndex = NoteInput.Text.Length;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ResultNote = NoteInput.Text?.Trim();
            DialogResult = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ResultNote = "";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                Save_Click(sender, e);
            }
        }
    }
}
