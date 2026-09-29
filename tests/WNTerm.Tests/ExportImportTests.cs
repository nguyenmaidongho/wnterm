using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WNTerm.Models;
using WNTerm.Services;
using Xunit;

namespace WNTerm.Tests;

public class ExportImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _testPaths;
    private readonly SessionStore _sessionStore;
    private readonly SessionExporter _exporter;
    private readonly SessionImporter _importer;

    public ExportImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WNTermExportTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _testPaths = new AppPaths(_tempDir, _tempDir);
        _sessionStore = new SessionStore(_testPaths);
        _exporter = new SessionExporter();
        _importer = new SessionImporter(_sessionStore, _testPaths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void ExportImport_Unprotected_RoundTrip_PreservesAllAndNoPasswordsInFile()
    {
        string secretPass = "SuperSecretPassword123";
        var sessions = new List<SessionInfo>
        {
            new SessionInfo
            {
                Name = "Máy chủ tiếng Việt: Ắ Ằ Ợ",
                Group = "Dev",
                Host = "10.0.0.1",
                Port = 22,
                Username = "ubuntu",
                SavePassword = true,
                EncryptedPassword = SecretProtector.Encrypt(secretPass)
            }
        };

        string exportPath = Path.Combine(_tempDir, "unprotected.wnterm");
        _exporter.Export(sessions, exportPath, exportPassword: null);

        // 1. Raw file must NOT contain secret password
        string rawJson = File.ReadAllText(exportPath);
        Assert.DoesNotContain(secretPass, rawJson);
        Assert.Contains("Máy chủ tiếng Việt", rawJson);

        // 2. Import
        var exportFile = _importer.ReadAndValidateFile(exportPath);
        Assert.Null(exportFile.Protection);

        var result = _importer.Import(exportFile, null, ConflictResolution.Overwrite);
        Assert.Equal(1, result.ImportedCount);

        var loaded = _sessionStore.Load(out _);
        Assert.Single(loaded);
        Assert.Equal("Máy chủ tiếng Việt: Ắ Ằ Ợ", loaded[0].Name);
        Assert.Null(loaded[0].EncryptedPassword); // Unprotected mode leaves password empty
    }

    [Fact]
    public void ExportImport_Protected_RoundTrip_DecryptsCorrectlyWithAAD()
    {
        string exportPass = "StrongPassword@123";
        string vmPass = "VMPassword_456!";

        // Create a dummy key file
        string keyFile = Path.Combine(_tempDir, "id_ed25519");
        File.WriteAllText(keyFile, "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----");

        var sessions = new List<SessionInfo>
        {
            new SessionInfo
            {
                Name = "Protected-VM",
                Host = "10.0.0.5",
                Port = 22,
                Username = "root",
                SavePassword = true,
                EncryptedPassword = SecretProtector.Encrypt(vmPass),
                KeyFilePath = keyFile
            }
        };

        string exportPath = Path.Combine(_tempDir, "protected.wnterm");
        _exporter.Export(sessions, exportPath, exportPass, includeKeyContent: true);

        // Check raw json: no plain text password
        string rawJson = File.ReadAllText(exportPath);
        Assert.DoesNotContain(vmPass, rawJson);

        var exportFile = _importer.ReadAndValidateFile(exportPath);
        Assert.NotNull(exportFile.Protection);

        // Wrong password should fail
        Assert.Throws<CryptographicException>(() =>
            _importer.Import(exportFile, "WrongPassword", ConflictResolution.Overwrite));

        // Correct password succeeds
        var result = _importer.Import(exportFile, exportPass, ConflictResolution.Overwrite);
        Assert.Equal(1, result.ImportedCount);

        var loaded = _sessionStore.Load(out _);
        Assert.Single(loaded);
        Assert.Equal(vmPass, SecretProtector.Decrypt(loaded[0].EncryptedPassword));
        Assert.NotNull(loaded[0].KeyFilePath);
        Assert.True(File.Exists(loaded[0].KeyFilePath));
    }

    [Fact]
    public void ExportImport_CorruptOneByte_SkipsThatSecretOnly()
    {
        string exportPass = "Pass123456";
        var sessions = new List<SessionInfo>
        {
            new SessionInfo { Name = "VM1", Host = "10.0.0.1", Username = "user1", EncryptedPassword = SecretProtector.Encrypt("p1") },
            new SessionInfo { Name = "VM2", Host = "10.0.0.2", Username = "user2", EncryptedPassword = SecretProtector.Encrypt("p2") }
        };

        string exportPath = Path.Combine(_tempDir, "corrupt_byte.wnterm");
        _exporter.Export(sessions, exportPath, exportPass);

        // Tamper 1 character in VM1's ciphertext
        string json = File.ReadAllText(exportPath);
        var exportFile = JsonSerializer.Deserialize<ExportFile>(json)!;
        var vm1 = exportFile.Sessions.First(s => s.Name == "VM1");
        byte[] cipherBytes = Convert.FromBase64String(vm1.Secrets!.CipherText);
        cipherBytes[0] ^= 0xFF; // flip bits
        vm1.Secrets.CipherText = Convert.ToBase64String(cipherBytes);

        var result = _importer.Import(exportFile, exportPass, ConflictResolution.Overwrite);
        Assert.Equal(1, result.CorruptSecretsCount); // 1 corrupt
        Assert.Equal(2, result.ImportedCount);

        var loaded = _sessionStore.Load(out _);
        var loadedVm1 = loaded.First(s => s.Name == "VM1");
        var loadedVm2 = loaded.First(s => s.Name == "VM2");

        Assert.Null(loadedVm1.EncryptedPassword); // Damaged secrets cleared
        Assert.Equal("p2", SecretProtector.Decrypt(loadedVm2.EncryptedPassword)); // VM2 still good
    }

    [Fact]
    public void ExportImport_SwappingSecretsBetweenSessions_FailsDueToAAD()
    {
        string exportPass = "Pass123456";
        var sessions = new List<SessionInfo>
        {
            new SessionInfo { Name = "VM1", Host = "10.0.0.1", Username = "user1", EncryptedPassword = SecretProtector.Encrypt("p1") },
            new SessionInfo { Name = "VM2", Host = "10.0.0.2", Username = "user2", EncryptedPassword = SecretProtector.Encrypt("p2") }
        };

        string exportPath = Path.Combine(_tempDir, "swap_secrets.wnterm");
        _exporter.Export(sessions, exportPath, exportPass);

        var exportFile = _importer.ReadAndValidateFile(exportPath);
        // Swap secrets block of VM1 and VM2
        var tempSecret = exportFile.Sessions[0].Secrets;
        exportFile.Sessions[0].Secrets = exportFile.Sessions[1].Secrets;
        exportFile.Sessions[1].Secrets = tempSecret;

        var result = _importer.Import(exportFile, exportPass, ConflictResolution.Overwrite);
        Assert.Equal(2, result.CorruptSecretsCount); // Both swapped secrets fail AAD check

        var loaded = _sessionStore.Load(out _);
        Assert.All(loaded, s => Assert.Null(s.EncryptedPassword));
    }

    [Fact]
    public void ExportImport_ConflictResolutions_Skip_Overwrite_AddCopy()
    {
        var existing = new SessionInfo
        {
            Name = "Original VM",
            Host = "10.0.0.1",
            Port = 22,
            Username = "user"
        };
        _sessionStore.Save(new[] { existing });

        var exportFile = new ExportFile
        {
            Format = "wnterm-sessions",
            Version = 1,
            Sessions = new List<ExportSessionItem>
            {
                new ExportSessionItem
                {
                    Id = existing.Id,
                    Name = "Renamed VM",
                    Host = "10.0.0.1",
                    Port = 22,
                    Username = "user"
                }
            }
        };

        // 1. Skip: does not change
        var resSkip = _importer.Import(exportFile, null, ConflictResolution.Skip);
        Assert.Equal(1, resSkip.SkippedCount);
        var loaded = _sessionStore.Load(out _);
        Assert.Equal("Original VM", loaded[0].Name);

        // 2. Overwrite: updates name
        var resOverwrite = _importer.Import(exportFile, null, ConflictResolution.Overwrite);
        Assert.Equal(1, resOverwrite.OverwrittenCount);
        loaded = _sessionStore.Load(out _);
        Assert.Equal("Renamed VM", loaded[0].Name);

        // 3. AddCopy: adds new item with (nhập)
        var resCopy = _importer.Import(exportFile, null, ConflictResolution.AddCopy);
        Assert.Equal(1, resCopy.AddedCopyCount);
        loaded = _sessionStore.Load(out _);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, s => s.Name == "Renamed VM (nhập)");
    }

    [Fact]
    public void ExportImport_InvalidFormatOrVersion_ThrowsException()
    {
        string badFormat = Path.Combine(_tempDir, "bad_format.json");
        File.WriteAllText(badFormat, "{\"format\": \"unknown\", \"version\": 1}");
        Assert.Throws<InvalidDataException>(() => _importer.ReadAndValidateFile(badFormat));

        string higherVersion = Path.Combine(_tempDir, "high_version.json");
        File.WriteAllText(higherVersion, "{\"format\": \"wnterm-sessions\", \"version\": 999}");
        Assert.Throws<NotSupportedException>(() => _importer.ReadAndValidateFile(higherVersion));
    }
}
