using System;
using System.IO;
using System.Linq;
using System.Windows;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.Views;

public partial class ImportDialog : Window
{
    private string? _filePath;
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
                FileSummaryBlock.Text = "Chưa chọn file nào";
                ProtectionStatusBlock.Text = "Nhấn 'Chọn...' hoặc 'Quét MobaXterm' để tải file cấu hình";
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
                FileSummaryBlock.Text = $"{Path.GetFileName(_filePath)} ({_exportFile.Sessions.Count} VM từ MobaXterm)";
                ProtectionStatusBlock.Text = "Nhập từ MobaXterm (không kèm mật khẩu)";
                PasswordPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                FileSummaryBlock.Text = $"{Path.GetFileName(_filePath)} ({_exportFile.Sessions.Count} VM)";

                if (_exportFile.Protection != null)
                {
                    ProtectionStatusBlock.Text = "🔒 File có mã hóa mật khẩu bảo vệ";
                    PasswordPanel.Visibility = Visibility.Visible;
                    ExportPasswordBox.Focus();
                }
                else
                {
                    ProtectionStatusBlock.Text = "File không kèm mật khẩu bảo vệ";
                    PasswordPanel.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi đọc file", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Filter = "Tất cả file hỗ trợ (*.snterm;*.mxtsessions;*.ini)|*.snterm;*.mxtsessions;*.ini|SN Term Export (*.snterm)|*.snterm|MobaXterm Sessions (*.mxtsessions;*.ini;*.txt)|*.mxtsessions;*.ini;*.txt|Tất cả file (*.*)|*.*",
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
            MessageBox.Show("Vui lòng chọn hoặc quét file trước khi Import.", "Chưa có file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? password = null;
        if (_exportFile.Protection != null)
        {
            password = ExportPasswordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Vui lòng nhập mật khẩu của file.", "Cần mật khẩu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var resolution = ConflictResolution.Skip;
        if (ConflictOverwriteRadio.IsChecked == true) resolution = ConflictResolution.Overwrite;
        else if (ConflictAddCopyRadio.IsChecked == true) resolution = ConflictResolution.AddCopy;

        try
        {
            Result = _importer.Import(_exportFile, password, resolution);

            string summary = $"Kết quả Import:\n- Tổng số VM trong file: {Result.TotalInFile}\n- Đã nhập mới: {Result.ImportedCount}\n- Ghi đè: {Result.OverwrittenCount}\n- Bỏ qua: {Result.SkippedCount}";
            if (Result.CorruptSecretsCount > 0)
            {
                summary += $"\n- Mật khẩu hỏng không giải mã được: {Result.CorruptSecretsCount}";
            }

            MessageBox.Show(summary, "Import hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show("Sai mật khẩu file. Vui lòng thử lại.", "Sai mật khẩu", MessageBoxButton.OK, MessageBoxImage.Warning);
            ExportPasswordBox.SelectAll();
            ExportPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi import: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                ProtectionStatusBlock.Text = $"Đã tìm thấy tự động từ {Path.GetFileName(candidate)}";
                MessageBox.Show($"Tìm thấy {_exportFile.Sessions.Count} VM từ MobaXterm trong '{Path.GetFileName(candidate)}'. Nhấn 'Import' để lưu vào danh sách.", "Quét thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file MobaXterm (.mxtsessions hoặc .ini)",
            Filter = "MobaXterm Sessions (*.mxtsessions;*.ini;*.txt)|*.mxtsessions;*.ini;*.txt|Tất cả file (*.*)|*.*",
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