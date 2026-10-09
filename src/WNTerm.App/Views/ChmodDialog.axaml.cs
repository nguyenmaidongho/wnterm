using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WNTerm.App.Services;
using WNTerm.Services;

namespace WNTerm.App.Views;

public partial class ChmodDialog : DialogView<bool?>
{
    private bool _updating;

    /// <summary>Chữ số bát phân viết như số thập phân (vd. 755) — quy ước của SSH.NET ChangePermissions.</summary>
    public short PermissionsValue { get; private set; } = 755;
    public bool IsRecursive => RecursiveCheckBox.IsChecked == true;

    public ChmodDialog() : this("", "", false) { }

    public ChmodDialog(string targetName, string currentPerms, bool hasDirectory)
    {
        InitializeComponent();
        Title = LocalizationManager.Get("Str_ChmodTitle");
        DialogWidth = 380;

        TargetBlock.Text = $"{LocalizationManager.Get("Str_Item")}{targetName}";
        if (hasDirectory) RecursiveCheckBox.IsVisible = true;

        foreach (var c in new[] { ChkOwnerR, ChkOwnerW, ChkOwnerX, ChkGroupR, ChkGroupW, ChkGroupX, ChkOthersR, ChkOthersW, ChkOthersX })
            c.IsCheckedChanged += (_, _) => UpdateFromCheckboxes();

        OctalBox.TextChanged += (_, _) => UpdateFromOctal();

        InitPermissions(currentPerms);

        AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => { OctalBox.Focus(); OctalBox.SelectAll(); }, DispatcherPriority.Loaded);
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
        UpdateFromOctal();
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
        finally { _updating = false; }
    }

    private void UpdateFromOctal()
    {
        if (_updating) return;
        string text = (OctalBox.Text ?? "").Trim();
        if (text.Length == 4 && text.StartsWith("0")) text = text[1..];
        if (text.Length != 3 || !Regex.IsMatch(text, "^[0-7]{3}$")) return;

        _updating = true;
        try
        {
            int o = text[0] - '0', g = text[1] - '0', a = text[2] - '0';
            ChkOwnerR.IsChecked = (o & 4) != 0; ChkOwnerW.IsChecked = (o & 2) != 0; ChkOwnerX.IsChecked = (o & 1) != 0;
            ChkGroupR.IsChecked = (g & 4) != 0; ChkGroupW.IsChecked = (g & 2) != 0; ChkGroupX.IsChecked = (g & 1) != 0;
            ChkOthersR.IsChecked = (a & 4) != 0; ChkOthersW.IsChecked = (a & 2) != 0; ChkOthersX.IsChecked = (a & 1) != 0;
        }
        finally { _updating = false; }
    }

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        string text = (OctalBox.Text ?? "").Trim();
        if (text.Length == 4 && text.StartsWith("0")) text = text[1..];

        if (text.Length != 3 || !Regex.IsMatch(text, "^[0-7]{3}$"))
        {
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_InputErrorTitle"), LocalizationManager.Get("Str_InvalidOctal"), DialogIcon.Warning);
            OctalBox.Focus();
            return;
        }

        try
        {
            // SSH.NET nhận "755" dạng số thập phân 755 rồi tự hiểu từng chữ số là bát phân.
            PermissionsValue = short.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            Close(true);
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_Error"), $"{LocalizationManager.Get("Str_Error")}: {ex.Message}", DialogIcon.Error);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
