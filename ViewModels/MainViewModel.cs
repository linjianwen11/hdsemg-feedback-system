using EMGFeedbackSystem.Models;
using EMGFeedbackSystem.Services;
using EMGFeedbackSystem.Utils;
using System.Windows.Media.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EMGFeedbackSystem.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private const string LeftLeg = "左腿";
        private const string RightLeg = "右腿";
        private const string HealthySide = "健侧";
        private const string AffectedSide = "患侧";
        private const int GroupSize = 21;
        private const int GroupALower = 0;
        private const int GroupBLower = 21;
        private const int GroupCLower = 42;
        private const int StableFramesRequired = 4;
        private const double MinDominantRatio = 0.34;
        private const double MinDominanceGap = 0.02;

        private readonly TcpServerService _tcpService;
        private readonly DatabaseService _dbService;
        private readonly Dispatcher _dispatcher;
        private bool _isUpdatingLegSelection;
        private ActiveGroup _activeGroup = ActiveGroup.Unknown;
        private ActiveGroup _pendingGroup = ActiveGroup.Unknown;
        private int _pendingFrames;

        public event PropertyChangedEventHandler? PropertyChanged;

        private enum ActiveGroup
        {
            Unknown = 0,
            A = 1,
            B = 2,
            C = 3
        }

        public MainViewModel()
        {
            _tcpService = new TcpServerService();
            _dbService = new DatabaseService();
            _dispatcher = Application.Current.Dispatcher;

            _tcpService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _tcpService.DataReceived += OnDataReceived;
            _tcpService.LogMessage += OnLogMessage;

            InitializeCommands();
            InitializeLegPositions();

            GenderOptions = new List<string> { "男", "女" };
            LegPositionOptions = new List<string> { LeftLeg, RightLeg };
            SelectedGender = GenderOptions.FirstOrDefault();
            SelectedLegPosition = LegPositionOptions.FirstOrDefault();
            LeftLegSide = HealthySide;
            RightLegSide = HealthySide;
            SelectedLegSide = HealthySide;
        }

        private void InitializeCommands()
        {
            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
            StartCollectionCommand = new RelayCommand(async () => await StartCollectionAsync(), () => IsConnected && !IsCollecting);
            StopCollectionCommand = new RelayCommand(async () => await StopCollectionAsync(), () => IsConnected && IsCollecting);
            SaveSubjectCommand = new RelayCommand(SaveSubject, () => !string.IsNullOrWhiteSpace(SubjectName));
            LoadSubjectCommand = new RelayCommand(LoadSubject, () => !string.IsNullOrWhiteSpace(SubjectName));
            NewSubjectCommand = new RelayCommand(ClearSubjectInfo);
            SearchSubjectCommand = new RelayCommand(SearchSubject);
        }

        private void InitializeLegPositions()
        {
            ElectrodeAColor = Brushes.Gray;
            ElectrodeBColor = Brushes.Gray;
            ElectrodeCColor = Brushes.Gray;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnConnectionStatusChanged(object? sender, bool isConnected)
        {
            _dispatcher.Invoke(() =>
            {
                IsConnected = isConnected;
                if (!isConnected)
                {
                    IsCollecting = false;
                    ResetClassificationState();
                    ClearRealtimeDisplay();
                }
            });
        }

        private void OnDataReceived(object? sender, EMGData data)
        {
            _dispatcher.Invoke(() =>
            {
                ProcessEMGData(data);
            });
        }

        private void OnLogMessage(object? sender, string message)
        {
            _dispatcher.Invoke(() =>
            {
                LogMessages = $"{DateTime.Now:HH:mm:ss} - {message}\n{LogMessages}";
            });
        }

        private void ProcessEMGData(EMGData data)
        {
            if (!IsCollecting)
            {
                return;
            }

            var electrodeData = new ElectrodeData();

            double rawA = AverageRange(data.AbsMeanValues, GroupALower, GroupSize);
            double rawB = AverageRange(data.AbsMeanValues, GroupBLower, GroupSize);
            double rawC = AverageRange(data.AbsMeanValues, GroupCLower, GroupSize);
            ActiveGroup active = ResolveActiveGroup(rawA, rawB, rawC);

            electrodeData.CurrentValueA = active == ActiveGroup.A ? rawA : 0;
            electrodeData.CurrentValueB = active == ActiveGroup.B ? rawB : 0;
            electrodeData.CurrentValueC = active == ActiveGroup.C ? rawC : 0;

            CurrentValueA = electrodeData.CurrentValueA;
            CurrentValueB = electrodeData.CurrentValueB;
            CurrentValueC = electrodeData.CurrentValueC;

            if (electrodeData.CurrentValueA > MaxValueA)
            {
                MaxValueA = electrodeData.CurrentValueA;
            }
            if (electrodeData.CurrentValueB > MaxValueB)
            {
                MaxValueB = electrodeData.CurrentValueB;
            }
            if (electrodeData.CurrentValueC > MaxValueC)
            {
                MaxValueC = electrodeData.CurrentValueC;
            }

            UpdateElectrodeColors(electrodeData);
            UpdateBarValues(electrodeData);
            UpdateHeatmaps(data, active);
        }

        private void UpdateHeatmaps(EMGData data, ActiveGroup active)
        {
            double maxLimit = UpperLimit > 0 ? UpperLimit : 1.0;
            bool showA = active == ActiveGroup.A;
            bool showB = active == ActiveGroup.B;
            bool showC = active == ActiveGroup.C;

            // A鐢垫瀬锛氶€氶亾1-21锛岀储寮?-20锛屽叡21涓€氶亾锛屾帓鍒楁垚7x3缃戞牸锛?琛?鍒楋級
            double[,,] dataA = new double[7, 3, 1];
            double sumA = 0;
            for (int i = 0; i < 21 && i < data.AbsMeanValues.Length; i++)
            {
                int row = i % 7;  // 0-6
                int col = i / 7;  // 0-2
                double value = showA ? NormalizeValue(data.AbsMeanValues[i], maxLimit) : 0;
                dataA[row, col, 0] = value;
                sumA += value;
            }
            CenterHeatmapA = Heatmap.GenerateHeatmapBitmap(dataA);
            double avgA = sumA / 21.0;
            avgA = Math.Max(0, Math.Min(1, avgA));
            ProgressBarValueA = avgA * 100;
            ProgressBarColorA = new SolidColorBrush(Heatmap.ValueToColor(avgA));
            TextBlockValueA = (avgA * maxLimit).ToString("0.##");

            // B鐢垫瀬锛氶€氶亾22-42锛岀储寮?1-41锛屽叡21涓€氶亾锛屾帓鍒楁垚7x3缃戞牸
            double[,,] dataB = new double[7, 3, 1];
            double sumB = 0;
            for (int i = 0; i < 21 && (21 + i) < data.AbsMeanValues.Length; i++)
            {
                int row = i % 7;  // 0-6
                int col = i / 7;  // 0-2
                double value = showB ? NormalizeValue(data.AbsMeanValues[21 + i], maxLimit) : 0;
                dataB[row, col, 0] = value;
                sumB += value;
            }
            CenterHeatmapB = Heatmap.GenerateHeatmapBitmap(dataB);
            double avgB = sumB / 21.0;
            avgB = Math.Max(0, Math.Min(1, avgB));
            ProgressBarValueB = avgB * 100;
            ProgressBarColorB = new SolidColorBrush(Heatmap.ValueToColor(avgB));
            TextBlockValueB = (avgB * maxLimit).ToString("0.##");

            // C鐢垫瀬锛氶€氶亾43-63锛岀储寮?2-62锛屽叡21涓€氶亾锛屾帓鍒楁垚7x3缃戞牸
            double[,,] dataC = new double[7, 3, 1];
            double sumC = 0;
            for (int i = 0; i < 21 && (42 + i) < data.AbsMeanValues.Length; i++)
            {
                int row = i % 7;  // 0-6
                int col = i / 7;  // 0-2
                double value = showC ? NormalizeValue(data.AbsMeanValues[42 + i], maxLimit) : 0;
                dataC[row, col, 0] = value;
                sumC += value;
            }
            CenterHeatmapC = Heatmap.GenerateHeatmapBitmap(dataC);
            double avgC = sumC / 21.0;
            avgC = Math.Max(0, Math.Min(1, avgC));
            ProgressBarValueC = avgC * 100;
            ProgressBarColorC = new SolidColorBrush(Heatmap.ValueToColor(avgC));
            TextBlockValueC = (avgC * maxLimit).ToString("0.##");

            // 鍚屾椂鏇存柊鍙充晶鍖哄煙鐑姏鍥撅紙浣跨敤3x7缃戞牸淇濇寔鍘熸湁甯冨眬锛?
            double[,] rightDataA = new double[3, 7];
            for (int i = 0; i < 21 && i < data.AbsMeanValues.Length; i++)
            {
                int row = i / 7;
                int col = i % 7;
                rightDataA[row, col] = showA ? NormalizeValue(data.AbsMeanValues[i], maxLimit) : 0;
            }
            HeatmapA = Heatmap.GenerateHeatmapBitmap(rightDataA);

            double[,] rightDataB = new double[3, 7];
            for (int i = 0; i < 21 && (21 + i) < data.AbsMeanValues.Length; i++)
            {
                int row = i / 7;
                int col = i % 7;
                rightDataB[row, col] = showB ? NormalizeValue(data.AbsMeanValues[21 + i], maxLimit) : 0;
            }
            HeatmapB = Heatmap.GenerateHeatmapBitmap(rightDataB);

            double[,] rightDataC = new double[3, 7];
            for (int i = 0; i < 21 && (42 + i) < data.AbsMeanValues.Length; i++)
            {
                int row = i / 7;
                int col = i % 7;
                rightDataC[row, col] = showC ? NormalizeValue(data.AbsMeanValues[42 + i], maxLimit) : 0;
            }
            HeatmapC = Heatmap.GenerateHeatmapBitmap(rightDataC);
        }

        private static double AverageRange(double[] values, int start, int count)
        {
            if (values == null || values.Length == 0 || start >= values.Length)
            {
                return 0;
            }

            int end = Math.Min(values.Length, start + count);
            double sum = 0;
            int n = 0;
            for (int i = start; i < end; i++)
            {
                double v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    continue;
                }

                if (v < 0)
                {
                    v = -v;
                }

                sum += v;
                n++;
            }

            return n > 0 ? sum / n : 0;
        }

        private static double NormalizeValue(double value, double maxLimit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            if (maxLimit <= 0)
            {
                maxLimit = 1.0;
            }

            if (value < 0)
            {
                value = -value;
            }

            double normalized = value / maxLimit;
            if (normalized < 0) return 0;
            if (normalized > 1) return 1;
            return normalized;
        }

        private ActiveGroup ResolveActiveGroup(double rawA, double rawB, double rawC)
        {
            ActiveGroup candidate = ClassifyCandidate(rawA, rawB, rawC);
            if (candidate == ActiveGroup.Unknown)
            {
                // Fallback: ensure one group is visible when signal exists.
                if (_activeGroup == ActiveGroup.Unknown)
                {
                    return StrongestGroup(rawA, rawB, rawC);
                }
                return _activeGroup;
            }

            if (_activeGroup == ActiveGroup.Unknown)
            {
                if (_pendingGroup == candidate)
                {
                    _pendingFrames++;
                }
                else
                {
                    _pendingGroup = candidate;
                    _pendingFrames = 1;
                }

                if (_pendingFrames >= StableFramesRequired)
                {
                    _activeGroup = candidate;
                    _pendingGroup = ActiveGroup.Unknown;
                    _pendingFrames = 0;
                }

                return _activeGroup;
            }

            if (candidate == _activeGroup)
            {
                _pendingGroup = ActiveGroup.Unknown;
                _pendingFrames = 0;
                return _activeGroup;
            }

            if (_pendingGroup == candidate)
            {
                _pendingFrames++;
            }
            else
            {
                _pendingGroup = candidate;
                _pendingFrames = 1;
            }

            if (_pendingFrames >= StableFramesRequired)
            {
                _activeGroup = candidate;
                _pendingGroup = ActiveGroup.Unknown;
                _pendingFrames = 0;
            }

            return _activeGroup;
        }

        private static ActiveGroup ClassifyCandidate(double a, double b, double c)
        {
            a = Math.Max(0, a);
            b = Math.Max(0, b);
            c = Math.Max(0, c);

            double total = a + b + c;
            if (total <= 1e-9)
            {
                return ActiveGroup.Unknown;
            }

            double ra = a / total;
            double rb = b / total;
            double rc = c / total;

            double best = ra;
            double second = Math.Max(rb, rc);
            ActiveGroup group = ActiveGroup.A;

            if (rb > best)
            {
                second = Math.Max(best, rc);
                best = rb;
                group = ActiveGroup.B;
            }

            if (rc > best)
            {
                second = Math.Max(best, rb);
                best = rc;
                group = ActiveGroup.C;
            }

            if (best < MinDominantRatio)
            {
                return ActiveGroup.Unknown;
            }

            if ((best - second) < MinDominanceGap)
            {
                return ActiveGroup.Unknown;
            }

            return group;
        }

        private static ActiveGroup StrongestGroup(double a, double b, double c)
        {
            a = Math.Max(0, a);
            b = Math.Max(0, b);
            c = Math.Max(0, c);

            if (a <= 1e-9 && b <= 1e-9 && c <= 1e-9)
            {
                return ActiveGroup.Unknown;
            }

            if (a >= b && a >= c) return ActiveGroup.A;
            if (b >= a && b >= c) return ActiveGroup.B;
            return ActiveGroup.C;
        }

        private void ResetClassificationState()
        {
            _activeGroup = ActiveGroup.Unknown;
            _pendingGroup = ActiveGroup.Unknown;
            _pendingFrames = 0;
        }

        private void ClearRealtimeDisplay()
        {
            CurrentValueA = 0;
            CurrentValueB = 0;
            CurrentValueC = 0;
            BarValueA = 0;
            BarValueB = 0;
            BarValueC = 0;
            ProgressBarValueA = 0;
            ProgressBarValueB = 0;
            ProgressBarValueC = 0;
            TextBlockValueA = "0";
            TextBlockValueB = "0";
            TextBlockValueC = "0";
            ElectrodeAColor = Brushes.Gray;
            ElectrodeBColor = Brushes.Gray;
            ElectrodeCColor = Brushes.Gray;
            ProgressBarColorA = Brushes.Gray;
            ProgressBarColorB = Brushes.Gray;
            ProgressBarColorC = Brushes.Gray;
            UpdateHeatmaps(new EMGData(), ActiveGroup.Unknown);
        }

        private void UpdateElectrodeColors(ElectrodeData data)
        {
            double threshold = UpperLimit * 0.1;

            ElectrodeAColor = GetColorByValue(data.CurrentValueA, threshold);
            ElectrodeBColor = GetColorByValue(data.CurrentValueB, threshold);
            ElectrodeCColor = GetColorByValue(data.CurrentValueC, threshold);
        }

        private SolidColorBrush GetColorByValue(double value, double threshold)
        {
            if (value < threshold * 0.3)
                return Brushes.Gray;
            else if (value < threshold * 0.6)
                return Brushes.Green;
            else if (value < threshold * 0.9)
                return Brushes.Yellow;
            else
                return Brushes.Red;
        }

        private void UpdateBarValues(ElectrodeData data)
        {
            double maxLimit = UpperLimit > 0 ? UpperLimit : 1.0;

            BarValueA = Math.Min(data.CurrentValueA / maxLimit, 1.0);
            BarValueB = Math.Min(data.CurrentValueB / maxLimit, 1.0);
            BarValueC = Math.Min(data.CurrentValueC / maxLimit, 1.0);
        }

        private async Task ConnectAsync()
        {
            try
            {
                await _tcpService.StartServerAsync();
                await _tcpService.SendHandshakeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StartCollectionAsync()
        {
            try
            {
                ResetClassificationState();
                ClearRealtimeDisplay();
                await _tcpService.SendStartCollectionAsync();
                IsCollecting = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopCollectionAsync()
        {
            try
            {
                await _tcpService.SendStopCollectionAsync();
                IsCollecting = false;
                ResetClassificationState();
                ClearRealtimeDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSubject()
        {
            try
            {
                var subject = new Subject
                {
                    SubjectId = string.IsNullOrWhiteSpace(SubjectId) ? _dbService.GenerateNewSubjectId() : SubjectId,
                    Name = SubjectName,
                    Gender = SelectedGender ?? string.Empty,
                    Age = int.TryParse(SubjectAge, out int age) ? age : 0,
                    Notes = SubjectNotes,
                    UpperLimit = UpperLimit,
                    LeftLegMaxA = SelectedLegPosition == LeftLeg ? MaxValueA : LeftLegMaxA,
                    LeftLegMaxB = SelectedLegPosition == LeftLeg ? MaxValueB : LeftLegMaxB,
                    LeftLegMaxC = SelectedLegPosition == LeftLeg ? MaxValueC : LeftLegMaxC,
                    RightLegMaxA = SelectedLegPosition == RightLeg ? MaxValueA : RightLegMaxA,
                    RightLegMaxB = SelectedLegPosition == RightLeg ? MaxValueB : RightLegMaxB,
                    RightLegMaxC = SelectedLegPosition == RightLeg ? MaxValueC : RightLegMaxC,
                    LeftLegSide = SelectedLegPosition == LeftLeg ? SelectedLegSide : LeftLegSide,
                    RightLegSide = SelectedLegPosition == RightLeg ? SelectedLegSide : RightLegSide
                };

                _dbService.SaveSubject(subject);
                SubjectId = subject.SubjectId;
                LeftLegMaxA = subject.LeftLegMaxA;
                LeftLegMaxB = subject.LeftLegMaxB;
                LeftLegMaxC = subject.LeftLegMaxC;
                RightLegMaxA = subject.RightLegMaxA;
                RightLegMaxB = subject.RightLegMaxB;
                RightLegMaxC = subject.RightLegMaxC;
                LeftLegSide = subject.LeftLegSide;
                RightLegSide = subject.RightLegSide;
                UpdateLegSideByPosition();
                UpdateComparisonMarkers();
                
                MessageBox.Show("操作成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSubject()
        {
            try
            {
                var subject = _dbService.GetSubjectByNameAndInfo(SubjectName, SelectedGender ?? string.Empty, 
                    int.TryParse(SubjectAge, out int age) ? age : 0);

                if (subject != null)
                {
                    SubjectId = subject.SubjectId;
                    SubjectNotes = subject.Notes;
                    UpperLimit = subject.UpperLimit;
                    LeftLegMaxA = subject.LeftLegMaxA;
                    LeftLegMaxB = subject.LeftLegMaxB;
                    LeftLegMaxC = subject.LeftLegMaxC;
                    RightLegMaxA = subject.RightLegMaxA;
                    RightLegMaxB = subject.RightLegMaxB;
                    RightLegMaxC = subject.RightLegMaxC;
                    LeftLegSide = subject.LeftLegSide;
                    RightLegSide = subject.RightLegSide;

                    UpdateMaxValuesByLegPosition();
                    UpdateLegSideByPosition();
                    
                    MessageBox.Show("操作成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var result = MessageBox.Show("未找到匹配的受试者信息，是否创建新受试者？", "提示",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        SubjectId = _dbService.GenerateNewSubjectId();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchSubject()
        {
            var searchWindow = new Views.SubjectSearchWindow(_dbService);
            if (searchWindow.ShowDialog() == true && searchWindow.SelectedSubject != null)
            {
                LoadSubjectFromSearch(searchWindow.SelectedSubject);
            }
        }

        private void LoadSubjectFromSearch(Subject subject)
        {
            SubjectId = subject.SubjectId;
            SubjectName = subject.Name;
            SelectedGender = subject.Gender;
            SubjectAge = subject.Age.ToString();
            SubjectNotes = subject.Notes;
            UpperLimit = subject.UpperLimit;
            LeftLegMaxA = subject.LeftLegMaxA;
            LeftLegMaxB = subject.LeftLegMaxB;
            LeftLegMaxC = subject.LeftLegMaxC;
            RightLegMaxA = subject.RightLegMaxA;
            RightLegMaxB = subject.RightLegMaxB;
            RightLegMaxC = subject.RightLegMaxC;
            LeftLegSide = subject.LeftLegSide;
            RightLegSide = subject.RightLegSide;

            UpdateMaxValuesByLegPosition();
            UpdateLegSideByPosition();
        }

        private void ClearSubjectInfo()
        {
            SubjectId = string.Empty;
            SubjectName = string.Empty;
            SelectedGender = GenderOptions.FirstOrDefault();
            SubjectAge = string.Empty;
            SubjectNotes = string.Empty;
            UpperLimit = 1.0;
            LeftLegMaxA = 0;
            LeftLegMaxB = 0;
            LeftLegMaxC = 0;
            RightLegMaxA = 0;
            RightLegMaxB = 0;
            RightLegMaxC = 0;
            LeftLegSide = HealthySide;
            RightLegSide = HealthySide;
            SelectedLegSide = HealthySide;
            MaxValueA = 0;
            MaxValueB = 0;
            MaxValueC = 0;
            CurrentValueA = 0;
            CurrentValueB = 0;
            CurrentValueC = 0;
        }

        private void UpdateMaxValuesByLegPosition()
        {
            if (SelectedLegPosition == LeftLeg)
            {
                MaxValueA = LeftLegMaxA;
                MaxValueB = LeftLegMaxB;
                MaxValueC = LeftLegMaxC;
            }
            else
            {
                MaxValueA = RightLegMaxA;
                MaxValueB = RightLegMaxB;
                MaxValueC = RightLegMaxC;
            }

            UpdateComparisonMarkers();
        }

        private void UpdateLegSideByPosition()
        {
            if (SelectedLegPosition == LeftLeg)
            {
                SelectedLegSide = string.IsNullOrWhiteSpace(LeftLegSide) ? HealthySide : LeftLegSide;
            }
            else
            {
                SelectedLegSide = string.IsNullOrWhiteSpace(RightLegSide) ? HealthySide : RightLegSide;
            }
        }

        private void UpdateComparisonMarkers()
        {
            double maxLimit = UpperLimit > 0 ? UpperLimit : 1.0;
            double refA;
            double refB;
            double refC;

            if (SelectedLegPosition == LeftLeg)
            {
                // Left A <-> Right C, Left B <-> Right B, Left C <-> Right A
                refA = RightLegMaxC;
                refB = RightLegMaxB;
                refC = RightLegMaxA;
            }
            else
            {
                refA = LeftLegMaxC;
                refB = LeftLegMaxB;
                refC = LeftLegMaxA;
            }

            ComparisonMarkerA = ClampToPercent(refA, maxLimit);
            ComparisonMarkerB = ClampToPercent(refB, maxLimit);
            ComparisonMarkerC = ClampToPercent(refC, maxLimit);
        }

        private static double ClampToPercent(double value, double maxLimit)
        {
            if (maxLimit <= 0)
            {
                return 0;
            }

            double percent = (value / maxLimit) * 100.0;
            if (percent < 0) return 0;
            if (percent > 100) return 100;
            return percent;
        }

        private void SyncLegSelectionFromPosition()
        {
            _isUpdatingLegSelection = true;
            bool isLeftLeg = SelectedLegPosition != RightLeg;
            _isLeftLegSelected = isLeftLeg;
            _isRightLegSelected = !isLeftLeg;
            OnPropertyChanged(nameof(IsLeftLegSelected));
            OnPropertyChanged(nameof(IsRightLegSelected));
            _isUpdatingLegSelection = false;
        }

        private static string NormalizeLegPosition(string? value)
        {
            if (string.Equals(value?.Trim(), RightLeg, StringComparison.OrdinalIgnoreCase))
            {
                return RightLeg;
            }

            return LeftLeg;
        }

        public void Cleanup()
        {
            _tcpService.StopServer();
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); }
        }

        private bool _isCollecting;
        public bool IsCollecting
        {
            get => _isCollecting;
            set { _isCollecting = value; OnPropertyChanged(); }
        }

        private string _subjectId = string.Empty;
        public string SubjectId
        {
            get => _subjectId;
            set { _subjectId = value; OnPropertyChanged(); }
        }

        private string _subjectName = string.Empty;
        public string SubjectName
        {
            get => _subjectName;
            set { _subjectName = value; OnPropertyChanged(); }
        }

        private string? _selectedGender;
        public string? SelectedGender
        {
            get => _selectedGender;
            set { _selectedGender = value; OnPropertyChanged(); }
        }

        private string _subjectAge = string.Empty;
        public string SubjectAge
        {
            get => _subjectAge;
            set { _subjectAge = value; OnPropertyChanged(); }
        }

        private string? _subjectNotes;
        public string? SubjectNotes
        {
            get => _subjectNotes;
            set { _subjectNotes = value; OnPropertyChanged(); }
        }

        private string? _selectedLegPosition;
        public string? SelectedLegPosition
        {
            get => _selectedLegPosition;
            set 
            { 
                _selectedLegPosition = NormalizeLegPosition(value);
                OnPropertyChanged();
                SyncLegSelectionFromPosition();
                UpdateMaxValuesByLegPosition();
                UpdateLegSideByPosition();
            }
        }

        private double _upperLimit = 1.0;
        private bool _upperLimitSet = false;
        public double UpperLimit
        {
            get => _upperLimit;
            set
            {
                _upperLimit = value;
                _upperLimitSet = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UpperLimitText));
                UpdateComparisonMarkers();
            }
        }

        public string UpperLimitText => _upperLimitSet ? _upperLimit.ToString("F2") : "";

        private double _currentValueA;
        public double CurrentValueA
        {
            get => _currentValueA;
            set { _currentValueA = value; OnPropertyChanged(); }
        }

        private double _currentValueB;
        public double CurrentValueB
        {
            get => _currentValueB;
            set { _currentValueB = value; OnPropertyChanged(); }
        }

        private double _currentValueC;
        public double CurrentValueC
        {
            get => _currentValueC;
            set { _currentValueC = value; OnPropertyChanged(); }
        }

        private double _maxValueA;
        public double MaxValueA
        {
            get => _maxValueA;
            set { _maxValueA = value; OnPropertyChanged(); }
        }

        private double _maxValueB;
        public double MaxValueB
        {
            get => _maxValueB;
            set { _maxValueB = value; OnPropertyChanged(); }
        }

        private double _maxValueC;
        public double MaxValueC
        {
            get => _maxValueC;
            set { _maxValueC = value; OnPropertyChanged(); }
        }

        private double _leftLegMaxA;
        public double LeftLegMaxA
        {
            get => _leftLegMaxA;
            set { _leftLegMaxA = value; OnPropertyChanged(); }
        }

        private double _leftLegMaxB;
        public double LeftLegMaxB
        {
            get => _leftLegMaxB;
            set { _leftLegMaxB = value; OnPropertyChanged(); }
        }

        private double _leftLegMaxC;
        public double LeftLegMaxC
        {
            get => _leftLegMaxC;
            set { _leftLegMaxC = value; OnPropertyChanged(); }
        }

        private double _rightLegMaxA;
        public double RightLegMaxA
        {
            get => _rightLegMaxA;
            set { _rightLegMaxA = value; OnPropertyChanged(); }
        }

        private double _rightLegMaxB;
        public double RightLegMaxB
        {
            get => _rightLegMaxB;
            set { _rightLegMaxB = value; OnPropertyChanged(); }
        }

        private double _rightLegMaxC;
        public double RightLegMaxC
        {
            get => _rightLegMaxC;
            set { _rightLegMaxC = value; OnPropertyChanged(); }
        }

        private string _leftLegSide = string.Empty;
        public string LeftLegSide
        {
            get => _leftLegSide;
            set { _leftLegSide = value; OnPropertyChanged(); }
        }

        private string _rightLegSide = string.Empty;
        public string RightLegSide
        {
            get => _rightLegSide;
            set { _rightLegSide = value; OnPropertyChanged(); }
        }

        private string _selectedLegSide = string.Empty;
        public string SelectedLegSide
        {
            get => _selectedLegSide;
            set
            {
                _selectedLegSide = NormalizeLegSide(value);
                OnPropertyChanged();

                if (SelectedLegPosition == LeftLeg)
                {
                    LeftLegSide = _selectedLegSide;
                }
                else
                {
                    RightLegSide = _selectedLegSide;
                }
            }
        }

        private static string NormalizeLegSide(string? value)
        {
            if (string.Equals(value?.Trim(), AffectedSide, StringComparison.OrdinalIgnoreCase))
            {
                return AffectedSide;
            }

            return HealthySide;
        }

        private double _barValueA;
        public double BarValueA
        {
            get => _barValueA;
            set { _barValueA = value; OnPropertyChanged(); }
        }

        private double _barValueB;
        public double BarValueB
        {
            get => _barValueB;
            set { _barValueB = value; OnPropertyChanged(); }
        }

        private double _barValueC;
        public double BarValueC
        {
            get => _barValueC;
            set { _barValueC = value; OnPropertyChanged(); }
        }

        private BitmapSource? _heatmapA;
        public BitmapSource? HeatmapA
        {
            get => _heatmapA;
            set { _heatmapA = value; OnPropertyChanged(); }
        }

        private BitmapSource? _heatmapB;
        public BitmapSource? HeatmapB
        {
            get => _heatmapB;
            set { _heatmapB = value; OnPropertyChanged(); }
        }

        private BitmapSource? _heatmapC;
        public BitmapSource? HeatmapC
        {
            get => _heatmapC;
            set { _heatmapC = value; OnPropertyChanged(); }
        }

        // 涓棿鍖哄煙鐑姏鍥惧睘鎬?
        private BitmapSource? _centerHeatmapA;
        public BitmapSource? CenterHeatmapA
        {
            get => _centerHeatmapA;
            set { _centerHeatmapA = value; OnPropertyChanged(); }
        }

        private BitmapSource? _centerHeatmapB;
        public BitmapSource? CenterHeatmapB
        {
            get => _centerHeatmapB;
            set { _centerHeatmapB = value; OnPropertyChanged(); }
        }

        private BitmapSource? _centerHeatmapC;
        public BitmapSource? CenterHeatmapC
        {
            get => _centerHeatmapC;
            set { _centerHeatmapC = value; OnPropertyChanged(); }
        }

        // 宸﹀彸鑵块€夋嫨灞炴€?
        private bool _isLeftLegSelected = true;
        public bool IsLeftLegSelected
        {
            get => _isLeftLegSelected;
            set
            {
                if (_isUpdatingLegSelection || _isLeftLegSelected == value)
                {
                    return;
                }

                _isLeftLegSelected = value;
                if (value)
                {
                    _isRightLegSelected = false;
                    _selectedLegPosition = LeftLeg;
                    OnPropertyChanged(nameof(SelectedLegPosition));
                    UpdateMaxValuesByLegPosition();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRightLegSelected));
            }
        }

        private bool _isRightLegSelected = false;
        public bool IsRightLegSelected
        {
            get => _isRightLegSelected;
            set
            {
                if (_isUpdatingLegSelection || _isRightLegSelected == value)
                {
                    return;
                }

                _isRightLegSelected = value;
                if (value)
                {
                    _isLeftLegSelected = false;
                    _selectedLegPosition = RightLeg;
                    OnPropertyChanged(nameof(SelectedLegPosition));
                    UpdateMaxValuesByLegPosition();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLeftLegSelected));
            }
        }

        // 杩涘害鏉″拰鏁板€兼樉绀哄睘鎬?
        private double _progressBarValueA;
        public double ProgressBarValueA
        {
            get => _progressBarValueA;
            set { _progressBarValueA = value; OnPropertyChanged(); }
        }

        private double _progressBarValueB;
        public double ProgressBarValueB
        {
            get => _progressBarValueB;
            set { _progressBarValueB = value; OnPropertyChanged(); }
        }

        private double _progressBarValueC;
        public double ProgressBarValueC
        {
            get => _progressBarValueC;
            set { _progressBarValueC = value; OnPropertyChanged(); }
        }

        private double _comparisonMarkerA;
        public double ComparisonMarkerA
        {
            get => _comparisonMarkerA;
            set { _comparisonMarkerA = value; OnPropertyChanged(); }
        }

        private double _comparisonMarkerB;
        public double ComparisonMarkerB
        {
            get => _comparisonMarkerB;
            set { _comparisonMarkerB = value; OnPropertyChanged(); }
        }

        private double _comparisonMarkerC;
        public double ComparisonMarkerC
        {
            get => _comparisonMarkerC;
            set { _comparisonMarkerC = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _progressBarColorA = Brushes.Gray;
        public SolidColorBrush ProgressBarColorA
        {
            get => _progressBarColorA;
            set { _progressBarColorA = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _progressBarColorB = Brushes.Gray;
        public SolidColorBrush ProgressBarColorB
        {
            get => _progressBarColorB;
            set { _progressBarColorB = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _progressBarColorC = Brushes.Gray;
        public SolidColorBrush ProgressBarColorC
        {
            get => _progressBarColorC;
            set { _progressBarColorC = value; OnPropertyChanged(); }
        }

        private string _textBlockValueA = "0.00";
        public string TextBlockValueA
        {
            get => _textBlockValueA;
            set { _textBlockValueA = value; OnPropertyChanged(); }
        }

        private string _textBlockValueB = "0.00";
        public string TextBlockValueB
        {
            get => _textBlockValueB;
            set { _textBlockValueB = value; OnPropertyChanged(); }
        }

        private string _textBlockValueC = "0.00";
        public string TextBlockValueC
        {
            get => _textBlockValueC;
            set { _textBlockValueC = value; OnPropertyChanged(); }
        }

        // 鐑姏鍥惧彉閲忛€夋嫨锛?=RMS, 1=宄板嘲鍊? 2=MAV锛?
        private int _heatMapVariable = 0;
        public int HeatMapVariable
        {
            get => _heatMapVariable;
            set { _heatMapVariable = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _electrodeAColor = Brushes.Gray;
        public SolidColorBrush ElectrodeAColor
        {
            get => _electrodeAColor;
            set { _electrodeAColor = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _electrodeBColor = Brushes.Gray;
        public SolidColorBrush ElectrodeBColor
        {
            get => _electrodeBColor;
            set { _electrodeBColor = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _electrodeCColor = Brushes.Gray;
        public SolidColorBrush ElectrodeCColor
        {
            get => _electrodeCColor;
            set { _electrodeCColor = value; OnPropertyChanged(); }
        }

        private string _logMessages = string.Empty;
        public string LogMessages
        {
            get => _logMessages;
            set { _logMessages = value; OnPropertyChanged(); }
        }

        public List<string> GenderOptions { get; }
        public List<string> LegPositionOptions { get; }

        public ICommand ConnectCommand { get; private set; } = null!;
        public ICommand StartCollectionCommand { get; private set; } = null!;
        public ICommand StopCollectionCommand { get; private set; } = null!;
        public ICommand SaveSubjectCommand { get; private set; } = null!;
        public ICommand LoadSubjectCommand { get; private set; } = null!;
        public ICommand NewSubjectCommand { get; private set; } = null!;
        public ICommand SearchSubjectCommand { get; private set; } = null!;
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();
    }
}


