using System.Net;
using System.Reflection;

namespace WNTerm.App.Services;

/// <summary>
/// Phục vụ thư mục wwwroot (nhúng trong assembly) qua loopback để WebView nào cũng nạp được xterm.js,
/// không phụ thuộc cơ chế virtual-host riêng của WebView2.
/// </summary>
public sealed class LocalWebServer : IDisposable
{
    private const string Prefix = "WNTerm.App.wwwroot.";
    private static LocalWebServer? _instance;
    private static readonly object InitLock = new();

    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, string> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Assembly _asm = typeof(LocalWebServer).Assembly;

    public int Port { get; }
    public Uri TerminalUri => new($"http://127.0.0.1:{Port}/terminal.html");

    public static LocalWebServer Instance
    {
        get
        {
            lock (InitLock) return _instance ??= new LocalWebServer();
        }
    }

    private LocalWebServer()
    {
        foreach (var name in _asm.GetManifestResourceNames())
        {
            if (name.StartsWith(Prefix, StringComparison.Ordinal))
                _resources[name[Prefix.Length..]] = name;
        }

        // Thử vài cổng ngẫu nhiên cho tới khi bind được.
        var rnd = new Random();
        for (int i = 0; ; i++)
        {
            int port = rnd.Next(20000, 60000);
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                Port = port;
                break;
            }
            catch (HttpListenerException) when (i < 20) { }
        }

        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    // ===== Kênh truyền tin terminal.js <-> C# qua HTTP (dùng khi cầu nối gốc của WebView không có, vd. Android) =====

    /// <summary>Một kênh hai chiều: JS POST lên (/__msg), C# đẩy xuống bằng SSE (/__events).</summary>
    public sealed class Channel
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        internal Action<string> OnMessage { get; }
        internal System.Threading.Channels.Channel<string> Outbox { get; } = System.Threading.Channels.Channel.CreateUnbounded<string>();

        internal Channel(Action<string> onMessage) => OnMessage = onMessage;

        /// <summary>Gửi một chuỗi JSON xuống terminal.js.</summary>
        public void Send(string json) => Outbox.Writer.TryWrite(json);

        public void Close() => Outbox.Writer.TryComplete();
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Channel> _channels = new();

    public Channel OpenChannel(Action<string> onMessage)
    {
        var ch = new Channel(onMessage);
        _channels[ch.Id] = ch;
        return ch;
    }

    public void CloseChannel(Channel ch)
    {
        _channels.TryRemove(ch.Id, out _);
        ch.Close();
    }

    private bool TryHandleBridge(HttpListenerContext ctx, string rawPath)
    {
        if (rawPath != "/__msg" && rawPath != "/__events") return false;

        var id = ctx.Request.QueryString["ch"] ?? "";
        if (!_channels.TryGetValue(id, out var ch))
        {
            ctx.Response.StatusCode = 404;
            return true;
        }

        if (rawPath == "/__msg")
        {
            using var reader = new StreamReader(ctx.Request.InputStream, System.Text.Encoding.UTF8);
            string body = reader.ReadToEnd();
            ch.OnMessage(body);
            ctx.Response.StatusCode = 204;
            return true;
        }

        // SSE: giữ kết nối mở, đẩy từng dòng "data: <json>".
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.SendChunked = true;
        var output = ctx.Response.OutputStream;
        var reader2 = ch.Outbox.Reader;
        try
        {
            var hello = System.Text.Encoding.UTF8.GetBytes(": ok\n\n");
            output.Write(hello, 0, hello.Length);
            output.Flush();
            while (true)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                string? json;
                try
                {
                    if (!reader2.WaitToReadAsync(cts.Token).AsTask().GetAwaiter().GetResult()) break;
                    if (!reader2.TryRead(out json)) continue;
                }
                catch (OperationCanceledException)
                {
                    var ping = System.Text.Encoding.UTF8.GetBytes(": ping\n\n");
                    output.Write(ping, 0, ping.Length);
                    output.Flush();
                    continue;
                }
                var bytes = System.Text.Encoding.UTF8.GetBytes("data: " + json + "\n\n");
                output.Write(bytes, 0, bytes.Length);
                output.Flush();
            }
        }
        catch { }
        return true;
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            // Chỉ phục vụ chính máy này (kênh /__msg điều khiển được terminal).
            var remote = ctx.Request.RemoteEndPoint;
            var addr = remote?.Address;
            if (addr != null && addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
            if (addr == null || !IPAddress.IsLoopback(addr))
            {
                ctx.Response.StatusCode = 403;
                return;
            }

            if (TryHandleBridge(ctx, ctx.Request.Url!.AbsolutePath)) return;
            string path = ctx.Request.Url!.AbsolutePath.TrimStart('/').Replace('/', '.');
            if (path.Length == 0) path = "terminal.html";

            if (!_resources.TryGetValue(path, out var resName))
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            using var stream = _asm.GetManifestResourceStream(resName)!;
            ctx.Response.ContentType = ContentTypeOf(path);
            ctx.Response.ContentLength64 = stream.Length;
            stream.CopyTo(ctx.Response.OutputStream);
        }
        catch { }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static string ContentTypeOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream"
    };

    public void Dispose()
    {
        try { _listener.Stop(); } catch { }
    }
}
