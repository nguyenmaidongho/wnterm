using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.ViewModels;

namespace WNTerm.App.Views;

public partial class SftpPanel : UserControl
{
    /// <summary>So sánh SftpItemType của dòng với tên trong ConverterParameter (thay DataTrigger của WPF).</summary>
    public static readonly IValueConverter ItemTypeIs = new ItemTypeEqualsConverter();

    private sealed class ItemTypeEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private SftpViewModel? _hooked;

    private SftpViewModel? ViewModel => DataContext as SftpViewModel;

    public SftpPanel()
    {
        InitializeComponent();

        // Tổng độ rộng các cột tối thiểu 525; rộng hơn thì cột Tên co giãn, hẹp hơn thì cuộn ngang (như WPF).
        ListScroll.SizeChanged += (_, e) => ListGrid.Width = Math.Max(525, e.NewSize.Width);
        FileListView.AddHandler(DragDrop.DragOverEvent, FileListView_DragOver);
        FileListView.AddHandler(DragDrop.DropEvent, FileListView_Drop);
    }


    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_hooked != null) _hooked.PropertyChanged -= Vm_PropertyChanged;
        _hooked = ViewModel;
        if (_hooked != null)
        {
            _hooked.PropertyChanged += Vm_PropertyChanged;
            PathTextBox.Text = _hooked.CurrentPath;
        }
        else
        {
            PathTextBox.Text = "";
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SftpViewModel.CurrentPath))
            Ui.Run(() => PathTextBox.Text = _hooked?.CurrentPath ?? "");
    }

    // Rời ô đường dẫn mà chưa bấm Enter: trả lại đường dẫn đang mở (chỉ Enter mới chuyển thư mục),
    // tránh CurrentPath bị đổi trong khi danh sách file vẫn là của thư mục cũ.
    private void PathTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null && PathTextBox.Text != ViewModel.CurrentPath)
            PathTextBox.Text = ViewModel.CurrentPath;
    }

    private void PathTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel != null)
        {
            _ = ViewModel.NavigateToAsync(PathTextBox.Text);
            e.Handled = true;
        }
    }

    private void FileContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item && (item.IsParentDirectory || item.Name == ".."))
            e.Cancel = true;
    }

    private void OpenItem(SftpItem item)
    {
        if (ViewModel == null) return;
        if (item.IsParentDirectory || item.Name == "..")
            _ = ViewModel.GoUpAsync();
        else if (item.IsDirectory)
            _ = ViewModel.NavigateToAsync(item.FullName);
        else
            _ = ViewModel.EditFileDefaultAsync(item);
    }

    private void FileListView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item) OpenItem(item);
    }

    private void FileListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileListView.SelectedItem is SftpItem item && ViewModel != null)
        {
            OpenItem(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && ViewModel != null)
        {
            _ = ViewModel.GoUpAsync();
            e.Handled = true;
        }
    }

    private async void UploadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        try
        {
            var files = await Ui.PickOpenFilesAsync(LocalizationManager.Tr("Select files to upload to SFTP", "Chọn file upload lên SFTP"));
            if (files.Count > 0) await ViewModel.UploadPathsAsync(files);
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Upload", "Tải lên"), ex.Message, DialogIcon.Error);
        }
    }

    private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var selected = FileListView.SelectedItems?.OfType<SftpItem>().ToList() ?? new List<SftpItem>();
        if (selected.Count == 0) return;

        // async void: mọi lỗi phải bắt ở đây, nếu không sẽ làm sập app.
        IStorageFolder? folder;
        string? local;
        try
        {
            folder = await Ui.PickFolderAsync(LocalizationManager.Tr("Select download folder", "Chọn thư mục lưu file tải về"));
            if (folder == null) return;
            local = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
            {
                await ViewModel.DownloadItemsAsync(selected, local);
                return;
            }
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Download", "Tải về"), ex.Message, DialogIcon.Error);
            return;
        }

        // Điện thoại: thư mục chọn là content:// → tải về thư mục tạm rồi chép sang.
        string? staging = null;
        try
        {
            staging = Ui.CreateStagingFolder();
            await ViewModel.DownloadItemsAsync(selected, staging);
            await Ui.CopyDirectoryToStorageAsync(staging, folder);
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Download", "Tải về"), ex.Message, DialogIcon.Error);
        }
        finally { if (staging != null) try { Directory.Delete(staging, true); } catch { } }
    }

    private static IReadOnlyList<string> GetDroppedPaths(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return Array.Empty<string>();
        return files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
    }

    private void FileListView_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileListView_Drop(object? sender, DragEventArgs e)
    {
        if (ViewModel == null) return;
        var paths = GetDroppedPaths(e);
        if (paths.Count > 0) _ = ViewModel.UploadPathsAsync(paths);
        e.Handled = true;
    }

    private void ContextMenuDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null && FileListView.SelectedItems?.Count > 0)
            _ = ViewModel.DeleteItemsAsync(FileListView.SelectedItems.Cast<object>().ToList());
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpItem item)
            await Ui.SetClipboardTextAsync(item.FullName);
    }

    private async void ChmodMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var selected = (FileListView.SelectedItems?.OfType<SftpItem>() ?? Enumerable.Empty<SftpItem>())
            .Where(i => !i.IsParentDirectory && i.Name != "..")
            .ToList();
        if (selected.Count == 0) return;

        string displayName = selected.Count == 1 ? selected[0].Name : LocalizationManager.Get("Str_ItemsCount", selected.Count);
        string currentPerms = selected[0].Permissions;
        bool hasDirectory = selected.Any(i => i.IsDirectory);

        var dlg = new ChmodDialog(displayName, currentPerms, hasDirectory);
        if (await Dialogs.ShowAsync(dlg) == true)
            await ViewModel.ChangePermissionsAsync(selected, dlg.PermissionsValue, dlg.IsRecursive);
    }
}
