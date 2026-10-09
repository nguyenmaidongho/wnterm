﻿using System;

using System.Threading.Tasks;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Renci.SshNet.Common;

using WNTerm.Connections;

using WNTerm.Models;

using WNTerm.Services;

using WNTerm.App.Services;
using WNTerm.App.Views;


namespace WNTerm.ViewModels;


public partial class TerminalTabViewModel : ObservableObject, IDisposable

{

    private readonly SshConnectionFactory _factory;

    private readonly SessionStore _sessionStore;

    private readonly AppSettings _appSettings;

    private static readonly SemaphoreSlim PasswordPromptGate = new(1, 1);


    public Guid Id { get; } = Guid.NewGuid();


    [ObservableProperty]

    private string title = "";


    [ObservableProperty]

    private string tooltipText = "";


    [ObservableProperty]

    private ConnectionStatus status = ConnectionStatus.Disconnected;


    [ObservableProperty]

    private IBrush statusBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140));


    [ObservableProperty]

    private bool isSelected;


    public SessionInfo Session { get; }

    public SshConnection Connection { get; }

    public TerminalView TerminalControl { get; }

    public SftpViewModel Sftp { get; }

    public event Action<TerminalTabViewModel>? ConnectedSuccess;


    private int _cols = 80;

    private int _rows = 24;

    private string? _cachedPassword;

    private string? _cachedPassphrase;
    private readonly System.Text.StringBuilder _recentInput = new();

    /// <summary>Theo dõi dòng lệnh người dùng gõ (cho "Lưu lệnh vừa gõ").</summary>
    public WNTerm.App.Services.TypedLineTracker InputTracker { get; } = new();

    /// <summary>Có thể gửi lệnh vào terminal này không (đang kết nối).</summary>
    public bool CanSendSnippet => Status == ConnectionStatus.Connected;

    /// <summary>Gửi lệnh của snippet như thể người dùng gõ; thêm Enter khi AutoEnter. Trả false nếu chưa kết nối.</summary>
    public bool SendSnippet(Snippet snippet) => SendCommandText(snippet.Command, snippet.AutoEnter);

    public bool SendCommandText(string command, bool pressEnter)
    {
        if (Status != ConnectionStatus.Connected || string.IsNullOrEmpty(command)) return false;
        var lines = command.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0) OnTerminalInput(lines[i]);
            if (i < lines.Length - 1 || pressEnter) OnTerminalInput("\r");
        }
        return true;
    }


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

        Sftp = new SftpViewModel(session, factory, _appSettings);


        TerminalControl.TerminalReady += OnTerminalReady;

        TerminalControl.TerminalInput += OnTerminalInput;

        TerminalControl.TerminalResized += OnTerminalResized;

        TerminalControl.CopyRequested += OnCopyRequested;

        TerminalControl.PasteRequested += OnPasteRequested;

        TerminalControl.PasteTextRequested += OnPasteTextRequested;

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


    // ===== Phím phụ cho điện thoại =====

    private bool _ctrlArmed;

    /// <summary>Ctrl "dính": ký tự kế tiếp gõ vào sẽ thành Ctrl+ký tự (dùng với hàng phím phụ trên điện thoại).</summary>
    public bool CtrlArmed
    {
        get => _ctrlArmed;
        set
        {
            if (_ctrlArmed == value) return;
            _ctrlArmed = value;
            OnPropertyChanged(nameof(CtrlArmed));
        }
    }

    /// <summary>Gửi một phím đặc biệt: ESC, TAB, UP, DOWN, LEFT, RIGHT, HOME, END, PGUP, PGDN, CTRLC hoặc ký tự thường.</summary>
    public void SendSpecialKey(string key)
    {
        const string esc = "\u001b";
        string seq = key switch
        {
            "ESC" => esc,
            "ENTER" => ((char)13).ToString(),
            "TAB" => "\t",
            "UP" => esc + "[A",
            "DOWN" => esc + "[B",
            "RIGHT" => esc + "[C",
            "LEFT" => esc + "[D",
            "HOME" => esc + "[H",
            "END" => esc + "[F",
            "PGUP" => esc + "[5~",
            "PGDN" => esc + "[6~",
            "CTRLC" => "\u0003",
            _ => key
        };
        OnTerminalInput(seq);
    }

    private void OnTerminalInput(string data)
    {
        if (CtrlArmed && data.Length == 1 && char.IsAsciiLetter(data[0]))
        {
            data = ((char)(char.ToUpperInvariant(data[0]) & 0x1f)).ToString();
            Ui.Post(() => CtrlArmed = false);
        }

        if (Status == ConnectionStatus.Disconnected)
        {
            if (data.Contains("r", StringComparison.OrdinalIgnoreCase) ||
                data.Contains("\r") || data.Contains("\n"))
            {
                _ = ReconnectAsync();
                return;
            }
            return;
        }

        InputTracker.Feed(data);

        if (data.Contains("\r") || data.Contains("\n"))
        {
            string line = _recentInput.ToString().Trim().ToLowerInvariant();
            _recentInput.Clear();
            if (line.Equals("reboot") || line.EndsWith(" reboot") ||
                line.Contains("reboot ") || line.Contains("shutdown -r") ||
                line.Contains("init 6") || line.Contains("systemctl reboot"))
            {
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(800);
                        if (Status != ConnectionStatus.Connected) break;
                        Connection.CheckHealthNow();
                    }
                });
            }
        }
        else if (data.Length < 100)
        {
            if (data == "\b" || data == "\x7f")
            {
                if (_recentInput.Length > 0) _recentInput.Length--;
            }
            else
            {
                _recentInput.Append(data);
                if (_recentInput.Length > 200) _recentInput.Remove(0, 100);
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
        _ = Ui.SetClipboardTextAsync(text);
    }

    private async void OnPasteRequested()
    {
        try
        {
            string? text = await Ui.GetClipboardTextAsync();
            if (!string.IsNullOrEmpty(text) && await ConfirmPasteAsync(text))
            {
                TerminalControl.PostPaste(text);
            }
        }
        catch { }
    }

    /// <summary>Dán từ sự kiện paste gốc của WebView (terminal.js đã chặn lại để hỏi trước).</summary>
    private async void OnPasteTextRequested(string text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text) && await ConfirmPasteAsync(text))
            {
                TerminalControl.PostPaste(text);
            }
        }
        catch { }
    }

    /// <summary>Cài đặt "Xác nhận khi dán nhiều dòng": hỏi trước khi dán văn bản có xuống dòng (có thể chạy lệnh ngay).</summary>
    private async Task<bool> ConfirmPasteAsync(string text)
    {
        if (!_appSettings.ConfirmMultilinePaste) return true;
        if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0) return true;

        var lines = text.Replace("\r\n", "\n").Split('\n', '\r');
        int lineCount = lines.Length;
        string preview = string.Join("\n", lines.Take(5).Select(l => l.Length > 80 ? l[..80] + "…" : l));
        if (lineCount > 5) preview += "\n…";

        return await Dialogs.ConfirmAsync(
            LocalizationManager.Tr("Paste multiple lines?", "Dán nhiều dòng?"),
            string.Format(LocalizationManager.Tr(
                "You are about to paste {0} lines into {1}. Each line may run as a command.\n\n{2}",
                "Bạn sắp dán {0} dòng vào {1}. Mỗi dòng có thể được chạy như một lệnh.\n\n{2}"), lineCount, Session.DisplayName, preview),
            DialogIcon.Warning);
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

        Ui.Post(() => TerminalControl.PostOutput(b64));

    }


    private void OnConnectionStatusChanged(string msg)

    {

        Ui.Post(() =>
        {
            Status = Connection.Status;
            UpdateStatusBrush();
        });

    }


    private void OnConnectionDisconnected(string? error)
    {
        StopServerMonitoring();

        Action update = () =>
        {
            Status = ConnectionStatus.Disconnected;
            UpdateStatusBrush();

            PostDisconnectNotice(error);
        };

        Ui.Post(update);
    }

    private void PostDisconnectNotice(string? error)
    {
        string label = LocalizationManager.Tr("Disconnected", "Mất kết nối");
        string prompt = LocalizationManager.Tr("Press [R] to reconnect", "Bấm [R] để kết nối lại");
        string detail = !string.IsNullOrEmpty(error) ? $": {error}" : "";
        TerminalControl.PostStatus($"\r\n\x1b[31m[{label}{detail}]\x1b[0m\r\n\x1b[33m{prompt}\x1b[0m\r\n");
    }

    private void PostWarning(string text)
    {
        TerminalControl.PostStatus("\r\n\x1b[33m" + text + "\x1b[0m\r\n");
    }

    /// <summary>
    /// Cập nhật bản ghi VM ĐANG LƯU (tìm theo Id) thay vì ghi đè cả đối tượng Session của tab (có thể đã cũ).
    /// VM đã bị xóa thì không thêm lại. Lỗi IO không bao giờ được làm hỏng kết nối đang chạy.
    /// </summary>
    private void PersistToStore(Action<SessionInfo> apply)
    {
        try
        {
            var list = _sessionStore.Load(out _);
            var cur = list.Find(x => x.Id == Session.Id);
            if (cur == null) return;
            apply(cur);
            _sessionStore.Save(list);
        }
        catch { }
    }

    /// <summary>Hỏi mật khẩu (xếp hàng: mỗi lần chỉ hiện 1 hộp thoại). Trả false nếu người dùng hủy.</summary>
    private async Task<bool> PromptPasswordAsync(bool wasRejected)
    {
        await PasswordPromptGate.WaitAsync();
        try
        {
            return await Ui.RunAsync(async () =>
            {
                var dlg = new PasswordPromptDialog(Session.DisplayName, Session.Username, Session.Host, Session.Port, wasRejected);
                if (await Dialogs.ShowAsync(dlg) != true) return false;

                _cachedPassword = dlg.Password;
                if (dlg.SavePassword)
                {
                    string? enc = SecretProtector.Encrypt(_cachedPassword);
                    Session.SavePassword = true;
                    Session.EncryptedPassword = enc;
                    PersistToStore(cur =>
                    {
                        cur.SavePassword = true;
                        cur.EncryptedPassword = enc;
                    });
                }
                return true;
            });
        }
        finally
        {
            PasswordPromptGate.Release();
        }
    }

    /// <summary>Hỏi passphrase của SSH key. Trả false nếu người dùng hủy.</summary>
    private async Task<bool> PromptPassphraseAsync(bool wasRejected)
    {
        await PasswordPromptGate.WaitAsync();
        try
        {
            string label = wasRejected
                ? LocalizationManager.Tr("Wrong passphrase. Enter the passphrase for the SSH key:", "Sai passphrase. Nhập passphrase của SSH key:")
                : LocalizationManager.Tr("Enter the passphrase for the SSH key:", "Nhập passphrase của SSH key:");
            string? input = await Ui.RunAsync(() => Dialogs.PromptAsync(
                $"{Session.DisplayName} — " + LocalizationManager.Tr("Key passphrase", "Passphrase của key"),
                label + "\n" + Session.KeyFilePath, "", isPassword: true));
            if (input == null) return false;
            _cachedPassphrase = input;
            return true;
        }
        finally
        {
            PasswordPromptGate.Release();
        }
    }

    private static bool IsPassphraseError(Exception ex)
        => ex is SshPassPhraseNullOrEmptyException
           || ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase);

    private void SetCanceled()
    {
        Status = ConnectionStatus.Disconnected;
        UpdateStatusBrush();
        TerminalControl.PostStatus("\r\n" + WNTerm.Services.LocalizationManager.Get("Str_ConnectionCanceled") + "\r\n");
    }


    public async Task StartConnectionAsync()
    {
        if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Connecting) return;

        Status = ConnectionStatus.Connecting;
        UpdateStatusBrush();
        TerminalControl.PostStatus(WNTerm.Services.LocalizationManager.Get("Str_ConnectingTo", Session.DisplayName, Session.Host));

        bool authRetry = false;
        int passphraseAttempts = 0;

        while (true)
        {
            bool hasKeyPath = !string.IsNullOrWhiteSpace(Session.KeyFilePath);
            bool keyUsable = hasKeyPath && File.Exists(Session.KeyFilePath);

            // Kiểm tra mật khẩu cần hỏi người dùng
            if (string.IsNullOrEmpty(_cachedPassword))
            {
                if (Session.SavePassword && !string.IsNullOrEmpty(Session.EncryptedPassword) && !authRetry)
                {
                    _cachedPassword = SecretProtector.Decrypt(Session.EncryptedPassword);
                    if (string.IsNullOrEmpty(_cachedPassword))
                    {
                        // Ví dụ: dữ liệu chép từ máy khác (DPAPI/khóa khác) → không giải mã được.
                        PostWarning(LocalizationManager.Tr(
                            "The saved password could not be decrypted on this device.",
                            "Không giải mã được mật khẩu đã lưu trên máy này."));
                    }
                }

                // Không có mật khẩu dùng được và (không có key dùng được, hoặc lần thử trước bị từ chối) → hỏi.
                if (string.IsNullOrEmpty(_cachedPassword) && (!keyUsable || authRetry))
                {
                    if (hasKeyPath && !keyUsable && !authRetry)
                    {
                        PostWarning(string.Format(LocalizationManager.Tr(
                            "Key file not found: {0}. Falling back to password.",
                            "Không tìm thấy file key: {0}. Chuyển sang đăng nhập bằng mật khẩu."), Session.KeyFilePath));
                    }

                    if (!await PromptPasswordAsync(authRetry))
                    {
                        SetCanceled();
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

                var now = DateTime.UtcNow;
                Session.LastConnectedAt = now;

                _ = Sftp.InitializeAsync(_cachedPassword, _cachedPassphrase);

                ConnectedSuccess?.Invoke(this);

                // Chỉ cập nhật LastConnectedAt của bản ghi đang lưu (không ghi đè/không thêm lại VM đã xóa).
                PersistToStore(cur => cur.LastConnectedAt = now);

                StartServerMonitoring();
#if DEBUG
                if (Environment.GetEnvironmentVariable("WNTERM_SFTPTEST") is { Length: > 0 } sftpLog) _ = Task.Run(() => SftpSelfTestAsync(sftpLog));
                // Debug/test: tự gõ lệnh sau khi kết nối (WNTERM_AUTOTYPE; ký tự @ đại diện cho Enter).
                if (Environment.GetEnvironmentVariable("WNTERM_AUTOTYPE") is { Length: > 0 } auto)
                    _ = Task.Run(async () => { await Task.Delay(1500); OnTerminalInput(auto.Replace("@", ((char)13).ToString())); });
#endif
                return;
            }
            catch (SshAuthenticationException)
            {
                // Mật khẩu sai không được giữ lại: lần kết nối lại sau phải hỏi lại.
                _cachedPassword = null;

                if (!authRetry)
                {
                    // Lần đầu bị từ chối (mật khẩu đã lưu sai, hoặc key bị từ chối) → hỏi mật khẩu 1 lần.
                    authRetry = true;
                    string msg = !string.IsNullOrEmpty(Session.EncryptedPassword)
                        ? WNTerm.Services.LocalizationManager.Get("Str_WrongSavedPassword")
                        : WNTerm.Services.LocalizationManager.Get("Str_InvalidCredentials");
                    TerminalControl.PostStatus("\r\n\x1b[31m" + msg + "\x1b[0m\r\n");
                    continue;
                }

                Status = ConnectionStatus.Disconnected;
                UpdateStatusBrush();
                TerminalControl.PostStatus("\r\n\x1b[31m" + WNTerm.Services.LocalizationManager.Get("Str_InvalidCredentials") + "\x1b[0m\r\n");
                return;
            }
            catch (Exception ex) when (keyUsable && passphraseAttempts < 3 && IsPassphraseError(ex))
            {
                // Key có passphrase mà chưa có / sai passphrase → hỏi passphrase.
                bool wrong = ex is not SshPassPhraseNullOrEmptyException || passphraseAttempts > 0;
                passphraseAttempts++;
                _cachedPassphrase = null;
                if (!await PromptPassphraseAsync(wrong))
                {
                    SetCanceled();
                    return;
                }
                continue;
            }
            catch (Exception ex)
            {
                Status = ConnectionStatus.Disconnected;
                UpdateStatusBrush();
                PostDisconnectNotice(ErrorTranslator.Translate(ex, Session.Host, Session.Port));
                return;
            }
        }
    }


    [RelayCommand]

    public async Task ReconnectAsync()

    {

        if (Status == ConnectionStatus.Connecting) return;

        StopServerMonitoring();
        Connection.Disconnect();

        // Ngắt chủ động không phát sự kiện Disconnected → tự đặt lại trạng thái, nếu không StartConnectionAsync bỏ qua.
        Status = ConnectionStatus.Disconnected;
        UpdateStatusBrush();

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
        StopServerMonitoring();
        Connection.Dispose();
        Sftp.Dispose();
        // Android/iOS: đóng kênh HTTP của LocalWebServer gắn với terminal này (tránh rò rỉ).
        Ui.Run(() => TerminalControl.Cleanup());
    }

    public static string DetectOsGroup(string osName, string osPretty = "")
    {
        string allOsText = $"{osName} {osPretty}".ToLowerInvariant();
        if (allOsText.Contains("cloudlinux")
            || allOsText.Contains("cloud")
            || allOsText.Contains("alma")
            || allOsText.Contains("rocky")
            || allOsText.Contains("centos")
            || allOsText.Contains("redhat")
            || allOsText.Contains("rhel")
            || allOsText.Contains("fedora")
            || allOsText.Contains("oracle")
            || allOsText.Contains("amzn"))
        {
            return "redhat";
        }
        if (allOsText.Contains("ubuntu") || allOsText.Contains("mint") || allOsText.Contains("pop")) return "ubuntu";
        if (allOsText.Contains("debian") || allOsText.Contains("kali") || allOsText.Contains("raspbian")) return "debian";
        if (allOsText.Contains("alpine")) return "alpine";
        if (allOsText.Contains("arch") || allOsText.Contains("manjaro") || allOsText.Contains("endeavour")) return "arch";
        if (allOsText.Contains("suse") || allOsText.Contains("opensuse")) return "suse";
        return "linux";
    }

#if DEBUG
    /// <summary>Debug/test: tự kiểm thử upload/download SFTP (WNTERM_SFTPTEST=file log).</summary>
    private async Task SftpSelfTestAsync(string logFile)
    {
        var log = new System.Text.StringBuilder();
        try
        {
            for (int i = 0; i < 100 && string.IsNullOrEmpty(Sftp.CurrentPath); i++) await Task.Delay(100);
            log.AppendLine("path=" + Sftp.CurrentPath + " items=" + Sftp.Items.Count);

            string tmp = Path.Combine(Path.GetTempPath(), "wnterm_sftptest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string name = "wnterm_selftest_" + Guid.NewGuid().ToString("N")[..6] + ".txt";
            string local = Path.Combine(tmp, name);
            string content = "hello sftp " + DateTime.UtcNow.Ticks;
            File.WriteAllText(local, content);

            await Ui.RunAsync(() => Sftp.UploadPathsAsync(new[] { local }));
            await Task.Delay(1500);
            await Ui.RunAsync(() => Sftp.RefreshAsync());
            var item = Sftp.Items.FirstOrDefault(x => x.Name == name);
            log.AppendLine("uploaded_visible=" + (item != null) + " size=" + item?.Size);

            if (item != null)
            {
                string dest = Path.Combine(tmp, "dl");
                Directory.CreateDirectory(dest);
                await Ui.RunAsync(() => Sftp.DownloadItemsAsync(new[] { item }, dest));
                await Task.Delay(1500);
                string got = File.Exists(Path.Combine(dest, name)) ? File.ReadAllText(Path.Combine(dest, name)) : "<missing>";
                log.AppendLine("download_match=" + (got == content));
                await Connection.RunCommandAsync("rm -f '" + item.FullName + "'", 5);
            }
        }
        catch (Exception ex) { log.AppendLine("ERROR " + ex); }
        File.WriteAllText(logFile, log.ToString());
    }
#endif

    private CancellationTokenSource? _monitorCts;

    private void StartServerMonitoring()
    {
        StopServerMonitoring();
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        Task.Run(async () =>
        {
            long lastBusy = 0, lastTotal = 0;
            long lastRx = 0, lastTx = 0;
            DateTime lastTime = DateTime.UtcNow;

            // df có thể treo (NFS/mount chết) → bọc bằng `timeout 2` nếu máy chủ có lệnh timeout.
            string cmdText = "export LC_ALL=C; to=''; command -v timeout >/dev/null 2>&1 && to='timeout 2'; hn=$(uname -n 2>/dev/null || hostname); usr=$(whoami 2>/dev/null || echo root); os=''; [ -f /etc/cloudlinux-release ] && os=cloudlinux; [ -z \"$os\" ] && [ -f /etc/redhat-release ] && os=$(cat /etc/redhat-release 2>/dev/null); [ -z \"$os\" ] && os=$(grep -E '^(ID|ID_LIKE)=' /etc/os-release 2>/dev/null | cut -d= -f2- | tr -d '\"' | tr '\\n' ' '); [ -z \"$os\" ] && os=$(uname -s); up=$(awk '{print int($1)}' /proc/uptime 2>/dev/null || echo 0); mem=$(awk '/MemTotal/{t=$2} /MemAvailable/{a=$2} END{if(t>0) printf \"%d %d\", (t-a)/1024, t/1024}' /proc/meminfo 2>/dev/null); disks=$($to df -hP 2>/dev/null | awk 'NR>1 && $1 !~ \"^(tmpfs|devtmpfs|udev|overlay|shm|none|cgroup)\" { if ($6 !~ \"^(/dev|/run|/proc|/sys|/snap|/var/lib/docker|/boot/efi)\") printf \"%s: %s  \", $6, $5 }' | sed 's/  $//'); [ -z \"$disks\" ] && disks=$($to df -h / 2>/dev/null | awk 'NR==2{print \"/: \" $5}'); net=$(awk '$1 !~ /lo:|Face/{rx+=$2; tx+=$10} END{printf \"%d %d\", rx, tx}' /proc/net/dev 2>/dev/null); cpustat=$(awk '/^cpu /{print $2+$3+$4, $2+$3+$4+$5}' /proc/stat 2>/dev/null); osp=$(grep -E '^PRETTY_NAME=' /etc/os-release 2>/dev/null | head -1 | cut -d= -f2- | tr -d '\"'); [ -z \"$osp\" ] && [ -f /etc/cloudlinux-release ] && osp=$(cat /etc/cloudlinux-release 2>/dev/null); [ -z \"$osp\" ] && [ -f /etc/redhat-release ] && osp=$(cat /etc/redhat-release 2>/dev/null); [ -z \"$osp\" ] && [ -f /etc/issue ] && osp=$(head -1 /etc/issue 2>/dev/null | sed 's/\\\\.*//'); [ -z \"$osp\" ] && osp=\"$os\"; krn=$(uname -r 2>/dev/null); arch=$(uname -m 2>/dev/null); dfh=$( ($to df -hP -x tmpfs -x devtmpfs -x overlay -x squashfs 2>/dev/null || $to df -hP 2>/dev/null || $to df -h 2>/dev/null) | tr '\\n' '�'); echo \"MON|$hn|$usr|$os|$up|$mem|$disks|$net|$cpustat|$osp|$krn|$arch|$dfh\"";

            // Lệnh monitor lỗi/hết giờ liên tiếp → giãn chu kỳ (3s → 6s → 12s ... tối đa 60s), không làm rớt shell.
            int failures = 0;

            while (!token.IsCancellationRequested && Status == ConnectionStatus.Connected)
            {
                bool ok = false;
                try
                {
                    // Tối đa ~5 lần df (mỗi lần bị timeout 2s) → cho lệnh đủ thời gian.
                    string? raw = await Connection.RunCommandAsync(cmdText, timeoutSeconds: 12);
                    if (!string.IsNullOrEmpty(raw) && raw.Contains("MON|"))
                    {
                        ok = true;
                        string line = raw.Split('\n').FirstOrDefault(l => l.StartsWith("MON|")) ?? "";
                        var parts = line.Split('|', 13);
                        if (parts.Length >= 9)
                        {
                            string hostname = parts[1].Trim();
                            string username = parts[2].Trim();
                            string osName = parts[3].Trim();
                            long uptimeSec = long.TryParse(parts[4].Trim(), out var u) ? u : 0;
                            string[] memParts = parts[5].Trim().Split(' ');
                            int usedMb = memParts.Length > 0 && int.TryParse(memParts[0], out var um) ? um : 0;
                            int totalMb = memParts.Length > 1 && int.TryParse(memParts[1], out var tm) ? tm : 0;
                            string diskUsage = parts[6].Trim();
                            string[] netParts = parts[7].Trim().Split(' ');
                            long rxBytes = netParts.Length > 0 && long.TryParse(netParts[0], out var rx) ? rx : 0;
                            long txBytes = netParts.Length > 1 && long.TryParse(netParts[1], out var tx) ? tx : 0;
                            string[] cpuParts = parts[8].Trim().Split(' ');
                            long busyJiffies = cpuParts.Length > 0 && long.TryParse(cpuParts[0], out var bj) ? bj : 0;
                            long totalJiffies = cpuParts.Length > 1 && long.TryParse(cpuParts[1], out var tj) ? tj : 0;

                            string osPretty = parts.Length > 9 ? parts[9].Trim() : "";
                            string kernel = parts.Length > 10 ? parts[10].Trim() : "";
                            string arch = parts.Length > 11 ? parts[11].Trim() : "";
                            string rawDfh = parts.Length > 12 ? parts[12].Trim() : "";
                                                        string dfOutput = "";
                            if (!string.IsNullOrWhiteSpace(rawDfh))
                            {
                                try
                                {
                                    byte[] bytes = Convert.FromBase64String(rawDfh);
                                    dfOutput = System.Text.Encoding.UTF8.GetString(bytes).TrimEnd();
                                }
                                catch
                                {
                                    dfOutput = rawDfh
                                        .Replace("\uFFFD", "\n")
                                        .Replace("¶", "\n")
                                        .Replace("§", "\n")
                                        .Replace("@", "\n")
                                        .Replace("\r", "")
                                        .TrimEnd();
                                }
                            }

                            string osGroup = DetectOsGroup(osName, osPretty);

                            int cpuPercent = 0;
                            if (lastTotal > 0 && totalJiffies > lastTotal)
                            {
                                long dBusy = busyJiffies - lastBusy;
                                long dTotal = totalJiffies - lastTotal;
                                if (dTotal > 0)
                                {
                                    cpuPercent = (int)Math.Clamp((dBusy * 100) / dTotal, 0, 100);
                                }
                            }
                            lastBusy = busyJiffies;
                            lastTotal = totalJiffies;

                            DateTime now = DateTime.UtcNow;
                            double elapsedSec = (now - lastTime).TotalSeconds;
                            lastTime = now;

                            double uploadMbps = 0, downloadMbps = 0;
                            if (lastTx > 0 && lastRx > 0 && elapsedSec > 0.5)
                            {
                                long dTx = Math.Max(0, txBytes - lastTx);
                                long dRx = Math.Max(0, rxBytes - lastRx);
                                uploadMbps = (dTx * 8.0) / (elapsedSec * 1_000_000.0);
                                downloadMbps = (dRx * 8.0) / (elapsedSec * 1_000_000.0);
                            }
                            lastTx = txBytes;
                            lastRx = rxBytes;

                            string ramText = totalMb > 1024
                                ? $"{usedMb / 1024.0:F2} GB / {totalMb / 1024.0:F2} GB"
                                : $"{usedMb} MB / {totalMb} MB";

                            string upSpeedText = uploadMbps < 0.1
                                ? $"{uploadMbps * 1000.0 / 8.0:F1} KB/s"
                                : $"{uploadMbps:F2} Mb/s";
                            string downSpeedText = downloadMbps < 0.1
                                ? $"{downloadMbps * 1000.0 / 8.0:F1} KB/s"
                                : $"{downloadMbps:F2} Mb/s";

                            string uptimeText;
                            if (uptimeSec >= 86400) uptimeText = $"{uptimeSec / 86400} days";
                            else if (uptimeSec >= 3600) uptimeText = $"{uptimeSec / 3600}h {(uptimeSec % 3600) / 60}m";
                            else uptimeText = $"{uptimeSec / 60}m";

                            string diskText = string.IsNullOrWhiteSpace(diskUsage)
                                ? "/: --"
                                : (diskUsage.StartsWith('/') ? diskUsage : $"/: {diskUsage}");

                            {
                                await Ui.RunAsync(() =>
                                {
                                    int ramPercent = totalMb > 0 ? (int)Math.Clamp(Math.Round((double)usedMb / totalMb * 100), 0, 100) : 0;
                                    TerminalControl.UpdateMonitor(
                                        osGroup,
                                        string.IsNullOrEmpty(hostname) ? Session.Host : hostname,
                                        cpuPercent,
                                        ramPercent,
                                        ramText,
                                        upSpeedText,
                                        downSpeedText,
                                        uptimeText,
                                        string.IsNullOrEmpty(username) ? Session.Username : username,
                                        diskText,
                                        osPretty,
                                        kernel,
                                        arch,
                                        dfOutput);
                                    return Task.CompletedTask;
                                });
                            }
                        }
                    }
                }
                catch { ok = false; }

                failures = ok ? 0 : Math.Min(failures + 1, 5);
                int delayMs = failures == 0 ? 3000 : Math.Min(60000, 3000 << failures);

                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopServerMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts = null;
        Ui.Post(() => TerminalControl.HideMonitor());
    }
}