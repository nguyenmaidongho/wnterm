using System.Windows;
using System.Windows.Media;

namespace WNTerm.Views;

public enum HostKeyDecision
{
    Cancel,
    TrustOnce,
    TrustAndSave
}

public partial class HostKeyDialog : Window
{
    public HostKeyDecision Decision { get; private set; } = HostKeyDecision.Cancel;

    public HostKeyDialog(string host, int port, string algorithm, string fingerprint, bool isChanged)
    {
        InitializeComponent();

        HostBlock.Text = $"{host}:{port}";
        AlgorithmBlock.Text = algorithm;
        FingerprintBox.Text = fingerprint;

        if (isChanged)
        {
            TitleBlock.Text = WNTerm.Services.LocalizationManager.Tr("Host key changed warning", "Cảnh báo thay đổi Host Key");
            TitleBlock.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));
            WarningBox.Visibility = Visibility.Visible;
            TrustAndSaveButton.Content = WNTerm.Services.LocalizationManager.Tr("Connect anyway & update", "Vẫn kết nối & Cập nhật");
            TrustAndSaveButton.Background = new SolidColorBrush(Color.FromRgb(217, 45, 32));
            TrustOnceButton.Visibility = Visibility.Collapsed;
            CancelButton.IsDefault = true;
        }
        else
        {
            TrustAndSaveButton.IsDefault = true;
        }
    }

    private void TrustAndSave_Click(object sender, RoutedEventArgs e)
    {
        Decision = HostKeyDecision.TrustAndSave;
        DialogResult = true;
        Close();
    }

    private void TrustOnce_Click(object sender, RoutedEventArgs e)
    {
        Decision = HostKeyDecision.TrustOnce;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Decision = HostKeyDecision.Cancel;
        DialogResult = false;
        Close();
    }
}
