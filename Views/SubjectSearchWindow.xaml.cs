using EMGFeedbackSystem.Models;
using EMGFeedbackSystem.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace EMGFeedbackSystem.Views
{
    public partial class SubjectSearchWindow : Window
    {
        private readonly DatabaseService _dbService;
        public Subject? SelectedSubject { get; private set; }

        public SubjectSearchWindow(DatabaseService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
            LoadAllSubjects();
        }

        private void LoadAllSubjects()
        {
            var subjects = _dbService.GetAllSubjects();
            SubjectsDataGrid.ItemsSource = subjects;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string keyword = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                LoadAllSubjects();
            }
            else
            {
                var subjects = _dbService.SearchSubjects(keyword);
                SubjectsDataGrid.ItemsSource = subjects;
            }
        }

        private void ShowAllButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            LoadAllSubjects();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (SubjectsDataGrid.SelectedItem is Subject subject)
            {
                SelectedSubject = subject;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("请选择一个受试者", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
