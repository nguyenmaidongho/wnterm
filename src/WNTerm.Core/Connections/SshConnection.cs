using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.Connections;

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

    // Mỗi lần kết nối tăng "thế hệ": sự kiện đến muộn từ SshClient CŨ (sau khi đã reconnect)
    // mang thế hệ cũ nên bị bỏ qua, không thể làm rớt kết nối MỚI.
    private int _generation;

    // Các hàm gỡ handler đã gắn vào client/shell/session của kết nối hiện tại.
    private List<Action>? _detachHandlers;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public event Action<string>? OutputBase64Received;
    public event Action<string>? StatusChanged;
    public event Action<string?>? Disconnected;

    public bool IsConnected => _sshClient?.IsConnected == true && _shellStream != null && _disconnectedFlag == 0;

    /// <summary>
    /// Chạy 1 lệnh phụ (vd. Server Monitor) trên kênh exec riêng. Lỗi/hết giờ chỉ trả về null —
    /// KHÔNG được làm rớt shell tương tác (việc phát hiện mất kết nối là của keepalive/health check).
    /// </summary>
    public async Task<string?> RunCommandAsync(string commandText, int timeoutSeconds = 2)
    {
        var client = _sshClient;
        if (client == null || !client.IsConnected || _disconnectedFlag != 0) return null;

        timeoutSeconds = Math.Max(1, timeoutSeconds);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        SshCommand? cmd = null;
        Task? runTask = null;
        try
        {
            // Mở kênh exec có thể chặn luồng (chờ máy chủ) → chạy ở luồng nền.
            runTask = Task.Run(async () =>
            {
                cmd = client.CreateCommand(commandText);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                await cmd.ExecuteAsync(cts.Token).ConfigureAwait(false);
            });

            var guard = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds + 2));
            if (await Task.WhenAny(runTask, guard).ConfigureAwait(false) != runTask)
            {
                return null;
            }

            await runTask.ConfigureAwait(false);
            return cmd?.Result;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { cts.Cancel(); } catch { }
            if (runTask == null || runTask.IsCompleted)
            {
                try { cmd?.Dispose(); } catch { }
                cts.Dispose();
            }
            else
            {
                // Lệnh còn treo: dọn khi nó kết thúc, không chờ ở đây.
                _ = runTask.ContinueWith(_ =>
                {
                    try { cmd?.Dispose(); } catch { }
                    cts.Dispose();
                }, TaskScheduler.Default);
            }
        }
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
        try
        {
            await ConnectCoreAsync(session, factory, password, passphrase, cols, rows, keepAliveSeconds);
        }
        catch
        {
            // Kết nối thất bại: đừng để Status kẹt ở Connecting, và dọn client dở dang (im lặng, không báo Disconnected).
            if (Status == ConnectionStatus.Connecting) Status = ConnectionStatus.Disconnected;
            CleanupFailedConnect();
            throw;
        }
    }

    private void CleanupFailedConnect()
    {
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _disconnectedFlag, 1);

        var shellStream = _shellStream;
        var sshClient = _sshClient;
        var detach = _detachHandlers;
        _shellStream = null;
        _sshClient = null;
        _socket = null;
        _detachHandlers = null;

        RunDetach(detach);
        Task.Run(() =>
        {
            try { shellStream?.Dispose(); } catch { }
            try { sshClient?.Dispose(); } catch { }
        });
    }

    private static void RunDetach(List<Action>? detach)
    {
        if (detach == null) return;
        foreach (var d in detach)
        {
            try { d(); } catch { }
        }
    }

    /// <summary>Chỉ ngắt kết nối nếu sự kiện thuộc về kết nối hiện tại (cùng thế hệ).</summary>
    private void TriggerDisconnectFrom(int generation, string? reason)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        TriggerDisconnect(reason);
    }

    private async Task ConnectCoreAsync(
        SessionInfo session,
        SshConnectionFactory factory,
        string? password,
        string? passphrase,
        int cols,
        int rows,
        int keepAliveSeconds = 5)
    {
        Status = ConnectionStatus.Connecting;
        StatusChanged?.Invoke(WNTerm.Services.LocalizationManager.Tr("Connecting SSH...", "Đang kết nối SSH..."));

        _host = session.Host;
        _port = session.Port;

        // Tăng thế hệ TRƯỚC khi mở cờ: mọi sự kiện muộn của client cũ từ đây bị bỏ qua.
        int gen = Interlocked.Increment(ref _generation);
        _disconnectedFlag = 0;

        cols = Math.Max(10, cols);
        rows = Math.Max(5, rows);

        var connInfo = factory.CreateConnectionInfo(session, password, passphrase);
        int interval = keepAliveSeconds > 0 ? Math.Clamp(keepAliveSeconds, 2, 60) : 5;
        var client = new SshClient(connInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(interval)
        };
        _sshClient = client;
        var detach = new List<Action>();
        _detachHandlers = detach;

        EventHandler<ExceptionEventArgs> onClientError = (sender, e) =>
            TriggerDisconnectFrom(gen, ErrorTranslator.Translate(e.Exception, _host, _port));
        client.ErrorOccurred += onClientError;
        detach.Add(() => client.ErrorOccurred -= onClientError);

        factory.AttachHostKeyVerification(client, session.Host, session.Port);

        await client.ConnectAsync(CancellationToken.None);

        AttachSessionAndSocket(client, gen, detach);

        var shell = client.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 65536);
        _shellStream = shell;

        EventHandler<EventArgs> onShellClosed = (sender, e) => TriggerDisconnectFrom(gen, null);
        EventHandler<ExceptionEventArgs> onShellError = (sender, e) =>
            TriggerDisconnectFrom(gen, ErrorTranslator.Translate(e.Exception, _host, _port));
        shell.Closed += onShellClosed;
        shell.ErrorOccurred += onShellError;
        detach.Add(() => shell.Closed -= onShellClosed);
        detach.Add(() => shell.ErrorOccurred -= onShellError);

        Status = ConnectionStatus.Connected;
        StatusChanged?.Invoke(WNTerm.Services.LocalizationManager.Get("Str_Connected"));

        var readCts = new CancellationTokenSource();
        _readCts = readCts;
        _readTask = Task.Run(() => ReadLoopAsync(shell, readCts.Token, gen));

        _healthCts = new CancellationTokenSource();
        StartHealthCheckLoop(_healthCts.Token, gen);
    }

    private void AttachSessionAndSocket(SshClient client, int gen, List<Action> detach)
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
                    EventHandler handler = (sender, e) => TriggerDisconnectFrom(gen, null);
                    disEvent.AddEventHandler(session, handler);
                    detach.Add(() => disEvent.RemoveEventHandler(session, handler));
                }

                var errEvent = sessionType.GetEvent("ErrorOccured");
                if (errEvent != null && errEvent.EventHandlerType == typeof(EventHandler<ExceptionEventArgs>))
                {
                    EventHandler<ExceptionEventArgs> errHandler = (sender, e) =>
                        TriggerDisconnectFrom(gen, ErrorTranslator.Translate(e.Exception, _host, _port));
                    errEvent.AddEventHandler(session, errHandler);
                    detach.Add(() => errEvent.RemoveEventHandler(session, errHandler));
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

    private void StartHealthCheckLoop(CancellationToken token, int gen)
    {
        Task.Run(async () =>
        {
            int probeFailures = 0;
            while (!token.IsCancellationRequested && _disconnectedFlag == 0 && gen == Volatile.Read(ref _generation))
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

                // 1. Socket: chỉ xem trạng thái/lỗi. KHÔNG dùng Poll(SelectRead)&&Available==0 —
                // cách đó tranh dữ liệu với luồng đọc của SSH.NET và báo nhầm mất kết nối.
                var sock = _socket;
                if (sock != null)
                {
                    try
                    {
                        if (!sock.Connected || sock.Poll(0, SelectMode.SelectError))
                        {
                            TriggerDisconnectFrom(gen, ErrorTranslator.Translate(new SocketException((int)SocketError.ConnectionReset), _host, _port));
                            break;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        TriggerDisconnectFrom(gen, ErrorTranslator.Translate(ex, _host, _port));
                        break;
                    }
                }

                // 2. Keepalive chủ động (có giới hạn thời gian chờ) + IsConnected
                var client = _sshClient;
                if (client != null && _disconnectedFlag == 0)
                {
                    if (!client.IsConnected)
                    {
                        TriggerDisconnectFrom(gen, null);
                        break;
                    }

                    // Mạng di động/thiết bị chậm: chỉ coi là mất kết nối khi probe lỗi liên tiếp 3 lần.
                    if (ProbeKeepAliveOrTimeout(client, out var probeError))
                    {
                        probeFailures = 0;
                    }
                    else if (++probeFailures >= 3)
                    {
                        TriggerDisconnectFrom(gen, ErrorTranslator.Translate(probeError ?? new SocketException((int)SocketError.TimedOut), _host, _port));
                        break;
                    }
                }
            }
        }, token);
    }

    private static readonly TimeSpan KeepAliveProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gọi SendKeepAlive() trên luồng nền và chờ có giới hạn thời gian.
    /// Nếu mạng bị rớt kiểu "im lặng" (không có RST/FIN, chỉ mất gói),
    /// SendKeepAlive() có thể không trả về (hoặc không ném lỗi) trong thời
    /// gian dài, khiến việc dò mất kết nối bị "treo" vô thời hạn. Giới hạn
    /// thời gian chờ ở đây đảm bảo mất kết nối luôn được phát hiện trong vài
    /// giây, bất kể hành vi bên trong SSH.NET/OS ra sao.
    /// </summary>
    private bool ProbeKeepAliveOrTimeout(SshClient client, out Exception? error)
    {
        Exception? captured = null;
        var probeTask = Task.Run(() =>
        {
            try
            {
                #pragma warning disable CS0618
                client.SendKeepAlive();
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        if (!probeTask.Wait(KeepAliveProbeTimeout))
        {
            error = new TimeoutException();
            return false;
        }

        error = captured;
        return captured == null;
    }

    public void CheckHealthNow()
    {
        if (_disconnectedFlag != 0 || Status != ConnectionStatus.Connected) return;

        var sock = _socket;
        if (sock != null)
        {
            try
            {
                if (!sock.Connected || sock.Poll(0, SelectMode.SelectError))
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

            if (!ProbeKeepAliveOrTimeout(client, out var probeError))
            {
                TriggerDisconnect(ErrorTranslator.Translate(probeError ?? new SocketException((int)SocketError.TimedOut), _host, _port));
                return;
            }
        }
    }

    private async Task ReadLoopAsync(ShellStream stream, CancellationToken token, int gen)
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
            TriggerDisconnectFrom(gen, ErrorTranslator.Translate(ex, _host, _port));
            return;
        }
        finally
        {
            if (batchStream.Length > 0 && gen == Volatile.Read(ref _generation))
            {
                FlushBatch(batchStream);
            }
        }

        TriggerDisconnectFrom(gen, null);
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
#if DEBUG
        Console.WriteLine("[WNTerm] TriggerDisconnect: " + reason);
#endif
        if (Interlocked.Exchange(ref _disconnectedFlag, 1) != 0) return;

        Status = ConnectionStatus.Disconnected;

        // Lấy tạm (snapshot) rồi xoá field ngay lập tức. Nếu người dùng bấm R
        // để reconnect trong lúc việc dọn dẹp kết nối cũ bên dưới còn đang chạy
        // (có thể chậm/treo), field của SshConnection sẽ không bị luồng dọn dẹp
        // cũ ghi đè null lên kết nối MỚI vừa tạo.
        var healthCts = _healthCts;
        var readCts = _readCts;
        var shellStream = _shellStream;
        var sshClient = _sshClient;
        var socket = _socket;
        var detach = _detachHandlers;

        _healthCts = null;
        _readCts = null;
        _shellStream = null;
        _sshClient = null;
        _socket = null;
        _detachHandlers = null;

        try { healthCts?.Cancel(); } catch { }
        try { readCts?.Cancel(); } catch { }

        // Gỡ handler khỏi client/shell/session cũ để chúng không còn gọi ngược vào đối tượng này.
        RunDetach(detach);

        // Báo mất kết nối cho UI NGAY, KHÔNG chờ dọn dẹp socket/SSH client cũ.
        // Khi mất mạng kiểu "im lặng" (không có RST/FIN, chỉ rớt gói), việc gọi
        // SshClient.Disconnect()/Dispose() bên dưới cố gắng gửi gói ngắt kết nối
        // lịch sự qua 1 socket đã chết và có thể bị OS chặn (block) rất lâu chờ
        // timeout TCP. Nếu để việc đó chạy trước, UI sẽ "treo" không hiện được
        // thông báo mất kết nối / phím R.
        if (!isExplicit)
        {
            Disconnected?.Invoke(reason);
        }

        // Dọn dẹp kết nối cũ ở luồng nền (fire-and-forget). Có thể chậm nhưng
        // không còn ảnh hưởng gì tới UI hay tới kết nối mới (nếu người dùng đã
        // reconnect) vì các field đã được xoá/thay thế ở trên.
        Task.Run(() =>
        {
            try { shellStream?.Dispose(); } catch { }
            // Đóng thẳng socket TCP (nhanh, không cần bắt tay) trước, để nếu
            // SshClient cố gắng đóng "lịch sự" thì cũng phát hiện socket đã
            // đóng ngay thay vì tiếp tục chờ mạng.
            try { socket?.Close(); } catch { }
            try
            {
                if (sshClient?.IsConnected == true)
                {
                    sshClient.Disconnect();
                }
            }
            catch { }
            try { sshClient?.Dispose(); } catch { }
        });
    }

    public void Disconnect() => Dispose();

    public void Dispose()
    {
        TriggerDisconnect(null, isExplicit: true);
    }
}