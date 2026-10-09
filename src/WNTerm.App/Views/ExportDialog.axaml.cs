using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

public partial class ExportDialog : DialogView<bool?>
{
    private readonly List<SessionInfo> _allSessions;

    public ExportDialog(IEnumerable<SessionInfo> allSessions, IEnumerable<SessionInfo>? preSelected = null)
    {
        InitializeComponent();
        Title = LocalizationManager.Get("Str_ExportTitle");
        DialogWidth = 520;

        _allSessions = allSessions.ToList();
        var list = SessionsList;
        list.ItemsSource = _allSessions;

        var selectedSet = preSelected != null ? new HashSet<Guid>(preSelected.Select(s => s.Id)) : null;
        if (selectedSet != null && selectedSet.Count > 0)
        {
            foreach (var item in _allSessions)
                if (selectedSet.Contains(item.Id)) list.SelectedItems!.Add(item);
        }
        else
        {
            list.SelectAll();
        }
    }


    private void SelectAll_Click(object? sender, RoutedEventArgs e) => SessionsList.SelectAll();
    private void DeselectAll_Click(object? sender, RoutedEventArgs e) => SessionsList.UnselectAll();

    private void PasswordMode_Changed(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<StackPanel>("PasswordFieldsPanel");
        if (panel != null) panel.IsEnabled = WithPasswordRadio.IsChecked == true;
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var selected = SessionsList.SelectedItems!.OfType<SessionInfo>().ToList();
        if (selected.Count == 0)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Notice", "Thông báo"), LocalizationManager.Tr("Select at least one VM to export.", "Vui lòng chọn ít nhất một VM để export."), DialogIcon.Warning);
            return;
        }

        string? exportPassword = null;
        if (WithPasswordRadio.IsChecked == true)
        {
            string pass1 = ExportPasswordBox.Text ?? "";
            string pass2 = ConfirmPasswordBox.Text ?? "";

            if (string.IsNullOrEmpty(pass1) || pass1.Length < 8)
            {
                await Dialogs.MessageAsync(LocalizationManager.Tr("Password error", "Lỗi mật khẩu"), LocalizationManager.Tr("The export password must be at least 8 characters.", "Mật khẩu Export phải có tối thiểu 8 ký tự."), DialogIcon.Warning);
                return;
            }

            if (pass1 != pass2)
            {
                await Dialogs.MessageAsync(LocalizationManager.Tr("Password error", "Lỗi mật khẩu"), LocalizationManager.Tr("Password confirmation does not match.", "Mật khẩu xác nhận không khớp."), DialogIcon.Warning);
                return;
            }

            exportPassword = pass1;
        }

        bool includeKey = IncludeKeyFileCheckBox.IsChecked == true;
        ExportBtn.IsEnabled = false;
        try
        {
            string? saved = await Ui.SaveFileAsync(
                LocalizationManager.Tr("Save VM export file", "Lưu file Export VM"),
                $"wnterm_backup_{DateTime.Now:yyyyMMdd_HHmmss}.wnterm",
                "wnterm", "WN Term Export (*.wnterm)", new[] { "*.wnterm" },
                path => Task.Run(() => new SessionExporter().Export(selected, path, exportPassword, includeKey)));
            if (saved == null) return;

            await Dialogs.MessageAsync(LocalizationManager.Tr("Done", "Hoàn tất"), string.Format(LocalizationManager.Tr("Exported {0} VMs successfully!", "Export thành công {0} VM!"), selected.Count));
            Close(true);
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Error", "Lỗi"), string.Format(LocalizationManager.Tr("Export failed: {0}", "Lỗi khi export: {0}"), ex.Message), DialogIcon.Error);
        }
        finally
        {
            ExportBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
