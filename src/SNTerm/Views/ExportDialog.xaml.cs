using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.Views;

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
            MessageBox.Show("Vui lòng chọn ít nhất một VM để export.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? exportPassword = null;
        if (WithPasswordRadio.IsChecked == true)
        {
            string pass1 = ExportPasswordBox.Password;
            string pass2 = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(pass1) || pass1.Length < 8)
            {
                MessageBox.Show("Mật khẩu Export phải có tối thiểu 8 ký tự.", "Lỗi mật khẩu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (pass1 != pass2)
            {
                MessageBox.Show("Mật khẩu xác nhận không khớp.", "Lỗi mật khẩu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            exportPassword = pass1;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Lưu file Export VM",
            Filter = "SN Term Export (*.snterm)|*.snterm",
            FileName = $"snterm_backup_{DateTime.Now:yyyyMMdd_HHmmss}.snterm"
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

                MessageBox.Show($"Export thành công {selected.Count} VM!", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi export: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
