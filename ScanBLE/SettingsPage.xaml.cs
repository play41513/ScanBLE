namespace ScanBLE;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        // 讀取設定檔，若無設定則給予預設值
        EntMinTemp.Text = Preferences.Default.Get("MinTemp", 20.0).ToString();
        EntMaxTemp.Text = Preferences.Default.Get("MaxTemp", 40.0).ToString();
        EntMinBattery.Text = Preferences.Default.Get("MinBattery", 80).ToString();
        EntFwVersion.Text = Preferences.Default.Get("TargetFwVersion", "21");
        EntMinRssi.Text = Preferences.Default.Get("MinRssi", -60).ToString();
        EntMaxRssi.Text = Preferences.Default.Get("MaxRssi", -20).ToString();
        EntIgnoreRssi.Text = Preferences.Default.Get("IgnoreRssi", -80).ToString();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // 儲存至手機系統 Preferences
        if (double.TryParse(EntMinTemp.Text, out double minTemp)) Preferences.Default.Set("MinTemp", minTemp);
        if (double.TryParse(EntMaxTemp.Text, out double maxTemp)) Preferences.Default.Set("MaxTemp", maxTemp);
        if (int.TryParse(EntMinBattery.Text, out int minBatt)) Preferences.Default.Set("MinBattery", minBatt);
        Preferences.Default.Set("TargetFwVersion", EntFwVersion.Text?.Trim() ?? "");
        if (int.TryParse(EntMinRssi.Text, out int minRssi)) Preferences.Default.Set("MinRssi", minRssi);
        if (int.TryParse(EntMaxRssi.Text, out int maxRssi)) Preferences.Default.Set("MaxRssi", maxRssi);
        if (int.TryParse(EntIgnoreRssi.Text, out int ignoreRssi)) Preferences.Default.Set("IgnoreRssi", ignoreRssi);

        await DisplayAlert("成功", "測試標準已儲存！", "確定");
        await Navigation.PopAsync(); // 關閉設定頁面
    }
}