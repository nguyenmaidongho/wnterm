using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WNTerm.App.Services;

/// <summary>Truy cập UI thread, clipboard, hộp chọn file — một chỗ cho mọi nền tảng (desktop + mobile).</summary>
public static class Ui
{
    /// <summary>TopLevel chính (Window trên desktop, view gốc trên mobile). Do MainView gán khi gắn vào cây.</summary>
    public static TopLevel? Main { get; set; }

    // ----- UI thread -----
    public static bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    /// <summary>Chạy ngay nếu đang ở UI thread, ngược lại đẩy sang UI thread (không chờ).</summary>
    public static void Run(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Đẩy sang UI thread và chờ xong (giống Dispatcher.Invoke của WPF).</summary>
    public static void RunAndWait(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
    }

    public static Task RunAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);

    public static Task<T> RunAsync<T>(Func<Task<T>> action) => Dispatcher.UIThread.InvokeAsync(action);

    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    // ----- Clipboard -----
    public static async Task<bool> SetClipboardTextAsync(string text)
    {
        try
        {
            var cb = Main?.Clipboard;
            if (cb == null) return false;
            await cb.SetTextAsync(text);
            return true;
        }
        catch { return false; }
    }

    public static async Task<string?> GetClipboardTextAsync()
    {
        try
        {
            var cb = Main?.Clipboard;
            return cb == null ? null : await cb.TryGetTextAsync();
        }
        catch { return null; }
    }

    // ----- Hộp chọn file / thư mục -----
    // Android/iOS trả về file dạng content:// (không có đường dẫn cục bộ) → chép qua thư mục tạm của app
    // để phần còn lại của app vẫn làm việc với đường dẫn file bình thường.

    private static string StagingRoot => Path.Combine(Path.GetTempPath(), "wnterm-picked");

    private static string StagingDir()
    {
        CleanupStaging(TimeSpan.FromHours(6));
        string dir = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Xóa các bản chép tạm (file chọn trên điện thoại) cũ hơn maxAge — tránh đầy bộ nhớ máy.</summary>
    public static void CleanupStaging(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(StagingRoot)) return;
            foreach (var d in Directory.GetDirectories(StagingRoot))
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(d) > maxAge)
                    try { Directory.Delete(d, true); } catch { }
        }
        catch { }
    }

    private static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    /// <summary>Đường dẫn cục bộ của file đã chọn; nếu không có (Android content://) thì chép vào copyToDir (mặc định: thư mục tạm).</summary>
    private static async Task<string?> MaterializeAsync(IStorageFile file, string? copyToDir)
    {
        string? local = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(local) && File.Exists(local)) return local;
        try
        {
            string dir = copyToDir ?? StagingDir();
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, SafeName(file.Name));
            await using var src = await file.OpenReadAsync();
            await using var dst = File.Create(dest);
            await src.CopyToAsync(dst);
            return dest;
        }
        catch { return null; }
    }

    /// <summary>
    /// Chọn 1 file để mở. patterns: ví dụ ["*.wnterm", "*.mxtsessions"]. Trả đường dẫn cục bộ hoặc null.
    /// copyToDir: nơi giữ bản sao lâu dài khi file không có đường dẫn cục bộ (ví dụ file SSH key trên điện thoại).
    /// </summary>
    public static async Task<string?> PickOpenFileAsync(string title, string filterName = "", params string[] patterns)
        => await PickOpenFileToAsync(title, null, filterName, patterns);

    public static async Task<string?> PickOpenFileToAsync(string title, string? copyToDir, string filterName = "", params string[] patterns)
    {
        var sp = Main?.StorageProvider;
        if (sp == null) return null;
        var opts = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
        // Trên điện thoại đuôi riêng (.wnterm, .mxtsessions) không có MIME type → lọc sẽ làm mờ file; để hiện tất cả.
        if (patterns.Length > 0 && !IsMobile)
            opts.FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = patterns }, FilePickerFileTypes.All };
        var files = await sp.OpenFilePickerAsync(opts);
        return files.Count > 0 ? await MaterializeAsync(files[0], copyToDir) : null;
    }

    public static async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title)
    {
        var sp = Main?.StorageProvider;
        if (sp == null) return Array.Empty<string>();
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = true });
        var result = new List<string>();
        foreach (var f in files)
            if (await MaterializeAsync(f, null) is string p) result.Add(p);
        return result;
    }

    public static bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>
    /// Chọn nơi lưu rồi ghi file bằng write(đường dẫn). Trên điện thoại ghi ra file tạm rồi chép vào nơi đã chọn.
    /// Trả về tên file đã lưu, hoặc null nếu người dùng hủy.
    /// </summary>
    public static async Task<string?> SaveFileAsync(string title, string suggestedName, string defaultExt, string filterName, string[] patterns, Func<string, Task> write)
    {
        var sp = Main?.StorageProvider;
        if (sp == null) return null;
        var opts = new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName, DefaultExtension = defaultExt };
        if (patterns.Length > 0 && !IsMobile)
            opts.FileTypeChoices = new[] { new FilePickerFileType(filterName) { Patterns = patterns } };
        var file = await sp.SaveFilePickerAsync(opts);
        if (file == null) return null;

        string? local = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(local))
        {
            await write(local);
            return local;
        }

        string tmp = Path.Combine(StagingDir(), SafeName(file.Name));
        try
        {
            await write(tmp);
            await using var src = File.OpenRead(tmp);
            await using var dst = await file.OpenWriteAsync();
            await src.CopyToAsync(dst);
            return file.Name;
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>Chọn thư mục. Trả về thư mục đã chọn (có thể không có đường dẫn cục bộ trên điện thoại) hoặc null.</summary>
    public static async Task<IStorageFolder?> PickFolderAsync(string title)
    {
        var sp = Main?.StorageProvider;
        if (sp == null) return null;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0] : null;
    }

    /// <summary>Tạo một thư mục tạm để chứa dữ liệu trước khi chép vào thư mục không có đường dẫn cục bộ.</summary>
    public static string CreateStagingFolder() => StagingDir();

    /// <summary>Chép toàn bộ nội dung một thư mục cục bộ vào thư mục người dùng đã chọn (content:// trên Android).</summary>
    public static async Task CopyDirectoryToStorageAsync(string sourceDir, IStorageFolder target)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dstFile = await target.CreateFileAsync(Path.GetFileName(file));
            if (dstFile == null) continue;
            await using var src = File.OpenRead(file);
            await using var dst = await dstFile.OpenWriteAsync();
            await src.CopyToAsync(dst);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var sub = await target.CreateFolderAsync(Path.GetFileName(dir));
            if (sub != null) await CopyDirectoryToStorageAsync(dir, sub);
        }
    }

    /// <summary>Mở file/URL bằng ứng dụng mặc định của hệ điều hành.</summary>
    public static void OpenWithShell(string pathOrUrl)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
        }
        catch { }
    }
}
