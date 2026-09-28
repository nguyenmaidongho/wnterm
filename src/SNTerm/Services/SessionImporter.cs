using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SNTerm.Models;

namespace SNTerm.Services;

public enum ConflictResolution
{
    Skip,
    Overwrite,
    AddCopy
}

public class ImportResult
{
    public int TotalInFile { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int OverwrittenCount { get; set; }
    public int AddedCopyCount { get; set; }
    public int CorruptSecretsCount { get; set; }
    public List<string> Messages { get; } = new();
}

public class SessionImporter
{
    private readonly SessionStore _store;
    private readonly AppPaths _paths;

    public SessionImporter(SessionStore store, AppPaths? paths = null)
    {
        _store = store;
        _paths = paths ?? AppPaths.Default;
    }

    public ExportFile ReadAndValidateFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Không tìm thấy file import.", filePath);
        }

        if (fileInfo.Length > 20 * 1024 * 1024)
        {
            throw new InvalidDataException("Kích thước file vượt quá giới hạn 20MB.");
        }

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        var exportFile = JsonSerializer.Deserialize<ExportFile>(json);

        if (exportFile == null || exportFile.Format != "snterm-sessions")
        {
            throw new InvalidDataException("File không đúng định dạng SN Term.");
        }

        if (exportFile.Version > 1)
        {
            throw new NotSupportedException("File được tạo bởi phiên bản SN Term mới hơn, hãy cập nhật ứng dụng.");
        }

        return exportFile;
    }

    public bool VerifyPassword(ExportFile exportFile, string password, out byte[]? derivedKey)
    {
        derivedKey = null;
        if (exportFile.Protection == null) return true;

        try
        {
            byte[] salt = Convert.FromBase64String(exportFile.Protection.Salt);
            derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                exportFile.Protection.Iterations,
                HashAlgorithmName.SHA256,
                32);

            byte[] checkPlain = DecryptBlock(
                derivedKey,
                exportFile.Protection.Check,
                Encoding.UTF8.GetBytes("SNTERM-CHECK"));

            string checkStr = Encoding.UTF8.GetString(checkPlain);
            if (checkStr == "SNTERM-OK")
            {
                return true;
            }
        }
        catch { }

        if (derivedKey != null)
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            derivedKey = null;
        }

        return false;
    }

    public ImportResult Import(
        ExportFile exportFile,
        string? exportPassword,
        ConflictResolution conflictResolution)
    {
        var result = new ImportResult
        {
            TotalInFile = exportFile.Sessions.Count
        };

        byte[]? derivedKey = null;

        try
        {
            if (exportFile.Protection != null)
            {
                if (string.IsNullOrEmpty(exportPassword) || !VerifyPassword(exportFile, exportPassword, out derivedKey) || derivedKey == null)
                {
                    throw new CryptographicException("Sai mật khẩu file.");
                }
            }

            _store.BackupBeforeImport();

            var currentSessions = _store.Load(out _).ToList();

            foreach (var item in exportFile.Sessions)
            {
                // Validate fields
                if (string.IsNullOrWhiteSpace(item.Host) || item.Port < 1 || item.Port > 65535 || string.IsNullOrWhiteSpace(item.Username))
                {
                    result.SkippedCount++;
                    result.Messages.Add($"Bỏ qua VM '{item.Name}' do thông tin không hợp lệ.");
                    continue;
                }

                string? plainPassword = null;
                string? plainPassphrase = null;
                string? resolvedKeyFilePath = null;

                if (derivedKey != null && item.Secrets != null)
                {
                    try
                    {
                        byte[] aad = Encoding.UTF8.GetBytes(item.Id.ToString());
                        byte[] secretBytes = DecryptBlock(derivedKey, item.Secrets, aad);
                        string secretJson = Encoding.UTF8.GetString(secretBytes);
                        var payload = JsonSerializer.Deserialize<ExportSecretPayload>(secretJson);

                        if (payload != null)
                        {
                            plainPassword = payload.Password;
                            plainPassphrase = payload.Passphrase;

                            if (!string.IsNullOrEmpty(payload.KeyFileContent) && !string.IsNullOrEmpty(item.KeyFileName))
                            {
                                byte[] keyBytes = Convert.FromBase64String(payload.KeyFileContent);
                                string targetKeyFile = Path.Combine(_paths.KeysDir, $"{item.Id:N}_{item.KeyFileName}");
                                File.WriteAllBytes(targetKeyFile, keyBytes);
                                resolvedKeyFilePath = targetKeyFile;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        result.CorruptSecretsCount++;
                        result.Messages.Add($"VM '{item.Name}': Không thể giải mã mật khẩu (dữ liệu bí mật bị hỏng hoặc đã bị sửa).");
                    }
                }

                // Check conflict: same Id OR (same Host + Port + Username)
                var existing = currentSessions.FirstOrDefault(s =>
                    s.Id == item.Id ||
                    (string.Equals(s.Host, item.Host, StringComparison.OrdinalIgnoreCase) &&
                     s.Port == item.Port &&
                     string.Equals(s.Username, item.Username, StringComparison.OrdinalIgnoreCase)));

                if (existing != null)
                {
                    switch (conflictResolution)
                    {
                        case ConflictResolution.Skip:
                            result.SkippedCount++;
                            continue;

                        case ConflictResolution.Overwrite:
                            existing.Name = item.Name;
                            existing.Group = item.Group;
                            existing.Host = item.Host.Trim();
                            existing.Port = item.Port;
                            existing.Username = item.Username.Trim();
                            if (plainPassword != null)
                            {
                                existing.SavePassword = true;
                                existing.EncryptedPassword = SecretProtector.Encrypt(plainPassword);
                            }
                            if (plainPassphrase != null)
                            {
                                existing.EncryptedPassphrase = SecretProtector.Encrypt(plainPassphrase);
                            }
                            if (resolvedKeyFilePath != null)
                            {
                                existing.KeyFilePath = resolvedKeyFilePath;
                            }
                            result.OverwrittenCount++;
                            continue;

                        case ConflictResolution.AddCopy:
                            var newCopy = new SessionInfo
                            {
                                Id = Guid.NewGuid(),
                                Name = $"{item.Name} (nhập)",
                                Group = item.Group,
                                Host = item.Host.Trim(),
                                Port = item.Port,
                                Username = item.Username.Trim(),
                                SavePassword = plainPassword != null,
                                EncryptedPassword = SecretProtector.Encrypt(plainPassword),
                                EncryptedPassphrase = SecretProtector.Encrypt(plainPassphrase),
                                KeyFilePath = resolvedKeyFilePath,
                                CreatedAt = DateTime.UtcNow
                            };
                            currentSessions.Add(newCopy);
                            result.AddedCopyCount++;
                            result.ImportedCount++;
                            continue;
                    }
                }

                // New session
                var newSession = new SessionInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Group = item.Group,
                    Host = item.Host.Trim(),
                    Port = item.Port,
                    Username = item.Username.Trim(),
                    SavePassword = plainPassword != null,
                    EncryptedPassword = SecretProtector.Encrypt(plainPassword),
                    EncryptedPassphrase = SecretProtector.Encrypt(plainPassphrase),
                    KeyFilePath = resolvedKeyFilePath,
                    CreatedAt = DateTime.UtcNow
                };

                currentSessions.Add(newSession);
                result.ImportedCount++;
            }

            _store.Save(currentSessions);
            return result;
        }
        finally
        {
            if (derivedKey != null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
        }
    }

    private static byte[] DecryptBlock(byte[] key, ExportCipherBlock block, byte[] aad)
    {
        byte[] nonce = Convert.FromBase64String(block.Nonce);
        byte[] cipherBytes = Convert.FromBase64String(block.CipherText);
        byte[] tag = Convert.FromBase64String(block.Tag);
        byte[] plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes, aad);
        }

        return plainBytes;
    }
}
