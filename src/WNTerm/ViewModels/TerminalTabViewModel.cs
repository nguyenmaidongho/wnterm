﻿using System;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Renci.SshNet.Common;

using WNTerm.Connections;

using WNTerm.Models;

using WNTerm.Services;

using WNTerm.Views;


namespace WNTerm.ViewModels;


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

    public SftpViewModel Sftp { get; }

    public event Action<TerminalTabViewModel>? ConnectedSuccess;


    private int _cols = 80;

    private int _rows = 24;

    private string? _cachedPassword;

    private string? _cachedPassphrase;
    private readonly System.Text.StringBuilder _recentInput = new();


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
            if (data.Contains("r", StringComparison.OrdinalIgnoreCase) ||
                data.Contains("\r") || data.Contains("\n"))
            {
                _ = ReconnectAsync();
                return;
            }
            return;
        }

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
        StopServerMonitoring();

        Action update = () =>
        {
            Status = ConnectionStatus.Disconnected;
            UpdateStatusBrush();

            bool isVi = string.Equals(LocalizationManager.CurrentLanguage, "vi", StringComparison.OrdinalIgnoreCase);
            string prompt = isVi ? "Bấm [R] để kết nối lại" : "Press [R] to reconnect";

            string detail = !string.IsNullOrEmpty(error) ? $": {error}" : "";
            TerminalControl.PostStatus($"\r\n\x1b[31m[Disconnect{detail}]\x1b[0m\r\n\x1b[33m{prompt}\x1b[0m\r\n");
        };

        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.InvokeAsync(update);
        }
        else
        {
            update();
        }
    }


    public async Task StartConnectionAsync()

    {

        if (Status == ConnectionStatus.Connected || Status == ConnectionStatus.Connecting) return;


        Status = ConnectionStatus.Connecting;

        UpdateStatusBrush();

        TerminalControl.PostStatus(WNTerm.Services.LocalizationManager.Get("Str_ConnectingTo", Session.DisplayName, Session.Host));


        bool authRetry = false;

        while (true)

        {

            // Kiểm tra mật khẩu cần hỏi người d�ng

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

                                    _sessionStore.UpdateSession(Session);

                                }

                                prompted = true;

                            }

                        });

                    }


                    if (!prompted)

                    {

                        Status = ConnectionStatus.Disconnected;

                        UpdateStatusBrush();

                        TerminalControl.PostStatus("\r\n" + WNTerm.Services.LocalizationManager.Get("Str_ConnectionCanceled") + "\r\n");

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

                _ = Sftp.InitializeAsync(_cachedPassword, _cachedPassphrase);

                ConnectedSuccess?.Invoke(this);

                _sessionStore.UpdateSession(Session);

                StartServerMonitoring();

                return;

            }

            catch (SshAuthenticationException)

            {

                if (!authRetry && !string.IsNullOrEmpty(Session.EncryptedPassword))

                {

                    // Stored password rejected

                    authRetry = true;

                    _cachedPassword = null;

                    TerminalControl.PostStatus("\r\n\x1b[31m" + WNTerm.Services.LocalizationManager.Get("Str_WrongSavedPassword") + "\x1b[0m\r\n");

                    continue;

                }


                Status = ConnectionStatus.Disconnected;

                UpdateStatusBrush();

                TerminalControl.PostStatus("\r\n\x1b[31m" + WNTerm.Services.LocalizationManager.Get("Str_InvalidCredentials") + "\x1b[0m\r\n");

                return;

            }

            catch (Exception ex)

            {

                Status = ConnectionStatus.Disconnected;

                UpdateStatusBrush();

                string errorText = ErrorTranslator.Translate(ex, Session.Host, Session.Port);

                bool isVi = string.Equals(LocalizationManager.CurrentLanguage, "vi", StringComparison.OrdinalIgnoreCase); string prompt = isVi ? "Bấm [R] để kết nối lại" : "Press [R] to reconnect"; TerminalControl.PostStatus($"\r\n\x1b[31m[Disconnect: {errorText}]\x1b[0m\r\n\x1b[33m{prompt}\x1b[0m\r\n");

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
        StopServerMonitoring();
        Connection.Dispose();
        Sftp.Dispose();
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

            string cmdText = "export LC_ALL=C; hn=$(uname -n 2>/dev/null || hostname); usr=$(whoami 2>/dev/null || echo root); os=''; [ -f /etc/cloudlinux-release ] && os=cloudlinux; [ -z \"$os\" ] && [ -f /etc/redhat-release ] && os=$(cat /etc/redhat-release 2>/dev/null); [ -z \"$os\" ] && os=$(grep -E '^(ID|ID_LIKE)=' /etc/os-release 2>/dev/null | cut -d= -f2- | tr -d '\"' | tr '\\n' ' '); [ -z \"$os\" ] && os=$(uname -s); up=$(awk '{print int($1)}' /proc/uptime 2>/dev/null || echo 0); mem=$(awk '/MemTotal/{t=$2} /MemAvailable/{a=$2} END{if(t>0) printf \"%d %d\", (t-a)/1024, t/1024}' /proc/meminfo 2>/dev/null); disks=$(df -hP 2>/dev/null | awk 'NR>1 && $1 !~ \"^(tmpfs|devtmpfs|udev|overlay|shm|none|cgroup)\" { if ($6 !~ \"^(/dev|/run|/proc|/sys|/snap|/var/lib/docker|/boot/efi)\") printf \"%s: %s  \", $6, $5 }' | sed 's/  $//'); [ -z \"$disks\" ] && disks=$(df -h / 2>/dev/null | awk 'NR==2{print \"/: \" $5}'); net=$(awk '$1 !~ /lo:|Face/{rx+=$2; tx+=$10} END{printf \"%d %d\", rx, tx}' /proc/net/dev 2>/dev/null); cpustat=$(awk '/^cpu /{print $2+$3+$4, $2+$3+$4+$5}' /proc/stat 2>/dev/null); osp=$(grep -E '^PRETTY_NAME=' /etc/os-release 2>/dev/null | head -1 | cut -d= -f2- | tr -d '\"'); [ -z \"$osp\" ] && [ -f /etc/cloudlinux-release ] && osp=$(cat /etc/cloudlinux-release 2>/dev/null); [ -z \"$osp\" ] && [ -f /etc/redhat-release ] && osp=$(cat /etc/redhat-release 2>/dev/null); [ -z \"$osp\" ] && [ -f /etc/issue ] && osp=$(head -1 /etc/issue 2>/dev/null | sed 's/\\\\.*//'); [ -z \"$osp\" ] && osp=\"$os\"; krn=$(uname -r 2>/dev/null); arch=$(uname -m 2>/dev/null); dfh=$( (df -hP -x tmpfs -x devtmpfs -x overlay -x squashfs 2>/dev/null || df -hP 2>/dev/null || df -h 2>/dev/null) | tr '\\n' '�'); echo \"MON|$hn|$usr|$os|$up|$mem|$disks|$net|$cpustat|$osp|$krn|$arch|$dfh\"";

            while (!token.IsCancellationRequested && Status == ConnectionStatus.Connected)
            {
                try
                {
                    string? raw = await Connection.RunCommandAsync(cmdText, timeoutSeconds: 2);
                    if (!string.IsNullOrEmpty(raw) && raw.Contains("MON|"))
                    {
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

                            if (Application.Current != null)
                            {
                                await Application.Current.Dispatcher.InvokeAsync(() =>
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
                                });
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    await Task.Delay(3000, token);
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
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TerminalControl.HideMonitor();
        });
    }
}