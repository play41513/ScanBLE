namespace ScanBLE;

public class NasLogModel
{
    public string? LogId { get; set; } // 用來識別每筆唯一的上傳紀錄
    public string? DeviceId { get; set; }
    public DateTime PassTime { get; set; }
    public string? CsvContent { get; set; }
    public string? Status { get; set; }
}