﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.Views;

public class WebMessage
{
    public string? type { get; set; }
    public string? data { get; set; }
    public string? text { get; set; }
    public string? name { get; set; }
    public int? cols { get; set; }
    public int? rows { get; set; }
    public bool? hasSelection { get; set; }
}

public partial class TerminalView : UserControl
{
    private static CoreWebView2Environment? _sharedEnvironment;
    private static readonly SemaphoreSlim EnvLock = new(1, 1);

    public event Action<int, int>? TerminalReady;
    public event Action<string>? TerminalInput;
    public event Action<int, int>? TerminalResized;
    public event Action<string>? CopyRequested;
    public event Action? PasteRequested;
    public event Action<bool>? ContextMenuRequested;
    public event Action<string>? HotkeyTriggered;

    private bool _isReady;
    private readonly List<object> _pendingMessages = new();
    private Task? _initTask;

    private readonly TextBlock _tipHostname = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeights.SemiBold,
        FontSize = 11,
        Margin = new Thickness(0, 2, 0, 2)
    };
    private readonly TextBlock _tipOs = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0xC3, 0x79)),
        FontWeight = FontWeights.Medium,
        FontSize = 11,
        Margin = new Thickness(0, 2, 0, 2)
    };
    private readonly TextBlock _tipKernel = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B)),
        FontSize = 11,
        Margin = new Thickness(0, 2, 0, 2)
    };
    private readonly TextBlock _tipArch = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
        FontSize = 11,
        Margin = new Thickness(0, 2, 0, 2)
    };
    private readonly TextBlock _tipDiskDf = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
        FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New, monospace"),
        FontSize = 11
    };

    public TerminalView()
    {
        InitializeComponent();
        try
        {
            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 30, 30, 30);
        }
        catch { }
        SetupTooltips();
        Loaded += TerminalView_Loaded;
        _ = InitializeWebViewAsync();
    }

    private void SetupTooltips()
    {
        // OS & Hostname ToolTip
        var osPanel = new StackPanel();
        osPanel.Children.Add(new TextBlock
        {
            Text = "Thông tin hệ thống",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0xAF, 0xEF)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (int i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] labels = ["Hostname:", "Hệ điều hành:", "Kernel:", "Kiến trúc:"];
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

        OsInfoPanel.ToolTip = new ToolTip
        {
            Content = osPanel,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x44, 0x51)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8)
        };

        // Disk ToolTip matching MobaXterm
        var diskPanel = new StackPanel();
        diskPanel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Get("Str_DfOutputTitle"),
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });

        _tipDiskDf.Text = "Đang tải dữ liệu...";
        var scroll = new ScrollViewer
        {
            MaxHeight = 450,
            MaxWidth = 800,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _tipDiskDf
        };
        diskPanel.Children.Add(scroll);

        DiskPanel.ToolTip = new ToolTip
        {
            Content = diskPanel,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x44, 0x51)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            MaxWidth = 850
        };
    }

    private static readonly Dictionary<string, ImageSource> OsIconCache = new(StringComparer.OrdinalIgnoreCase);

    private static ImageSource? GetOsIcon(string group)
    {
        if (string.IsNullOrWhiteSpace(group)) group = "linux";
        if (OsIconCache.TryGetValue(group, out var cached)) return cached;
        try
        {
            var uri = new Uri($"pack://application:,,,/SNTerm;component/Assets/os_{group}.png", UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            OsIconCache[group] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private async void TerminalView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isReady) return;
        await InitializeWebViewAsync();
    }

    public Task InitializeWebViewAsync()
    {
        if (_initTask != null) return _initTask;
        _initTask = InitializeWebViewInternalAsync();
        return _initTask;
    }

    private async Task InitializeWebViewInternalAsync()
    {
        try
        {
            if (_sharedEnvironment == null)
            {
                await EnvLock.WaitAsync();
                try
                {
                    if (_sharedEnvironment == null)
                    {
                        string webView2Dir = AppPaths.Default.WebView2Dir;
                        _sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, webView2Dir);
                    }
                }
                finally
                {
                    EnvLock.Release();
                }
            }

            await WebView.EnsureCoreWebView2Async(_sharedEnvironment);

            var settings = WebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsStatusBarEnabled = false;
#if !DEBUG
            settings.AreDevToolsEnabled = false;
#endif

            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "snterm.local",
                wwwroot,
                CoreWebView2HostResourceAccessKind.Allow);

            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            WebView.CoreWebView2.Navigate("https://snterm.local/terminal.html");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TerminalView] WebView2 init error: {ex}");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string rawJson = e.WebMessageAsJson;
            var msg = JsonSerializer.Deserialize<WebMessage>(rawJson);
            if (msg == null || string.IsNullOrEmpty(msg.type)) return;

            switch (msg.type)
            {
                case "ready":
                    _isReady = true;
                    WebView.Visibility = Visibility.Visible;
                    LoadingText.Visibility = Visibility.Collapsed;
                    TerminalReady?.Invoke(msg.cols ?? 80, msg.rows ?? 24);
                    lock (_pendingMessages)
                    {
                        foreach (var pending in _pendingMessages)
                        {
                            try
                            {
                                string json = JsonSerializer.Serialize(pending);
                                WebView.CoreWebView2?.PostWebMessageAsJson(json);
                            }
                            catch { }
                        }
                        _pendingMessages.Clear();
                    }
                    break;
                case "input":
                    if (!string.IsNullOrEmpty(msg.data))
                        TerminalInput?.Invoke(msg.data);
                    break;
                case "resize":
                    if (msg.cols.HasValue && msg.rows.HasValue)
                        TerminalResized?.Invoke(msg.cols.Value, msg.rows.Value);
                    break;
                case "copy":
                    if (!string.IsNullOrEmpty(msg.text))
                        CopyRequested?.Invoke(msg.text);
                    break;
                case "requestPaste":
                    PasteRequested?.Invoke();
                    break;
                case "contextMenu":
                    ContextMenuRequested?.Invoke(msg.hasSelection ?? false);
                    break;
                case "hotkey":
                    if (!string.IsNullOrEmpty(msg.name))
                        HotkeyTriggered?.Invoke(msg.name);
                    break;
            }
        }
        catch { }
    }

    public void PostOutput(string b64)
    {
        PostJson(new { type = "output", b64 });
    }

    public void PostPaste(string text)
    {
        PostJson(new { type = "paste", text });
    }

    public void PostStatus(string text)
    {
        PostJson(new { type = "status", text });
    }

    public void PostSettings(AppSettings settings)
    {
        bool isLight = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        try
        {
            WebView.DefaultBackgroundColor = isLight
                ? System.Drawing.Color.FromArgb(255, 255, 255, 255)
                : System.Drawing.Color.FromArgb(255, 30, 30, 30);
        }
        catch { }

        object? themeObj;
        if (isLight)
        {
            themeObj = new
            {
                background = "#ffffff",
                foreground = "#1e1e1e",
                cursor = "#1e1e1e",
                selectionBackground = "#add6ff"
            };
        }
        else
        {
            themeObj = new
            {
                background = "#1e1e1e",
                foreground = "#d4d4d4",
                cursor = "#aeafad",
                selectionBackground = "#264f78"
            };
        }

        PostJson(new
        {
            type = "settings",
            fontFamily = settings.FontFamily,
            fontSize = settings.FontSize,
            theme = themeObj,
            scrollback = settings.Scrollback,
            copyOnSelect = settings.CopyOnSelect,
            rightClickAction = settings.RightClickAction
        });
    }

    public void PostClear() => PostJson(new { type = "clear" });
    public void PostSelectAll() => PostJson(new { type = "selectAll" });
    public void PostFocus()
    {
        WebView.Focus();
        PostJson(new { type = "focus" });
    }

    private void PostJson(object obj)
    {
        if (_isReady && WebView.CoreWebView2 != null)
        {
            try
            {
                string json = JsonSerializer.Serialize(obj);
                WebView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }
        else
        {
            lock (_pendingMessages)
            {
                _pendingMessages.Add(obj);
            }
        }
    }

    private readonly List<double> _cpuHistory = new();
    private readonly List<double> _ramHistory = new();

    public void UpdateMonitor(
        string osGroup,
        string hostname,
        int cpuPercent,
        int ramPercent,
        string ramText,
        string uploadText,
        string downloadText,
        string uptimeText,
        string usernameText,
        string diskText,
        string osPretty,
        string kernel,
        string arch,
        string dfOutput)
    {
        var icon = GetOsIcon(osGroup);
        if (icon != null)
        {
            OsIconImage.Source = icon;
            OsIconImage.Visibility = Visibility.Visible;
            OsIconText.Visibility = Visibility.Collapsed;
        }
        else
        {
            OsIconImage.Visibility = Visibility.Collapsed;
            OsIconText.Text = "🐧";
            OsIconText.Visibility = Visibility.Visible;
        }

        HostnameText.Text = hostname;
        CpuText.Text = $"{cpuPercent}%";
        RamText.Text = ramText;
        UploadText.Text = uploadText;
        DownloadText.Text = downloadText;
        UptimeText.Text = uptimeText;
        UserText.Text = usernameText;
        DiskText.Text = diskText;

        _tipHostname.Text = string.IsNullOrEmpty(hostname) ? "--" : hostname;
        _tipOs.Text = string.IsNullOrEmpty(osPretty) ? (string.IsNullOrEmpty(osGroup) ? "--" : osGroup) : osPretty;
        _tipKernel.Text = string.IsNullOrEmpty(kernel) ? "--" : kernel;
        _tipArch.Text = string.IsNullOrEmpty(arch) ? "--" : arch;
        _tipDiskDf.Text = string.IsNullOrWhiteSpace(dfOutput) ? "Không có dữ liệu df" : dfOutput.TrimEnd();

        _cpuHistory.Add(cpuPercent);
        while (_cpuHistory.Count > 8)
        {
            _cpuHistory.RemoveAt(0);
        }

        var points = new PointCollection();
        double w = 40.0;
        double h = 14.0;
        double step = _cpuHistory.Count > 1 ? w / (_cpuHistory.Count - 1) : w;

        for (int i = 0; i < _cpuHistory.Count; i++)
        {
            double val = Math.Clamp(_cpuHistory[i], 0, 100);
            double x = i * step;
            double y = h - (val / 100.0 * (h - 2.0)) - 1.0;
            points.Add(new Point(x, y));
        }

        CpuPolyline.Points = points;

        // RAM history graph
        _ramHistory.Add(ramPercent);
        while (_ramHistory.Count > 8)
        {
            _ramHistory.RemoveAt(0);
        }

        var ramPoints = new PointCollection();
        double ramStep = _ramHistory.Count > 1 ? w / (_ramHistory.Count - 1) : w;

        for (int i = 0; i < _ramHistory.Count; i++)
        {
            double val = Math.Clamp(_ramHistory[i], 0, 100);
            double x = i * ramStep;
            double y = h - (val / 100.0 * (h - 2.0)) - 1.0;
            ramPoints.Add(new Point(x, y));
        }

        RamPolyline.Points = ramPoints;
        MonitorBar.Visibility = Visibility.Visible;
    }

    public void UpdateMonitor(
        string osGroup,
        string hostname,
        int cpuPercent,
        string ramText,
        string uploadText,
        string downloadText,
        string uptimeText,
        string usernameText,
        string diskText,
        string osPretty,
        string kernel,
        string arch,
        string dfOutput)
    {
        UpdateMonitor(osGroup, hostname, cpuPercent, 0, ramText, uploadText, downloadText, uptimeText, usernameText, diskText, osPretty, kernel, arch, dfOutput);
    }

    public void HideMonitor()
    {
        MonitorBar.Visibility = Visibility.Collapsed;
        _cpuHistory.Clear();
        _ramHistory.Clear();
    }
}