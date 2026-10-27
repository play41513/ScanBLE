using SMBLibrary;
using SMBLibrary.Client;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ScanBLE;

public static class NasHelper
{
    private const string NasIp = "192.168.14.26";
    private const string ShareName = "swtool";
    private const string Username = "user1234";
    private const string Password = "user1234";
    public static void SaveLogToLocalCache(NasLogModel log)
    {
        var logs = GetPendingLogs();
        // 避免重複加入同一個 LogId
        if (!logs.Any(x => x.LogId == log.LogId))
        {
            logs.Add(log);
            string json = JsonSerializer.Serialize(logs);
            Preferences.Default.Set("PendingNasLogs", json);
        }
    }

    public static List<NasLogModel> GetPendingLogs()
    {
        string json = Preferences.Default.Get("PendingNasLogs", string.Empty);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<NasLogModel>>(json) ?? []; } catch { return []; }
    }

    public static void RemoveLogFromLocalCache(string? logId)
    {
        var logs = GetPendingLogs();
        var item = logs.FirstOrDefault(x => x.LogId == logId);
        if (item != null)
        {
            logs.Remove(item);
            Preferences.Default.Set("PendingNasLogs", JsonSerializer.Serialize(logs));
        }
    }

    // --- NAS 寫入邏輯 ---
    public static async Task UploadPendingLogsAsync()
    {
        var pendingLogs = GetPendingLogs();
        if (pendingLogs.Count == 0) return;

        // 避免在主執行緒卡死 UI，放入背景執行
        await Task.Run(() =>
        {
            try
            {
                SMB2Client client = new();
                bool isConnected = client.Connect(IPAddress.Parse(NasIp), SMBTransportType.DirectTCPTransport);
                if (!isConnected) return;

                NTStatus status = client.Login(String.Empty, Username, Password);
                if (status != NTStatus.STATUS_SUCCESS) return;

                ISMBFileStore fileStore = client.TreeConnect(ShareName, out status);
                if (status != NTStatus.STATUS_SUCCESS) return;

                foreach (var log in pendingLogs.ToList()) // 使用 ToList() 複製一份來跑迴圈
                {
                    bool success = WriteFileToNas(fileStore, log);
                    if (success)
                    {
                        // 寫入 NAS 成功，從手機快取中刪除
                        RemoveLogFromLocalCache(log.LogId);
                    }
                }

                client.Logoff();
                client.Disconnect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NAS 連線異常: {ex.Message}");
            }
        });
    }

    private static bool WriteFileToNas(ISMBFileStore fileStore, NasLogModel log)
    {
        string dateStr = log.PassTime.ToString("yyyy-MM-dd");
        string timeStr = log.PassTime.ToString("HHmmss");

        string dirPath = $@"logs\OE-TR02\{dateStr}";
        string safeDeviceId = log.DeviceId?.Replace(":", "") ?? "Unknown";
        string fileName = $"{timeStr}_{safeDeviceId}_{log.Status}.csv";
        string filePath = $@"{dirPath}\{fileName}";

        // 1. 確保日期資料夾存在 (FILE_OPEN_IF)
        NTStatus status = fileStore.CreateFile(out object dirHandle, out _, dirPath,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, SMBLibrary.FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN_IF,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT, null);

        if (status == NTStatus.STATUS_SUCCESS)
            fileStore.CloseFile(dirHandle);

        // 2. 建立或覆寫 CSV 檔案
        status = fileStore.CreateFile(out object fileHandle, out _, filePath,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, SMBLibrary.FileAttributes.Normal,
            ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT, null);

        if (status == NTStatus.STATUS_SUCCESS)
        {
            byte[] data = Encoding.UTF8.GetBytes(log.CsvContent ?? string.Empty);
            fileStore.WriteFile(out _, fileHandle, 0, data);

            fileStore.CloseFile(fileHandle);
            return true;
        }
        return false;
    }
    // 從 NAS 讀取檔案列表
    public static async Task<List<string>> GetLogFileListAsync(DateTime date)
    {
        List<string> fileNames = [];
        string dateStr = date.ToString("yyyy-MM-dd");
        string dirPath = $@"logs\OE-TR02\{dateStr}";

        await Task.Run(() =>
        {
            try
            {
                SMB2Client client = new();
                if (client.Connect(IPAddress.Parse(NasIp), SMBTransportType.DirectTCPTransport))
                {
                    if (client.Login(String.Empty, Username, Password) == NTStatus.STATUS_SUCCESS)
                    {
                        ISMBFileStore fileStore = client.TreeConnect(ShareName, out NTStatus status);
                        if (status == NTStatus.STATUS_SUCCESS)
                        {

                            // 開啟資料夾
                            status = fileStore.CreateFile(out object dirHandle, out FileStatus fileStatus, dirPath,
                                AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE | (AccessMask)0x0001, SMBLibrary.FileAttributes.Directory,
                                ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
                                CreateOptions.FILE_DIRECTORY_FILE, null);

                            if (status == NTStatus.STATUS_SUCCESS)
                            {
                                // 第一筆查詢使用 "*" 找出所有檔案
                                status = fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> fileList, dirHandle, "*.*", FileInformationClass.FileIdBothDirectoryInformation);

                                // 使用迴圈分批讀取，直到回傳 STATUS_NO_MORE_FILES
                                while (fileList != null && fileList.Count > 0)
                                {
                                    foreach (var file in fileList)
                                    {
                                        if (file is FileIdBothDirectoryInformation dirInfo)
                                        {
                                            // 排除資料夾，並且找出 .csv 結尾的檔案
                                            if (!dirInfo.FileAttributes.HasFlag(SMBLibrary.FileAttributes.Directory) &&
                                                dirInfo.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                                            {
                                                fileNames.Add(dirInfo.FileName);
                                            }
                                        }
                                    }

                                    // 處理完這批資料後，如果 NAS 已經宣告「沒有更多檔案了」，就跳出迴圈
                                    if (status == NTStatus.STATUS_NO_MORE_FILES)
                                    {
                                        break;
                                    }

                                    // 如果還是 SUCCESS，代表檔案太多，跟 NAS 要「下一批」
                                    status = fileStore.QueryDirectory(out fileList, dirHandle, String.Empty, FileInformationClass.FileIdBothDirectoryInformation);

                                    // 防呆：如果遇到存取拒絕或其他未知錯誤，直接中斷
                                    if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                                    {
                                        break;
                                    }
                                }

                                fileStore.CloseFile(dirHandle);
                            }
                        }
                    }
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"讀取 NAS 列表失敗: {ex.Message}");
            }
        });

        return fileNames;
    }

    // 讀取單一 CSV 檔案內容
    public static async Task<string> ReadLogContentAsync(DateTime date, string? fileName)
    {
        string content = "";
        string dateStr = date.ToString("yyyy-MM-dd");
        string filePath = $@"logs\OE-TR02\{dateStr}\{fileName}";

        await Task.Run(() =>
        {
            try
            {
                SMB2Client client = new();
                if (client.Connect(IPAddress.Parse(NasIp), SMBTransportType.DirectTCPTransport))
                {
                    if (client.Login(String.Empty, Username, Password) == NTStatus.STATUS_SUCCESS)
                    {
                        ISMBFileStore fileStore = client.TreeConnect(ShareName, out NTStatus status);
                        if (status == NTStatus.STATUS_SUCCESS)
                        {
                            status = fileStore.CreateFile(out object fileHandle, out FileStatus fileStatus, filePath,
                                AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Normal,
                                ShareAccess.Read, CreateDisposition.FILE_OPEN,
                                CreateOptions.FILE_NON_DIRECTORY_FILE, null);

                            if (status == NTStatus.STATUS_SUCCESS)
                            {
                                // 假設檔案不大，一次讀取最大長度
                                fileStore.ReadFile(out byte[] data, fileHandle, 0, 1024 * 1024);
                                if (data != null) content = Encoding.UTF8.GetString(data);
                                fileStore.CloseFile(fileHandle);
                            }
                        }
                    }
                    client.Disconnect();
                }
            }
            catch { /* 忽略讀取異常 */ }
        });
        return content;
    }
}