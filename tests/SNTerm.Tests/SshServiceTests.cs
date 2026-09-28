using System;
using System.IO;
using System.Net.Sockets;
using Renci.SshNet.Common;
using SNTerm.Services;
using Xunit;

namespace SNTerm.Tests;

public class SshServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _testPaths;

    public SshServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SNTermHostKeyTest_" + Guid.NewGuid().ToString("N"));
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
    public void KnownHostsStore_NewHost_ThenTrusted_ThenChanged()
    {
        var store = new KnownHostsStore(_testPaths);

        // 1. Host is new
        var status = store.CheckHost("10.0.0.1", 22, "sha256_fp_1");
        Assert.Equal(HostKeyStatus.NewHost, status);

        // 2. Add host
        store.AddOrUpdate("10.0.0.1", 22, "ssh-rsa", "sha256_fp_1");

        // Reload to verify persistence
        var reloadedStore = new KnownHostsStore(_testPaths);
        status = reloadedStore.CheckHost("10.0.0.1", 22, "sha256_fp_1");
        Assert.Equal(HostKeyStatus.Trusted, status);

        // 3. Different fingerprint -> Changed
        status = reloadedStore.CheckHost("10.0.0.1", 22, "sha256_fp_DIFFERENT");
        Assert.Equal(HostKeyStatus.Changed, status);
    }

    [Fact]
    public void ErrorTranslator_TranslatesStandardExceptions()
    {
        var authEx = new SshAuthenticationException("Permission denied");
        string authMsg = ErrorTranslator.Translate(authEx);
        Assert.Contains("Sai thông tin đăng nhập", authMsg);

        var sockEx = new SocketException(10061);
        string sockMsg = ErrorTranslator.Translate(sockEx, "192.168.1.1", 22);
        Assert.Contains("192.168.1.1:22", sockMsg);
        Assert.Contains("Không kết nối được", sockMsg);

        var timeoutEx = new SshOperationTimeoutException("Timeout");
        string timeoutMsg = ErrorTranslator.Translate(timeoutEx, "myhost", 22);
        Assert.Contains("Hết thời gian chờ", timeoutMsg);
    }
}
