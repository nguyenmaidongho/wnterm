using System.Windows;
using Microsoft.Win32;
using SNTerm.ViewModels;

namespace SNTerm.Views;

public partial class SessionEditorDialog : Window
{
    private readonly SessionEditorViewModel _viewModel;
    private bool _isPasswordRevealed;

    public SessionEditorDialog(SessionEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestClose += (success) =>
        {
            DialogResult = success;
            Close();
        };
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isPasswordRevealed)
        {
            _viewModel.Password = PasswordBoxControl.Password;
        }
    }

    private void PasswordRevealBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isPasswordRevealed)
        {
            _viewModel.Password = PasswordRevealBox.Text;
        }
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            PasswordRevealBox.Text = PasswordBoxControl.Password;
            PasswordBoxControl.Visibility = Visibility.Collapsed;
            PasswordRevealBox.Visibility = Visibility.Visible;
            PasswordRevealBox.Focus();
            PasswordRevealBox.CaretIndex = PasswordRevealBox.Text.Length;
        }
        else
        {
            PasswordBoxControl.Password = PasswordRevealBox.Text;
            PasswordRevealBox.Visibility = Visibility.Collapsed;
            PasswordBoxControl.Visibility = Visibility.Visible;
            PasswordBoxControl.Focus();
        }
    }

    private void PassphraseBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Passphrase = PassphraseBoxControl.Password;
    }

    private void BrowseKeyFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn file SSH Key",
            Filter = "Key file (*.pem;*.key;*.ppk;id_*)|*.pem;*.key;*.ppk;id_*|Tất cả file (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) == true)
        {
            _viewModel.KeyFilePath = dlg.FileName;
        }
    }
}
