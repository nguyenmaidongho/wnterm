using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.ViewModels;
using Xunit;

namespace WNTerm.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _testPaths;

    public SessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WNTermTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _testPaths = new AppPaths(_tempDir, _tempDir);
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
    public void SecretProtector_Encrypt_And_Decrypt_ReturnsOriginalText()
    {
        string original = "MậtKhẩu_Siêu_Bí_Mật_123!@#";
        string? encrypted = SecretProtector.Encrypt(original);

        Assert.NotNull(encrypted);
        Assert.NotEqual(original, encrypted);

        string? decrypted = SecretProtector.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void SessionStore_SaveAndLoad_PreservesAllFieldsAndVietnamese()
    {
        var store = new SessionStore(_testPaths);
        var sessions = new List<SessionInfo>
        {
            new SessionInfo
            {
                Name = "Máy chủ thử nghiệm tiếng Việt: đ á à",
                Group = "Nhóm Phát Triển",
                Host = "192.168.1.100",
                Port = 2222,
                Username = "admin",
                SavePassword = true,
                EncryptedPassword = SecretProtector.Encrypt("pass123")
            }
        };

        store.Save(sessions);

        var loaded = store.Load(out bool recovered);
        Assert.False(recovered);
        Assert.Single(loaded);

        var s = loaded[0];
        Assert.Equal("Máy chủ thử nghiệm tiếng Việt: đ á à", s.Name);
        Assert.Equal("Nhóm Phát Triển", s.Group);
        Assert.Equal("192.168.1.100", s.Host);
        Assert.Equal(2222, s.Port);
        Assert.Equal("admin", s.Username);
        Assert.Equal("pass123", SecretProtector.Decrypt(s.EncryptedPassword));

        // Ensure raw json file does NOT contain plain text password
        string rawJson = File.ReadAllText(_testPaths.SessionsFile);
        Assert.DoesNotContain("pass123", rawJson);
        Assert.Contains("Máy chủ thử nghiệm tiếng Việt", rawJson);
    }

    [Fact]
    public void SessionStore_CorruptJson_RecoversAndCreatesCorruptBackup()
    {
        var store = new SessionStore(_testPaths);

        // First save a valid session
        var valid = new List<SessionInfo>
        {
            new SessionInfo { Name = "VM Gốc", Host = "10.0.0.1", Username = "user" }
        };
        store.Save(valid);

        // Force a backup
        string backupFile = Path.Combine(_testPaths.BackupsDir, "sessions-20260928.json");
        File.Copy(_testPaths.SessionsFile, backupFile, true);

        // Corrupt sessions.json
        File.WriteAllText(_testPaths.SessionsFile, "{ invalid json broken content");

        var loaded = store.Load(out bool recovered);
        Assert.True(recovered);
        Assert.Single(loaded);
        Assert.Equal("VM Gốc", loaded[0].Name);

        var corruptFiles = Directory.GetFiles(_testPaths.DataDir, "sessions.corrupt-*.json");
        Assert.NotEmpty(corruptFiles);
    }

    [Theory]
    [InlineData("", 22, "user", false)] // Empty host
    [InlineData("10.0.0.1", 0, "user", false)] // Port 0 invalid
    [InlineData("10.0.0.1", 70000, "user", false)] // Port > 65535 invalid
    [InlineData("10.0.0.1", 22, "", false)] // Empty user
    [InlineData("10.0.0.1", 22, "ubuntu", true)] // Valid
    public void SessionEditorViewModel_Validation(string host, int port, string user, bool expectedValid)
    {
        var vm = new SessionEditorViewModel
        {
            Host = host,
            Port = port,
            Username = user
        };

        bool isValid = vm.Validate();
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void SessionInfo_Clone_ProducesIndependentCopy()
    {
        var original = new SessionInfo
        {
            Name = "web-01",
            Host = "10.0.0.5",
            Port = 22,
            Username = "ubuntu",
            EncryptedPassword = "enc_pass"
        };

        var clone = original.Clone();

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Contains(clone.Name, new[] { "web-01 (copy)", "web-01 (bản sao)" });
        Assert.Equal(original.Host, clone.Host);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Username, clone.Username);
        Assert.Equal(original.EncryptedPassword, clone.EncryptedPassword);
    }
}
