using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScanBLE;

public class BleDeviceModel : INotifyPropertyChanged
{
    public string? DeviceId { get; set; } // ID 通常不會變

    private string? _cableStatus;
    private string? _lastUpdateTime;
    public string? FwVersion { get; set; }
    private int _rssi;
    private int _battery;
    private double _temperature;
    private bool _isExpanded;
    private DateTime _lastSeen;
    public DateTime ScanStartTime { get; set; }
    // 記錄從產品廣播包抓到的初始硬體時間
    public int InitialDeviceUptime { get; set; }

    private Color? _tempColor;
    private Color? _batteryColor;
    private Color? _rssiColor;
    private Color? _fwVersionColor;
    private Color? _cableColor;

    public Guid PluginBleId { get; set; }


    public string? MacAddress { get; set; }

    // 取 MAC 後六碼作為面板顯示的 ID (假設格式為 XX:XX:XX:XX:AA:BB)
    public string DisplayId => MacAddress?.Replace(":", "")[Math.Max(0, MacAddress.Replace(":", "").Length - 6)..] ?? "";

    private byte[]? _manufacturerData;
    public byte[]? ManufacturerData
    {
        get => _manufacturerData;
        set { _manufacturerData = value; OnPropertyChanged(); }
    }

    public Color? TempColor
    {
        get => _tempColor;
        set { _tempColor = value; OnPropertyChanged(); }
    }

    public Color? BatteryColor
    {
        get => _batteryColor;
        set { _batteryColor = value; OnPropertyChanged(); }
    }

    public Color? RssiColor
    {
        get => _rssiColor;
        set { _rssiColor = value; OnPropertyChanged(); }
    }

    public Color? FwVersionColor
    {
        get => _fwVersionColor;
        set { _fwVersionColor = value; OnPropertyChanged(); }
    }

    public int Rssi
    {
        get => _rssi;
        set { _rssi = value; OnPropertyChanged(); }
    }

    public double Temperature
    {
        get => _temperature;
        set { _temperature = value; OnPropertyChanged(); }
    }

    public int Battery
    {
        get => _battery;
        set { _battery = value; OnPropertyChanged(); }
    }

    public string? CableStatus
    {
        get => _cableStatus;
        set { _cableStatus = value; OnPropertyChanged(); }
    }

    public Color? CableColor
    {
        get => _cableColor;
        set { _cableColor = value; OnPropertyChanged(); }
    }

    public string? LastUpdateTime
    {
        get => _lastUpdateTime;
        set { _lastUpdateTime = value; OnPropertyChanged(); }
    }



    private string? _passFailText;
    private Color? _passFailColor;

    public string? PassFailText
    {
        get => _passFailText;
        set { _passFailText = value; OnPropertyChanged(); }
    }

    public Color? PassFailColor
    {
        get => _passFailColor;
        set { _passFailColor = value; OnPropertyChanged(); }
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set
        {
            _lastSeen = value;
            OnPropertyChanged();
            // 當 LastSeen 更新時，同時通知畫面更新 經過的秒數
            OnPropertyChanged(nameof(ElapsedTimeText));
        }
    }

    // 控制卡片展開/縮合的狀態
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    // 計算從第一次掃描到現在經過的秒數
    public string ElapsedTimeText
    {
        get
        {
            var elapsed = LastSeen - ScanStartTime;
            // 總秒數 = (APP 掃描經過的秒數) + (產品廣播的初始時間)
            int totalSeconds = (int)elapsed.TotalSeconds + InitialDeviceUptime;
            return $"{totalSeconds} 秒";
        }
    }
    //控制卡片是否被靜音/隱藏
    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotMuted)); // 同步通知反向狀態更新
        }
    }
    public bool IsNotMuted => !IsMuted;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}