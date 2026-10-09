using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WNTerm.App.Services;
using WNTerm.Services;

namespace WNTerm.App.Views;

/// <summary>Hỏi mật khẩu khi kết nối (có tuỳ chọn lưu mật khẩu). Giống PasswordPromptDialog của bản WPF.</summary>
public sealed class PasswordPromptDialog : DialogView<bool?>
{
    private readonly TextBox _password = new() { PasswordChar = '•', RevealPassword = false };
    private readonly CheckBox _save = new() { IsChecked = true };

    public string Password => _password.Text ?? "";
    public bool SavePassword => _save.IsChecked == true;

    public PasswordPromptDialog(string vmName, string user, string host, int port, bool wasRejected = false)
    {
        Title = $"{vmName} — " + LocalizationManager.Tr("Enter password", "Nhập mật khẩu");
        DialogWidth = 420;

        _save.Content = LocalizationManager.Get("Str_SavePasswordCheck");

        var root = new StackPanel { Spacing = 10 };
        // Không hiện port để tránh lộ ra khi ai đó nhìn màn hình lúc nhập mật khẩu.
        root.Children.Add(new TextBlock { Text = $"{user}@{host}", Classes = { "muted" } });

        if (wasRejected)
        {
            root.Children.Add(new TextBlock
            {
                Text = LocalizationManager.Get("Str_PpWrong"),
                Foreground = new SolidColorBrush(Color.Parse("#F04438")),
                TextWrapping = TextWrapping.Wrap
            });
        }

        root.Children.Add(new TextBlock { Text = LocalizationManager.Get("Str_PpLabel") });
        root.Children.Add(_password);
        root.Children.Add(_save);

        var login = new Button { Content = LocalizationManager.Get("Str_PpLogin"), Classes = { "primary" }, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = LocalizationManager.Get("Str_Cancel"), IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        login.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { cancel, login }
        });

        Content = root;
        _password.AttachedToVisualTree += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _password.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public override void OnCancelRequested() => Close(false);
}
