using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.Views;

namespace WNTerm.ViewModels;

public partial class SftpViewModel : ObservableObject, IDisposable
{
    private readonly SessionInfo _session;
    private readonly SshConnectionFactory _factory;
    private readonly AppSettings? _appSettings;
    private readonly Dictionary<int, string> _userMap = new() { { 0, "root" } };
    private readonly Dictionary<int, string> _groupMap = new() { { 0, "root" } };
    private readonly List<FileSystemWatcher> _watchers = new();

    private SftpClient? _client;
    private string _homeDirectory = "";

    [ObservableProperty]
    private string currentPath = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool showHiddenFiles = true;

    [ObservableProperty]
    private SftpItem? selectedItem;

    [ObservableProperty]
    private string statusMessage = "";

    public ObservableCollection<SftpItem> Items { get; } = new();
    public TransferQueueViewModel TransferQueue { get; } = new();

    public SftpViewModel(SessionInfo session, SshConnectionFactory factory, AppSettings? appSettings = null)
    {
        _session = session;
        _factory = factory;
        _appSettings = appSettings;
        if (appSettings != null)
        {
            ShowHiddenFiles = appSettings.ShowHiddenFiles;
        }
    }

    private async Task LoadUserGroupMapsAsync()
    {
        if (_client == null || !_client.IsConnected) return;

        await Task.Run(() =>
        {
            try
            {
                if (_client.Exists("/etc/passwd"))
                {
                    using var stream = _client.OpenRead("/etc/passwd");
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 3 && int.TryParse(parts[2], out int uid) && !string.IsNullOrEmpty(parts[0]))
                        {
                            _userMap[uid] = parts[0];
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (_client.Exists("/etc/group"))
                {
                    using var stream = _client.OpenRead("/etc/group");
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 3 && int.TryParse(parts[2], out int gid) && !string.IsNullOrEmpty(parts[0]))
                        {
                            _groupMap[gid] = parts[0];
                        }
                    }
                }
            }
            catch { }
        });
    }

    public async Task<bool> CheckIsDirectoryAsync(string path)
    {
        if (_client == null || !_client.IsConnected) return false;
        return await Task.Run(() =>
        {
            try
            {
                var attrs = _client.GetAttributes(path);
                return attrs.IsDirectory;
            }
            catch
            {
                return false;
            }
        });
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
            await LoadUserGroupMapsAsync();

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
            if (_userMap.Count <= 1)
            {
                await LoadUserGroupMapsAsync();
            }

            var items = await Task.Run(() =>
            {
                var list = _client.ListDirectory(path).ToList();

                var fileItems = list
                    .Where(f => f.Name != "." && f.Name != "..")
                    .Where(f => ShowHiddenFiles || !f.Name.StartsWith("."))
                    .Select(f =>
                    {
                        bool isDir = f.IsDirectory;
                        bool isSymlink = f.IsSymbolicLink;
                        if (isSymlink)
                        {
                            try
                            {
                                var attrs = _client.GetAttributes(f.FullName);
                                if (attrs.IsDirectory)
                                {
                                    isDir = true;
                                }
                            }
                            catch { }
                        }

                        string ownerName = _userMap.TryGetValue(f.UserId, out var u) ? u : (f.UserId == 0 ? "root" : f.UserId.ToString());
                        string groupName = _groupMap.TryGetValue(f.GroupId, out var g) ? g : (f.GroupId == 0 ? "root" : f.GroupId.ToString());

                        char typeChar = isDir ? 'd' : (isSymlink ? 'l' : (f.IsSocket ? 's' : (f.IsNamedPipe ? 'p' : '-')));
                        string perms = $"{typeChar}" +
                                       $"{(f.OwnerCanRead ? 'r' : '-')}{(f.OwnerCanWrite ? 'w' : '-')}{(f.OwnerCanExecute ? 'x' : '-')}" +
                                       $"{(f.GroupCanRead ? 'r' : '-')}{(f.GroupCanWrite ? 'w' : '-')}{(f.GroupCanExecute ? 'x' : '-')}" +
                                       $"{(f.OthersCanRead ? 'r' : '-')}{(f.OthersCanWrite ? 'w' : '-')}{(f.OthersCanExecute ? 'x' : '-')}";

                        return new SftpItem
                        {
                            Name = f.Name,
                            FullName = f.FullName,
                            IsDirectory = isDir,
                            IsSymbolicLink = isSymlink,
                            Size = f.Length,
                            LastModified = f.LastWriteTime,
                            UserId = f.UserId,
                            GroupId = f.GroupId,
                            Owner = ownerName,
                            Group = groupName,
                            Permissions = perms
                        };
                    })
                    .OrderByDescending(f => f.IsDirectory)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string normPath = path.Replace('\\', '/').Trim();
                if (normPath.Length > 1) normPath = normPath.TrimEnd('/');
                if (string.IsNullOrEmpty(normPath)) normPath = "/";

                string parent = (normPath == "/" || string.IsNullOrEmpty(normPath))
                    ? "/"
                    : (Path.GetDirectoryName(normPath)?.Replace('\\', '/') ?? "/");
                if (string.IsNullOrEmpty(parent)) parent = "/";

                fileItems.Insert(0, new SftpItem
                {
                    Name = "..",
                    FullName = parent,
                    IsDirectory = true,
                    IsParentDirectory = true,
                    Permissions = "",
                    Owner = "",
                    Group = ""
                });

                return fileItems;
            });

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            CurrentPath = path;
            StatusMessage = LocalizationManager.Get("Str_ItemsCount", Items.Count(i => !i.IsParentDirectory));
        }
        catch (SftpPermissionDeniedException)
        {
            StatusMessage = LocalizationManager.Get("Str_PermissionDenied");
            MessageBox.Show(LocalizationManager.Get("Str_PermissionDenied"), LocalizationManager.Get("Str_PermissionDeniedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{LocalizationManager.Get("Str_Error")}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        if (CurrentPath == "/")
        {
            await LoadDirectoryAsync("/");
            return;
        }
        string normPath = CurrentPath.TrimEnd('/');
        string parent = Path.GetDirectoryName(normPath)?.Replace('\\', '/') ?? "/";
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
        if (_appSettings != null)
        {
            _appSettings.ShowHiddenFiles = value;
        }
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task CreateDirectoryAsync()
    {
        if (_client == null || !_client.IsConnected) return;

        var inputDlg = new InputDialog("Tạo thư mục mới", "Nhập tên thư mục:");
        inputDlg.Owner = Application.Current?.MainWindow;

        if (inputDlg.ShowDialog() != true) return;

        string folderName = inputDlg.InputText.Trim();
        if (string.IsNullOrWhiteSpace(folderName)) return;

        string newPath = $"{CurrentPath.TrimEnd('/')}/{folderName.Trim()}";

        try
        {
            await Task.Run(() => _client.CreateDirectory(newPath));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.Get("Str_ErrorCreateDir", ex.Message), LocalizationManager.Get("Str_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteItemsAsync(System.Collections.IList? list)
    {
        if (_client == null || !_client.IsConnected || list == null) return;
        var toDelete = list.OfType<SftpItem>().Where(i => !i.IsParentDirectory && i.Name != "..").ToList();
        if (toDelete.Count == 0) return;

        string prompt = toDelete.Count == 1
            ? LocalizationManager.Get("Str_ConfirmDeletePrompt", toDelete[0].Name)
            : LocalizationManager.Get("Str_ConfirmDeleteMultiplePrompt", toDelete.Count);

        if (MessageBox.Show(prompt, LocalizationManager.Get("Str_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
                        DeleteDirectoryRecursive(item.FullName);
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
            MessageBox.Show(LocalizationManager.Get("Str_ErrorDelete", ex.Message), LocalizationManager.Get("Str_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void DeleteDirectoryRecursive(string path)
    {
        if (_client == null) return;
        var files = _client.ListDirectory(path);
        foreach (var file in files)
        {
            if (file.Name == "." || file.Name == "..") continue;
            if (file.IsDirectory)
            {
                DeleteDirectoryRecursive(file.FullName);
            }
            else
            {
                _client.DeleteFile(file.FullName);
            }
        }
        _client.DeleteDirectory(path);
    }

    [RelayCommand]
    public async Task RenameItemAsync(SftpItem? item)
    {
        item ??= SelectedItem;
        if (_client == null || !_client.IsConnected || item == null || item.IsParentDirectory || item.Name == "..") return;

        var inputDlg = new InputDialog("Đổi tên", "Nhập tên mới:", item.Name);
        inputDlg.Owner = Application.Current?.MainWindow;

        if (inputDlg.ShowDialog() != true) return;

        string newName = inputDlg.InputText.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        string parent = Path.GetDirectoryName(item.FullName)?.Replace('\\', '/') ?? CurrentPath;
        string newPath = $"{parent.TrimEnd('/')}/{newName}";

        try
        {
            await Task.Run(() => _client.RenameFile(item.FullName, newPath));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.Get("Str_ErrorRename", ex.Message), LocalizationManager.Get("Str_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    
    public async Task ChangePermissionsAsync(IEnumerable<SftpItem> items, short permissions, bool recursive)
    {
        if (_client == null || !_client.IsConnected) return;

        IsLoading = true;
        StatusMessage = LocalizationManager.Get("Str_ChangingPermissions");

        try
        {
            var targetList = items.Where(i => !i.IsParentDirectory && i.Name != "..").ToList();
            await Task.Run(() =>
            {
                foreach (var item in targetList)
                {
                    ApplyChmod(_client, item.FullName, item.IsDirectory, permissions, recursive);
                }
            });

            StatusMessage = LocalizationManager.Get("Str_PermissionsSuccess");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.Get("Str_CannotChangePerms", ex.Message);
            MessageBox.Show(LocalizationManager.Get("Str_CannotChangePerms", ex.Message), LocalizationManager.Get("Str_PermissionDeniedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyChmod(SftpClient client, string path, bool isDir, short permissions, bool recursive)
    {
        try
        {
            client.ChangePermissions(path, permissions);
        }
        catch { }

        if (recursive && isDir)
        {
            try
            {
                var children = client.ListDirectory(path);
                foreach (var child in children)
                {
                    if (child.Name == "." || child.Name == "..") continue;
                    ApplyChmod(client, child.FullName, child.IsDirectory, permissions, true);
                }
            }
            catch { }
        }
    }

    [RelayCommand]
    public async Task EditFileDefaultAsync(SftpItem? item)
    {
        item ??= SelectedItem;
        if (item == null || item.IsDirectory || item.IsParentDirectory || item.Name == "..") return;

        string? customPath = _appSettings?.CustomEditorPath;
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            await EditFileAsync(item, customPath);
        }
        else
        {
            await EditFileAsync(item, null);
        }
    }

    [RelayCommand]
    public async Task EditFileWithAppAsync(SftpItem? item)
    {
        item ??= SelectedItem;
        if (item == null || item.IsDirectory || item.IsParentDirectory || item.Name == "..") return;

        var dlg = new OpenFileDialog
        {
            Title = "Chọn ứng dụng để sửa file",
            Filter = "Chương trình (*.exe)|*.exe|Tất cả file (*.*)|*.*"
        };

        if (dlg.ShowDialog(Application.Current?.MainWindow) == true)
        {
            await EditFileAsync(item, dlg.FileName);
        }
    }

    private async Task EditFileAsync(SftpItem item, string? editorExecutable)
    {
        if (_client == null || !_client.IsConnected) return;

        IsLoading = true;
        StatusMessage = $"Đang tải {item.Name} để sửa...";

        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WNTerm_Edit", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string localFilePath = Path.Combine(tempDir, item.Name);

            using (var fileStream = File.Create(localFilePath))
            {
                await Task.Run(() => _client.DownloadFile(item.FullName, fileStream));
            }

            var watcher = new FileSystemWatcher(tempDir, item.Name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            DateTime lastUpload = DateTime.MinValue;
            watcher.Changed += async (s, e) =>
            {
                if ((DateTime.UtcNow - lastUpload).TotalMilliseconds < 800) return;
                lastUpload = DateTime.UtcNow;

                await Task.Delay(300);

                try
                {
                    using var stream = File.OpenRead(localFilePath);
                    if (_client?.IsConnected == true)
                    {
                        await Task.Run(() => _client.UploadFile(stream, item.FullName, true));
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            StatusMessage = LocalizationManager.Get("Str_AutoSavedStatus", item.Name);
                            await RefreshAsync();
                        });
                    }
                }
                catch { }
            };

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);

            if (!string.IsNullOrEmpty(editorExecutable) && File.Exists(editorExecutable))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editorExecutable,
                    Arguments = $"\"{localFilePath}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = localFilePath,
                    UseShellExecute = true
                });
            }

            StatusMessage = LocalizationManager.Get("Str_OpeningFileStatus", item.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.Get("Str_ErrorOpenFile", ex.Message), LocalizationManager.Get("Str_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UploadPathsAsync(IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        await UploadFilesAsync(paths, CurrentPath);
    }

    public async Task UploadFilesAsync(IEnumerable<string> filePaths, string remoteDirectory)
    {
        if (_client == null || !_client.IsConnected) return;

        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                await UploadSingleFileAsync(path, remoteDirectory);
            }
            else if (Directory.Exists(path))
            {
                await UploadDirectoryRecursiveAsync(path, remoteDirectory);
            }
        }

        await RefreshAsync();
    }

    private async Task UploadSingleFileAsync(string localPath, string remoteDirectory)
    {
        if (_client == null) return;
        string fileName = Path.GetFileName(localPath);
        string remotePath = $"{remoteDirectory.TrimEnd('/')}/{fileName}";

        TransferQueue.StartTransfer($"Upload: {fileName}");

        try
        {
            using var fileStream = File.OpenRead(localPath);
            ulong totalBytes = (ulong)fileStream.Length;
            DateTime lastTime = DateTime.UtcNow;
            ulong lastTransferred = 0;

            await Task.Run(() =>
            {
                _client.UploadFile(fileStream, remotePath, true, (transferred) =>
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
            try { _client.DeleteFile(remotePath); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.Get("Str_ErrorUpload", fileName, ex.Message), LocalizationManager.Get("Str_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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

        var validItems = items.Where(i => !i.IsParentDirectory && i.Name != "..").ToList();
        foreach (var item in validItems)
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
            MessageBox.Show(LocalizationManager.Get("Str_ErrorDownload", item.Name, ex.Message), LocalizationManager.Get("Str_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();

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
