using Microsoft.Win32;
using PDFiumSharp;
using PDFiumSharp.Enums;
using PDFiumSharp.Types;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

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
            MessageBoxResult result = MessageBox.Show("프로그램을 종료하시겠습니까?", "프로그램 종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                        string referenceFileName = parts[2].Trim();
                        Rules.Add(new Tuple<string, string, string>(fileNamePattern, samePages, referenceFileName));
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
                string referenceFileName = Path.GetFileName(referenceFullName);
                File.Copy(referenceFullName, Path.Combine(ReferenceFolder, referenceFileName), true);
                Rules.Add(new Tuple<string, string, string>(fileNamePattern, samePagesInput, referenceFileName));
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
        private async void WriteLog(string message)
        {
            await Task.Run(() =>
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CareLink.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logFilePath, logEntry);
                Dispatcher.Invoke(() =>
                {
                    UxLogPane.AppendText(logEntry);
                });
            });
        }

        // 작업 실행 -------------------------------------------------------------------------------------------------------------------------------------------
        private void UxRunTask_Click(object sender, RoutedEventArgs e)
        {
            CollectSourceFullNames();
            WriteLog($"총 {fileFullNames.Count}개 파일을 찾았습니다.");
            foreach (KeyValuePair<string, string> fileFullName in fileFullNames)
            {
                foreach (Tuple<string, string, string> rule in Rules)
                {
                    string fileNamePattern = rule.Item1;
                    string samePagesInput = rule.Item2;
                    string referenceFileName = rule.Item3;

                    // 파일 이름이 패턴과 일치하는지 확인
                    if (Path.GetFileName(fileFullName.Key).Contains(fileNamePattern))
                    {
                        string referenceFullName = Path.Combine(ReferenceFolder, referenceFileName);
                        List<int> samePages = [];
                        foreach (string samePageInput in samePagesInput.Split(','))
                        {
                            if (int.TryParse(samePageInput, out int samePageNumber))
                            {
                                samePages.Add(samePageNumber - 1); // 0부터 시작하는 인덱스로 변환
                            }
                        }
                        bool isModified = false;
                        foreach (int pageIndex in samePages)
                        {
                            try
                            {
                                bool arePagesEqual = ArePagesEqual(fileFullName.Key, pageIndex, referenceFullName, pageIndex, tolerance: 0.01f, dpi: 150);
                                Debug.WriteLine($"비교 중: {Path.GetFileName(fileFullName.Key)} 페이지 {pageIndex + 1} vs {referenceFileName} 페이지 {pageIndex + 1} => 동일: {arePagesEqual}");
                                if (!arePagesEqual)
                                {
                                    isModified = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"오류 발생: {ex.Message}");
                                isModified = false;
                                break;
                            }
                        }
                        if (isModified)
                        {
                            WriteLog($"{Path.GetFileName(fileFullName.Key)}...[X]");
                        }
                        else
                        {
                            using (var doc = new PdfDocument(fileFullName.Key))
                            {
                                // 동일 위치에 새 빈 페이지 생성
                                // (기존 페이지와 동일한 크기로 생성하고 싶으면 인접 페이지 참고)
                                double width = 595;  // A4 기본 (points 단위)
                                double height = 842;
                                //if (pageCount > 0)
                                //{
                                //    width = doc.Pages[Math.Min(targetPageIndex, pageCount - 1)].Width;
                                //    height = doc.Pages[Math.Min(targetPageIndex, pageCount - 1)].Height;
                                //}
                                foreach (int pageIndex in samePages)
                                {
                                    // 기존 페이지 제거
                                    doc.Pages.RemoveAt(pageIndex);  // 내부적으로 FPDFPage_Delete 호출
                                    doc.Pages.Insert(pageIndex, width, height);
                                }
                                // 결과 저장
                                doc.Pages.Insert(0, width, height);
                                doc.Pages.Insert(0, width, height);
                                doc.Save(fileFullName.Value);
                            }
                            File.Delete(fileFullName.Key);
                            WriteLog($"{Path.GetFileName(fileFullName.Key)}...[O]");
                        }
                        break; // 첫 번째 일치하는 규칙만 적용
                    }
                }
            }
        }

        // 두 PDF 페이지 비교 -----------------------------------------------------------------------------------------------------------------------------------
        public static bool ArePagesEqual(string pdfPath1, int pageIndex1, string pdfPath2, int pageIndex2, float tolerance = 0f, int dpi = 72)
        {
            using var doc1 = new PdfDocument(pdfPath1);
            using var doc2 = new PdfDocument(pdfPath2);

            var page1 = doc1.Pages[pageIndex1];
            var page2 = doc2.Pages[pageIndex2];

            if (page1.Width != page2.Width || page1.Height != page2.Height)
                return false;

            int width = (int)(page1.Width * dpi / 72f);
            int height = (int)(page1.Height * dpi / 72f);

            using var bitmap1 = new PDFiumBitmap(width, height, hasAlpha: true);
            using var bitmap2 = new PDFiumBitmap(width, height, hasAlpha: true);

            page1.Render(bitmap1, PageOrientations.Normal, RenderingFlags.LcdText);
            page2.Render(bitmap2, PageOrientations.Normal, RenderingFlags.LcdText);

            return CompareBitmaps(bitmap1, bitmap2, tolerance);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool CompareBitmaps(PDFiumBitmap bmp1, PDFiumBitmap bmp2, float tolerance)
        {
            if (bmp1.Width != bmp2.Width || bmp1.Height != bmp2.Height)
                return false;

            unsafe
            {
                byte* ptr1 = (byte*)bmp1.Scan0.ToPointer();
                byte* ptr2 = (byte*)bmp2.Scan0.ToPointer();
                int stride1 = bmp1.Stride;
                int stride2 = bmp2.Stride;
                int width = bmp1.Width;
                int height = bmp1.Height;
                int bpp = 4; // BGRA

                long totalDiff = 0;
                long totalPixels = (long)width * height;

                for (int y = 0; y < height; y++)
                {
                    byte* row1 = ptr1 + y * stride1;
                    byte* row2 = ptr2 + y * stride2;

                    for (int x = 0; x < width; x++)
                    {
                        byte b1 = row1[0], g1 = row1[1], r1 = row1[2], a1 = row1[3];
                        byte b2 = row2[0], g2 = row2[1], r2 = row2[2], a2 = row2[3];

                        totalDiff += Math.Abs(b1 - b2) + Math.Abs(g1 - g2) + Math.Abs(r1 - r2) + Math.Abs(a1 - a2);

                        row1 += bpp;
                        row2 += bpp;
                    }
                }

                double avgDiff = totalDiff / (double)(totalPixels * 4 * 255); // 0~1 정규화
                return avgDiff <= tolerance;
            }
        }
    }
}