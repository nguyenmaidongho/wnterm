using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

public partial class CloudBackupDialog : DialogView<bool?>
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _sessionStore;
    private bool _initialized;

    /// <summary>True nếu đã khôi phục (import) VM từ cloud để cửa sổ chính nạp lại danh sách.</summary>
    public bool Restored { get; private set; }

    public CloudBackupDialog(AppSettings settings, SettingsStore settingsStore, SessionStore sessionStore)
    {
        InitializeComponent();
        Title = "Cloud Backup (S3)";
        DialogWidth = 640;
        _settings = settings;
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;

        EndpointBox.Text = settings.S3Endpoint;
        BucketBox.Text = settings.S3Bucket;
        RegionBox.Text = settings.S3Region;
        PrefixBox.Text = settings.S3Prefix;
        AccessKeyBox.Text = settings.S3AccessKey;
        SecretKeyBox.Text = SecretProtector.Decrypt(settings.S3SecretKeyEnc) ?? "";
        BackupPasswordBox.Text = SecretProtector.Decrypt(settings.CloudBackupPasswordEnc) ?? "";
        OnlyChangedCheck.IsChecked = settings.CloudBackupOnlyIfChanged;
        foreach (var it in IntervalCombo.Items.OfType<ComboBoxItem>())
            if (it.Tag?.ToString() == settings.EffectiveCloudIntervalMinutes.ToString()) IntervalCombo.SelectedItem = it;
        if (IntervalCombo.SelectedItem == null) IntervalCombo.SelectedIndex = 4;
        PathStyleCheck.IsChecked = settings.S3PathStyle;
        KeepBox.Text = settings.CloudKeepCount.ToString();

        AttachedToVisualTree += (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            Dispatcher.UIThread.Post(OnLoaded, DispatcherPriority.Loaded);
        };
    }


    private async void OnLoaded()
    {
        if (CloudBackupService.IsConfigured(_settings))
        {
            ConfigExpander.IsExpanded = false;
            await RefreshListAsync(); // tự hiện "Last backup" khi thành công, hoặc thông báo lỗi khi thất bại
        }
        else
        {
            UpdateLastBackupText();
        }
    }

    private CloudBackupService Service => new(_settings, _sessionStore);

    private void UpdateLastBackupText()
    {
        StatusText.Text = _settings.LastCloudBackupUtc is DateTime t
            ? string.Format(LocalizationManager.Tr("Last backup: {0}", "Backup gần nhất: {0}"), t.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
            : LocalizationManager.Tr("No backup from this computer yet.", "Chưa có backup nào từ máy này.");
    }

    private bool ReadConfig()
    {
        if (string.IsNullOrWhiteSpace(BucketBox.Text) || string.IsNullOrWhiteSpace(AccessKeyBox.Text) ||
            string.IsNullOrEmpty(SecretKeyBox.Text))
        {
            ShowTest(LocalizationManager.Tr("Enter Bucket, Access Key and Secret Key.", "Nhập đủ Bucket, Access Key, Secret Key."), false);
            return false;
        }
        if (string.IsNullOrEmpty(BackupPasswordBox.Text))
        {
            ShowTest(LocalizationManager.Tr("A backup password is required.", "Cần đặt mật khẩu backup."), false);
            return false;
        }

        _settings.S3Endpoint = (EndpointBox.Text ?? "").Trim();
        _settings.S3Bucket = BucketBox.Text!.Trim();
        _settings.S3Region = string.IsNullOrWhiteSpace(RegionBox.Text) ? "us-east-1" : RegionBox.Text.Trim();
        _settings.S3Prefix = (PrefixBox.Text ?? "").Trim();
        _settings.S3AccessKey = AccessKeyBox.Text!.Trim();
        _settings.S3SecretKeyEnc = SecretProtector.Encrypt(SecretKeyBox.Text!);
        _settings.CloudBackupPasswordEnc = SecretProtector.Encrypt(BackupPasswordBox.Text!);
        int minutes = int.TryParse((IntervalCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int m) ? m : 1440;
        _settings.CloudBackupIntervalMinutes = minutes;
        _settings.CloudBackupOnlyIfChanged = OnlyChangedCheck.IsChecked == true;
        _settings.CloudAutoBackup = minutes > 0;
        _settings.S3PathStyle = PathStyleCheck.IsChecked == true;
        _settings.CloudKeepCount = int.TryParse(KeepBox.Text, out int k) && k >= 1 ? k : 30;
        _settingsStore.Save(_settings);
        return true;
    }

    private void ShowTest(string text, bool ok)
    {
        TestResultText.Text = text;
        TestResultText.Foreground = ok ? Brushes.LimeGreen : Brushes.OrangeRed;
    }

    private async void SaveTest_Click(object? sender, RoutedEventArgs e)
    {
        if (!ReadConfig()) return;
        ShowTest(LocalizationManager.Tr("Testing...", "Đang kiểm tra..."), true);
        try
        {
            await Service.TestAsync();
            ShowTest(LocalizationManager.Tr("Connection OK, settings saved.", "Kết nối OK, đã lưu cấu hình."), true);
            await RefreshListAsync();
        }
        catch (Exception ex)
        {
            ShowTest(LocalizationManager.Tr("Error: ", "Lỗi: ") + ex.Message, false);
        }
    }

    private async Task RefreshListAsync()
    {
        if (!CloudBackupService.IsConfigured(_settings)) return;
        try
        {
            StatusText.Text = LocalizationManager.Tr("Loading list...", "Đang tải danh sách...");
            var items = await Service.ListAsync();
            BackupsList.ItemsSource = items;
            if (items.Count > 0) BackupsList.SelectedIndex = 0;
            UpdateLastBackupText();
            StatusText.Text += "  |  " + string.Format(LocalizationManager.Tr("{0} backups in the cloud.", "{0} bản trên cloud."), items.Count);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.Tr("Could not load the list: ", "Không tải được danh sách: ") + ex.Message;
        }
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshListAsync();

    private async void BackupNow_Click(object? sender, RoutedEventArgs e)
    {
        if (!ReadConfig()) { ConfigExpander.IsExpanded = true; return; }
        BackupNowBtn.IsEnabled = false;
        StatusText.Text = LocalizationManager.Tr("Backing up...", "Đang backup...");
        try
        {
            string key = (await Service.BackupAsync())!;
            _settingsStore.Save(_settings);
            await RefreshListAsync();
            StatusText.Text = LocalizationManager.Tr("Backup successful: ", "Backup thành công: ") + Path.GetFileName(key);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.Tr("Backup failed: ", "Backup lỗi: ") + ex.Message;
        }
        finally
        {
            BackupNowBtn.IsEnabled = true;
        }
    }

    private void BackupsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (BackupsList.SelectedItem != null) Restore_Click(sender, e);
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not CloudBackupItem item)
        {
            StatusText.Text = LocalizationManager.Tr("Select a backup in the list.", "Chọn một bản backup trong danh sách.");
            return;
        }
        if (!CloudBackupService.IsConfigured(_settings)) return;

        StatusText.Text = LocalizationManager.Tr("Downloading backup...", "Đang tải bản backup...");
        string? file = null;
        try
        {
            file = await Service.DownloadAsync(item.Key);
            StatusText.Text = "";
            // Dùng lại luồng Import: tự sao lưu local trước khi nhập, chọn cách xử lý trùng.
            var dlg = new ImportDialog(file, _sessionStore)
            {
                PrefillPassword = SecretProtector.Decrypt(_settings.CloudBackupPasswordEnc)
            };
            if (await Dialogs.ShowAsync(dlg) == true)
            {
                Restored = true;
                StatusText.Text = LocalizationManager.Tr("Restore complete.", "Đã khôi phục xong.");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.Tr("Restore failed: ", "Khôi phục lỗi: ") + ex.Message;
        }
        finally
        {
            if (file != null) { try { File.Delete(file); } catch { } }
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not CloudBackupItem item) return;
        if (!await Dialogs.ConfirmAsync(LocalizationManager.Tr("Delete backup", "Xóa backup"),
                string.Format(LocalizationManager.Tr("Delete backup {0} from the cloud?", "Xóa bản backup {0} trên cloud?"), item.FileName),
                DialogIcon.Warning)) return;
        try
        {
            await Service.DeleteAsync(item.Key);
            await RefreshListAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.Tr("Delete failed: ", "Xóa lỗi: ") + ex.Message;
        }
    }
}
