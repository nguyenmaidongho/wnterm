using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WNTerm.App.Services;
using WNTerm.App.Views;
using WNTerm.Connections;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly SnippetStore _snippetStore;
    private readonly SettingsStore _settingsStore;
    private readonly KnownHostsStore _knownHostsStore;
    private readonly SshConnectionFactory _connectionFactory;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _connectSemaphore;

    public AppSettings Settings => _settings;

    [ObservableProperty]
    private string windowTitle = "WN Term";

    [ObservableProperty]
    private string statusMessage = LocalizationManager.Get("Str_Ready");

    [ObservableProperty]
    private double leftColumnWidth = 400;

    [ObservableProperty]
    private TerminalTabViewModel? selectedTab;

    [ObservableProperty]
    private SftpViewModel? currentSftp;

    [ObservableProperty]
    private int selectedLeftTabIndex = 0;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = new();

    public SessionListViewModel SessionList { get; }

    /// <summary>Báo bản mới + cập nhật trong app (thanh trạng thái).</summary>
    public UpdateViewModel Update { get; } = new();

    public MainViewModel()
    {
        _paths = AppPaths.Default;
        _sessionStore = new SessionStore(_paths);
        _snippetStore = new SnippetStore(_paths);
        _settingsStore = new SettingsStore(_paths);
        _knownHostsStore = new KnownHostsStore(_paths);
        _connectionFactory = new SshConnectionFactory(_knownHostsStore);

        _settings = _settingsStore.Load();
        LeftColumnWidth = _settings.LeftColumnWidth;
        if (LeftColumnWidth < 140 || LeftColumnWidth > 600) LeftColumnWidth = 400;
        int parallel = Math.Clamp(_settings.MaxParallelConnects, 1, 16);
        _connectSemaphore = new SemaphoreSlim(parallel, parallel);

        AppPlatform.ApplyTheme(_settings.Theme);
        LocalizationManager.ApplyLanguage(_settings.Language);

        SessionList = new SessionListViewModel(_sessionStore);
        SessionList.RequestConnect += OpenSessionTab;
        SessionList.RequestConnectMultiple += async s => await OpenMultipleSessionsAsync(s);

        // Đồng bộ/khôi phục ghi lại danh sách VM → nạp lại ngay (cùng lượt UI) để không lưu đè danh sách cũ.
        _sessionStore.ReplacedExternally += () => Ui.Run(SessionList.LoadSessions);
        // Người dùng thêm/sửa/xóa VM → đồng bộ sau vài giây (gom nhiều thay đổi liên tiếp).
        _sessionStore.ChangedByUser += () => Ui.Run(() => { _syncSoonAtUtc = DateTime.UtcNow.AddSeconds(5); SessionList.RefreshTrashCount(); });
        // Snippet: cùng cơ chế. Danh sách snippet không được giữ trong bộ nhớ (hộp thoại đọc lại từ đĩa mỗi lần mở) nên
        // ReplacedExternally không cần nạp lại gì; hộp thoại đang mở thì tự đồng bộ nền đã bị hoãn (Dialogs.IsOpen).
        _snippetStore.ChangedByUser += () => Ui.Run(() => { _syncSoonAtUtc = DateTime.UtcNow.AddSeconds(5); });
    }

    partial void OnSelectedTabChanged(TerminalTabViewModel? oldValue, TerminalTabViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null)
        {
            newValue.IsSelected = true;
            WindowTitle = $"{newValue.Title} — WN Term";
            StatusMessage = string.Format(LocalizationManager.Tr("Viewing tab: {0} ({1}@{2})", "Đang xem tab: {0} ({1}@{2})"), newValue.Title, newValue.Session.Username, newValue.Session.Host);
            CurrentSftp = newValue.Sftp;
            newValue.TerminalControl.PostFocus();
        }
        else
        {
            WindowTitle = "WN Term";
            StatusMessage = LocalizationManager.Get("Str_Ready");
            CurrentSftp = null;
        }
    }

    [RelayCommand]
    public async Task ConnectSelectedVm()
    {
        if (SessionList.SelectedSession != null)
            SessionList.Connect(SessionList.SelectedSession);
        else if (SessionList.Sessions.Count > 0)
            SessionList.Connect(SessionList.Sessions[0]);
        else
            await SessionList.AddSession();
    }

    [RelayCommand]
    public void OpenVmList() => SelectedLeftTabIndex = 0;

    [RelayCommand]
    private Task AddVm() => SessionList.AddSession();

    [RelayCommand]
    public async Task EditSelectedVm()
    {
        if (SessionList.SelectedSession != null)
            await SessionList.EditSession(SessionList.SelectedSession);
        else if (SessionList.Sessions.Count > 0)
            await SessionList.EditSession(SessionList.Sessions[0]);
    }

    [RelayCommand]
    public async Task OpenSettings()
    {
        var dlg = new SettingsDialog(_settings)
        {
            ExportVmsAction = () => Export(),
            ImportVmsAction = () => Import()
        };
        if (await Dialogs.ShowAsync(dlg) == true)
        {
            _settingsStore.Save(_settings);
            AppPlatform.ApplyTheme(_settings.Theme);
            LocalizationManager.ApplyLanguage(_settings.Language);

            foreach (var tab in Tabs)
                tab.TerminalControl.PostSettings(_settings);
        }
    }

    [RelayCommand]
    public async Task OpenPalette()
    {
        var tab = SelectedTab;
        var dlg = new CommandPaletteDialog(SessionList.Sessions, SnippetStore.ForVm(_snippetStore.Load(), tab?.Session.Id.ToString()));
        await Dialogs.ShowAsync(dlg);
        if (dlg.Chosen != null)
        {
            SessionList.Connect(dlg.Chosen);
            if (dlg.OpenSftp) SelectedLeftTabIndex = 1;
        }
        else if (dlg.ChosenSnippet != null)
        {
            if (tab == null || !tab.SendSnippet(dlg.ChosenSnippet))
                StatusMessage = LocalizationManager.Tr("Open a connected terminal first to send a snippet.", "Hãy mở một terminal đang kết nối trước khi gửi lệnh.");
            else tab.TerminalControl.PostFocus();
        }
    }

    /// <summary>Hộp thoại Lệnh đã lưu (Ctrl+Shift+S): gửi vào tab đang chọn, hoặc chỉ quản lý khi chưa có tab.</summary>
    [RelayCommand]
    public async Task OpenSnippets()
    {
        if (Dialogs.IsOpen) return;
        var tab = SelectedTab;
        var dlg = new SnippetsDialog(_snippetStore, SessionList.Sessions.ToList(), tab);
        bool sent = await Dialogs.ShowAsync(dlg) == true;
        if (sent) tab?.TerminalControl.PostFocus();
    }

    public void SaveWindowState(double width, double height, double leftCol)
    {
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.LeftColumnWidth = leftCol;
        _settingsStore.Save(_settings);
    }

    /// <summary>Hỏi xác nhận nếu còn tab đang kết nối; true = cho phép đóng cửa sổ (đã đóng hết tab).</summary>
    public async Task<bool> CanCloseWindowAsync()
    {
        int connectedCount = Tabs.Count(t => t.Status == ConnectionStatus.Connected);
        if (connectedCount > 0)
        {
            bool yes = await Dialogs.ConfirmAsync(
                LocalizationManager.Get("Str_ConfirmCloseTitle"),
                LocalizationManager.Get("Str_ConfirmCloseApp", connectedCount));
            if (!yes) return false;
        }

        CloseAllTabs();
        return true;
    }

    [RelayCommand]
    public async Task Export(object? parameter = null)
    {
        IEnumerable<SessionInfo>? preSelected = null;
        if (parameter is SessionInfo single)
            preSelected = new[] { single };
        else if (parameter is System.Collections.IList list)
            preSelected = list.OfType<SessionInfo>().ToList();

        var dlg = new ExportDialog(SessionList.Sessions, preSelected);
        await Dialogs.ShowAsync(dlg);
    }

    [RelayCommand]
    public async Task OpenCloudBackup()
    {
        // Mặc định: backup/đồng bộ lên máy chủ chung bằng tài khoản. S3 riêng là tùy chọn nâng cao mở từ hộp thoại này.
        await Dialogs.ShowAsync(new AccountDialog(_settings, _settingsStore, _sessionStore, _snippetStore));
        SessionList.LoadSessions(); // có thể đã khôi phục/nhập VM (kể cả từ S3)
    }

    // ===== Tự đồng bộ tài khoản + tự backup S3 (nền, im lặng) =====
    // Hai việc độc lập: đồng bộ tài khoản (giữa các máy) chạy khi đã đăng nhập; backup S3 chạy thêm nếu đã cấu hình S3.
    private const int AccountSyncMinutes = 10;
    private DispatcherTimer? _autoBackupTimer;
    private DateTime _lastS3AttemptUtc = DateTime.MinValue;
    private DateTime _lastSyncAttemptUtc = DateTime.MinValue;
    private DateTime? _syncSoonAtUtc;
    private bool _syncRunning, _s3Running;

    /// <summary>Bắt đầu vòng kiểm tra (15 giây/lần) và đồng bộ ngay khi mở app.</summary>
    public void RunAutoCloudBackupIfDue()
    {
        if (_autoBackupTimer == null)
        {
            _autoBackupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _autoBackupTimer.Tick += async (_, _) => await AutoTickAsync();
            _autoBackupTimer.Start();
        }
        _syncSoonAtUtc = DateTime.UtcNow;
        _ = AutoTickAsync();
    }

    private async Task AutoTickAsync()
    {
        await AutoAccountSyncAsync();
        await AutoS3BackupAsync();
    }

    private async Task AutoAccountSyncAsync()
    {
        if (_syncRunning || !_settings.AccountAutoSync || Dialogs.IsOpen) return;
        var account = new AccountService(_settings, _settingsStore, _sessionStore, _paths, _snippetStore);
        if (!account.IsLoggedIn) return;

        var now = DateTime.UtcNow;
        bool due = (_syncSoonAtUtc is DateTime soon && now >= soon) || (now - _lastSyncAttemptUtc).TotalMinutes >= AccountSyncMinutes;
        if (!due) return;

        _syncRunning = true;
        _syncSoonAtUtc = null;
        _lastSyncAttemptUtc = now;
        try
        {
            var r = await account.SyncAsync();
            if (r.Added + r.Updated + r.Deleted > 0)
                StatusMessage = string.Format(LocalizationManager.Tr("Auto backup: received {0} new, {1} updated, {2} moved to recycle bin.", "Tự động backup: nhận {0} mới, {1} cập nhật, {2} vào thùng rác."), r.Added, r.Updated, r.Deleted);
        }
        catch (AccountException ex) when (ex.Status == 401)
        {
            StatusMessage = LocalizationManager.Tr("Backup: session expired — open Cloud to sign in again.", "Backup: phiên đăng nhập hết hạn — mở Cloud để đăng nhập lại.");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.Tr("Auto backup failed: ", "Tự động backup lỗi: ") + ex.Message;
        }
        finally
        {
            _syncRunning = false;
        }
    }

    private async Task AutoS3BackupAsync()
    {
        if (_s3Running || !CloudBackupService.IsConfigured(_settings)) return;
        int minutes = _settings.EffectiveCloudIntervalMinutes;
        if (minutes <= 0) return;

        var reference = _lastS3AttemptUtc;
        if (_settings.LastCloudBackupUtc is DateTime last && last > reference) reference = last;
        if ((DateTime.UtcNow - reference).TotalMinutes < minutes) return;

        _s3Running = true;
        _lastS3AttemptUtc = DateTime.UtcNow;
        try
        {
            // Chỉ upload khi dữ liệu thay đổi, tránh sinh ra hàng loạt bản trùng nhau.
            var key = await new CloudBackupService(_settings, _sessionStore).BackupAsync(onlyIfChanged: _settings.CloudBackupOnlyIfChanged);
            if (key != null)
            {
                _settingsStore.Save(_settings);
                StatusMessage = LocalizationManager.Tr("Automatic S3 backup done.", "Đã tự động backup lên S3.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.Tr("S3 auto backup failed: ", "Auto backup S3 lỗi: ") + ex.Message;
        }
        finally
        {
            _s3Running = false;
        }
    }

    [RelayCommand]
    public async Task Import(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            filePath = await Ui.PickOpenFileAsync(
                LocalizationManager.Get("Str_ImportTitle"),
                LocalizationManager.Tr("All supported files", "Tất cả file hỗ trợ"),
                "*.wnterm", "*.mxtsessions", "*.ini", "*.txt");
            if (string.IsNullOrEmpty(filePath)) return;
        }

        var dlg = new ImportDialog(filePath, _sessionStore);
        if (await Dialogs.ShowAsync(dlg) == true)
            SessionList.LoadSessions();
    }

    // ===== Tab =====

    public void OpenSessionTab(SessionInfo session)
    {
        var tab = CreateTab(session);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    public async Task OpenMultipleSessionsAsync(IEnumerable<SessionInfo> sessions)
    {
        var list = sessions.ToList();
        if (list.Count == 0) return;

        if (list.Count >= 10)
        {
            bool yes = await Dialogs.ConfirmAsync(
                LocalizationManager.Get("Str_ConfirmOpenManyTitle"),
                LocalizationManager.Get("Str_ConfirmOpenMany", list.Count));
            if (!yes) return;
        }

        TerminalTabViewModel? firstTab = null;
        foreach (var session in list)
        {
            var tab = CreateTab(session);
            Tabs.Add(tab);
            firstTab ??= tab;
        }

        if (firstTab != null) SelectedTab = firstTab;
    }

    private TerminalTabViewModel CreateTab(SessionInfo session)
    {
        string baseName = session.DisplayName;
        string uniqueName = baseName;
        int count = 2;
        while (Tabs.Any(t => string.Equals(t.Title, uniqueName, StringComparison.OrdinalIgnoreCase)))
            uniqueName = $"{baseName} ({count++})";

        var tab = new TerminalTabViewModel(session, _connectionFactory, _sessionStore, _settings)
        {
            Title = uniqueName
        };

        tab.CloseRequested += CloseTab;
        tab.HotkeyAction += HandleHotkey;
        tab.ConnectedSuccess += t =>
        {
            if (SelectedTab == t) SelectedLeftTabIndex = 1;
        };
        tab.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TerminalTabViewModel.Status))
                RefreshSessionLiveStatus(tab.Session);
        };

        return tab;
    }

    /// <summary>Tính lại trạng thái "sống" của 1 VM dựa trên mọi tab đang mở cho VM đó (icon on/off trong danh sách).</summary>
    private void RefreshSessionLiveStatus(SessionInfo session)
    {
        var relevantTabs = Tabs.Where(t => ReferenceEquals(t.Session, session)).ToList();

        ConnectionStatus? aggregate = null;
        if (relevantTabs.Any(t => t.Status == ConnectionStatus.Connected))
            aggregate = ConnectionStatus.Connected;
        else if (relevantTabs.Any(t => t.Status == ConnectionStatus.Connecting || t.Status == ConnectionStatus.Reconnecting))
            aggregate = ConnectionStatus.Connecting;

        session.LiveStatus = aggregate;
    }

    private void RemoveTab(TerminalTabViewModel tab)
    {
        tab.Dispose();
        Tabs.Remove(tab);
        RefreshSessionLiveStatus(tab.Session);
    }

    [RelayCommand]
    public void CloseTab(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        int index = Tabs.IndexOf(tab);
        RemoveTab(tab);

        if (SelectedTab == tab)
        {
            SelectedTab = Tabs.Count > 0 ? Tabs[Math.Min(index, Tabs.Count - 1)] : null;
        }
    }

    [RelayCommand]
    public void CloseOtherTabs(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        foreach (var t in Tabs.Where(t => t != tab).ToList()) RemoveTab(t);
        SelectedTab = tab;
    }

    [RelayCommand]
    public void CloseRightTabs(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return;

        foreach (var t in Tabs.Skip(idx + 1).ToList()) RemoveTab(t);
    }

    [RelayCommand]
    public void CloseAllTabs()
    {
        foreach (var t in Tabs.ToList()) RemoveTab(t);
        SelectedTab = null;
    }

    [RelayCommand]
    public void DuplicateTab(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;
        OpenSessionTab(tab.Session);
    }

    private void HandleHotkey(string name)
    {
        switch (name)
        {
            case "CloseTab":
                if (SelectedTab != null) CloseTab(SelectedTab);
                break;
            case "Snippets":
                _ = OpenSnippets();
                break;
            case "NextTab":
                SelectNextTab();
                break;
            case "PrevTab":
                SelectPrevTab();
                break;
            default:
                if (name.StartsWith("Tab") && int.TryParse(name.AsSpan(3), out int num))
                {
                    int index = num - 1;
                    if (index >= 0 && index < Tabs.Count) SelectedTab = Tabs[index];
                }
                break;
        }
    }

    public void SelectNextTab()
    {
        if (Tabs.Count <= 1 || SelectedTab == null) return;
        int idx = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(idx + 1) % Tabs.Count];
    }

    public void SelectPrevTab()
    {
        if (Tabs.Count <= 1 || SelectedTab == null) return;
        int idx = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(idx - 1 + Tabs.Count) % Tabs.Count];
    }
}
