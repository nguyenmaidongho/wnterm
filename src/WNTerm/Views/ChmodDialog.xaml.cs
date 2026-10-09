using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WNTerm.Services;

namespace WNTerm.Views;

public partial class ChmodDialog : Window
{
    private bool _updating;

    public short PermissionsValue { get; private set; } = 493; // 755
    public bool IsRecursive => RecursiveCheckBox.IsChecked == true;

    public ChmodDialog(string targetName, string currentPerms, bool hasDirectory)
    {
        InitializeComponent();
        TargetBlock.Text = $"{LocalizationManager.Get("Str_Item")}{targetName}";
        if (hasDirectory)
        {
            RecursiveCheckBox.Visibility = Visibility.Visible;
        }

        // Đăng ký sự kiện
        ChkOwnerR.Click += (s, e) => UpdateFromCheckboxes();
        ChkOwnerW.Click += (s, e) => UpdateFromCheckboxes();
        ChkOwnerX.Click += (s, e) => UpdateFromCheckboxes();
        ChkGroupR.Click += (s, e) => UpdateFromCheckboxes();
        ChkGroupW.Click += (s, e) => UpdateFromCheckboxes();
        ChkGroupX.Click += (s, e) => UpdateFromCheckboxes();
        ChkOthersR.Click += (s, e) => UpdateFromCheckboxes();
        ChkOthersW.Click += (s, e) => UpdateFromCheckboxes();
        ChkOthersX.Click += (s, e) => UpdateFromCheckboxes();

        OctalBox.TextChanged += (s, e) => UpdateFromOctal();

        // Khởi tạo giá trị
        InitPermissions(currentPerms);

        Loaded += (s, e) =>
        {
            OctalBox.Focus();
            OctalBox.SelectAll();
        };
    }

    private void InitPermissions(string perms)
    {
        string octal = "755";
        if (!string.IsNullOrEmpty(perms) && perms.Length >= 10)
        {
            int o = (perms[1] == 'r' ? 4 : 0) + (perms[2] == 'w' ? 2 : 0) + (perms[3] == 'x' || perms[3] == 's' ? 1 : 0);
            int g = (perms[4] == 'r' ? 4 : 0) + (perms[5] == 'w' ? 2 : 0) + (perms[6] == 'x' || perms[6] == 's' ? 1 : 0);
            int a = (perms[7] == 'r' ? 4 : 0) + (perms[8] == 'w' ? 2 : 0) + (perms[9] == 'x' || perms[9] == 't' ? 1 : 0);
            octal = $"{o}{g}{a}";
        }
        OctalBox.Text = octal;
    }

    private void UpdateFromCheckboxes()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            int o = (ChkOwnerR.IsChecked == true ? 4 : 0) + (ChkOwnerW.IsChecked == true ? 2 : 0) + (ChkOwnerX.IsChecked == true ? 1 : 0);
            int g = (ChkGroupR.IsChecked == true ? 4 : 0) + (ChkGroupW.IsChecked == true ? 2 : 0) + (ChkGroupX.IsChecked == true ? 1 : 0);
            int a = (ChkOthersR.IsChecked == true ? 4 : 0) + (ChkOthersW.IsChecked == true ? 2 : 0) + (ChkOthersX.IsChecked == true ? 1 : 0);
            OctalBox.Text = $"{o}{g}{a}";
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateFromOctal()
    {
        if (_updating) return;
        string text = OctalBox.Text.Trim();
        if (text.Length == 4 && text.StartsWith("0"))
        {
            text = text[1..];
        }
        if (text.Length != 3 || !Regex.IsMatch(text, "^[0-7]{3}$")) return;

        _updating = true;
        try
        {
            int o = text[0] - '0';
            int g = text[1] - '0';
            int a = text[2] - '0';

            ChkOwnerR.IsChecked = (o & 4) != 0;
            ChkOwnerW.IsChecked = (o & 2) != 0;
            ChkOwnerX.IsChecked = (o & 1) != 0;

            ChkGroupR.IsChecked = (g & 4) != 0;
            ChkGroupW.IsChecked = (g & 2) != 0;
            ChkGroupX.IsChecked = (g & 1) != 0;

            ChkOthersR.IsChecked = (a & 4) != 0;
            ChkOthersW.IsChecked = (a & 2) != 0;
            ChkOthersX.IsChecked = (a & 1) != 0;
        }
        finally
        {
            _updating = false;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string text = OctalBox.Text.Trim();
        if (text.Length == 4 && text.StartsWith("0"))
        {
            text = text[1..];
        }

        if (text.Length != 3 || !Regex.IsMatch(text, "^[0-7]{3}$"))
        {
            MessageBox.Show(LocalizationManager.Get("Str_InvalidOctal"),
                            LocalizationManager.Get("Str_InputErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            OctalBox.Focus();
            return;
        }

        try
        {
            PermissionsValue = Convert.ToInt16(text, 8);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
