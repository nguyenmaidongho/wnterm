using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNTerm.Connections;
using SNTerm.Models;
using SNTerm.Services;

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

    [ObservableProperty]
    private string windowTitle = "SN Term";

    [ObservableProperty]
    private string statusMessage = "Sẵn sàng";

    [ObservableProperty]
    private double leftColumnWidth = 280;

    [ObservableProperty]
    private TerminalTabViewModel? selectedTab;

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
            StatusMessage = $"Đang xem tab: {newValue.Title} ({newValue.TooltipText})";
            newValue.TerminalControl.PostFocus();
        }
        else
        {
            WindowTitle = "SN Term";
            StatusMessage = "Sẵn sàng";
        }
    }

    [RelayCommand]
    private void AddVm()
    {
        SessionList.AddSession();
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
                $"Bạn sắp mở đồng thời {list.Count} kết nối. Bạn có muốn tiếp tục?",
                "Xác nhận mở nhiều VM",
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

        return tab;
    }

    [RelayCommand]
    public void CloseTab(TerminalTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        int index = Tabs.IndexOf(tab);
        tab.Dispose();
        Tabs.Remove(tab);

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
            t.Dispose();
            Tabs.Remove(t);
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
            t.Dispose();
            Tabs.Remove(t);
        }
    }

    [RelayCommand]
    public void CloseAllTabs()
    {
        var list = Tabs.ToList();
        foreach (var t in list)
        {
            t.Dispose();
            Tabs.Remove(t);
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
