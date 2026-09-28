using System;
using System.IO;
using System.Windows;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.Views;

public partial class ImportDialog : Window
{
    private readonly string _filePath;
    private readonly SessionImporter _importer;
    private ExportFile? _exportFile;

    public ImportResult? Result { get; private set; }

    public ImportDialog(string filePath, SessionStore store)
    {
        InitializeComponent();
        _filePath = filePath;
        _importer = new SessionImporter(store);

        Loaded += ImportDialog_Loaded;
    }

    private void ImportDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _exportFile = _importer.ReadAndValidateFile(_filePath);
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
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi đọc file", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_exportFile == null) return;

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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
