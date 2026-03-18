using EMGFeedbackSystem.ViewModels;
using EMGFeedbackSystem.Views;
using System.Windows;

namespace EMGFeedbackSystem
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        private void SubjectNameButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者姓名：", _viewModel.SubjectName ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectName = dialog.InputText;
            }
        }

        private void SubjectGenderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者性别（男/女）：", _viewModel.SelectedGender ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedGender = dialog.InputText;
            }
        }

        private void SubjectAgeButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者年龄：", _viewModel.SubjectAge ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectAge = dialog.InputText;
            }
        }

        private void SubjectIdButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者编号：", _viewModel.SubjectId ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectId = dialog.InputText;
            }
        }

        private void SubjectNotesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者备注：", _viewModel.SubjectNotes ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectNotes = dialog.InputText;
            }
        }

        private void ElectrodePositionButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入电极粘贴部位（左腿/右腿）：", _viewModel.SelectedLegPosition ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedLegPosition = dialog.InputText;
            }
        }

        private void LegSideButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入该腿侧别（健侧/患侧）：", _viewModel.SelectedLegSide ?? string.Empty)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedLegSide = dialog.InputText;
            }
        }

        private void UpperLimitButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入上限基准值（如 1.0）：", _viewModel.UpperLimit.ToString())
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                if (double.TryParse(dialog.InputText, out double value))
                {
                    _viewModel.UpperLimit = value;
                }
                else
                {
                    MessageBox.Show("请输入有效的数字！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BackToWelcome_Click(object sender, RoutedEventArgs e)
        {
            var welcomeWindow = new WelcomeWindow();
            welcomeWindow.Show();
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _viewModel.Cleanup();
            base.OnClosing(e);
        }
    }
}
