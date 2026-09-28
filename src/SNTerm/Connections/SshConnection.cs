using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
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
    private Socket? _socket;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _healthCts;
    private Task? _readTask;
    private int _disconnectedFlag;
    private string _host = "";
    private int _port = 22;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public event Action<string>? OutputBase64Received;
    public event Action<string>? StatusChanged;
    public event Action<string?>? Disconnected;

    public bool IsConnected => _sshClient?.IsConnected == true && _shellStream != null && _disconnectedFlag == 0;

    public async Task<string?> RunCommandAsync(string commandText, int timeoutSeconds = 2)
    {
        var client = _sshClient;
        if (client == null || !client.IsConnected || _disconnectedFlag != 0)
        {
            if (Status == ConnectionStatus.Connected)
            {
                TriggerDisconnect(null);
            }
            return null;
        }

        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() =>
        {
            try
            {
                using var cmd = client.CreateCommand(commandText);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                return cmd.Execute();
            }
            catch (Exception ex)
            {
                TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
                return null;
            }
        }, cts.Token);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds + 2));
        var completed = await Task.WhenAny(runTask, timeoutTask);

        if (completed != runTask)
        {
            cts.Cancel();
            TriggerDisconnect(ErrorTranslator.Translate(new TimeoutException(), _host, _port));
            return null;
        }

        return await runTask;
    }

    public async Task ConnectAsync(
        SessionInfo session,
        SshConnectionFactory factory,
        string? password,
        string? passphrase,
        int cols,
        int rows,
        int keepAliveSeconds = 5)
    {
        Status = ConnectionStatus.Connecting;
        StatusChanged?.Invoke("Đang kết nối SSH...");

        _host = session.Host;
        _port = session.Port;
        _disconnectedFlag = 0;

        cols = Math.Max(10, cols);
        rows = Math.Max(5, rows);

        var connInfo = factory.CreateConnectionInfo(session, password, passphrase);
        int interval = keepAliveSeconds > 0 ? Math.Clamp(keepAliveSeconds, 2, 60) : 5;
        _sshClient = new SshClient(connInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(interval)
        };

        _sshClient.ErrorOccurred += (sender, e) =>
        {
            TriggerDisconnect(ErrorTranslator.Translate(e.Exception, _host, _port));
        };

        factory.AttachHostKeyVerification(_sshClient, session.Host, session.Port);

        await _sshClient.ConnectAsync(CancellationToken.None);

        AttachSessionAndSocket(_sshClient);

        _shellStream = _sshClient.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 65536);

        _shellStream.Closed += (sender, e) =>
        {
            TriggerDisconnect(null);
        };

        _shellStream.ErrorOccurred += (sender, e) =>
        {
            TriggerDisconnect(ErrorTranslator.Translate(e.Exception, _host, _port));
        };

        Status = ConnectionStatus.Connected;
        StatusChanged?.Invoke("Đã kết nối");

        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_shellStream, _readCts.Token));

        _healthCts = new CancellationTokenSource();
        StartHealthCheckLoop(_healthCts.Token);
    }

    private void AttachSessionAndSocket(SshClient client)
    {
        try
        {
            var sessionProp = typeof(BaseClient).GetProperty("Session", BindingFlags.NonPublic | BindingFlags.Instance);
            var session = sessionProp?.GetValue(client);
            if (session != null)
            {
                var sessionType = session.GetType();

                var disEvent = sessionType.GetEvent("Disconnected");
                if (disEvent != null)
                {
                    EventHandler handler = (sender, e) => TriggerDisconnect(null);
                    disEvent.AddEventHandler(session, handler);
                }

                var errEvent = sessionType.GetEvent("ErrorOccured");
                if (errEvent != null)
                {
                    var handlerMethod = GetType().GetMethod(nameof(OnSessionErrorOccured), BindingFlags.NonPublic | BindingFlags.Instance);
                    if (handlerMethod != null)
                    {
                        var del = Delegate.CreateDelegate(errEvent.EventHandlerType!, this, handlerMethod);
                        errEvent.AddEventHandler(session, del);
                    }
                }

                var socketField = sessionType.GetField("_socket", BindingFlags.NonPublic | BindingFlags.Instance);
                if (socketField?.GetValue(session) is Socket socket)
                {
                    _socket = socket;
                    try
                    {
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 2);
                        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
                        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private void OnSessionErrorOccured(object? sender, ExceptionEventArgs e)
    {
        TriggerDisconnect(ErrorTranslator.Translate(e.Exception, _host, _port));
    }

    private void StartHealthCheckLoop(CancellationToken token)
    {
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _disconnectedFlag == 0)
            {
                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_disconnectedFlag != 0) break;

                // 1. Socket poll
                var sock = _socket;
                if (sock != null)
                {
                    try
                    {
                        if (!sock.Connected)
                        {
                            TriggerDisconnect(ErrorTranslator.Translate(new SocketException((int)SocketError.ConnectionReset), _host, _port));
                            break;
                        }

                        if (sock.Poll(0, SelectMode.SelectRead) && sock.Available == 0)
                        {
                            TriggerDisconnect(ErrorTranslator.Translate(new SocketException((int)SocketError.ConnectionReset), _host, _port));
                            break;
                        }

                        if (sock.Poll(0, SelectMode.SelectError))
                        {
                            TriggerDisconnect(ErrorTranslator.Translate(new SocketException((int)SocketError.ConnectionReset), _host, _port));
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
                        break;
                    }
                }

                // 2. Active probe
                var client = _sshClient;
                if (client != null && _disconnectedFlag == 0)
                {
                    if (!client.IsConnected)
                    {
                        TriggerDisconnect(null);
                        break;
                    }

                    try
                    {
                        #pragma warning disable CS0618
                        client.SendKeepAlive();
#pragma warning restore CS0618
                    }
                    catch (Exception ex)
                    {
                        TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
                        break;
                    }
                }
            }
        }, token);
    }

    public void CheckHealthNow()
    {
        if (_disconnectedFlag != 0 || Status != ConnectionStatus.Connected) return;

        var sock = _socket;
        if (sock != null)
        {
            try
            {
                if (!sock.Connected || (sock.Poll(0, SelectMode.SelectRead) && sock.Available == 0) || sock.Poll(0, SelectMode.SelectError))
                {
                    TriggerDisconnect(ErrorTranslator.Translate(new SocketException((int)SocketError.ConnectionReset), _host, _port));
                    return;
                }
            }
            catch (Exception ex)
            {
                TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
                return;
            }
        }

        var client = _sshClient;
        if (client != null)
        {
            if (!client.IsConnected)
            {
                TriggerDisconnect(null);
                return;
            }

            try
            {
                #pragma warning disable CS0618
                        client.SendKeepAlive();
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
                return;
            }
        }
    }

    private async Task ReadLoopAsync(ShellStream stream, CancellationToken token)
    {
        byte[] buffer = new byte[32768];
        using var batchStream = new MemoryStream();

        try
        {
            while (!token.IsCancellationRequested && stream.CanRead && _disconnectedFlag == 0)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (read <= 0) break;

                batchStream.Write(buffer, 0, read);

                if (!stream.DataAvailable || batchStream.Length >= 65536)
                {
                    FlushBatch(batchStream);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
            return;
        }
        finally
        {
            if (batchStream.Length > 0)
            {
                FlushBatch(batchStream);
            }
        }

        TriggerDisconnect(null);
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
        if (_shellStream == null || !IsConnected || _disconnectedFlag != 0)
        {
            if (Status == ConnectionStatus.Connected)
            {
                TriggerDisconnect(null);
            }
            return;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            _shellStream.Write(bytes, 0, bytes.Length);
            _shellStream.Flush();
        }
        catch (Exception ex)
        {
            TriggerDisconnect(ErrorTranslator.Translate(ex, _host, _port));
        }
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

    private void TriggerDisconnect(string? reason, bool isExplicit = false)
    {
        if (Interlocked.Exchange(ref _disconnectedFlag, 1) != 0) return;

        Status = ConnectionStatus.Disconnected;

        try { _healthCts?.Cancel(); } catch { }
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
        _socket = null;

        if (!isExplicit)
        {
            Disconnected?.Invoke(reason);
        }
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        TriggerDisconnect(null, isExplicit: true);
    }
}