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

        // 受试者姓名按钮点�?
private void SubjectNameButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者姓�?", _viewModel.SubjectName ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectName = dialog.InputText;
            }
        }

        // 受试者性别按钮点击
        private void SubjectGenderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者性别 (�?�?:", _viewModel.SelectedGender ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedGender = dialog.InputText;
            }
        }

        // 受试者年龄按钮点�?
private void SubjectAgeButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者年�?", _viewModel.SubjectAge ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectAge = dialog.InputText;
            }
        }

        // 受试者编号按钮点�?
private void SubjectIdButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者编�?", _viewModel.SubjectId ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectId = dialog.InputText;
            }
        }

        // 受试者备注按钮点�?
private void SubjectNotesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入受试者备�?", _viewModel.SubjectNotes ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SubjectNotes = dialog.InputText;
            }
        }

        // 电极粘贴部位按钮点击
        private void ElectrodePositionButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("������缫ճ����λ(����/����):", _viewModel.SelectedLegPosition ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedLegPosition = dialog.InputText;
            }
        }

        private void LegSideButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("��������Ȳ��(����/����):", _viewModel.SelectedLegSide ?? string.Empty);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedLegSide = dialog.InputText;
            }
        }

        private void UpperLimitButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("请输入上限基准�?(�?1.0):", _viewModel.UpperLimit.ToString());
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                if (double.TryParse(dialog.InputText, out double value))
                {
                    _viewModel.UpperLimit = value;
                }
                else
                {
                    MessageBox.Show("请输入有效的数字!", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _viewModel.Cleanup();
            base.OnClosing(e);
        }
    }
}

