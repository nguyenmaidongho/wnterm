using Avalonia;
using Avalonia.Styling;
using WNTerm.Services;

namespace WNTerm.App.Services;

/// <summary>Nối các điểm mở rộng của WNTerm.Core vào Avalonia + áp theme/ngôn ngữ.</summary>
public static class AppPlatform
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Windows: WebView2 mặc định ghi dữ liệu cạnh file exe → lỗi "Access denied" khi cài vào Program Files.
        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Default.WebView2Dir);
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", AppPaths.Default.WebView2Dir);
            }
            catch { }
        }

        LocalizationManager.LanguageApplied = dict =>
        {
            void Apply()
            {
                var res = Application.Current?.Resources;
                if (res == null) return;
                foreach (var kv in dict) res[kv.Key] = kv.Value;
            }
            Ui.Run(Apply);
        };

        LocalizationManager.FallbackResolver = key =>
            Application.Current != null && Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) ? v?.ToString() : null;

        SshConnectionFactory.HostKeyPrompt = info =>
        {
            // Được gọi từ thread SSH: hiện overlay trên UI thread và chờ quyết định.
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Dialogs.HostKeyAsync(info)).GetAwaiter().GetResult();
        };
    }

    public static void ApplyTheme(string? theme)
    {
        if (Application.Current == null) return;
        bool light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        Application.Current.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
