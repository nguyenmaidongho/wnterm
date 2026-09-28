using SNTerm.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.ViewModels;

public partial class SftpViewModel : ObservableObject, IDisposable
{
    private readonly SessionInfo _session;
    private readonly SshConnectionFactory _factory;
    private SftpClient? _client;
    private string _homeDirectory = "";

    [ObservableProperty]
    private string currentPath = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool showHiddenFiles;

    [ObservableProperty]
    private SftpItem? selectedItem;

    [ObservableProperty]
    private string statusMessage = "";

    public ObservableCollection<SftpItem> Items { get; } = new();
    public TransferQueueViewModel TransferQueue { get; } = new();

    public SftpViewModel(SessionInfo session, SshConnectionFactory factory)
    {
        _session = session;
        _factory = factory;
    }

    public async Task InitializeAsync(string? password, string? passphrase)
    {
        if (_client != null && _client.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Đang kết nối SFTP...";

        try
        {
            var connInfo = _factory.CreateConnectionInfo(_session, password, passphrase);
            _client = new SftpClient(connInfo);

            _factory.AttachHostKeyVerification(_client, _session.Host, _session.Port);

            await Task.Run(() => _client.Connect());

            _homeDirectory = _client.WorkingDirectory;
            CurrentPath = _homeDirectory;

            await LoadDirectoryAsync(CurrentPath);
        }
        catch (Exception ex)
        {
            StatusMessage = ErrorTranslator.Translate(ex, _session.Host, _session.Port);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadDirectoryAsync(string path)
    {
        if (_client == null || !_client.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Đang tải danh sách file...";

        try
        {
            var list = await Task.Run(() => _client.ListDirectory(path).ToList());

            Items.Clear();

            var sorted = list
                .Where(f => f.Name != "." && f.Name != "..")
                .Where(f => ShowHiddenFiles || !f.Name.StartsWith("."))
                .OrderByDescending(f => f.IsDirectory)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var file in sorted)
            {
                Items.Add(new SftpItem
                {
                    Name = file.Name,
                    FullName = file.FullName,
                    IsDirectory = file.IsDirectory,
                    Size = file.Length,
                    LastModified = file.LastWriteTime,
                    Permissions = file.OwnerCanRead ? "r" : "-"
                });
            }

            CurrentPath = path;
            StatusMessage = $"{Items.Count} mục";
        }
        catch (SftpPermissionDeniedException)
        {
            StatusMessage = "Không có quyền truy cập thư mục này.";
            MessageBox.Show("Không có quyền truy cập thư mục này.", "Lỗi quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || CurrentPath == "/") return;
        string parent = Path.GetDirectoryName(CurrentPath)?.Replace('\\', '/') ?? "/";
        if (string.IsNullOrEmpty(parent)) parent = "/";
        await LoadDirectoryAsync(parent);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            await LoadDirectoryAsync(CurrentPath);
        }
    }

    [RelayCommand]
    public async Task GoHomeAsync()
    {
        if (!string.IsNullOrEmpty(_homeDirectory))
        {
            await LoadDirectoryAsync(_homeDirectory);
        }
    }

    [RelayCommand]
    public async Task NavigateToAsync(string? path)
    {
        path ??= CurrentPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await LoadDirectoryAsync(path.Trim());
        }
    }

    partial void OnShowHiddenFilesChanged(bool value)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task CreateDirectoryAsync()
    {
        if (_client == null || !_client.IsConnected) return;

        string? folderName = InputDialog.Show(Application.Current?.MainWindow, "Nhập tên thư mục mới:", "Tạo thư mục SFTP", "new_folder");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        string newPath = $"{CurrentPath.TrimEnd('/')}/{folderName.Trim()}";

        try
        {
            await Task.Run(() => _client.CreateDirectory(newPath));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không tạo được thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteItemsAsync(System.Collections.IList? list)
    {
        if (_client == null || !_client.IsConnected || list == null) return;
        var toDelete = list.OfType<SftpItem>().ToList();
        if (toDelete.Count == 0) return;

        string prompt = toDelete.Count == 1
            ? $"Bạn có chắc chắn muốn xóa '{toDelete[0].Name}'?"
            : $"Bạn có chắc chắn muốn xóa {toDelete.Count} mục đã chọn?";

        if (MessageBox.Show(prompt, "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var item in toDelete)
                {
                    if (item.IsDirectory)
                    {
                        DeleteDirectoryRecursive(_client, item.FullName);
                    }
                    else
                    {
                        _client.DeleteFile(item.FullName);
                    }
                }
            });

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi xóa: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void DeleteDirectoryRecursive(SftpClient client, string path)
    {
        foreach (var file in client.ListDirectory(path))
        {
            if (file.Name == "." || file.Name == "..") continue;
            if (file.IsDirectory)
                DeleteDirectoryRecursive(client, file.FullName);
            else
                client.DeleteFile(file.FullName);
        }
        client.DeleteDirectory(path);
    }

    [RelayCommand]
    public async Task RenameItemAsync(SftpItem? item)
    {
        item ??= SelectedItem;
        if (item == null || _client == null || !_client.IsConnected) return;

        string? newName = InputDialog.Show(Application.Current?.MainWindow, "Nhập tên mới:", "Đổi tên file/thư mục", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        string newFullPath = $"{CurrentPath.TrimEnd('/')}/{newName.Trim()}";
        try
        {
            await Task.Run(() => _client.RenameFile(item.FullName, newFullPath));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không đổi tên được: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task UploadPathsAsync(IEnumerable<string> localPaths)
    {
        if (_client == null || !_client.IsConnected) return;

        foreach (var path in localPaths)
        {
            if (File.Exists(path))
            {
                await UploadSingleFileAsync(path, CurrentPath);
            }
            else if (Directory.Exists(path))
            {
                await UploadDirectoryRecursiveAsync(path, CurrentPath);
            }
        }

        await RefreshAsync();
    }

    private async Task UploadSingleFileAsync(string localFile, string remoteFolder)
    {
        if (_client == null) return;
        string fileName = Path.GetFileName(localFile);
        string remotePath = $"{remoteFolder.TrimEnd('/')}/{fileName}";

        TransferQueue.StartTransfer($"Upload: {fileName}");

        try
        {
            using var fileStream = File.OpenRead(localFile);
            ulong totalBytes = (ulong)fileStream.Length;
            DateTime lastTime = DateTime.UtcNow;
            ulong lastTransferred = 0;

            await Task.Run(() =>
            {
                _client.UploadFile(fileStream, remotePath, (transferred) =>
                {
                    if (TransferQueue.Token.IsCancellationRequested)
                    {
                        try { _client.DeleteFile(remotePath); } catch { }
                        return;
                    }

                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastTime).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        double speed = (transferred - lastTransferred) / elapsed;
                        lastTime = now;
                        lastTransferred = transferred;
                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            TransferQueue.UpdateProgress(transferred, totalBytes, speed);
                        });
                    }
                });
            }, TransferQueue.Token);
        }
        catch (OperationCanceledException)
        {
            try { _client.DeleteFile(remotePath); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi upload {fileName}: {ex.Message}", "Lỗi Upload", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TransferQueue.EndTransfer();
        }
    }

    private async Task UploadDirectoryRecursiveAsync(string localDir, string remoteParent)
    {
        if (_client == null) return;
        string dirName = Path.GetFileName(localDir);
        string remoteDir = $"{remoteParent.TrimEnd('/')}/{dirName}";

        await Task.Run(() =>
        {
            if (!_client.Exists(remoteDir))
                _client.CreateDirectory(remoteDir);
        });

        foreach (var file in Directory.GetFiles(localDir))
        {
            await UploadSingleFileAsync(file, remoteDir);
        }

        foreach (var subDir in Directory.GetDirectories(localDir))
        {
            await UploadDirectoryRecursiveAsync(subDir, remoteDir);
        }
    }

    public async Task DownloadItemsAsync(IEnumerable<SftpItem> items, string destinationFolder)
    {
        if (_client == null || !_client.IsConnected) return;

        foreach (var item in items)
        {
            if (item.IsDirectory)
            {
                await DownloadDirectoryRecursiveAsync(item.FullName, Path.Combine(destinationFolder, item.Name));
            }
            else
            {
                await DownloadSingleFileAsync(item, destinationFolder);
            }
        }
    }

    private async Task DownloadSingleFileAsync(SftpItem item, string localFolder)
    {
        if (_client == null) return;
        string localPath = Path.Combine(localFolder, item.Name);

        TransferQueue.StartTransfer($"Download: {item.Name}");

        try
        {
            using var fileStream = File.Create(localPath);
            ulong totalBytes = (ulong)item.Size;
            DateTime lastTime = DateTime.UtcNow;
            ulong lastTransferred = 0;

            await Task.Run(() =>
            {
                _client.DownloadFile(item.FullName, fileStream, (transferred) =>
                {
                    if (TransferQueue.Token.IsCancellationRequested) return;

                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastTime).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        double speed = (transferred - lastTransferred) / elapsed;
                        lastTime = now;
                        lastTransferred = transferred;
                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            TransferQueue.UpdateProgress(transferred, totalBytes, speed);
                        });
                    }
                });
            }, TransferQueue.Token);
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(localPath); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi tải file {item.Name}: {ex.Message}", "Lỗi Download", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TransferQueue.EndTransfer();
        }
    }

    private async Task DownloadDirectoryRecursiveAsync(string remoteDir, string localDir)
    {
        if (_client == null) return;
        Directory.CreateDirectory(localDir);

        var list = await Task.Run(() => _client.ListDirectory(remoteDir).ToList());

        foreach (var file in list)
        {
            if (file.Name == "." || file.Name == "..") continue;

            if (file.IsDirectory)
            {
                await DownloadDirectoryRecursiveAsync(file.FullName, Path.Combine(localDir, file.Name));
            }
            else
            {
                var sftpItem = new SftpItem
                {
                    Name = file.Name,
                    FullName = file.FullName,
                    Size = file.Length
                };
                await DownloadSingleFileAsync(sftpItem, localDir);
            }
        }
    }

    public void Dispose()
    {
        TransferQueue.Cancel();
        try
        {
            if (_client?.IsConnected == true)
            {
                _client.Disconnect();
            }
            _client?.Dispose();
        }
        catch { }
        _client = null;
    }
}


