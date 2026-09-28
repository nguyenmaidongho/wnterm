using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.Connections;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public class SshConnection : IDisposable
{
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public event Action<string>? OutputBase64Received;
    public event Action<string>? StatusChanged;
    public event Action<string?>? Disconnected;

    public bool IsConnected => _sshClient?.IsConnected == true && _shellStream != null;

    public async Task ConnectAsync(
        SessionInfo session,
        SshConnectionFactory factory,
        string? password,
        string? passphrase,
        int cols,
        int rows,
        int keepAliveSeconds = 30)
    {
        Status = ConnectionStatus.Connecting;
        StatusChanged?.Invoke("Đang kết nối SSH...");

        cols = Math.Max(10, cols);
        rows = Math.Max(5, rows);

        var connInfo = factory.CreateConnectionInfo(session, password, passphrase);
        _sshClient = new SshClient(connInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(5, keepAliveSeconds))
        };

        factory.AttachHostKeyVerification(_sshClient, session.Host, session.Port);

        await _sshClient.ConnectAsync(CancellationToken.None);

        _shellStream = _sshClient.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 65536);

        Status = ConnectionStatus.Connected;
        StatusChanged?.Invoke("Đã kết nối");

        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_shellStream, _readCts.Token));
    }

    private async Task ReadLoopAsync(ShellStream stream, CancellationToken token)
    {
        byte[] buffer = new byte[32768];
        using var batchStream = new MemoryStream();
        var lastFlush = DateTime.UtcNow;

        try
        {
            while (!token.IsCancellationRequested && stream.CanRead)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (read <= 0) break;

                batchStream.Write(buffer, 0, read);

                var elapsed = (DateTime.UtcNow - lastFlush).TotalMilliseconds;
                if (batchStream.Length >= 65536 || elapsed >= 16)
                {
                    FlushBatch(batchStream);
                    lastFlush = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Disconnected?.Invoke(ErrorTranslator.Translate(ex));
            return;
        }
        finally
        {
            if (batchStream.Length > 0)
            {
                FlushBatch(batchStream);
            }
        }

        Status = ConnectionStatus.Disconnected;
        Disconnected?.Invoke(null);
    }

    private void FlushBatch(MemoryStream stream)
    {
        if (stream.Length == 0) return;
        string b64 = Convert.ToBase64String(stream.ToArray());
        stream.SetLength(0);
        OutputBase64Received?.Invoke(b64);
    }

    public void SendInput(string data)
    {
        if (_shellStream == null || !IsConnected) return;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            _shellStream.Write(bytes, 0, bytes.Length);
            _shellStream.Flush();
        }
        catch { }
    }

    public void Resize(int cols, int rows)
    {
        if (_shellStream == null || !IsConnected) return;

        try
        {
            cols = Math.Max(10, cols);
            rows = Math.Max(5, rows);
            _shellStream.ChangeWindowSize((uint)cols, (uint)rows, 0, 0);
        }
        catch { }
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        Status = ConnectionStatus.Disconnected;

        try { _readCts?.Cancel(); } catch { }
        try { _shellStream?.Dispose(); } catch { }
        try
        {
            if (_sshClient?.IsConnected == true)
            {
                _sshClient.Disconnect();
            }
            _sshClient?.Dispose();
        }
        catch { }

        _shellStream = null;
        _sshClient = null;
    }
}

