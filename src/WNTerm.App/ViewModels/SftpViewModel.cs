using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.App.Services;

namespace WNTerm.ViewModels;

public partial class SftpViewModel : ObservableObject, IDisposable
{
    private readonly SessionInfo _session;
    private readonly SshConnectionFactory _factory;
    private readonly AppSettings? _appSettings;
    private readonly Dictionary<int, string> _userMap = new() { { 0, "root" } };
    private readonly Dictionary<int, string> _groupMap = new() { { 0, "root" } };

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
        StatusMessage = LocalizationManager.Tr("Connecting to SFTP...", "Đang kết nối SFTP...");

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
        StatusMessage = LocalizationManager.Tr("Loading file list...", "Đang tải danh sách file...");

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
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_PermissionDeniedTitle"), LocalizationManager.Get("Str_PermissionDenied"), DialogIcon.Warning);
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

        string? input = await Dialogs.PromptAsync(LocalizationManager.Tr("New folder", "Tạo thư mục mới"), LocalizationManager.Tr("Enter folder name:", "Nhập tên thư mục:"));
        if (input == null) return;

        string folderName = input.Trim();
        if (string.IsNullOrWhiteSpace(folderName)) return;

        string newPath = $"{CurrentPath.TrimEnd('/')}/{folderName.Trim()}";

        try
        {
            await Task.Run(() => _client.CreateDirectory(newPath));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_Error"), LocalizationManager.Get("Str_ErrorCreateDir", ex.Message), DialogIcon.Error);
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

        if (!await Dialogs.ConfirmAsync(LocalizationManager.Get("Str_ConfirmDeleteTitle"), prompt, DialogIcon.Warning))
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
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_Error"), LocalizationManager.Get("Str_ErrorDelete", ex.Message), DialogIcon.Error);
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

        string? input = await Dialogs.PromptAsync(LocalizationManager.Tr("Rename", "Đổi tên"), LocalizationManager.Tr("Enter new name:", "Nhập tên mới:"), item.Name);
        if (input == null) return;

        string newName = input.Trim();
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
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_Error"), LocalizationManager.Get("Str_ErrorRename", ex.Message), DialogIcon.Error);
        }
    }

    /// <summary>
    /// Đổi quyền. <paramref name="permissions"/> là các chữ số bát phân viết như số thập phân (vd. 755 cho rwxr-xr-x)
    /// — đúng quy ước của SSH.NET ChangePermissions/SetPermissions (KHÔNG phải giá trị đã đổi sang cơ số 8 như 493).
    /// </summary>
    public async Task ChangePermissionsAsync(IEnumerable<SftpItem> items, short permissions, bool recursive)
    {
        var client = _client;
        if (client == null || !client.IsConnected) return;

        IsLoading = true;
        StatusMessage = LocalizationManager.Get("Str_ChangingPermissions");

        var result = new ChmodResult();
        try
        {
            var targetList = items.Where(i => !i.IsParentDirectory && i.Name != "..").ToList();
            await Task.Run(() =>
            {
                foreach (var item in targetList)
                {
                    ApplyChmod(client, item.FullName, item.IsDirectory, permissions, recursive, result);
                }
            });

            await RefreshAsync();

            if (result.Failed == 0)
            {
                StatusMessage = LocalizationManager.Get("Str_PermissionsSuccess");
            }
            else
            {
                string msg = string.Format(
                    LocalizationManager.Tr("Changed permissions of {0} item(s), {1} failed: {2}", "Đã đổi quyền {0} mục, lỗi {1} mục: {2}"),
                    result.Ok, result.Failed, result.FirstError);
                StatusMessage = msg;
                await Dialogs.MessageAsync(LocalizationManager.Get("Str_PermissionDeniedTitle"), msg, DialogIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.Get("Str_CannotChangePerms", ex.Message);
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_PermissionDeniedTitle"), LocalizationManager.Get("Str_CannotChangePerms", ex.Message), DialogIcon.Warning);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private sealed class ChmodResult
    {
        public int Ok;
        public int Failed;
        public string? FirstError;

        public void Fail(string path, Exception ex)
        {
            Failed++;
            FirstError ??= $"{path}: {ex.Message}";
        }
    }

    private static void ApplyChmod(SftpClient client, string path, bool isDir, short permissions, bool recursive, ChmodResult result)
    {
        try
        {
            client.ChangePermissions(path, permissions);
            result.Ok++;
        }
        catch (Exception ex)
        {
            result.Fail(path, ex);
        }

        if (recursive && isDir)
        {
            List<ISftpFile> children;
            try
            {
                children = client.ListDirectory(path).ToList();
            }
            catch (Exception ex)
            {
                result.Fail(path, ex);
                return;
            }

            foreach (var child in children)
            {
                if (child.Name == "." || child.Name == "..") continue;
                // Không đi theo symlink khi đệ quy (tránh vòng lặp / đổi quyền ngoài thư mục).
                ApplyChmod(client, child.FullName, child.IsDirectory && !child.IsSymbolicLink, permissions, true, result);
            }
        }
    }

    /// <summary>Quyền hiện tại dạng "chữ số bát phân viết như thập phân" (vd. 644) để truyền lại cho ChangePermissions.</summary>
    private static short ModeOf(SftpFileAttributes a)
    {
        int o = (a.OwnerCanRead ? 4 : 0) + (a.OwnerCanWrite ? 2 : 0) + (a.OwnerCanExecute ? 1 : 0);
        int g = (a.GroupCanRead ? 4 : 0) + (a.GroupCanWrite ? 2 : 0) + (a.GroupCanExecute ? 1 : 0);
        int t = (a.OthersCanRead ? 4 : 0) + (a.OthersCanWrite ? 2 : 0) + (a.OthersCanExecute ? 1 : 0);
        return (short)(o * 100 + g * 10 + t);
    }

    // ===== Sửa file từ xa =====

    /// <summary>Sửa file bằng ứng dụng ngoài chỉ có trên máy tính (Android/iOS không mở được file tạm bằng app khác).</summary>
    public bool CanEditExternally => !Ui.IsMobile;

    /// <summary>Một phiên sửa file: thư mục tạm + theo dõi thay đổi + hẹn giờ upload (debounce).</summary>
    private sealed class EditSession : IDisposable
    {
        public required string TempDir { get; init; }
        public required string LocalPath { get; init; }
        public required string RemotePath { get; init; }
        public required string Name { get; init; }
        public FileSystemWatcher? Watcher { get; set; }
        public Timer? Debounce { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int LockedRetries { get; set; }

        /// <summary>Đặt lại hẹn giờ: chỉ upload sau lần thay đổi CUỐI CÙNG + delayMs.</summary>
        public void Schedule(int delayMs)
        {
            try { Debounce?.Change(delayMs, Timeout.Infinite); } catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            try
            {
                if (Watcher != null)
                {
                    Watcher.EnableRaisingEvents = false;
                    Watcher.Dispose();
                }
            }
            catch { }
            try { Debounce?.Dispose(); } catch { }
        }
    }

    private readonly List<EditSession> _editSessions = new();
    private volatile bool _disposed;

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
        if (!CanEditExternally)
        {
            StatusMessage = LocalizationManager.Tr("Editing remote files is not supported on this device.", "Thiết bị này không hỗ trợ sửa file từ xa.");
            return;
        }

        // Không lọc theo *.exe: trên Linux/macOS file thực thi không có phần mở rộng.
        string? exe = await Ui.PickOpenFileAsync(LocalizationManager.Tr("Choose an application to edit the file", "Chọn ứng dụng để sửa file"));
        if (!string.IsNullOrEmpty(exe))
        {
            await EditFileAsync(item, exe);
        }
    }

    private async Task EditFileAsync(SftpItem item, string? editorExecutable)
    {
        var client = _client;
        if (client == null || !client.IsConnected) return;
        if (!CanEditExternally)
        {
            StatusMessage = LocalizationManager.Tr("Editing remote files is not supported on this device.", "Thiết bị này không hỗ trợ sửa file từ xa.");
            return;
        }

        IsLoading = true;
        StatusMessage = string.Format(LocalizationManager.Tr("Downloading {0} for editing...", "Đang tải {0} để sửa..."), item.Name);

        string tempDir = Path.Combine(Path.GetTempPath(), "WNTerm_Edit", Guid.NewGuid().ToString("N"));
        EditSession? session = null;
        try
        {
            Directory.CreateDirectory(tempDir);
            // Tên file từ máy chủ có thể chứa ký tự lạ/../ → làm sạch, không cho thoát khỏi thư mục tạm.
            string localFilePath = SafeLocalPath(tempDir, item.Name) ?? Path.Combine(tempDir, "remote-file");
            string fileName = Path.GetFileName(localFilePath);

            using (var fileStream = File.Create(localFilePath))
            {
                await Task.Run(() => client.DownloadFile(item.FullName, fileStream));
            }

            session = new EditSession
            {
                TempDir = tempDir,
                LocalPath = localFilePath,
                RemotePath = item.FullName,
                Name = item.Name
            };
            var es = session;
            es.Debounce = new Timer(_ => _ = UploadEditedFileAsync(es), null, Timeout.Infinite, Timeout.Infinite);

            var watcher = new FileSystemWatcher(tempDir, fileName)
            {
                // FileName: nhiều trình sửa lưu bằng cách ghi file tạm rồi đổi tên đè lên file gốc.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            FileSystemEventHandler onChange = (_, _) => es.Schedule(500);
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Renamed += (_, e) =>
            {
                if (string.Equals(e.FullPath, localFilePath, StringComparison.OrdinalIgnoreCase)) es.Schedule(500);
            };
            es.Watcher = watcher;
            watcher.EnableRaisingEvents = true;
            lock (_editSessions) _editSessions.Add(es);

            if (!string.IsNullOrEmpty(editorExecutable) && File.Exists(editorExecutable))
            {
                var psi = new ProcessStartInfo { FileName = editorExecutable, UseShellExecute = false };
                psi.ArgumentList.Add(localFilePath);
                Process.Start(psi);
            }
            else
            {
                Ui.OpenWithShell(localFilePath);
            }

            StatusMessage = LocalizationManager.Get("Str_OpeningFileStatus", item.Name);
        }
        catch (Exception ex)
        {
            if (session == null)
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_Error"), LocalizationManager.Get("Str_ErrorOpenFile", ex.Message), DialogIcon.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Upload file đang sửa lên máy chủ (gọi từ hẹn giờ debounce, chạy ở luồng nền).</summary>
    private async Task UploadEditedFileAsync(EditSession es)
    {
        if (_disposed) return;

        // Đang upload lần trước → hẹn lại, không chạy chồng.
        if (!await es.Gate.WaitAsync(0))
        {
            es.Schedule(500);
            return;
        }

        try
        {
            var client = _client;
            if (client == null || !client.IsConnected)
            {
                Ui.Post(() => StatusMessage = string.Format(
                    LocalizationManager.Tr("Could not save {0} to the server: SFTP is disconnected.", "Không lưu được {0} lên máy chủ: SFTP đã mất kết nối."), es.Name));
                return;
            }

            byte[] data;
            try
            {
                using var fs = new FileStream(es.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms);
                data = ms.ToArray();
            }
            catch (IOException) when (es.LockedRetries < 10)
            {
                // Trình sửa còn đang ghi/khóa file → thử lại sau.
                es.LockedRetries++;
                es.Schedule(1000);
                return;
            }
            es.LockedRetries = 0;

            await Task.Run(() =>
            {
                using var ms = new MemoryStream(data);
                client.UploadFile(ms, es.RemotePath, true);
            });

            await Ui.RunAsync(async () =>
            {
                await RefreshAsync();
                StatusMessage = LocalizationManager.Get("Str_AutoSavedStatus", es.Name);
            });
        }
        catch (Exception ex)
        {
            Ui.Post(() => StatusMessage = string.Format(
                LocalizationManager.Tr("Could not save {0} to the server: {1}", "Không lưu được {0} lên máy chủ: {1}"), es.Name, ex.Message));
        }
        finally
        {
            es.Gate.Release();
        }
    }

    // ===== Làm sạch tên file từ máy chủ =====

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Đổi tên file/thư mục nhận từ máy chủ thành tên an toàn trên máy này: thay '/', '\', ':' và ký tự không hợp lệ
    /// bằng '_'; trả null nếu tên rỗng, "." hoặc "..".
    /// </summary>
    public static string? SanitizeFileName(string? remoteName)
    {
        if (string.IsNullOrEmpty(remoteName)) return null;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(remoteName.Length);
        foreach (char c in remoteName)
        {
            bool bad = c == '/' || c == '\\' || c == ':' || c < 32 || Array.IndexOf(invalid, c) >= 0;
            sb.Append(bad ? '_' : c);
        }

        string name = sb.ToString();
        if (OperatingSystem.IsWindows())
        {
            // Windows bỏ dấu chấm/khoảng trắng cuối tên → "..." thành "" (= thư mục cha).
            name = name.TrimEnd('.', ' ');
            string stem = name.Split('.')[0];
            if (WindowsReservedNames.Contains(stem)) name = "_" + name;
        }

        if (name.Length == 0 || name == "." || name == "..") return null;
        return name;
    }

    /// <summary>Đường dẫn cục bộ an toàn cho 1 tên từ máy chủ; null nếu không hợp lệ hoặc thoát ra ngoài thư mục đích.</summary>
    public static string? SafeLocalPath(string folder, string? remoteName)
    {
        string? name = SanitizeFileName(remoteName);
        if (name == null) return null;

        string root = Path.GetFullPath(folder);
        string full = Path.GetFullPath(Path.Combine(root, name));
        string rootWithSep = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(rootWithSep, cmp) || full.Length <= rootWithSep.Length) return null;
        return full;
    }

    // ===== Truyền file (upload/download) =====

    /// <summary>Đuôi file tạm trong lúc truyền; chỉ đổi tên thành file thật khi truyền xong.</summary>
    private const string PartSuffix = ".wnpart";

    /// <summary>Trạng thái chung của 1 lô truyền file: token hủy, quyết định ghi đè (hỏi 1 lần), lỗi.</summary>
    private sealed class TransferBatch
    {
        public CancellationToken Token { get; init; }
        public bool? Overwrite { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = new();
    }

    /// <summary>Stream bọc: Read/Write ném OperationCanceledException khi đã bấm Hủy (SSH.NET không nhận token).</summary>
    private sealed class CancellableStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _token;

        public CancellableStream(Stream inner, CancellationToken token)
        {
            _inner = inner;
            _token = token;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        public override int Read(byte[] buffer, int offset, int count)
        {
            _token.ThrowIfCancellationRequested();
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            _token.ThrowIfCancellationRequested();
            return _inner.Read(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _token.ThrowIfCancellationRequested();
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _token.ThrowIfCancellationRequested();
            _inner.Write(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _token.ThrowIfCancellationRequested();
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _token.ThrowIfCancellationRequested();
            await _inner.WriteAsync(buffer, cancellationToken);
        }
    }

    /// <summary>Đích đã tồn tại: hỏi 1 lần cho cả lô (Có = ghi đè tất cả, Không = bỏ qua các file đã có).</summary>
    private static async Task<bool> ConfirmOverwriteAsync(TransferBatch batch, string name)
    {
        if (batch.Overwrite is bool decided) return decided;

        bool yes = await Dialogs.ConfirmAsync(
            LocalizationManager.Tr("File already exists", "File đã tồn tại"),
            string.Format(LocalizationManager.Tr(
                "\"{0}\" already exists at the destination.\n\nOverwrite existing files in this transfer?\nYes = overwrite all, No = skip existing files.",
                "\"{0}\" đã có ở nơi nhận.\n\nGhi đè các file đã tồn tại trong lần truyền này?\nCó = ghi đè tất cả, Không = bỏ qua các file đã có."), name),
            DialogIcon.Warning);
        batch.Overwrite = yes;
        return yes;
    }

    /// <summary>Báo kết quả lô: lỗi gộp vào 1 hộp thoại, hủy/bỏ qua hiện ở thanh trạng thái.</summary>
    private async Task ReportBatchAsync(TransferBatch batch)
    {
        var notes = new List<string>();
        if (batch.Token.IsCancellationRequested)
            notes.Add(LocalizationManager.Tr("Transfer canceled.", "Đã hủy truyền file."));
        if (batch.Skipped > 0)
            notes.Add(string.Format(LocalizationManager.Tr("Skipped {0} existing file(s).", "Bỏ qua {0} file đã tồn tại."), batch.Skipped));
        if (batch.Errors.Count > 0)
            notes.Add(string.Format(LocalizationManager.Tr("{0} error(s).", "{0} lỗi."), batch.Errors.Count));
        if (notes.Count > 0) StatusMessage = string.Join(" ", notes);

        if (batch.Errors.Count > 0)
        {
            const int max = 8;
            string text = string.Join("\n", batch.Errors.Take(max));
            if (batch.Errors.Count > max)
                text += "\n" + string.Format(LocalizationManager.Tr("...and {0} more.", "...và {0} lỗi khác."), batch.Errors.Count - max);
            await Dialogs.MessageAsync(LocalizationManager.Get("Str_Error"), text, DialogIcon.Error);
        }
    }

    private Action<ulong> MakeProgress(ulong totalBytes)
    {
        DateTime lastTime = DateTime.UtcNow;
        ulong lastTransferred = 0;
        return transferred =>
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - lastTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                double speed = (transferred - lastTransferred) / elapsed;
                lastTime = now;
                lastTransferred = transferred;
                Ui.Post(() => TransferQueue.UpdateProgress(transferred, totalBytes, speed));
            }
        };
    }

    public async Task UploadPathsAsync(IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        await UploadFilesAsync(paths, CurrentPath);
    }

    public async Task UploadFilesAsync(IEnumerable<string> filePaths, string remoteDirectory)
    {
        if (_client == null || !_client.IsConnected) return;

        var batch = new TransferBatch { Token = TransferQueue.BeginBatch() };
        try
        {
            foreach (var path in filePaths)
            {
                if (batch.Token.IsCancellationRequested) break;
                try
                {
                    if (File.Exists(path))
                    {
                        await UploadSingleFileAsync(path, remoteDirectory, batch);
                    }
                    else if (Directory.Exists(path))
                    {
                        await UploadDirectoryRecursiveAsync(path, remoteDirectory, batch);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    batch.Errors.Add(LocalizationManager.Get("Str_ErrorUpload", Path.GetFileName(path), ex.Message));
                }
            }
        }
        finally
        {
            TransferQueue.EndBatch();
        }

        await RefreshAsync();
        await ReportBatchAsync(batch);
    }

    private async Task UploadSingleFileAsync(string localPath, string remoteDirectory, TransferBatch batch)
    {
        var client = _client;
        if (client == null) return;
        batch.Token.ThrowIfCancellationRequested();

        string fileName = Path.GetFileName(localPath);
        string remotePath = $"{remoteDirectory.TrimEnd('/')}/{fileName}";
        string partPath = remotePath + PartSuffix;

        bool exists = await Task.Run(() => client.Exists(remotePath));
        if (exists && !await ConfirmOverwriteAsync(batch, fileName))
        {
            batch.Skipped++;
            return;
        }
        batch.Token.ThrowIfCancellationRequested();

        TransferQueue.StartTransfer($"Upload: {fileName}");

        // Chỉ xóa file trên máy chủ khi hủy/lỗi nếu CHÍNH lần truyền này tạo ra nó (file .wnpart),
        // không bao giờ xóa file đích có sẵn.
        bool partCreated = false;
        try
        {
            using var fileStream = File.OpenRead(localPath);
            var input = new CancellableStream(fileStream, batch.Token);
            var progress = MakeProgress((ulong)fileStream.Length);

            await Task.Run(() =>
            {
                partCreated = true;
                client.UploadFile(input, partPath, true, progress);
                batch.Token.ThrowIfCancellationRequested();

                if (exists)
                {
                    // Ghi đè: giữ lại quyền của file cũ.
                    try { client.ChangePermissions(partPath, ModeOf(client.GetAttributes(remotePath))); } catch { }
                }

                ReplaceRemoteFile(client, partPath, remotePath);
                partCreated = false;
            }, batch.Token);
        }
        catch (Exception) when (batch.Token.IsCancellationRequested)
        {
            throw new OperationCanceledException(batch.Token);
        }
        catch (Exception ex)
        {
            batch.Errors.Add(LocalizationManager.Get("Str_ErrorUpload", fileName, ex.Message));
        }
        finally
        {
            if (partCreated)
            {
                try { await Task.Run(() => client.DeleteFile(partPath)); } catch { }
            }
            TransferQueue.EndTransfer();
        }
    }

    /// <summary>Đổi tên file tạm thành file đích (ghi đè nếu đã có), an toàn khi máy chủ không hỗ trợ posix-rename.</summary>
    private static void ReplaceRemoteFile(SftpClient client, string partPath, string target)
    {
        try
        {
            // posix-rename@openssh.com: ghi đè nguyên tử.
            client.RenameFile(partPath, target, true);
            return;
        }
        catch { }

        if (!client.Exists(target))
        {
            client.RenameFile(partPath, target);
            return;
        }

        // Máy chủ không có posix-rename: cất file cũ sang bên, đổi tên, lỗi thì trả lại file cũ.
        string backup = target + ".wnold";
        client.RenameFile(target, backup);
        try
        {
            client.RenameFile(partPath, target);
        }
        catch
        {
            try { client.RenameFile(backup, target); } catch { }
            throw;
        }
        try { client.DeleteFile(backup); } catch { }
    }

    private async Task UploadDirectoryRecursiveAsync(string localDir, string remoteParent, TransferBatch batch)
    {
        var client = _client;
        if (client == null) return;
        batch.Token.ThrowIfCancellationRequested();

        string dirName = Path.GetFileName(localDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string remoteDir = $"{remoteParent.TrimEnd('/')}/{dirName}";

        try
        {
            await Task.Run(() =>
            {
                if (!client.Exists(remoteDir))
                    client.CreateDirectory(remoteDir);
            });
        }
        catch (Exception ex) when (!batch.Token.IsCancellationRequested)
        {
            batch.Errors.Add(LocalizationManager.Get("Str_ErrorUpload", dirName, ex.Message));
            return;
        }

        string[] files, subDirs;
        try
        {
            files = Directory.GetFiles(localDir);
            subDirs = Directory.GetDirectories(localDir);
        }
        catch (Exception ex)
        {
            batch.Errors.Add(LocalizationManager.Get("Str_ErrorUpload", dirName, ex.Message));
            return;
        }

        foreach (var file in files)
        {
            batch.Token.ThrowIfCancellationRequested();
            try
            {
                await UploadSingleFileAsync(file, remoteDir, batch);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                batch.Errors.Add(LocalizationManager.Get("Str_ErrorUpload", Path.GetFileName(file), ex.Message));
            }
        }

        foreach (var subDir in subDirs)
        {
            batch.Token.ThrowIfCancellationRequested();
            try
            {
                await UploadDirectoryRecursiveAsync(subDir, remoteDir, batch);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                batch.Errors.Add(LocalizationManager.Get("Str_ErrorUpload", Path.GetFileName(subDir), ex.Message));
            }
        }
    }

    public async Task DownloadItemsAsync(IEnumerable<SftpItem> items, string destinationFolder)
    {
        if (_client == null || !_client.IsConnected) return;

        var validItems = items.Where(i => !i.IsParentDirectory && i.Name != "..").ToList();
        var batch = new TransferBatch { Token = TransferQueue.BeginBatch() };
        try
        {
            foreach (var item in validItems)
            {
                if (batch.Token.IsCancellationRequested) break;

                string? localPath = SafeLocalPath(destinationFolder, item.Name);
                if (localPath == null)
                {
                    batch.Errors.Add(InvalidNameError(item.Name));
                    continue;
                }

                try
                {
                    if (item.IsDirectory)
                    {
                        await DownloadDirectoryRecursiveAsync(item.FullName, localPath, batch);
                    }
                    else
                    {
                        await DownloadSingleFileAsync(item.FullName, item.Name, item.Size, localPath, batch);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    batch.Errors.Add(LocalizationManager.Get("Str_ErrorDownload", item.Name, ex.Message));
                }
            }
        }
        finally
        {
            TransferQueue.EndBatch();
        }

        await ReportBatchAsync(batch);
    }

    private static string InvalidNameError(string name) => string.Format(
        LocalizationManager.Tr("Skipped \"{0}\": unsafe file name.", "Bỏ qua \"{0}\": tên file không an toàn."), name);

    private async Task DownloadSingleFileAsync(string remotePath, string displayName, long size, string localPath, TransferBatch batch)
    {
        var client = _client;
        if (client == null) return;
        batch.Token.ThrowIfCancellationRequested();

        if (File.Exists(localPath) && !await ConfirmOverwriteAsync(batch, displayName))
        {
            batch.Skipped++;
            return;
        }
        batch.Token.ThrowIfCancellationRequested();

        // Tải vào file .wnpart rồi mới đổi tên → file cũ không bị hỏng nếu lỗi/hủy giữa chừng.
        string partPath = localPath + PartSuffix;
        TransferQueue.StartTransfer($"Download: {displayName}");

        bool done = false;
        try
        {
            using (var fileStream = File.Create(partPath))
            {
                var output = new CancellableStream(fileStream, batch.Token);
                var progress = MakeProgress((ulong)Math.Max(0, size));
                await Task.Run(() => client.DownloadFile(remotePath, output, progress), batch.Token);
            }
            batch.Token.ThrowIfCancellationRequested();

            File.Move(partPath, localPath, true);
            done = true;
        }
        catch (Exception) when (batch.Token.IsCancellationRequested)
        {
            throw new OperationCanceledException(batch.Token);
        }
        catch (Exception ex)
        {
            batch.Errors.Add(LocalizationManager.Get("Str_ErrorDownload", displayName, ex.Message));
        }
        finally
        {
            if (!done)
            {
                try { File.Delete(partPath); } catch { }
            }
            TransferQueue.EndTransfer();
        }
    }

    private async Task DownloadDirectoryRecursiveAsync(string remoteDir, string localDir, TransferBatch batch)
    {
        var client = _client;
        if (client == null) return;
        batch.Token.ThrowIfCancellationRequested();

        List<ISftpFile> list;
        try
        {
            Directory.CreateDirectory(localDir);
            list = await Task.Run(() => client.ListDirectory(remoteDir).ToList());
        }
        catch (Exception ex) when (!batch.Token.IsCancellationRequested)
        {
            batch.Errors.Add(LocalizationManager.Get("Str_ErrorDownload", remoteDir, ex.Message));
            return;
        }

        foreach (var file in list)
        {
            batch.Token.ThrowIfCancellationRequested();
            if (file.Name == "." || file.Name == "..") continue;

            string? localPath = SafeLocalPath(localDir, file.Name);
            if (localPath == null)
            {
                batch.Errors.Add(InvalidNameError(file.Name));
                continue;
            }

            try
            {
                if (file.IsDirectory)
                {
                    await DownloadDirectoryRecursiveAsync(file.FullName, localPath, batch);
                }
                else
                {
                    await DownloadSingleFileAsync(file.FullName, file.Name, file.Length, localPath, batch);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                batch.Errors.Add(LocalizationManager.Get("Str_ErrorDownload", file.Name, ex.Message));
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        TransferQueue.Cancel();

        List<EditSession> sessions;
        lock (_editSessions)
        {
            sessions = _editSessions.ToList();
            _editSessions.Clear();
        }
        foreach (var es in sessions) es.Dispose();

        var client = _client;
        _client = null;

        // Ngắt kết nối/dọn dẹp ở luồng nền: Disconnect() có thể treo lâu khi mạng đã chết.
        _ = Task.Run(() =>
        {
            try
            {
                if (client?.IsConnected == true)
                {
                    client.Disconnect();
                }
            }
            catch { }
            try { client?.Dispose(); } catch { }

            foreach (var es in sessions)
            {
                try { Directory.Delete(es.TempDir, true); } catch { }
            }
        });
    }
}
