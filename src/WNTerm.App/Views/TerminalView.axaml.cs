using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

/// <summary>xterm.js trong WebView + thanh Server Monitor. API giữ như TerminalView của bản WPF.</summary>
public partial class TerminalView : UserControl
{
    public event Action<int, int>? TerminalReady;
    public event Action<string>? TerminalInput;
    public event Action<int, int>? TerminalResized;
    public event Action<string>? CopyRequested;
    public event Action? PasteRequested;
    public event Action<bool>? ContextMenuRequested;
    public event Action<string>? HotkeyTriggered;

    /// <summary>terminal.js chặn sự kiện paste gốc (văn bản nhiều dòng) và gửi lên để hỏi xác nhận trước khi dán.</summary>
    public event Action<string>? PasteTextRequested;

    private LocalWebServer.Channel? _chan;
    private AppSettings? _lastSettings;
    private int _zoomDelta;
    private bool _isReady;
    private readonly List<string> _pending = new();
    private readonly List<double> _cpuHistory = new();
    private readonly List<double> _ramHistory = new();

    private readonly NativeWebView _web;
    private readonly TextBlock _tipHostname = Tip(Brushes.White, FontWeight.SemiBold);
    private readonly TextBlock _tipOs = Tip(new SolidColorBrush(Color.FromRgb(0x98, 0xC3, 0x79)), FontWeight.Medium);
    private readonly TextBlock _tipKernel = Tip(new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B)), FontWeight.Normal);
    private readonly TextBlock _tipArch = Tip(new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)), FontWeight.Normal);
    private readonly TextBlock _tipDiskDf = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
        FontFamily = new FontFamily("Consolas, Cascadia Code, Menlo, DejaVu Sans Mono, monospace"),
        FontSize = 11
    };

    private static TextBlock Tip(IBrush fg, FontWeight weight) => new()
    {
        Foreground = fg,
        FontWeight = weight,
        FontSize = 11,
        Margin = new Thickness(0, 2, 0, 2)
    };

    public TerminalView()
    {
        AvaloniaXamlLoader.Load(this);
        _web = this.FindControl<NativeWebView>("WebView")!;
        _web.WebMessageReceived += OnWebMessage;
        // Windows: đặt thư mục dữ liệu WebView2 vào LocalAppData (cài trong Program Files thì thư mục cạnh exe không ghi được).
        _web.EnvironmentRequested += (_, e) =>
        {
            if (e is WindowsWebView2EnvironmentRequestedEventArgs w)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.Default.WebView2Dir);
                    w.UserDataFolder = AppPaths.Default.WebView2Dir;
                }
                catch { }
            }
        };
        SetupTooltips();

        var server = LocalWebServer.Instance;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            // WebView di động: dùng kênh HTTP độc lập thay cho cầu nối gốc (không đáng tin trên Android).
            _chan = server.OpenChannel(HandleBody);
            _web.Navigate(new Uri(server.TerminalUri + "?bridge=http&ch=" + _chan.Id));
        }
        else
        {
            _web.Navigate(server.TerminalUri);
        }
    }

    // ===== Tooltips (OS/Hostname và df) =====
    private void SetupTooltips()
    {
        var osPanel = new StackPanel();
        osPanel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Tr("System information", "Thông tin hệ thống"),
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0xAF, 0xEF)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto") };
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        string[] labels = ["Hostname:", LocalizationManager.Tr("OS:", "Hệ điều hành:"), "Kernel:", LocalizationManager.Tr("Architecture:", "Kiến trúc:")];
        TextBlock[] values = [_tipHostname, _tipOs, _tipKernel, _tipArch];
        for (int i = 0; i < 4; i++)
        {
            var lbl = new TextBlock
            {
                Text = labels[i],
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(lbl, i);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
            Grid.SetRow(values[i], i);
            Grid.SetColumn(values[i], 2);
            grid.Children.Add(values[i]);
        }
        osPanel.Children.Add(grid);

        Avalonia.Controls.ToolTip.SetTip(this.FindControl<StackPanel>("OsInfoPanel")!, MakeTip(osPanel));
        Avalonia.Controls.ToolTip.SetShowDelay(this.FindControl<StackPanel>("OsInfoPanel")!, 150);

        var diskPanel = new StackPanel();
        diskPanel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Get("Str_DfOutputTitle"),
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _tipDiskDf.Text = LocalizationManager.Tr("Loading data...", "Đang tải dữ liệu...");
        diskPanel.Children.Add(new ScrollViewer
        {
            MaxHeight = 450,
            MaxWidth = 800,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _tipDiskDf
        });
        Avalonia.Controls.ToolTip.SetTip(this.FindControl<StackPanel>("DiskPanel")!, MakeTip(diskPanel));
        Avalonia.Controls.ToolTip.SetShowDelay(this.FindControl<StackPanel>("DiskPanel")!, 150);
    }

    private static Avalonia.Controls.ToolTip MakeTip(Control content) => new()
    {
        Content = content,
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x44, 0x51)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 8, 10, 8),
        MaxWidth = 850
    };

    // ===== Icon hệ điều hành =====
    private static readonly Dictionary<string, Bitmap?> OsIconCache = new(StringComparer.OrdinalIgnoreCase);

    private static Bitmap? GetOsIcon(string group)
    {
        if (string.IsNullOrWhiteSpace(group)) group = "linux";
        if (OsIconCache.TryGetValue(group, out var cached)) return cached;
        try
        {
            using var s = AssetLoader.Open(new Uri($"avares://WNTerm.App/Assets/os_{group}.png"));
            var bmp = new Bitmap(s);
            OsIconCache[group] = bmp;
            return bmp;
        }
        catch
        {
            OsIconCache[group] = null;
            return null;
        }
    }

    // ===== Cầu nối với terminal.js =====
    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e) => HandleBody(e.Body);

    /// <summary>Nhận 1 tin (object) hoặc 1 lô tin (array) từ terminal.js, tuần tự.</summary>
    private void HandleBody(string? body)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray()) HandleMessage(item);
            }
            else
            {
                HandleMessage(doc.RootElement);
            }
        }
        catch { }
    }

    private void HandleMessage(JsonElement root)
    {
        try
        {
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            int Int(string n, int d) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
            string Str(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            switch (type)
            {
                case "ready":
                    int cols = Int("cols", 80), rows = Int("rows", 24);
                    Ui.Post(() =>
                    {
                        _isReady = true;
                        this.FindControl<TextBlock>("LoadingText")!.IsVisible = false;
                        foreach (var json in _pending) Send(json);
                        _pending.Clear();
                        TerminalReady?.Invoke(cols, rows);
                    });
                    break;
                case "input":
                    string data = Str("data");
                    if (data.Length > 0) Ui.Post(() => TerminalInput?.Invoke(data));
                    break;
                case "resize":
                    int c2 = Int("cols", 0), r2 = Int("rows", 0);
                    if (c2 > 0 && r2 > 0) Ui.Post(() => TerminalResized?.Invoke(c2, r2));
                    break;
                case "copy":
                    string text = Str("text");
                    if (text.Length > 0) Ui.Post(() => CopyRequested?.Invoke(text));
                    break;
                case "requestPaste":
                    Ui.Post(() => PasteRequested?.Invoke());
                    break;
                case "contextMenu":
                    bool hasSel = root.TryGetProperty("hasSelection", out var hs) && hs.ValueKind == JsonValueKind.True;
                    Ui.Post(() => ContextMenuRequested?.Invoke(hasSel));
                    break;
                case "pasteText":
                    string pasteText = Str("text");
                    if (pasteText.Length > 0) Ui.Post(() => PasteTextRequested?.Invoke(pasteText));
                    break;
                case "hotkey":
                    string name = Str("name");
                    if (name.Length == 0) break;
                    // Ctrl+= / Ctrl+- / Ctrl+0: phóng to/thu nhỏ chữ của terminal này.
                    if (name == "ZoomIn") Ui.Post(() => Zoom(+1));
                    else if (name == "ZoomOut") Ui.Post(() => Zoom(-1));
                    else if (name == "ZoomReset") Ui.Post(() => Zoom(0));
                    else Ui.Post(() => HotkeyTriggered?.Invoke(name));
                    break;
            }
        }
        catch { }
    }

    public void PostOutput(string b64) => PostJson(new { type = "output", b64 });
    public void PostPaste(string text) => PostJson(new { type = "paste", text });
    public void PostStatus(string text) => PostJson(new { type = "status", text });
    public void PostClear() => PostJson(new { type = "clear" });
    public void PostSelectAll() => PostJson(new { type = "selectAll" });

    public void PostFocus()
    {
        _web.Focus();
        PostJson(new { type = "focus" });
    }

    public void PostSettings(AppSettings settings)
    {
        _lastSettings = settings;
        bool isLight = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        object themeObj = isLight
            ? new { background = "#ffffff", foreground = "#1e1e1e", cursor = "#1e1e1e", selectionBackground = "#add6ff" }
            : new { background = "#1e1e1e", foreground = "#d4d4d4", cursor = "#aeafad", selectionBackground = "#264f78" };

        PostJson(new
        {
            type = "settings",
            fontFamily = settings.FontFamily,
            fontSize = ZoomedFontSize(),
            theme = themeObj,
            scrollback = settings.Scrollback,
            copyOnSelect = settings.CopyOnSelect,
            rightClickAction = settings.RightClickAction,
            confirmMultilinePaste = settings.ConfirmMultilinePaste
        });
    }

    private int ZoomedFontSize() => Math.Clamp((_lastSettings?.FontSize ?? 14) + _zoomDelta, 6, 48);

    /// <summary>Phóng to (+1) / thu nhỏ (-1) / về mặc định (0) cỡ chữ của terminal này (không đổi cài đặt chung).</summary>
    public void Zoom(int step)
    {
        _zoomDelta = step == 0 ? 0 : Math.Clamp(_zoomDelta + step, -10, 30);
        PostJson(new { type = "settings", fontSize = ZoomedFontSize() });
    }

    /// <summary>Gọi khi đóng tab: đóng kênh HTTP (Android/iOS) để LocalWebServer không giữ kênh mãi.</summary>
    public void Cleanup()
    {
        var chan = _chan;
        _chan = null;
        _isReady = false;
        _pending.Clear();
        if (chan != null)
        {
            try { LocalWebServer.Instance.CloseChannel(chan); } catch { }
        }
    }

    private void PostJson(object obj)
    {
        string json = JsonSerializer.Serialize(obj);
        if (!Ui.CheckAccess()) { Ui.Post(() => PostJson(obj)); return; }
        if (_isReady) Send(json);
        else _pending.Add(json);
    }

    private void Send(string json)
    {
        try
        {
            if (_chan != null) { _chan.Send(json); return; }
            // json → literal chuỗi JS đã escape, gọi hàm nhận của terminal.js.
            string literal = JsonSerializer.Serialize(json);
            _web.InvokeScript($"window.__wntermReceive({literal})");
        }
        catch { }
    }

    // ===== Server Monitor =====
    public void UpdateMonitor(
        string osGroup, string hostname, int cpuPercent, int ramPercent, string ramText,
        string uploadText, string downloadText, string uptimeText, string usernameText,
        string diskText, string osPretty, string kernel, string arch, string dfOutput)
    {
        var icon = GetOsIcon(osGroup);
        var img = this.FindControl<Image>("OsIconImage")!;
        img.Source = icon;
        img.IsVisible = icon != null;

        this.FindControl<TextBlock>("HostnameText")!.Text = hostname;
        this.FindControl<TextBlock>("CpuText")!.Text = $"{cpuPercent}%";
        this.FindControl<TextBlock>("RamText")!.Text = ramText;
        this.FindControl<TextBlock>("UploadText")!.Text = uploadText;
        this.FindControl<TextBlock>("DownloadText")!.Text = downloadText;
        this.FindControl<TextBlock>("UptimeText")!.Text = uptimeText;
        this.FindControl<TextBlock>("UserText")!.Text = usernameText;
        this.FindControl<TextBlock>("DiskText")!.Text = diskText;

        _tipHostname.Text = string.IsNullOrEmpty(hostname) ? "--" : hostname;
        _tipOs.Text = string.IsNullOrEmpty(osPretty) ? (string.IsNullOrEmpty(osGroup) ? "--" : osGroup) : osPretty;
        _tipKernel.Text = string.IsNullOrEmpty(kernel) ? "--" : kernel;
        _tipArch.Text = string.IsNullOrEmpty(arch) ? "--" : arch;
        _tipDiskDf.Text = string.IsNullOrWhiteSpace(dfOutput) ? LocalizationManager.Tr("No df data", "Không có dữ liệu df") : dfOutput.TrimEnd();

        this.FindControl<Polyline>("CpuPolyline")!.Points = BuildPoints(_cpuHistory, cpuPercent);
        this.FindControl<Polyline>("RamPolyline")!.Points = BuildPoints(_ramHistory, ramPercent);
        this.FindControl<Border>("MonitorBar")!.IsVisible = true;
    }

    public void UpdateMonitor(
        string osGroup, string hostname, int cpuPercent, string ramText,
        string uploadText, string downloadText, string uptimeText, string usernameText,
        string diskText, string osPretty, string kernel, string arch, string dfOutput)
        => UpdateMonitor(osGroup, hostname, cpuPercent, 0, ramText, uploadText, downloadText, uptimeText, usernameText, diskText, osPretty, kernel, arch, dfOutput);

    private static List<Point> BuildPoints(List<double> history, double value)
    {
        history.Add(value);
        while (history.Count > 8) history.RemoveAt(0);

        const double w = 40.0, h = 14.0;
        double step = history.Count > 1 ? w / (history.Count - 1) : w;
        var points = new List<Point>();
        for (int i = 0; i < history.Count; i++)
        {
            double val = Math.Clamp(history[i], 0, 100);
            points.Add(new Point(i * step, h - (val / 100.0 * (h - 2.0)) - 1.0));
        }
        return points;
    }

    public void HideMonitor()
    {
        this.FindControl<Border>("MonitorBar")!.IsVisible = false;
        _cpuHistory.Clear();
        _ramHistory.Clear();
    }
}
