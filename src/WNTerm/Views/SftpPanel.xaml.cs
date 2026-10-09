using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WNTerm.Models;
using WNTerm.ViewModels;
using WNTerm.Services;

namespace WNTerm.Views;

public partial class SftpPanel : UserControl
{
    private SftpViewModel? ViewModel => DataContext as SftpViewModel;

    public SftpPanel()
    {
        InitializeComponent();
    }

    private void PathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel != null)
        {
            _ = ViewModel.NavigateToAsync(PathTextBox.Text);
            e.Handled = true;
        }
    }

    private void FileListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item && (item.IsParentDirectory || item.Name == ".."))
        {
            e.Handled = true;
        }
    }

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item && ViewModel != null)
        {
            if (item.IsParentDirectory || item.Name == "..")
            {
                _ = ViewModel.GoUpAsync();
            }
            else if (item.IsDirectory)
            {
                _ = ViewModel.NavigateToAsync(item.FullName);
            }
            else
            {
                _ = ViewModel.EditFileDefaultAsync(item);
            }
        }
    }

    private void FileListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileListView.SelectedItem is SftpItem item && ViewModel != null)
        {
            if (item.IsParentDirectory || item.Name == "..")
            {
                _ = ViewModel.GoUpAsync();
            }
            else if (item.IsDirectory)
            {
                _ = ViewModel.NavigateToAsync(item.FullName);
            }
            else
            {
                _ = ViewModel.EditFileDefaultAsync(item);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Back && ViewModel != null)
        {
            _ = ViewModel.GoUpAsync();
            e.Handled = true;
        }
    }

    private void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var dlg = new OpenFileDialog
        {
            Title = WNTerm.Services.LocalizationManager.Tr("Select files to upload to SFTP", "Chọn file upload lên SFTP"),
            Multiselect = true
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            _ = ViewModel.UploadPathsAsync(dlg.FileNames);
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var selected = FileListView.SelectedItems.OfType<SftpItem>().ToList();
        if (selected.Count == 0) return;

        var dlg = new OpenFolderDialog
        {
            Title = WNTerm.Services.LocalizationManager.Tr("Select download folder", "Chọn thư mục lưu file tải về")
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            _ = ViewModel.DownloadItemsAsync(selected, dlg.FolderName);
        }
    }

    private void FileListView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FileListView_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && paths.Length > 0)
            {
                _ = ViewModel.UploadPathsAsync(paths);
            }
        }
    }

    private void ContextMenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && FileListView.SelectedItems.Count > 0)
        {
            _ = ViewModel.DeleteItemsAsync(FileListView.SelectedItems);
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item)
        {
            try { Clipboard.SetText(item.FullName); } catch { }
        }
    }

    private void ChmodMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var selected = FileListView.SelectedItems.OfType<SftpItem>()
            .Where(i => !i.IsParentDirectory && i.Name != "..")
            .ToList();
        if (selected.Count == 0) return;

        string displayName = selected.Count == 1 ? selected[0].Name : LocalizationManager.Get("Str_ItemsCount", selected.Count);
        string currentPerms = selected[0].Permissions;
        bool hasDirectory = selected.Any(i => i.IsDirectory);

        var dlg = new ChmodDialog(displayName, currentPerms, hasDirectory)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() == true)
        {
            _ = ViewModel.ChangePermissionsAsync(selected, dlg.PermissionsValue, dlg.IsRecursive);
        }
    }
}
