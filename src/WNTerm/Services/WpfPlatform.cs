using System.Windows;
using WNTerm.Services;
using WNTerm.Views;

namespace WNTerm;

/// <summary>Nối các điểm mở rộng của WNTerm.Core vào WPF.</summary>
internal static class WpfPlatform
{
    public static void Register()
    {
        LocalizationManager.LanguageApplied = dict =>
        {
            var resources = Application.Current?.Resources;
            if (resources == null) return;
            foreach (var kv in dict)
                resources[kv.Key] = kv.Value;
        };

        LocalizationManager.FallbackResolver = key =>
            Application.Current?.Resources.Contains(key) == true
                ? Application.Current.Resources[key]?.ToString()
                : null;

        SshConnectionFactory.HostKeyPrompt = info =>
        {
            var app = Application.Current;
            if (app == null) return HostKeyDecision.Cancel;
            var decision = HostKeyDecision.Cancel;
            app.Dispatcher.Invoke(() =>
            {
                var dlg = new HostKeyDialog(info.Host, info.Port, info.KeyName, info.Fingerprint, info.IsChanged)
                {
                    Owner = app.MainWindow
                };
                dlg.ShowDialog();
                decision = dlg.Decision;
            });
            return decision;
        };
    }
}
