using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WNTerm.App.Services;
using WNTerm.Services;

namespace WNTerm.ViewModels;

/// <summary>
/// Thanh "Có bản mới" ở thanh trạng thái. Windows: tải bộ cài ngay trong app (có tiến độ + kiểm tra SHA-256), cài xong tự mở lại.
/// Android/khác: mở trang tải (Android không cho cập nhật ngầm khi cài ngoài Google Play).
/// </summary>
public partial class UpdateViewModel : ObservableObject
{
    private UpdateInfo? _info;
    private CancellationTokenSource? _cts;
    private bool _started;
    private string? _dismissedVersion;

    [ObservableProperty] private bool isAvailable;
    [ObservableProperty] private bool isRequired;
    [ObservableProperty] private bool isDownloading;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private string buttonText = "";

    public bool CanDismiss => !IsRequired && !IsDownloading;
    partial void OnIsRequiredChanged(bool value) => OnPropertyChanged(nameof(CanDismiss));
    partial void OnIsDownloadingChanged(bool value) => OnPropertyChanged(nameof(CanDismiss));

    private bool InApp => OperatingSystem.IsWindows() && _info?.DownloadUrl != null && _info.Sha256 != null;

    private static string T(string en, string vi) => LocalizationManager.Tr(en, vi);

    /// <summary>Kiểm tra lúc mở app (sau vài giây) rồi mỗi 12 giờ nếu app chạy lâu.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            while (true)
            {
                var info = await UpdateChecker.CheckAsync();
                if (info != null) Ui.Run(() => Show(info));
                await Task.Delay(TimeSpan.FromHours(12));
            }
        });
    }

    private void Show(UpdateInfo info)
    {
        if (IsDownloading) return;
        var cur = UpdateChecker.Current;
        bool required = cur != null && info.IsRequired(cur);
        if (!required && _dismissedVersion == info.Version) return;
        _info = info;
        IsRequired = required;
        Text = required
            ? T($"Update required: version {info.Version}", $"Cần cập nhật lên bản {info.Version}")
            : T($"New version {info.Version} available", $"Có bản mới {info.Version}");
        ButtonText = InApp ? T("Update & restart", "Cập nhật & khởi động lại") : T("Download", "Tải về");
        IsAvailable = true;
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (!CanDismiss) return;
        _dismissedVersion = _info?.Version;
        IsAvailable = false;
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_info == null || IsDownloading) return;
        if (!InApp) { Ui.OpenWithShellOrLauncher(_info.PageUrl); return; }

        _cts = new CancellationTokenSource();
        IsDownloading = true;
        Progress = 0;
        Text = T("Downloading update… 0%", "Đang tải bản cập nhật… 0%");
        try
        {
            var prog = new Progress<double>(p => Ui.Run(() =>
            {
                Progress = p;
                Text = T($"Downloading update… {p:0}%", $"Đang tải bản cập nhật… {p:0}%");
            }));
            string exe = await UpdateChecker.DownloadInstallerAsync(_info, prog, _cts.Token);
            Text = T("Starting installer…", "Đang khởi động bộ cài…");
            Progress = 100;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "/SILENT /CLOSEAPPLICATIONS /update=1") { UseShellExecute = true });
            await Task.Delay(500);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
            else Environment.Exit(0);
        }
        catch (Exception)
        {
            IsDownloading = false;
            Text = T("Update failed — opening the download page", "Cập nhật lỗi — mở trang tải thủ công");
            ButtonText = T("Download", "Tải về");
            Ui.OpenWithShellOrLauncher(_info.PageUrl);
        }
    }
}
