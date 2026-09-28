using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet.Common;
using SNTerm.Connections;
using SNTerm.Models;
using SNTerm.Services;
using SNTerm.Views;

namespace SNTerm.ViewModels;

public partial class TerminalTabViewModel : ObservableObject, IDisposable
{
    private readonly SshConnectionFactory _factory;
    private readonly SessionStore _sessionStore;
    private readonly AppSettings _appSettings;
    private static readonly object PasswordPromptLock = new();

    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string tooltipText = "";

    [ObservableProperty]
    private ConnectionStatus status = ConnectionStatus.Disconnected;

    [ObservableProperty]
    private Brush statusBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140));

    [ObservableProperty]
    private bool isSelected;

    public SessionInfo Session { get; }
    public SshConnection Connection { get; }
    public TerminalView TerminalControl { get; }

    private int _cols = 80;
    private int _rows = 24;
    private string? _cachedPassword;
    private string? _cachedPassphrase;

    public event Action<TerminalTabViewModel>? CloseRequested;
    public event Action<string>? HotkeyAction;

    public TerminalTabViewModel(
        SessionInfo session,
        SshConnectionFactory factory,
        SessionStore sessionStore,
        AppSettings appSettings)
    {
        Session = session;
        _factory = factory;
        _sessionStore = sessionStore;
        _appSettings = appSettings;

        Title = session.DisplayName;
        TooltipText = session.Subtitle;
        if (!string.IsNullOrEmpty(session.EncryptedPassphrase)) _cachedPassphrase = SecretProtector.Decrypt(session.EncryptedPassphrase);

        Connection = new SshConnection();
        TerminalControl = new TerminalView();

        TerminalControl.TerminalReady += OnTerminalReady;
        TerminalControl.TerminalInput += OnTerminalInput;
        TerminalControl.TerminalResized += OnTerminalResized;
        TerminalControl.CopyRequested += OnCopyRequested;
        TerminalControl.PasteRequested += OnPasteRequested;
        TerminalControl.ContextMenuRequested += OnContextMenuRequested;
        TerminalControl.HotkeyTriggered += OnHotkeyTriggered;

        Connection.OutputBase64Received += OnConnectionOutput;
        Connection.StatusChanged += OnConnectionStatusChanged;
        Connection.Disconnected += OnConnectionDisconnected;

        UpdateStatusBrush();
    }

    private void OnTerminalReady(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
        // Terminal ready
        TerminalControl.PostSettings(_appSettings);

        _ = StartConnectionAsync();
    }

    private void OnTerminalInput(string data)
    {
        if (Status == ConnectionStatus.Disconnected)
        {
            if (data.Contains("\r") || data.Contains("\n"))
            {
                _ = ReconnectAsync();
                return;
            }
        }

        Connection.SendInput(data);
    }

    private void OnTerminalResized(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
        Connection.Resize(cols, rows);
    }

    private void OnCopyRequested(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch { }
    }

    private void OnPasteRequested()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                TerminalControl.PostPaste(text);
            }
        }
        catch { }
    }

    private void OnContextMenuRequested(bool hasSelection)
    {
        // Handled by context menu in later phase or default
    }

    private void OnHotkeyTriggered(string name)
    {
        HotkeyAction?.Invoke(name);
    }

    private void OnConnectionOutput(string b64)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TerminalControl.PostOutput(b64);
        });
    }

    private void OnConnectionStatusChanged(string msg)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Status = Connection.Status;
            UpdateStatusBrush();
        });
    }

    private void OnConnectionDisconnected(string? error)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Status = ConnectionStatus.Disconnected;
            UpdateStatusBrush();

            string disconnectMsg = !string.IsNullOrEmpty(error)
                ? $"\r\n\x1b[31m[Lỗi]: {error}\x1b[0m"
                : "\r\n\x1b[33m[Đã ngắt kết nối]\x1b[0m";

            TerminalControl.PostStatus($"{disconnectMsg}\r\nNhấn Enter để kết nối lại...\r\n");
        });
    }

    public async Task StartConnectionAsync()
    {
        if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Connecting) return;

        Status = ConnectionStatus.Connecting;
        UpdateStatusBrush();
        TerminalControl.PostStatus($"Đang kết nối tới {Session.DisplayName} ({Session.Host}:{Session.Port})...");

        bool authRetry = false;
        while (true)
        {
            // Kiểm tra mật khẩu cần hỏi người dùng
            if (string.IsNullOrEmpty(_cachedPassword))
            {
                if (Session.SavePassword && !string.IsNullOrEmpty(Session.EncryptedPassword) && !authRetry)
                {
                    _cachedPassword = SecretProtector.Decrypt(Session.EncryptedPassword);
                }
                else if (string.IsNullOrEmpty(Session.KeyFilePath))
                {
                    // Prompt password
                    bool prompted = false;
                    lock (PasswordPromptLock)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var dlg = new PasswordPromptDialog(Session.DisplayName, Session.Username, Session.Host, Session.Port, authRetry)
                            {
                                Owner = Application.Current.MainWindow
                            };
                            if (dlg.ShowDialog() == true)
                            {
                                _cachedPassword = dlg.Password;
                                if (dlg.SavePassword)
                                {
                                    Session.SavePassword = true;
                                    Session.EncryptedPassword = SecretProtector.Encrypt(_cachedPassword);
                                    _sessionStore.Save(new[] { Session });
                                }
                                prompted = true;
                            }
                        });
                    }

                    if (!prompted)
                    {
                        Status = ConnectionStatus.Disconnected;
                        UpdateStatusBrush();
                        TerminalControl.PostStatus("\r\nĐã hủy kết nối.\r\nNhấn Enter để kết nối lại...\r\n");
                        return;
                    }
                }
            }

            try
            {
                await Connection.ConnectAsync(
                    Session,
                    _factory,
                    _cachedPassword,
                    _cachedPassphrase,
                    _cols,
                    _rows,
                    _appSettings.KeepAliveSeconds);

                Status = ConnectionStatus.Connected;
                UpdateStatusBrush();

                Session.LastConnectedAt = DateTime.UtcNow;
                _sessionStore.Save(new[] { Session });
                return;
            }
            catch (SshAuthenticationException)
            {
                if (!authRetry && !string.IsNullOrEmpty(Session.EncryptedPassword))
                {
                    // Stored password rejected
                    authRetry = true;
                    _cachedPassword = null;
                    TerminalControl.PostStatus("\r\n\x1b[31mMật khẩu đã lưu không chính xác.\x1b[0m\r\n");
                    continue;
                }

                Status = ConnectionStatus.Disconnected;
                UpdateStatusBrush();
                TerminalControl.PostStatus("\r\n\x1b[31mSai thông tin đăng nhập.\x1b[0m\r\nNhấn Enter để thử lại...\r\n");
                return;
            }
            catch (Exception ex)
            {
                Status = ConnectionStatus.Disconnected;
                UpdateStatusBrush();
                string errorText = ErrorTranslator.Translate(ex, Session.Host, Session.Port);
                TerminalControl.PostStatus($"\r\n\x1b[31m[Lỗi kết nối]: {errorText}\x1b[0m\r\nNhấn Enter để kết nối lại...\r\n");
                return;
            }
        }
    }

    [RelayCommand]
    public async Task ReconnectAsync()
    {
        Connection.Disconnect();
        await StartConnectionAsync();
    }

    [RelayCommand]
    public void Close()
    {
        CloseRequested?.Invoke(this);
    }

    private void UpdateStatusBrush()
    {
        StatusBrush = Status switch
        {
            ConnectionStatus.Connected => new SolidColorBrush(Color.FromRgb(82, 196, 26)),    // #52C41A
            ConnectionStatus.Connecting => new SolidColorBrush(Color.FromRgb(250, 173, 20)),  // #FAAD14
            ConnectionStatus.Reconnecting => new SolidColorBrush(Color.FromRgb(250, 173, 20)),// #FAAD14
            _ => new SolidColorBrush(Color.FromRgb(140, 140, 140))                            // #8C8C8C
        };
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}



