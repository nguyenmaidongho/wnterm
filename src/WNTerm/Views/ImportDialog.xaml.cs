using System;
using System.IO;
using System.Linq;
using System.Windows;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.Views;

public partial class ImportDialog : Window
{
    private string? _filePath;

    /// <summary>Mật khẩu điền sẵn khi restore từ cloud (mật khẩu backup đã lưu).</summary>
    public string? PrefillPassword { get; set; }
    private readonly SessionImporter _importer;
    private ExportFile? _exportFile;

    public ImportResult? Result { get; private set; }

    public ImportDialog(string? filePath, SessionStore store)
    {
        InitializeComponent();
        _filePath = filePath;
        _importer = new SessionImporter(store);

        Loaded += ImportDialog_Loaded;
    }

    private void ImportDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
        {
            LoadFile(_filePath);
        }
        else
        {
            string? candidate = FindMobaCandidate();
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                LoadFile(candidate);
            }
            else
            {
                FileSummaryBlock.Text = WNTerm.Services.LocalizationManager.Tr("No file selected", "Chưa chọn file nào");
                ProtectionStatusBlock.Text = WNTerm.Services.LocalizationManager.Tr("Click 'Browse...' or 'Scan MobaXterm' to load a config file", "Nhấn 'Chọn...' hoặc 'Quét MobaXterm' để tải file cấu hình");
            }
        }
    }

    private void LoadFile(string filePath)
    {
        try
        {
            _filePath = filePath;
            _exportFile = _importer.ReadAndValidateFile(_filePath);

            if (_exportFile.Format == "mobaxterm-sessions")
            {
                FileSummaryBlock.Text = string.Format(WNTerm.Services.LocalizationManager.Tr("{0} ({1} VMs from MobaXterm)", "{0} ({1} VM từ MobaXterm)"), Path.GetFileName(_filePath), _exportFile.Sessions.Count);
                ProtectionStatusBlock.Text = WNTerm.Services.LocalizationManager.Tr("Import from MobaXterm (no passwords)", "Nhập từ MobaXterm (không kèm mật khẩu)");
                PasswordPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                FileSummaryBlock.Text = $"{Path.GetFileName(_filePath)} ({_exportFile.Sessions.Count} VM)";

                if (_exportFile.Protection != null)
                {
                    ProtectionStatusBlock.Text = WNTerm.Services.LocalizationManager.Tr("🔒 File is password-protected", "🔒 File có mã hóa mật khẩu bảo vệ");
                    PasswordPanel.Visibility = Visibility.Visible;
                    if (!string.IsNullOrEmpty(PrefillPassword)) ExportPasswordBox.Password = PrefillPassword;
                    ExportPasswordBox.Focus();
                }
                else
                {
                    ProtectionStatusBlock.Text = WNTerm.Services.LocalizationManager.Tr("File has no password protection", "File không kèm mật khẩu bảo vệ");
                    PasswordPanel.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, WNTerm.Services.LocalizationManager.Tr("File read error", "Lỗi đọc file"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        string initDir = Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) initDir = dir;
        }

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationManager.Get("Str_ImportTitle"),
            Filter = WNTerm.Services.LocalizationManager.Tr("All supported files", "Tất cả file hỗ trợ") + " (*.wnterm;*.mxtsessions;*.ini)|*.wnterm;*.mxtsessions;*.ini|WN Term Export (*.wnterm)|*.wnterm|MobaXterm Sessions (*.mxtsessions;*.ini;*.txt)|*.mxtsessions;*.ini;*.txt|" + WNTerm.Services.LocalizationManager.Tr("All files", "Tất cả file") + " (*.*)|*.*",
            InitialDirectory = initDir
        };

        if (ofd.ShowDialog(this) == true)
        {
            LoadFile(ofd.FileName);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_exportFile == null)
        {
            MessageBox.Show(WNTerm.Services.LocalizationManager.Tr("Select or scan a file before importing.", "Vui lòng chọn hoặc quét file trước khi Import."), WNTerm.Services.LocalizationManager.Tr("No file", "Chưa có file"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? password = null;
        if (_exportFile.Protection != null)
        {
            password = ExportPasswordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show(WNTerm.Services.LocalizationManager.Tr("Enter the file password.", "Vui lòng nhập mật khẩu của file."), WNTerm.Services.LocalizationManager.Tr("Password required", "Cần mật khẩu"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var resolution = ConflictResolution.Skip;
        if (ConflictOverwriteRadio.IsChecked == true) resolution = ConflictResolution.Overwrite;
        else if (ConflictAddCopyRadio.IsChecked == true) resolution = ConflictResolution.AddCopy;

        try
        {
            Result = _importer.Import(_exportFile, password, resolution);

            string summary = string.Format(WNTerm.Services.LocalizationManager.Tr("Import result:\n- Total VMs in file: {0}\n- Newly imported: {1}\n- Overwritten: {2}\n- Skipped: {3}", "Kết quả Import:\n- Tổng số VM trong file: {0}\n- Đã nhập mới: {1}\n- Ghi đè: {2}\n- Bỏ qua: {3}"), Result.TotalInFile, Result.ImportedCount, Result.OverwrittenCount, Result.SkippedCount);
            if (Result.CorruptSecretsCount > 0)
            {
                summary += "\n- " + string.Format(WNTerm.Services.LocalizationManager.Tr("Passwords that could not be decrypted: {0}", "Mật khẩu hỏng không giải mã được: {0}"), Result.CorruptSecretsCount);
            }

            MessageBox.Show(summary, WNTerm.Services.LocalizationManager.Tr("Import complete", "Import hoàn tất"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show(WNTerm.Services.LocalizationManager.Tr("Wrong file password. Please try again.", "Sai mật khẩu file. Vui lòng thử lại."), WNTerm.Services.LocalizationManager.Tr("Wrong password", "Sai mật khẩu"), MessageBoxButton.OK, MessageBoxImage.Warning);
            ExportPasswordBox.SelectAll();
            ExportPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(WNTerm.Services.LocalizationManager.Tr("Import failed: {0}", "Lỗi khi import: {0}"), ex.Message), WNTerm.Services.LocalizationManager.Tr("Error", "Lỗi"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScanMobaXterm_Click(object sender, RoutedEventArgs e)
    {
        string? candidate = FindMobaCandidate();
        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
        {
            LoadFile(candidate);
            if (_exportFile != null)
            {
                ProtectionStatusBlock.Text = string.Format(WNTerm.Services.LocalizationManager.Tr("Found automatically from {0}", "Đã tìm thấy tự động từ {0}"), Path.GetFileName(candidate));
                MessageBox.Show(string.Format(WNTerm.Services.LocalizationManager.Tr("Found {0} VMs from MobaXterm in '{1}'. Click 'Import' to save them to the list.", "Tìm thấy {0} VM từ MobaXterm trong '{1}'. Nhấn 'Import' để lưu vào danh sách."), _exportFile.Sessions.Count, Path.GetFileName(candidate)), WNTerm.Services.LocalizationManager.Tr("Scan successful", "Quét thành công"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = WNTerm.Services.LocalizationManager.Tr("Select MobaXterm file (.mxtsessions or .ini)", "Chọn file MobaXterm (.mxtsessions hoặc .ini)"),
            Filter = "MobaXterm Sessions (*.mxtsessions;*.ini;*.txt)|*.mxtsessions;*.ini;*.txt|" + WNTerm.Services.LocalizationManager.Tr("All files", "Tất cả file") + " (*.*)|*.*",
            InitialDirectory = Directory.GetCurrentDirectory()
        };

        if (ofd.ShowDialog(this) == true)
        {
            LoadFile(ofd.FileName);
        }
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}