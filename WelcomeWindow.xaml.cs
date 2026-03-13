using System.Windows;

namespace EMGFeedbackSystem
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
        }

        private void StartTest_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void StartAnalysis_Click(object sender, RoutedEventArgs e)
        {
            var analysisWindow = new Views.AnalysisWindow();
            analysisWindow.Show();
            Close();
        }
    }
}
