using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace WNTerm.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<WNTerm.App.App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();
        // Ghi lỗi .NET chưa bắt được ra file để LauncherActivity hiện ở lần mở sau.
        // (Gắn ở OnCreate, không gắn trong constructor, để không phá bước đăng ký JNI.)
        try { WNTerm.App.Services.UpdateChecker.CurrentVersionText = PackageManager?.GetPackageInfo(PackageName!, 0)?.VersionName; } catch { }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => SaveCrash("UnhandledException", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => SaveCrash("UnobservedTaskException", e.Exception);
    }

    private void SaveCrash(string kind, object? ex)
    {
        try
        {
            File.AppendAllText(LauncherActivity.CrashFile(this), $"[{DateTime.Now:HH:mm:ss}] {kind}: {ex}{System.Environment.NewLine}{System.Environment.NewLine}");
        }
        catch { }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Dữ liệu ứng dụng nằm trong thư mục riêng của app (không cần quyền lưu trữ).
        var files = FilesDir?.AbsolutePath ?? System.IO.Path.GetTempPath();
        System.Environment.SetEnvironmentVariable("WNTERM_DATA_DIR", System.IO.Path.Combine(files, "data"));
        System.Environment.SetEnvironmentVariable("WNTERM_LOCAL_DIR", System.IO.Path.Combine(files, "local"));
        var maker = Build.Manufacturer ?? "";
        var model = Build.Model ?? "Android";
        WNTerm.Services.AccountService.DeviceNameOverride = (model.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? model : (System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(maker) + " " + model)).Trim();
        return base.CustomizeAppBuilder(builder);
    }
}

[Activity(
    Label = "WN Term",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = false,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnResume()
    {
        base.OnResume();
        // Chạy ổn định quá 6 giây => coi như khởi động thành công, xóa cờ để lần sau không hiện báo cáo lỗi.
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            try { File.Delete(LauncherActivity.FlagFile(this)); File.Delete(LauncherActivity.CrashFile(this)); } catch { }
        }, 6000);
    }
}
