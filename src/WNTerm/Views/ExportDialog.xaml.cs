using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.Views;

public partial class ExportDialog : Window
{
    private readonly List<SessionInfo> _allSessions;

    public ExportDialog(IEnumerable<SessionInfo> sessions, IEnumerable<SessionInfo>? preSelected = null)
    {
        InitializeComponent();
        _allSessions = sessions.ToList();
        SessionsList.ItemsSource = _allSessions;

        var selectedSet = preSelected != null ? new HashSet<Guid>(preSelected.Select(s => s.Id)) : null;
        Loaded += (s, e) =>
        {
            if (selectedSet != null && selectedSet.Count > 0)
            {
                foreach (var item in _allSessions)
                {
                    if (selectedSet.Contains(item.Id))
                    {
                        SessionsList.SelectedItems.Add(item);
                    }
                }
            }
            else
            {
                SessionsList.SelectAll();
            }
        };
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SessionsList.SelectAll();
    private void DeselectAll_Click(object sender, RoutedEventArgs e) => SessionsList.UnselectAll();

    private void PasswordMode_Changed(object sender, RoutedEventArgs e)
    {
        if (PasswordFieldsPanel != null)
        {
            PasswordFieldsPanel.IsEnabled = WithPasswordRadio.IsChecked == true;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var selected = SessionsList.SelectedItems.OfType<SessionInfo>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(WNTerm.Services.LocalizationManager.Tr("Select at least one VM to export.", "Vui lòng chọn ít nhất một VM để export."), WNTerm.Services.LocalizationManager.Tr("Notice", "Thông báo"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? exportPassword = null;
        if (WithPasswordRadio.IsChecked == true)
        {
            string pass1 = ExportPasswordBox.Password;
            string pass2 = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(pass1) || pass1.Length < 8)
            {
                MessageBox.Show(WNTerm.Services.LocalizationManager.Tr("The export password must be at least 8 characters.", "Mật khẩu Export phải có tối thiểu 8 ký tự."), WNTerm.Services.LocalizationManager.Tr("Password error", "Lỗi mật khẩu"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (pass1 != pass2)
            {
                MessageBox.Show(WNTerm.Services.LocalizationManager.Tr("Password confirmation does not match.", "Mật khẩu xác nhận không khớp."), WNTerm.Services.LocalizationManager.Tr("Password error", "Lỗi mật khẩu"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            exportPassword = pass1;
        }

        var dlg = new SaveFileDialog
        {
            Title = WNTerm.Services.LocalizationManager.Tr("Save VM export file", "Lưu file Export VM"),
            Filter = "WN Term Export (*.wnterm)|*.wnterm",
            FileName = $"wnterm_backup_{DateTime.Now:yyyyMMdd_HHmmss}.wnterm"
        };

        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                var exporter = new SessionExporter();
                exporter.Export(
                    selected,
                    dlg.FileName,
                    exportPassword,
                    IncludeKeyFileCheckBox.IsChecked == true);

                MessageBox.Show(string.Format(WNTerm.Services.LocalizationManager.Tr("Exported {0} VMs successfully!", "Export thành công {0} VM!"), selected.Count), WNTerm.Services.LocalizationManager.Tr("Done", "Hoàn tất"), MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(WNTerm.Services.LocalizationManager.Tr("Export failed: {0}", "Lỗi khi export: {0}"), ex.Message), WNTerm.Services.LocalizationManager.Tr("Error", "Lỗi"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
