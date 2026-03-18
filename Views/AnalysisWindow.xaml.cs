using EMGFeedbackSystem.Models;
using EMGFeedbackSystem.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace EMGFeedbackSystem.Views
{
    public partial class AnalysisWindow : Window
    {
        private readonly DatabaseService _dbService;

        public AnalysisWindow()
        {
            InitializeComponent();
            _dbService = new DatabaseService();
            LoadAllSubjects();
        }

        private void LoadAllSubjects()
        {
            var subjects = _dbService.GetAllSubjects();
            SubjectsDataGrid.ItemsSource = ToDisplayRows(subjects);
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
                SubjectsDataGrid.ItemsSource = ToDisplayRows(subjects);
            }
        }

        private void ShowAllButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            LoadAllSubjects();
        }

        private void BackToWelcome_Click(object sender, RoutedEventArgs e)
        {
            var welcomeWindow = new EMGFeedbackSystem.WelcomeWindow();
            welcomeWindow.Show();
            Close();
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static List<AnalysisRow> ToDisplayRows(IEnumerable<Subject> subjects)
        {
            return subjects.Select(subject => new AnalysisRow
            {
                SubjectId = subject.SubjectId,
                Name = subject.Name,
                Gender = subject.Gender,
                Age = subject.Age,
                UpperLimit = subject.UpperLimit,
                LeftLegMaxA = subject.LeftLegMaxA,
                RightLegMaxA = subject.RightLegMaxA,
                LeftLegSide = subject.LeftLegSide,
                RightLegSide = subject.RightLegSide,
                DisplayName = string.IsNullOrWhiteSpace(subject.Name)
                    ? $"测试数据 {subject.SubjectId}"
                    : subject.Name
            }).ToList();
        }

        private sealed class AnalysisRow
        {
            public string DisplayName { get; set; } = string.Empty;
            public string SubjectId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Gender { get; set; } = string.Empty;
            public int Age { get; set; }
            public double UpperLimit { get; set; }
            public double LeftLegMaxA { get; set; }
            public double RightLegMaxA { get; set; }
            public string LeftLegSide { get; set; } = string.Empty;
            public string RightLegSide { get; set; } = string.Empty;
        }
    }
}


