using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

    public TerminalView()
    {
        InitializeComponent();
        Loaded += TerminalView_Loaded;
    }

    private async void TerminalView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isReady) return;
        await InitializeWebViewAsync();
    }

    public async Task InitializeWebViewAsync()
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
                    LoadingText.Visibility = Visibility.Collapsed;
                    TerminalReady?.Invoke(msg.cols ?? 80, msg.rows ?? 24);
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
        PostJson(new
        {
            type = "settings",
            fontFamily = settings.FontFamily,
            fontSize = settings.FontSize,
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
        if (WebView.CoreWebView2 != null)
        {
            try
            {
                string json = JsonSerializer.Serialize(obj);
                WebView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }
    }
}
