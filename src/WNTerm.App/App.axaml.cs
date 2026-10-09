using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WNTerm.App.Views;

namespace WNTerm.App;

public class App : Application
{
    /// <summary>File truyền qua dòng lệnh (vd. double-click .wnterm) — import khi cửa sổ chính sẵn sàng.</summary>
    public static string? StartupFile { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        WNTerm.App.Services.AppPlatform.Register();

        // Lỗi chưa bắt trên UI thread (vd. từ async void handler): ghi log rồi bỏ qua, không để sập app.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            LogUnhandled(e.Exception);
            e.Handled = true;
        };
    }

    private static void LogUnhandled(Exception ex)
    {
        try
        {
            string dir = WNTerm.Services.AppPaths.Default.LogsDir;
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd}.log");
            System.IO.File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] UI: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
