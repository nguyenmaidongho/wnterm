using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

public partial class ImportDialog : DialogView<bool?>
{
    private string? _filePath;
    private readonly SessionImporter _importer;
    private ExportFile? _exportFile;
    private bool _initialized;

    /// <summary>Mật khẩu điền sẵn khi restore từ cloud (mật khẩu backup đã lưu).</summary>
    public string? PrefillPassword { get; set; }

    public ImportResult? Result { get; private set; }

    public ImportDialog(string? filePath, SessionStore store)
    {
        InitializeComponent();
        Title = LocalizationManager.Get("Str_ImportTitle");
        DialogWidth = 500;

        _filePath = filePath;
        _importer = new SessionImporter(store);
        ScanMobaBtn.IsVisible = !Ui.IsMobile; // MobaXterm chỉ có trên Windows

        AttachedToVisualTree += (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            Dispatcher.UIThread.Post(Initialize, DispatcherPriority.Loaded);
        };
    }


    private async void Initialize()
    {
        if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
        {
            await LoadFileAsync(_filePath);
        }
        else
        {
            string? candidate = FindMobaCandidate();
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                await LoadFileAsync(candidate);
            }
            else
            {
                FileSummaryBlock.Text = LocalizationManager.Tr("No file selected", "Chưa chọn file nào");
                ProtectionStatusBlock.Text = LocalizationManager.Tr("Click 'Browse...' or 'Scan MobaXterm' to load a config file", "Nhấn 'Chọn...' hoặc 'Quét MobaXterm' để tải file cấu hình");
            }
        }
    }

    private async Task LoadFileAsync(string filePath)
    {
        try
        {
            _filePath = filePath;
            _exportFile = _importer.ReadAndValidateFile(_filePath);

            if (_exportFile.Format == "mobaxterm-sessions")
            {
                FileSummaryBlock.Text = string.Format(LocalizationManager.Tr("{0} ({1} VMs from MobaXterm)", "{0} ({1} VM từ MobaXterm)"), Path.GetFileName(_filePath), _exportFile.Sessions.Count);
                ProtectionStatusBlock.Text = LocalizationManager.Tr("Import from MobaXterm (no passwords)", "Nhập từ MobaXterm (không kèm mật khẩu)");
                PasswordPanel.IsVisible = false;
            }
            else
            {
                FileSummaryBlock.Text = $"{Path.GetFileName(_filePath)} ({_exportFile.Sessions.Count} VM)";

                if (_exportFile.Protection != null)
                {
                    ProtectionStatusBlock.Text = LocalizationManager.Tr("File is password-protected", "File có mã hóa mật khẩu bảo vệ");
                    PasswordPanel.IsVisible = true;
                    if (!string.IsNullOrEmpty(PrefillPassword)) ExportPasswordBox.Text = PrefillPassword;
                    ExportPasswordBox.Focus();
                }
                else
                {
                    ProtectionStatusBlock.Text = LocalizationManager.Tr("File has no password protection", "File không kèm mật khẩu bảo vệ");
                    PasswordPanel.IsVisible = false;
                }
            }
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("File read error", "Lỗi đọc file"), ex.Message, DialogIcon.Error);
        }
    }

    private async void BrowseFile_Click(object? sender, RoutedEventArgs e)
    {
        string? file = await Ui.PickOpenFileAsync(
            LocalizationManager.Get("Str_ImportTitle"),
            LocalizationManager.Tr("All supported files", "Tất cả file hỗ trợ") + " (*.wnterm;*.mxtsessions;*.ini)",
            "*.wnterm", "*.mxtsessions", "*.ini");
        if (file != null) await LoadFileAsync(file);
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (_exportFile == null)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("No file", "Chưa có file"), LocalizationManager.Tr("Select or scan a file before importing.", "Vui lòng chọn hoặc quét file trước khi Import."), DialogIcon.Warning);
            return;
        }

        string? password = null;
        if (_exportFile.Protection != null)
        {
            password = ExportPasswordBox.Text ?? "";
            if (string.IsNullOrEmpty(password))
            {
                await Dialogs.MessageAsync(LocalizationManager.Tr("Password required", "Cần mật khẩu"), LocalizationManager.Tr("Enter the file password.", "Vui lòng nhập mật khẩu của file."), DialogIcon.Warning);
                return;
            }
        }

        var resolution = ConflictResolution.Skip;
        if (ConflictOverwriteRadio.IsChecked == true) resolution = ConflictResolution.Overwrite;
        else if (ConflictAddCopyRadio.IsChecked == true) resolution = ConflictResolution.AddCopy;

        var file = _exportFile;
        ImportBtn.IsEnabled = false;
        try
        {
            Result = await Task.Run(() => _importer.Import(file, password, resolution));

            string summary = string.Format(LocalizationManager.Tr("Import result:\n- Total VMs in file: {0}\n- Newly imported: {1}\n- Overwritten: {2}\n- Skipped: {3}", "Kết quả Import:\n- Tổng số VM trong file: {0}\n- Đã nhập mới: {1}\n- Ghi đè: {2}\n- Bỏ qua: {3}"), Result.TotalInFile, Result.ImportedCount, Result.OverwrittenCount, Result.SkippedCount);
            if (Result.CorruptSecretsCount > 0)
            {
                summary += "\n- " + string.Format(LocalizationManager.Tr("Passwords that could not be decrypted: {0}", "Mật khẩu hỏng không giải mã được: {0}"), Result.CorruptSecretsCount);
            }
            if (Result.Messages.Count > 0)
            {
                summary += "\n\n" + string.Join("\n", Result.Messages.Take(6));
                if (Result.Messages.Count > 6) summary += "\n...";
            }
            if (_filePath != null && _filePath.Contains("wnterm-picked")) { try { File.Delete(_filePath); } catch { } } // bản chép tạm từ điện thoại

            await Dialogs.MessageAsync(LocalizationManager.Tr("Import complete", "Import hoàn tất"), summary);
            Close(true);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Wrong password", "Sai mật khẩu"), LocalizationManager.Tr("Wrong file password. Please try again.", "Sai mật khẩu file. Vui lòng thử lại."), DialogIcon.Warning);
            ExportPasswordBox.SelectAll();
            ExportPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(LocalizationManager.Tr("Error", "Lỗi"), string.Format(LocalizationManager.Tr("Import failed: {0}", "Lỗi khi import: {0}"), ex.Message), DialogIcon.Error);
        }
        finally
        {
            ImportBtn.IsEnabled = true;
        }
    }

    private async void ScanMobaXterm_Click(object? sender, RoutedEventArgs e)
    {
        string? candidate = FindMobaCandidate();
        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
        {
            await LoadFileAsync(candidate);
            if (_exportFile != null)
            {
                ProtectionStatusBlock.Text = string.Format(LocalizationManager.Tr("Found automatically from {0}", "Đã tìm thấy tự động từ {0}"), Path.GetFileName(candidate));
                await Dialogs.MessageAsync(LocalizationManager.Tr("Scan successful", "Quét thành công"), string.Format(LocalizationManager.Tr("Found {0} VMs from MobaXterm in '{1}'. Click 'Import' to save them to the list.", "Tìm thấy {0} VM từ MobaXterm trong '{1}'. Nhấn 'Import' để lưu vào danh sách."), _exportFile.Sessions.Count, Path.GetFileName(candidate)));
            }
            return;
        }

        string? file = await Ui.PickOpenFileAsync(
            LocalizationManager.Tr("Select MobaXterm file (.mxtsessions or .ini)", "Chọn file MobaXterm (.mxtsessions hoặc .ini)"),
            "MobaXterm Sessions (*.mxtsessions;*.ini;*.txt)",
            "*.mxtsessions", "*.ini", "*.txt");
        if (file != null) await LoadFileAsync(file);
    }

    private static string? FindMobaCandidate()
    {
        try
        {
            string cwd = Directory.GetCurrentDirectory();
            var localMxt = Directory.GetFiles(cwd, "*.mxtsessions").FirstOrDefault();
            if (localMxt != null) return localMxt;
            var localIni = Path.Combine(cwd, "MobaXterm.ini");
            if (File.Exists(localIni)) return localIni;
        }
        catch { }

        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            var appMxt = Directory.GetFiles(appDir, "*.mxtsessions").FirstOrDefault();
            if (appMxt != null) return appMxt;
        }
        catch { }

        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop))
            {
                var dtMxt = Directory.GetFiles(desktop, "*.mxtsessions").FirstOrDefault();
                if (dtMxt != null) return dtMxt;
            }
        }
        catch { }

        try
        {
            string mobaIni = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MobaXterm", "MobaXterm.ini");
            if (File.Exists(mobaIni)) return mobaIni;
        }
        catch { }

        return null;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
