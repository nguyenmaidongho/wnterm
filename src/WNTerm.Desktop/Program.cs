using Avalonia;
using WNTerm.Services;

namespace WNTerm.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Ghi lỗi không bắt được ra file log để còn biết nguyên nhân khi app sập.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                string file = Path.Combine(AppPaths.Default.LogsDir, $"error-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] {e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        };

        WNTerm.App.App.StartupFile = args.FirstOrDefault(File.Exists);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<WNTerm.App.App>()
            .UsePlatformDetect()
            .LogToTrace();
}
