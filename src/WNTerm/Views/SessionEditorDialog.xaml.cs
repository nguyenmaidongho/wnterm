using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WNTerm.ViewModels;

namespace WNTerm.Views;

public partial class SessionEditorDialog : Window
{
    private readonly SessionEditorViewModel _viewModel;
    private bool _isPasswordRevealed;

    public SessionEditorDialog(SessionEditorViewModel viewModel)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height * 0.95;
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
            string current = PasswordBoxControl.Password;
            if (string.IsNullOrEmpty(current))
            {
                current = _viewModel.GetStoredPasswordPlain() ?? "";
            }
            PasswordRevealBox.Text = current;
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

    // Ô Port: chỉ cho nhập số.
    private void PortTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    // Chặn dán nội dung không phải số vào ô Port.
    private void PortTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string text = (string)e.DataObject.GetData(typeof(string))!;
            if (text.Length == 0 || !text.All(char.IsDigit))
            {
                e.CancelCommand();
            }
        }
        else
        {
            e.CancelCommand();
        }
    }

    // Bấm vào một tag đã có để thêm vào ô Tags (bỏ qua nếu đã có).
    private void ExistingTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string tag } || DataContext is not ViewModels.SessionEditorViewModel vm) return;
        var tags = Models.SessionInfo.ParseTags(vm.TagsText);
        if (tags.Exists(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase))) return;
        tags.Add(tag);
        vm.TagsText = string.Join(", ", tags);
    }

    // Bấm Tab (hoặc click) vào ô Port sẽ chọn sẵn toàn bộ nội dung, chỉ cần gõ số mới
    // đè lên mà không cần xóa giá trị mặc định trước.
    private void SelectAllOnFocus_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    private void SelectAllOnFocus_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
        }
    }
}
