using System.Windows;

namespace EMGFeedbackSystem.Views
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string prompt, string defaultText = "")
        {
            InitializeComponent();
            PromptTextBlock.Text = prompt;
            InputTextBox.Text = defaultText;
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
