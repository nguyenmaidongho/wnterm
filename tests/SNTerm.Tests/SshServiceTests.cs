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
        string authMsg = ErrorTranslator.Translate(authEx, language: "vi");
        Assert.Contains("Sai thông tin đăng nhập", authMsg);

        var sockEx = new SocketException(10061);
        string sockMsg = ErrorTranslator.Translate(sockEx, "192.168.1.1", 22, language: "vi");
        Assert.Contains("192.168.1.1", sockMsg);
        Assert.DoesNotContain("192.168.1.1:22", sockMsg); // Không lộ port trong thông báo hiển thị
        Assert.Contains("Không kết nối được", sockMsg);

        var timeoutEx = new SshOperationTimeoutException("Timeout");
        string timeoutMsg = ErrorTranslator.Translate(timeoutEx, "myhost", 22, language: "vi");
        Assert.Contains("Hết thời gian chờ", timeoutMsg);
    }

    [Fact]
    public void TestDisconnect_And_PressR_Reconnect()
    {
        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var session = new SNTerm.Models.SessionInfo { Host = "127.0.0.1", Port = 22, Username = "test" };
                var paths = new SNTerm.Services.AppPaths(System.IO.Path.GetTempPath(), System.IO.Path.GetTempPath());
                var store = new SNTerm.Services.SessionStore(paths);
                var factory = new SNTerm.Services.SshConnectionFactory(new SNTerm.Services.KnownHostsStore(paths));
                var settings = new SNTerm.Models.AppSettings();
                var vm = new SNTerm.ViewModels.TerminalTabViewModel(session, factory, store, settings);

                // 1. Invoke OnConnectionDisconnected
                var disMethod = typeof(SNTerm.ViewModels.TerminalTabViewModel).GetMethod("OnConnectionDisconnected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                disMethod?.Invoke(vm, new object?[] { "Mất kết nối" });

                Assert.Equal(SNTerm.Connections.ConnectionStatus.Disconnected, vm.Status);

                // 2. Check pending messages in TerminalControl
                var pendingField = typeof(SNTerm.Views.TerminalView).GetField("_pendingMessages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var pending = (System.Collections.IList?)pendingField?.GetValue(vm.TerminalControl);
                Assert.NotNull(pending);
                Assert.NotEmpty(pending);

                string allTexts = "";
                foreach (var item in pending)
                {
                    var textProp = item?.GetType().GetProperty("text");
                    if (textProp != null)
                    {
                        allTexts += textProp.GetValue(item)?.ToString() + "\n";
                    }
                }

                Assert.Contains("[Disconnect", allTexts);
                Assert.Contains("[R]", allTexts);

                // 3. Test OnTerminalInput("a") -> stays Disconnected
                var inputMethod = typeof(SNTerm.ViewModels.TerminalTabViewModel).GetMethod("OnTerminalInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                inputMethod?.Invoke(vm, new object[] { "a" });
                Assert.Equal(SNTerm.Connections.ConnectionStatus.Disconnected, vm.Status);

                // 4. Test OnTerminalInput("r") -> transitions to Connecting!
                inputMethod?.Invoke(vm, new object[] { "r" });
                Assert.Equal(SNTerm.Connections.ConnectionStatus.Connecting, vm.Status);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;
    }

    [Fact]
    public void TestFastSocketDisconnectDetection()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new System.Net.Sockets.TcpClient("127.0.0.1", port);
        using var server = listener.AcceptTcpClient();

        Assert.True(client.Client.Connected);

        // Server terminates abruptly
        server.Client.Shutdown(System.Net.Sockets.SocketShutdown.Both);
        server.Client.Close();

        System.Threading.Thread.Sleep(50);

        bool isClosed = client.Client.Poll(1000, System.Net.Sockets.SelectMode.SelectRead) && client.Client.Available == 0;
        Assert.True(isClosed);
    }
}