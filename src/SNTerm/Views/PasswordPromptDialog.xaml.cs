using System.Windows;

namespace SNTerm.Views;

public partial class PasswordPromptDialog : Window
{
    public string Password => PasswordBoxControl.Password;
    public bool SavePassword => SavePasswordCheckBox.IsChecked == true;

    public PasswordPromptDialog(string vmName, string user, string host, int port, bool wasRejected = false)
    {
        InitializeComponent();

        Title = $"{vmName} — Nhập mật khẩu";
        TitleBlock.Text = $"{vmName} — Nhập mật khẩu";
        // Không hiện port để tránh lộ ra khi ai đó nhìn màn hình lúc nhập mật khẩu.
        SubtitleBlock.Text = $"{user}@{host}";

        if (wasRejected)
        {
            WarningBlock.Visibility = Visibility.Visible;
        }

        Loaded += (s, e) => PasswordBoxControl.Focus();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
