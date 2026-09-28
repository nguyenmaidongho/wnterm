using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SNTerm.Models;
using SNTerm.ViewModels;

namespace SNTerm.Views;

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

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item && item.IsDirectory && ViewModel != null)
        {
            _ = ViewModel.NavigateToAsync(item.FullName);
        }
    }

    private void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Chọn file upload lên SFTP",
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
            Title = "Chọn thư mục lưu file tải về"
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
}
