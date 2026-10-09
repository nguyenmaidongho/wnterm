using System.Windows;

namespace WNTerm.Views;

public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;

    public InputDialog(string prompt, string title, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptBlock.Text = prompt;
        InputBox.Text = defaultValue;
        Loaded += (s, e) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string? Show(Window? owner, string prompt, string title, string defaultValue = "")
    {
        var dlg = new InputDialog(prompt, title, defaultValue)
        {
            Owner = owner ?? Application.Current?.MainWindow
        };
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }
}
