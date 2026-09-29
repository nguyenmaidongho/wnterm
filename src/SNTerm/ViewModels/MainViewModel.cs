using System.IO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNTerm.Connections;
using SNTerm.Models;
using SNTerm.Services;
using SNTerm.Views;

namespace SNTerm.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly SettingsStore _settingsStore;
    private readonly KnownHostsStore _knownHostsStore;
    private readonly SshConnectionFactory _connectionFactory;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _connectSemaphore;

    public AppSettings Settings => _settings;

    [ObservableProperty]
    private string windowTitle = "SN Term";

    [ObservableProperty]
    private string statusMessage = "Sẵn sàng";

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

    public MainViewModel()
    {
        _paths = AppPaths.Default;
        _sessionStore = new SessionStore(_paths);
        _settingsStore = new SettingsStore(_paths);
        _knownHostsStore = new KnownHostsStore(_paths);
        _connectionFactory = new SshConnectionFactory(_knownHostsStore);

        _settings = _settingsStore.Load();
        LeftColumnWidth = _settings.LeftColumnWidth;
        if (LeftColumnWidth < 140 || LeftColumnWidth > 600) LeftColumnWidth = 400;
        _connectSemaphore = new SemaphoreSlim(_settings.MaxParallelConnects, _settings.MaxParallelConnects);

        SessionList = new SessionListViewModel(_sessionStore);
        SessionList.RequestConnect += OpenSessionTab;
        SessionList.RequestConnectMultiple += OpenMultipleSessions;
    }

    partial void OnSelectedTabChanged(TerminalTabViewModel? oldValue, TerminalTabViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null)
        {
            newValue.IsSelected = true;
            WindowTitle = $"{newValue.Title} — SN Term";
            StatusMessage = $"Đang xem tab: {newValue.Title} ({newValue.Session.Username}@{newValue.Session.Host})";
            CurrentSftp = newValue.Sftp;
            newValue.TerminalControl.PostFocus();
        }
        else
        {
            WindowTitle = "SN Term";
            StatusMessage = "Sẵn sàng";
            CurrentSftp = null;
        }
    }

    [RelayCommand]
    public void ConnectSelectedVm()
    {
        if (SessionList.SelectedSession != null)
        {
            SessionList.Connect(SessionList.SelectedSession);
        }
        else if (SessionList.Sessions.Count > 0)
        {
            SessionList.Connect(SessionList.Sessions[0]);
        }
        else
        {
            AddVm();
        }
    }

    [RelayCommand]
    public void OpenVmList()
    {
        SelectedLeftTabIndex = 0;
    }
    [RelayCommand]
    private void AddVm()
    {
        SessionList.AddSession();
    }

    [RelayCommand]
    public void EditSelectedVm()
    {
        if (SessionList.SelectedSession != null)
        {
            SessionList.EditSession(SessionList.SelectedSession);
        }
        else if (SessionList.Sessions.Count > 0)
        {
            SessionList.EditSession(SessionList.Sessions[0]);
        }
    }

    [RelayCommand]
    public void OpenSettings()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) 
                    ?? Application.Current?.MainWindow;
        var dlg = new SettingsDialog(_settings)
        {
            Owner = owner,
            ShowInTaskbar = false
        };

        dlg.Loaded += (s, e) =>
        {
            dlg.Activate();
        };

        if (dlg.ShowDialog() == true)
        {
            _settingsStore.Save(_settings);
            ThemeManager.ApplyTheme(_settings.Theme);
            LocalizationManager.ApplyLanguage(_settings.Language);

            foreach (var tab in Tabs)
            {
                tab.TerminalControl.PostSettings(_settings);
            }
        }
    }

    public void SaveWindowState(double width, double height, double leftCol)
    {
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.LeftColumnWidth = leftCol;
        _settingsStore.Save(_settings);
    }

    public bool CanCloseWindow()
    {
        int connectedCount = Tabs.Count(t => t.Status == ConnectionStatus.Connected);
        if (connectedCount > 0)
        {
            var res = MessageBox.Show(
                LocalizationManager.Get("Str_ConfirmCloseApp", connectedCount),
                LocalizationManager.Get("Str_ConfirmCloseTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return false;
        }

        CloseAllTabs();
        return true;
    }

    [RelayCommand]
    public void Export(object? parameter = null)
    {
        IEnumerable<SessionInfo>? preSelected = null;
        if (parameter is SessionInfo single)
        {
            preSelected = new[] { single };
        }
        else if (parameter is System.Collections.IList list)
        {
            preSelected = list.OfType<SessionInfo>().ToList();
        }

        var dlg = new ExportDialog(SessionList.Sessions, preSelected)
        {
            Owner = Application.Current?.MainWindow
        };
        dlg.ShowDialog();
    }

    [RelayCommand]
    public void Import(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            string initDir = Directory.GetCurrentDirectory();
            string initFile = "";
            try
            {
                var candidate = Directory.GetFiles(initDir, "*.mxtsessions").FirstOrDefault();
                if (candidate == null)
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (Directory.Exists(desktop))
                    {
                        candidate = Directory.GetFiles(desktop, "*.mxtsessions").FirstOrDefault();
                        if (candidate != null) initDir = desktop;
                    }
                }
                if (candidate != null) initFile = Path.GetFileName(candidate);
            }
            catch { }

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationManager.Get("Str_ImportTitle"),
                Filter = "Tất cả file hỗ trợ (*.snterm;*.mxtsessions;*.ini)|*.snterm;*.mxtsessions;*.ini|SN Term Export (*.snterm)|*.snterm|MobaXterm Sessions (*.mxtsessions;*.ini;*.txt)|*.mxtsessions;*.ini;*.txt|Tất cả file (*.*)|*.*",
                InitialDirectory = initDir,
                FileName = initFile
            };

            if (ofd.ShowDialog(Application.Current?.MainWindow) == true)
            {
                filePath = ofd.FileName;
            }
            else
            {
                return;
            }
        }

        var dlg = new ImportDialog(filePath, _sessionStore)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dlg.ShowDialog() == true)
        {
            SessionList.LoadSessions();
        }
    }

    public void OpenSessionTab(SessionInfo session)
    {
        var tab = CreateTab(session);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    public void OpenMultipleSessions(IEnumerable<SessionInfo> sessions)
    {
        var list = sessions.ToList();
        if (list.Count == 0) return;

        if (list.Count >= 10)
        {
            var res = MessageBox.Show(
                LocalizationManager.Get("Str_ConfirmOpenMany", list.Count),
                LocalizationManager.Get("Str_ConfirmOpenManyTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;
        }

        TerminalTabViewModel? firstTab = null;

        foreach (var session in list)
        {
            var tab = CreateTab(session);
            Tabs.Add(tab);
            firstTab ??= tab;
        }

        if (firstTab != null)
        {
            SelectedTab = firstTab;
        }
    }

    private TerminalTabViewModel CreateTab(SessionInfo session)
    {
        string baseName = session.DisplayName;
        string uniqueName = baseName;
        int count = 2;
        while (Tabs.Any(t => string.Equals(t.Title, uniqueName, StringComparison.OrdinalIgnoreCase)))
        {
            uniqueName = $"{baseName} ({count++})";
        }

        var tab = new TerminalTabViewModel(session, _connectionFactory, _sessionStore, _settings)
        {
            Title = uniqueName
        };

        tab.CloseRequested += CloseTab;
        tab.HotkeyAction += HandleHotkey;
        tab.ConnectedSuccess += (t) =>
        {
            if (SelectedTab == t)
            {
                SelectedLeftTabIndex = 1;
            }
        };
        tab.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TerminalTabViewModel.Status))
            {
                RefreshSessionLiveStatus(tab.Session);
            }
        };

        return tab;
    }

    /// <summary>
    /// Tính lại trạng thái kết nối "sống" của 1 VM dựa trên tất cả tab đang mở
    /// cho VM đó (có thể mở nhiều tab cùng 1 VM qua Duplicate Tab), rồi cập nhật
    /// SessionInfo.LiveStatus để icon trong danh sách VM đổi màu on/off.
    /// </summary>
    private void RefreshSessionLiveStatus(SessionInfo session)
    {
        var relevantTabs = Tabs.Where(t => ReferenceEquals(t.Session, session)).ToList();

        ConnectionStatus? aggregate = null;
        if (relevantTabs.Any(t => t.Status == ConnectionStatus.Connected))
        {
            aggregate = ConnectionStatus.Connected;
        }
        else if (relevantTabs.Any(t => t.Status == ConnectionStatus.Connecting || t.Status == ConnectionStatus.Reconnecting))
        {
            aggregate = ConnectionStatus.Connecting;
        }

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
            if (Tabs.Count > 0)
            {
                int nextIndex = Math.Min(index, Tabs.Count - 1);
                SelectedTab = Tabs[nextIndex];
            }
            else
            {
                SelectedTab = null;
            }
        }
    }

    [RelayCommand]
    public void CloseOtherTabs(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        var others = Tabs.Where(t => t != tab).ToList();
        foreach (var t in others)
        {
            RemoveTab(t);
        }
        SelectedTab = tab;
    }

    [RelayCommand]
    public void CloseRightTabs(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return;

        var toRemove = Tabs.Skip(idx + 1).ToList();
        foreach (var t in toRemove)
        {
            RemoveTab(t);
        }
    }

    [RelayCommand]
    public void CloseAllTabs()
    {
        var list = Tabs.ToList();
        foreach (var t in list)
        {
            RemoveTab(t);
        }
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
            case "NextTab":
                SelectNextTab();
                break;
            case "PrevTab":
                SelectPrevTab();
                break;
            default:
                if (name.StartsWith("Tab") && int.TryParse(name.Substring(3), out int num))
                {
                    int index = num - 1;
                    if (index >= 0 && index < Tabs.Count)
                    {
                        SelectedTab = Tabs[index];
                    }
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