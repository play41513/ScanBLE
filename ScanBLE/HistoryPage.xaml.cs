using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScanBLE;

// 用來綁定列表的資料模型
public class HistoryLogItem : INotifyPropertyChanged
{
    public string? RawFileName { get; set; }
    public string? DisplayTime { get; set; }
    public string? DisplayId { get; set; }
    public string? Status { get; set; }
    public Color? StatusColor { get; set; }

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }

    private string _csvContent = "點擊載入詳細資料...";
    public string CsvContent { get => _csvContent; set { _csvContent = value; OnPropertyChanged(); } }

    private bool _isLoadingContent;
    public bool IsLoadingContent { get => _isLoadingContent; set { _isLoadingContent = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public partial class HistoryPage : ContentPage
{
    private List<HistoryLogItem> _allLogs = [];
    public ObservableCollection<HistoryLogItem> DisplayedLogs { get; set; } = [];

    public HistoryPage()
    {
        InitializeComponent();
        HistoryListView.ItemsSource = DisplayedLogs;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 進入畫面時自動載入當天資料
        _ = LoadLogsFromNas(LogDatePicker.Date);
    }

    private async void OnDateSelected(object sender, DateChangedEventArgs e)
    {
        await LoadLogsFromNas(e.NewDate);
    }

    private async Task LoadLogsFromNas(DateTime targetDate)
    {
        LoadingSpinner.IsVisible = true;
        LoadingSpinner.IsRunning = true;

        // 在背景執行緒去跟 NAS 要檔名清單，完全不卡 UI
        var files = await NasHelper.GetLogFileListAsync(targetDate);

        // 建立一個「暫存」的 List，把字串切割等運算都在這裡做完
        var parsedItems = new List<HistoryLogItem>();

        foreach (var file in files)
        {
            try
            {
                string nameWithoutExt = file.Replace(".csv", "");
                string[] parts = nameWithoutExt.Split('_');
                if (parts.Length >= 3)
                {
                    string timeStr = parts[0];
                    string formattedTime = timeStr.Length == 6 ? $"{timeStr[..2]}:{timeStr.Substring(2, 2)}:{timeStr.Substring(4, 2)}" : timeStr;
                    string idStr = parts[1];
                    string formattedId = idStr.Length == 6 ? $"{idStr[..2]}:{idStr.Substring(2, 2)}:{idStr.Substring(4, 2)}" : idStr;
                    string status = parts[2];

                    parsedItems.Add(new HistoryLogItem
                    {
                        RawFileName = file,
                        DisplayTime = formattedTime,
                        DisplayId = formattedId,
                        Status = status,
                        StatusColor = status == "PASS" ? Colors.Green : Colors.Red
                    });
                }
            }
            catch { /* 忽略解析錯誤的檔案 */ }
        }

        // 在背景將 2000 筆資料排好序
        var sortedList = parsedItems.OrderByDescending(x => x.DisplayTime).ToList();

        // 將完整的排序資料存入全域備份
        _allLogs = sortedList;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // 直接產生一個全新的 ObservableCollection，這樣畫面只會重新渲染「1次」
            DisplayedLogs = new ObservableCollection<HistoryLogItem>(_allLogs);
            HistoryListView.ItemsSource = DisplayedLogs; // 重新綁定來源

            LoadingSpinner.IsVisible = false;
            LoadingSpinner.IsRunning = false;

            // 如果輸入框有文字，順便套用搜尋過濾
            FilterLogs();
        });
    }

    private void OnSearchClicked(object sender, EventArgs e)
    {
        FilterLogs();
    }

    private void FilterLogs()
    {
        string query = SearchEntry.Text?.Trim().ToUpper() ?? "";

        if (query.Length > 6)
        {
            query = query[^6..];
            SearchEntry.Text = query;
        }

        // 同樣使用暫存 List 來準備過濾後的結果
        var filteredList = new List<HistoryLogItem>();

        foreach (var log in _allLogs)
        {
            string rawId = log.DisplayId?.Replace(":", "") ?? "";

            if (string.IsNullOrEmpty(query) || rawId.Contains(query))
            {
                filteredList.Add(log);
            }
        }

        // 一次性把過濾完的結果刷新到畫面上
        DisplayedLogs = new ObservableCollection<HistoryLogItem>(filteredList);
        HistoryListView.ItemsSource = DisplayedLogs;
    }

    private async void OnLogItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is HistoryLogItem tappedLog)
        {
            tappedLog.IsExpanded = !tappedLog.IsExpanded;

            // 當展開且還沒載入過內容時，才去 NAS 讀檔 
            if (tappedLog.IsExpanded && tappedLog.CsvContent == "點擊載入詳細資料...")
            {
                tappedLog.IsLoadingContent = true;
                tappedLog.CsvContent = ""; // 清空提示文字

                string content = await NasHelper.ReadLogContentAsync(LogDatePicker.Date, tappedLog.RawFileName);

                tappedLog.CsvContent = string.IsNullOrEmpty(content) ? "無法讀取檔案內容或檔案為空。" : content;
                tappedLog.IsLoadingContent = false;
            }
        }
    }
}