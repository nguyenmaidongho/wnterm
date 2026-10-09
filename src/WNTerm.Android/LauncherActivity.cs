using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace WNTerm.Android;

/// <summary>
/// Màn hình khởi động thuần Android: nếu lần mở trước app chết trước khi lên giao diện (cờ "launching" còn sót),
/// hiện báo cáo lỗi (log hệ thống + lỗi .NET) để sao chép; ngược lại chuyển thẳng sang MainActivity.
/// </summary>
[Activity(
    Label = "WN Term",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    NoHistory = true)]
public class LauncherActivity : Activity
{
    public static string FlagFile(Context c) => System.IO.Path.Combine(c.FilesDir!.AbsolutePath, "launching.flag");
    public static string CrashFile(Context c) => System.IO.Path.Combine(c.FilesDir!.AbsolutePath, "crash.txt");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        bool previousDied = File.Exists(FlagFile(this));
        if (!previousDied)
        {
            StartMain();
            return;
        }

        string report = BuildReport();
        ShowReport(report);
    }

    private void StartMain()
    {
        try { File.WriteAllText(FlagFile(this), DateTime.UtcNow.ToString("O")); } catch { }
        var intent = new Intent(this, typeof(MainActivity));
        StartActivity(intent);
        Finish();
    }

    private string BuildReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WN Term - bao cao loi lan mo truoc");
        sb.AppendLine($"Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}), {Build.Manufacturer} {Build.Model}, ABI {string.Join(",", Build.SupportedAbis ?? Array.Empty<string>())}");
        sb.AppendLine();

        try
        {
            string crash = CrashFile(this);
            if (File.Exists(crash))
            {
                sb.AppendLine("== Loi .NET ==");
                sb.AppendLine(File.ReadAllText(crash));
                sb.AppendLine();
            }
        }
        catch { }

        sb.AppendLine("== Logcat (loi cua app) ==");
        try
        {
            var proc = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "logcat", "-d", "-t", "600" });
            using var reader = new Java.IO.BufferedReader(new Java.IO.InputStreamReader(proc!.InputStream));
            string? line;
            var lines = new List<string>();
            while ((line = reader.ReadLine()) != null)
            {
                // Giữ dòng liên quan: lỗi, abort, .NET, mono, Avalonia, tên gói.
                if (line.Contains(" E ") || line.Contains(" F ") || line.Contains("wnterm") || line.Contains("DOTNET") ||
                    line.Contains("monodroid") || line.Contains("mono-rt") || line.Contains("Avalonia") || line.Contains("AndroidRuntime"))
                    lines.Add(line);
            }
            foreach (var l in lines.TakeLast(120)) sb.AppendLine(l);
        }
        catch (Exception ex)
        {
            sb.AppendLine("Khong doc duoc logcat: " + ex.Message);
        }
        return sb.ToString();
    }

    // Launcher chạy trước khi nạp cài đặt: theo ngôn ngữ hệ thống.
    private static string L(string en, string vi)
        => string.Equals(Java.Util.Locale.Default?.Language, "vi", StringComparison.OrdinalIgnoreCase) ? vi : en;

    private void ShowReport(string report)
    {
        var text = new TextView(this) { Text = report, TextSize = 11f };
        text.SetTextIsSelectable(true);
        text.SetTextColor(global::Android.Graphics.Color.White);
        text.SetPadding(24, 24, 24, 24);
        text.Typeface = global::Android.Graphics.Typeface.Monospace;

        var scroll = new ScrollView(this);
        scroll.AddView(text);

        var copy = new Button(this) { Text = L("Copy", "Sao chép") };
        copy.Click += (_, _) =>
        {
            var cm = (ClipboardManager?)GetSystemService(ClipboardService);
            cm?.PrimaryClip = ClipData.NewPlainText("WN Term log", report);
            Toast.MakeText(this, L("Copied", "Đã sao chép"), ToastLength.Short)?.Show();
        };

        var share = new Button(this) { Text = L("Share", "Chia sẻ") };
        share.Click += (_, _) =>
        {
            var send = new Intent(Intent.ActionSend);
            send.SetType("text/plain");
            send.PutExtra(Intent.ExtraText, report);
            StartActivity(Intent.CreateChooser(send, L("Send crash report", "Gửi báo cáo lỗi")));
        };

        var go = new Button(this) { Text = L("Open app", "Mở app") };
        go.Click += (_, _) =>
        {
            try { File.Delete(FlagFile(this)); File.Delete(CrashFile(this)); } catch { }
            StartMain();
        };

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        buttons.AddView(copy, lp);
        buttons.AddView(share, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        buttons.AddView(go, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#1E1E1E"));
        root.SetFitsSystemWindows(true);
        root.AddView(buttons);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        SetContentView(root);
    }
}
