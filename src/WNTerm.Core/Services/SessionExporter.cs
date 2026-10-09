using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WNTerm.Models;

namespace WNTerm.Services;

public class SessionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Export(
        IEnumerable<SessionInfo> sessions,
        string filePath,
        string? exportPassword = null,
        bool includeKeyContent = false)
    {
        bool hasProtection = !string.IsNullOrEmpty(exportPassword);
        byte[]? derivedKey = null;

        var exportFile = new ExportFile
        {
            Format = "wnterm-sessions",
            Version = 1,
            ExportedAt = DateTime.UtcNow,
            AppVersion = "1.0.0"
        };

        try
        {
            if (hasProtection && exportPassword != null)
            {
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                    exportPassword,
                    salt,
                    600000,
                    HashAlgorithmName.SHA256,
                    32);

                // Check block
                var checkBlock = EncryptBytes(
                    derivedKey,
                    Encoding.UTF8.GetBytes("WNTERM-OK"),
                    Encoding.UTF8.GetBytes("WNTERM-CHECK"));

                exportFile.Protection = new ExportProtection
                {
                    Kdf = "PBKDF2-SHA256",
                    Iterations = 600000,
                    Salt = Convert.ToBase64String(salt),
                    Check = checkBlock
                };
            }

            foreach (var s in sessions)
            {
                string? keyFileName = !string.IsNullOrWhiteSpace(s.KeyFilePath)
                    ? Path.GetFileName(s.KeyFilePath)
                    : null;

                var item = new ExportSessionItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    Group = s.Group,
                    Tags = s.Tags.Count > 0 ? new List<string>(s.Tags) : null,
                    Host = s.Host,
                    Port = s.Port,
                    Username = s.Username,
                    KeyFileName = keyFileName
                };

                if (hasProtection && derivedKey != null)
                {
                    string? plainPass = !string.IsNullOrEmpty(s.EncryptedPassword)
                        ? SecretProtector.Decrypt(s.EncryptedPassword)
                        : null;

                    string? plainPassphrase = !string.IsNullOrEmpty(s.EncryptedPassphrase)
                        ? SecretProtector.Decrypt(s.EncryptedPassphrase)
                        : null;

                    string? keyContentBase64 = null;
                    if (includeKeyContent && !string.IsNullOrEmpty(s.KeyFilePath) && File.Exists(s.KeyFilePath))
                    {
                        try
                        {
                            byte[] keyBytes = File.ReadAllBytes(s.KeyFilePath);
                            keyContentBase64 = Convert.ToBase64String(keyBytes);
                        }
                        catch { }
                    }

                    var secretPayload = new ExportSecretPayload
                    {
                        Password = plainPass,
                        Passphrase = plainPassphrase,
                        KeyFileContent = keyContentBase64
                    };

                    string secretJson = JsonSerializer.Serialize(secretPayload);
                    byte[] secretBytes = Encoding.UTF8.GetBytes(secretJson);
                    byte[] aad = Encoding.UTF8.GetBytes(s.Id.ToString());

                    item.Secrets = EncryptBytes(derivedKey, secretBytes, aad);
                }

                exportFile.Sessions.Add(item);
            }

            string json = JsonSerializer.Serialize(exportFile, JsonOptions);
            string tmp = filePath + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Move(tmp, filePath, true);
        }
        finally
        {
            if (derivedKey != null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
        }
    }

    private static ExportCipherBlock EncryptBytes(byte[] key, byte[] plainBytes, byte[] aad)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] cipherBytes = new byte[plainBytes.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using (var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag, aad);
        }

        return new ExportCipherBlock
        {
            Nonce = Convert.ToBase64String(nonce),
            CipherText = Convert.ToBase64String(cipherBytes),
            Tag = Convert.ToBase64String(tag)
        };
    }
}
