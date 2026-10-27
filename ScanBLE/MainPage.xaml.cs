using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System.Collections.ObjectModel;

namespace ScanBLE;

public partial class MainPage : ContentPage
{
    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;
    private bool _isScanning = false;
    private readonly IDispatcherTimer _restartScanTimer;
    private readonly IDispatcherTimer _cleanupTimer;

    // --- 快取 Preferences 變數 ---
    private double _minTemp, _maxTemp;
    private int _minBattery, _minRssi, _maxRssi, _ignoreRssi;
    private string? _targetFw = "";
    private Color? _normalTextColor;
    public ObservableCollection<BleDeviceModel> DevicesList { get; set; } = [];

    public MainPage()
    {
        InitializeComponent();
        _ble = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        DevicesCollectionView.ItemsSource = DevicesList;
        _adapter.DeviceAdvertised += OnDeviceAdvertised;

        //當藍牙掃描達到 ScanTimeout 時間而自動停止時，觸發此事件
        _adapter.ScanTimeoutElapsed += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isScanning = false;               
                BtnScan.Text = "Start Scanning";
                BtnScan.BackgroundColor = Color.FromArgb("#28a745");
                _cleanupTimer?.Stop();
            });
        };

        // 初始化淘汰定時器 (每 0.5 秒檢查一次)
        _cleanupTimer = Dispatcher.CreateTimer();
        _cleanupTimer.Interval = TimeSpan.FromSeconds(0.5);
        _cleanupTimer.Tick += OnCleanupTimerTicked;
        // 初始化 Android 的 5 分鐘機制
        _restartScanTimer = Dispatcher.CreateTimer();
        _restartScanTimer.Interval = TimeSpan.FromMinutes(5);
        _restartScanTimer.Tick += async (s, e) =>
        {
            if (_isScanning)
            {
                // 快速停止再立刻啟動，就能重置 Android 的 5 分鐘懲罰機制
                await _adapter.StopScanningForDevicesAsync();
                // 延遲1秒
                await Task.Delay(1000);

                _ = _adapter.StartScanningForDevicesAsync(allowDuplicatesKey: true);
            }
        };
        // APP 一開啟就去檢查有沒有上次關閉前來不及上傳的 PASS 資料
        _ = NasHelper.UploadPendingLogsAsync();
        // 保持螢幕常亮
        DeviceDisplay.Current.KeepScreenOn = true;
    }

    // 定時器觸發的清理邏輯
    private void OnCleanupTimerTicked(object? sender, EventArgs e)
    {
        // 設定超時標準：如果超過 5 秒沒收到廣播，就視為斷電或休眠
        var timeout = TimeSpan.FromSeconds(5);
        var now = DateTime.Now;

        for (int i = DevicesList.Count - 1; i >= 0; i--)
        {
            if (now - DevicesList[i].LastSeen > timeout)
            {
                if (DevicesList[i].PassFailText == "PASS")
                {
                    _ = NasHelper.UploadPendingLogsAsync();
                }

                DevicesList.RemoveAt(i);
            }
        }
    }

    private async void OnScanClicked(object sender, EventArgs e)
    {
        var status = await CheckBluetoothPermissions();
        if (!status) return;

        if (!_isScanning)
        {
            _isScanning = true;
            BtnScan.Text = "Stop Scanning";
            BtnScan.BackgroundColor = Colors.Blue;

            _minTemp = Preferences.Default.Get("MinTemp", 10.0);
            _maxTemp = Preferences.Default.Get("MaxTemp", 40.0);
            _minBattery = Preferences.Default.Get("MinBattery", 80);
            _targetFw = Preferences.Default.Get("TargetFwVersion", "21");
            _minRssi = Preferences.Default.Get("MinRssi", -70);
            _maxRssi = Preferences.Default.Get("MaxRssi", -20);
            _ignoreRssi = Preferences.Default.Get("IgnoreRssi", -80);

            _normalTextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black;

            _cleanupTimer.Start();
            _adapter.ScanTimeout = 720000000;
            _adapter.ScanMode = ScanMode.LowLatency;
            _adapter.ScanMatchMode = ScanMatchMode.AGRESSIVE;
            _restartScanTimer.Start();
            _ = _adapter.StartScanningForDevicesAsync(allowDuplicatesKey: true);
        }
        else
        {
            _isScanning = false;
            BtnScan.Text = "Start Scanning";
            BtnScan.BackgroundColor = Color.FromArgb("#28a745");

            _cleanupTimer.Stop();
            _restartScanTimer.Stop();
            await _adapter.StopScanningForDevicesAsync();
        }
    }
    //當按下卡片右上角的「隱藏」按鈕時觸發
    private void OnHideClicked(object sender, EventArgs e)
    {
        // 抓出是哪一顆按鈕被按，以及它對應的裝置模型
        if (sender is Button btn && btn.BindingContext is BleDeviceModel device)
        {
            device.IsMuted = true;       // 切換為隱藏狀態
            device.IsExpanded = false;   // 確保隱藏時，把展開面板也收起來
        }
    }
    // 處理卡片點擊事件，切換展開狀態
    private void OnDeviceTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is BleDeviceModel tappedDevice)
        {
            if (tappedDevice.IsMuted)
            {
                // 如果卡片目前是被隱藏的，點擊卡片就會「解除隱藏」
                tappedDevice.IsMuted = false;
            }
            else
            {
                // 如果卡片是正常顯示的，點擊則一樣是「展開/縮合詳細數據」
                tappedDevice.IsExpanded = !tappedDevice.IsExpanded;
            }
        }
    }
    private void OnDeviceAdvertised(object? sender, DeviceEventArgs e)
    {
        var device = e.Device;
        string? deviceMAC = "";
#if ANDROID
        if (device.NativeDevice is Android.Bluetooth.BluetoothDevice androidDevice)
        {
            deviceMAC = androidDevice.Address; // 格式: XX:XX:XX:XX:AA:BB
        }
#endif
        if (string.IsNullOrEmpty(deviceMAC)) return;

        DateTime now = DateTime.Now;
        int currentRssi = device.Rssi;

        // 【背景處理】：先在背景尋找是否已存在
        var existingDevice = DevicesList.FirstOrDefault(d => d.MacAddress == deviceMAC);

        // 如果距離太遠，且是新裝置，直接 return 掉
        if (currentRssi < _ignoreRssi && existingDevice == null) return;

        var advertisementRecords = device.AdvertisementRecords;
        var manufacturerRecord = advertisementRecords?.FirstOrDefault(r => r.Type == Plugin.BLE.Abstractions.AdvertisementRecordType.ManufacturerSpecificData);

        bool isOurCompanyDevice = false;
        if (manufacturerRecord != null && manufacturerRecord.Data.Length >= 20)
        {
            // Atmosic Technologies 的代碼，在 payload 前兩個 byte 為 0x24 與 0x0A
            if (manufacturerRecord.Data[0] == 0x24 && manufacturerRecord.Data[1] == 0x0A)
            {
                isOurCompanyDevice = true;
            }
        }

        // 如果沒有資料，或是雖然有資料但不是我們公司的產品：
        if (!isOurCompanyDevice)
        {
            // 如果之前已經認證過加進列表的，就只需要更新 LastSeen 和 Rssi 續命
            if (existingDevice != null)
            {
                // 限制單純續命的 UI 更新頻率，間隔大於 300ms 才更新
                if ((now - existingDevice.LastSeen).TotalMilliseconds > 300)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        existingDevice.LastSeen = now;
                        existingDevice.Rssi = currentRssi;
                        // 把 PluginBleId 記下來
                        existingDevice.PluginBleId = device.Id;
                    });
                }
            }
            return;
        }
        byte[] payload = manufacturerRecord.Data;
        string deviceId = $"{payload[5]:X2}:{payload[6]:X2}:{payload[7]:X2}";

        double temperature = payload[8];
        int initialUptime = payload[15] * 10;
        int battery = payload[14];

        bool isCableCut = payload[13] == 0x01;

        string fwVersion = payload[19].ToString("X2");

        string cableStatus = isCableCut ? "Cut (斷線)" : "Connect (正常)";
        Color cableColor = isCableCut ? Colors.Red : Colors.Green;

        // --- 使用記憶體快取變數進行 PASS / FAIL 判斷 ---
        bool isTempPass = temperature >= _minTemp && temperature <= _maxTemp;
        bool isBatteryPass = battery >= _minBattery;
        bool isFwPass = fwVersion == _targetFw;
        bool isRssiPass = currentRssi >= _minRssi && currentRssi <= _maxRssi;
        bool isCablePass = !isCableCut;

        bool isAllPass = isTempPass && isBatteryPass && isFwPass && isRssiPass && isCablePass;

        Color? tempColor = isTempPass ? _normalTextColor : Colors.Red;
        Color? batteryColor = isBatteryPass ? _normalTextColor : Colors.Red;
        Color? fwVersionColor = isFwPass ? _normalTextColor : Colors.Red;
        Color? rssiColor = isRssiPass ? Color.FromArgb("#007ACC") : Colors.Red;

        int baseUptime = existingDevice != null ? existingDevice.InitialDeviceUptime : initialUptime;
        double totalSeconds = (now - (existingDevice?.ScanStartTime ?? now)).TotalSeconds + baseUptime;

        string resultText = "FAIL";
        Color resultColor = Colors.Red;

        if (isAllPass)
        {
            if (totalSeconds < 15)
            {
                resultText = "Testing";
                resultColor = Colors.Orange;
            }
            else
            {
                resultText = "PASS";
                resultColor = Colors.Green;
            }
        }

        // --- 進 UI 執行緒前，先準備好寫入 Log 的任務 ---
        bool shouldSaveLog = existingDevice != null && resultText == "PASS" && existingDevice.PassFailText != "PASS";

        if (shouldSaveLog)
        {
            Task.Run(() =>
            {
                string csvHeader = "日期,時間,裝置ID,溫度,電量,線路狀態,韌體版本,RSSI\n";
                string csvData = $"{now:yyyy-MM-dd},{now:HH:mm:ss},{deviceId},{temperature},{battery},{cableStatus},{fwVersion},{currentRssi}\n";

                NasHelper.SaveLogToLocalCache(new NasLogModel
                {
                    LogId = $"{deviceId}_{now.Ticks}",
                    DeviceId = deviceId,
                    PassTime = now,
                    Status = resultText,
                    CsvContent = csvHeader + csvData
                });
            });
        }

        // 切換到 UI 執行緒更新畫面
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (existingDevice != null)
            {
                // 檢查距離上次更新畫面是否小於 300 毫秒，避免 UI 狂刷
                bool isUpdatingTooFast = (now - existingDevice.LastSeen).TotalMilliseconds < 300;
                bool isStatusChanged = existingDevice.PassFailText != resultText || existingDevice.CableStatus != cableStatus;

                if (!isUpdatingTooFast || isStatusChanged)
                {
                    existingDevice.PluginBleId = device.Id; // 更新 GUID
                    existingDevice.LastSeen = now;
                    existingDevice.Rssi = currentRssi;
                    existingDevice.ManufacturerData = payload;
                    existingDevice.Temperature = temperature;
                    existingDevice.Battery = battery;
                    existingDevice.CableStatus = cableStatus;
                    existingDevice.CableColor = cableColor;
                    existingDevice.PassFailText = resultText;
                    existingDevice.PassFailColor = resultColor;
                    existingDevice.TempColor = tempColor;
                    existingDevice.BatteryColor = batteryColor;
                    existingDevice.FwVersionColor = fwVersionColor;
                    existingDevice.RssiColor = rssiColor;
                }
            }
            else
            {
                // 新增面板
                DevicesList.Add(new BleDeviceModel
                {
                    PluginBleId = device.Id, //寫入 GUID
                    MacAddress = deviceMAC,
                    DeviceId = deviceId,
                    Rssi = currentRssi,
                    Temperature = temperature,
                    Battery = battery,
                    CableStatus = cableStatus,
                    CableColor = cableColor,
                    FwVersion = fwVersion,
                    PassFailText = resultText,
                    PassFailColor = resultColor,
                    ScanStartTime = now,
                    LastSeen = now,
                    IsExpanded = false,
                    InitialDeviceUptime = initialUptime,
                    TempColor = tempColor,
                    BatteryColor = batteryColor,
                    FwVersionColor = fwVersionColor,
                    RssiColor = rssiColor,
                    ManufacturerData = payload
                });
            }
        });
    }

    // 權限檢查邏輯維持不變
    private static async Task<bool> CheckBluetoothPermissions()
    {
        if (DeviceInfo.Version.Major >= 12)
        {
            var bleStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (bleStatus != PermissionStatus.Granted)
            {
                bleStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
            }
            return bleStatus == PermissionStatus.Granted;
        }
        else
        {
            var locStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (locStatus != PermissionStatus.Granted)
            {
                locStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }
            return locStatus == PermissionStatus.Granted;
        }
    }
}