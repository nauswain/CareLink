using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace CareLink
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 시간 스탬프 추출용 정규식
        private static readonly Regex regexTimeStamp = new(@"\[(?<time>.*?)\]\s(?<msg>.*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // 페이지 입력 정규식
        private static readonly Regex regexPageInput = new(@"^[,\s\d]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // 공백 정규식
        private static readonly Regex regexSpaces = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // 중복 쉼표 정규식
        private static readonly Regex regexCommas = new(@",{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // 원본 폴더 경로
        public string SourceFolder
        {
            get => sourceFolder;
            set
            {
                if (sourceFolder != value)
                {
                    sourceFolder = value;
                    Directory.CreateDirectory(sourceFolder);
                    OnPropertyChanged(nameof(SourceFolder));
                }
            }
        }
        private string sourceFolder = string.Empty;

        // 작업된 폴더 경로
        public string ModifiedFolder
        {
            get => modifiedFolder;
            set
            {
                if (modifiedFolder != value)
                {
                    modifiedFolder = value;
                    Directory.CreateDirectory(modifiedFolder);
                    OnPropertyChanged(nameof(ModifiedFolder));
                }
            }
        }
        private string modifiedFolder = string.Empty;

        // 기준 폴더 경로
        public string ReferenceFolder
        {
            get => referenceFolder;
            set
            {
                if (referenceFolder != value)
                {
                    referenceFolder = value;
                    Directory.CreateDirectory(referenceFolder);
                    OnPropertyChanged(nameof(ReferenceFolder));
                }
            }
        }
        private string referenceFolder = string.Empty;

        // 로그 시작일
        public DateTime StartDate
        {
            get => startDate;
            set
            {
                if (startDate != value)
                {
                    startDate = value;
                    OnPropertyChanged(nameof(StartDate));
                }
            }
        }
        private DateTime startDate = DateTime.Now.Date;

        // 로그 종료일
        public DateTime EndDate
        {
            get => endDate;
            set
            {
                if (endDate != value)
                {
                    endDate = value;
                    OnPropertyChanged(nameof(EndDate));
                }
            }
        }
        private DateTime endDate = DateTime.Now.Date;

        // 파일 구별 이름, 같은 페이지, 기준 파일
        public ObservableCollection<Tuple<string, string, string>> Rules
        {
            get => rules;
            set
            {
                if (rules != value)
                {
                    rules = value;
                    OnPropertyChanged(nameof(Rules));
                }
            }
        }
        private ObservableCollection<Tuple<string, string, string>> rules = [];

        // 수정된 파일 수
        public int ModifiedFileCount
        {
            get => modifiedFileCount;
            set
            {
                if (modifiedFileCount != value)
                {
                    modifiedFileCount = value;
                    OnPropertyChanged(nameof(ModifiedFileCount));
                }
            }
        }
        private int modifiedFileCount = 0;

        // 원본 파일명 -> 출력 파일명
        private Dictionary<string, string> fileFullNames = [];

        // 메인 윈도우 생성자 -----------------------------------------------------------------------------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();
            ReadRules();
            SourceFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Source");
            ModifiedFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Modified");
            ReferenceFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reference");
        }

        // 프로그램 종료 확인 -----------------------------------------------------------------------------------------------------------------------------------
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "프로그램을 종료하시겠습니까?",
                "프로그램 종료 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
        }

        // 원본 폴더에서 모든 PDF 파일의 전체 경로 수집 ------------------------------------------------------------------------------------------------------------
        private void CollectSourceFullNames()
        {
            fileFullNames.Clear();
            try
            {
                foreach (string sourceFullName in Directory.EnumerateFiles(sourceFolder, "*.pdf", SearchOption.AllDirectories))
                {
                    string modifiedFullName = Path.Combine(modifiedFolder, Path.GetFileName(sourceFullName));
                    fileFullNames[sourceFullName] = modifiedFullName;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("권한이 없는 폴더를 건너뜁니다.\n" + ex.Message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("오류 발생: " + ex.Message);
            }
        }

        // 원본 폴더 열기 ---------------------------------------------------------------------------------------------------------------------------------------
        private void UxSourceFolderOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog folderDialog = new() 
            {
                Title = "원본 폴더 선택"
            };
            if (folderDialog.ShowDialog() == true)
            {
                SourceFolder = folderDialog.FolderName;
            }
        }

        // 수정된 폴더 열기 -------------------------------------------------------------------------------------------------------------------------------------
        private void UxModifiedFolderOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog folderDialog = new()
            {
                Title = "수정된 폴더 선택"
            };
            if (folderDialog.ShowDialog() == true)
            {
                ModifiedFolder = folderDialog.FolderName;
            }
        }

        // 규칙 파일 읽기 ---------------------------------------------------------------------------------------------------------------------------------------
        private void ReadRules()
        {
            string rulesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CareLink.rules");
            if (File.Exists(rulesFilePath))
            {
                Rules.Clear();
                foreach (string line in File.ReadLines(rulesFilePath))
                {
                    string[] parts = line.Split(["->"], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3)
                    {
                        string fileNamePattern = parts[0].Trim();
                        string samePages = parts[1].Trim();
                        string referenceFullName = parts[2].Trim();
                        Rules.Add(new Tuple<string, string, string>(fileNamePattern, samePages, referenceFullName));
                    }
                }
            }
            else
            {
                WriteLog("규칙 파일을 찾지 못했습니다.");
                File.Create(rulesFilePath).Close();
                WriteLog("새규칙 파일을 생성하였습니다.");
            }
        }

        // 규칙 파일 쓰기 ---------------------------------------------------------------------------------------------------------------------------------------
        private void WriteRules()
        {
            string rulesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CareLink.rules");
            using StreamWriter writer = new(rulesFilePath);
            foreach (var rule in Rules)
            {
                // rule: (fileNamePattern, samePages, referenceFullName)
                writer.WriteLine($"{rule.Item1} -> {rule.Item2} -> {rule.Item3}");
            }
        }

        // 규칙 기준 파일 열기 ----------------------------------------------------------------------------------------------------------------------------------
        private void UxReferenceFileOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                Title = "기준 파일 선택"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                UxReferenceFullName.Text = openFileDialog.FileName;
            }
        }

        // 규칙 추가 -------------------------------------------------------------------------------------------------------------------------------------------
        private void UxAddRule_Click(object sender, RoutedEventArgs e)
        {
            string samePagesInput = UxSamePages.Text;
            string fileNamePattern = UxFileNamePattern.Text;
            string referenceFullName = UxReferenceFullName.Text;
            if (string.IsNullOrWhiteSpace(fileNamePattern))
            {
                MessageBox.Show("파일 이름 패턴을 입력하세요.");
            }
            else if (string.IsNullOrWhiteSpace(referenceFullName) || !File.Exists(referenceFullName))
            {
                MessageBox.Show("유효한 기준 파일을 선택하세요.");
            }
            else if (regexPageInput.IsMatch(samePagesInput))
            {
                samePagesInput = regexSpaces.Replace(samePagesInput, "");     // 공백 제거
                samePagesInput = regexCommas.Replace(samePagesInput, ",");    // 중복 쉼표 단일화
                List<int> samePages = [];
                foreach (string samePageInput in samePagesInput.Split(','))
                {
                    if (int.TryParse(samePageInput, out int samePageNumber))
                    {
                        if (!samePages.Contains(samePageNumber))
                        {
                            samePages.Add(samePageNumber);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"잘못된 페이지 번호: {samePageInput}");
                        return;
                    }
                }
                samePages.Sort();
                samePagesInput = string.Join(",", samePages);
                Rules.Add(new Tuple<string, string, string>(fileNamePattern, samePagesInput, referenceFullName));
                UxSamePages.Clear();
                UxFileNamePattern.Clear();
                UxReferenceFullName.Clear();
                WriteRules();
                WriteLog($"규칙 '{fileNamePattern}' 이 추가되었습니다.");
            }
            else
            {
                MessageBox.Show("같아야 하는 페이지는 숫자, 쉼표, 공백만 포함할 수 있습니다.");
            }
        }

        // 규칙 삭제 -------------------------------------------------------------------------------------------------------------------------------------------
        private void _DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Tuple<string, string, string> rule)
            {
                MessageBoxResult result = MessageBox.Show($"'{rule.Item1}' 규칙을 삭제 하시겠습니까?", "규칙 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    Rules.Remove(rule);
                    // 파일에서 해당 줄 삭제
                    WriteRules();
                    WriteLog($"'{rule.Item1}' 규칙이 삭제 되었습니다.");
                }
            }
        }

        // 로그 종료일 선택 변경 ---------------------------------------------------------------------------------------------------------------------------------
        private void UxEndDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UxEndDate.SelectedDate.HasValue && UxStartDate.SelectedDate.HasValue)
            {
                if (UxEndDate.SelectedDate.Value < UxStartDate.SelectedDate.Value)
                {
                    MessageBox.Show("종료일은 시작일보다 크거나 같아야 합니다.", "유효하지 않은 종료일");
                    UxEndDate.SelectedDate = UxStartDate.SelectedDate; // EndDate를 StartDate로 설정
                }
            }
        }

        // 로그 시작일 선택 변경 ---------------------------------------------------------------------------------------------------------------------------------
        private void UxStartDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UxStartDate.SelectedDate.HasValue && UxEndDate != null && UxEndDate.SelectedDate.HasValue)
            {
                if (UxEndDate.SelectedDate.Value < UxStartDate.SelectedDate.Value)
                {
                    MessageBox.Show("시작일은 종료일보다 작거나 같아야 합니다.", "유효하지 않은 시작일");
                    UxStartDate.SelectedDate = UxEndDate.SelectedDate; // StartDate를 EndDate로 설정
                }
            }
        }

        // 로그 이번 달 선택 ------------------------------------------------------------------------------------------------------------------------------------
        private void UxThisMonth_Click(object sender, RoutedEventArgs e)
        {
            StartDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            EndDate = StartDate.AddMonths(1).AddDays(-1);
            ReadLog();
        }

        // 로그 오늘 선택 ---------------------------------------------------------------------------------------------------------------------------------------
        private void UxToday_Click(object sender, RoutedEventArgs e)
        {
            StartDate = DateTime.Now.Date;
            EndDate = DateTime.Now.Date;
            ReadLog();
        }

        // 로그 보기 -------------------------------------------------------------------------------------------------------------------------------------------
        private void UxShowLog_Click(object sender, RoutedEventArgs e)
        {
            ReadLog();
        }

        // 로그 파일 읽기 ---------------------------------------------------------------------------------------------------------------------------------------
        private async void ReadLog()
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CareLink.log");
            DateTime startDateTime = startDate.Date;
            DateTime endDateTime = endDate.Date.AddDays(1).AddTicks(-1);
            ModifiedFileCount = 0;

            if (File.Exists(logFilePath))
            {
                await Task.Run(() =>
                {
                    StringBuilder logBuilder = new();
                    HashSet<string> modifiedFiles = [];
                    foreach (string line in File.ReadLines(logFilePath))
                    {
                        Match match = regexTimeStamp.Match(line);
                        if (match.Success)
                        {
                            if (DateTime.TryParse(match.Groups["time"].Value, out DateTime logTime))
                            {
                                if (logTime >= startDateTime && logTime <= endDateTime)
                                {
                                    logBuilder.AppendLine(line);
                                    if (line.Contains("...[O]"))
                                    {
                                        string[] parts = line.Split(["] ", "...[O]"], StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length > 0)
                                        {
                                            string fileName = Path.GetFileName(parts[1].Trim());
                                            if (modifiedFiles.Add(fileName))
                                            {
                                                ModifiedFileCount++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    Dispatcher.Invoke(() =>
                    {
                        UxLogPane.Text = logBuilder.ToString();
                        UxLogPane.ScrollToEnd();
                    });
                });
            }
            else
            {
                UxLogPane.Text = "로그 파일을 찾을 수 없습니다.";
            }
        }

        // 로그 파일 쓰기 ---------------------------------------------------------------------------------------------------------------------------------------
        private void WriteLog(string message)
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CareLink.log");
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(logFilePath, logEntry);
            UxLogPane.AppendText(logEntry);
        }

        // 작업 실행 -------------------------------------------------------------------------------------------------------------------------------------------
        private void UxRunTask_Click(object sender, RoutedEventArgs e)
        {
            CollectSourceFullNames();
            WriteLog($"총 {fileFullNames.Count}개 파일을 찾았습니다.");
        }
    }
}