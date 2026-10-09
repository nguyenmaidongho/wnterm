using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WNTerm.Services;

namespace WNTerm;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        WpfPlatform.Register();

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        ThemeManager.ApplyTheme(settings.Theme);
        LocalizationManager.ApplyLanguage(settings.Language);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        try
        {
            string version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                WNTerm.Services.LocalizationManager.Tr("WebView2 Runtime is not installed. Please install the Microsoft Edge WebView2 Runtime to run the terminal.", "Máy tính chưa cài đặt WebView2 Runtime. Vui lòng cài đặt Microsoft Edge WebView2 Runtime để chạy terminal."),
                WNTerm.Services.LocalizationManager.Tr("WebView2 Runtime missing", "Thiếu WebView2 Runtime"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("DispatcherUnhandledException", e.Exception);
        e.Handled = true; // Ngăn app bị crash đột ngột
        MessageBox.Show(string.Format(WNTerm.Services.LocalizationManager.Tr("An error occurred: {0}\nDetails were written to the log.", "Đã xảy ra sự cố: {0}\nChi tiết đã được ghi vào log."), e.Exception.Message), WNTerm.Services.LocalizationManager.Tr("System error", "Lỗi hệ thống"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError("AppDomainUnhandledException", ex);
        }
    }

    private static void LogError(string source, Exception ex)
    {
        try
        {
            string logsDir = AppPaths.Default.LogsDir;
            string logFile = Path.Combine(logsDir, $"error-{DateTime.Now:yyyyMMdd}.log");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n";
            File.AppendAllText(logFile, entry, Encoding.UTF8);
        }
        catch { }
    }
}
