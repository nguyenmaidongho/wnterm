using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WNTerm.Connections;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.Views;

namespace WNTerm.ViewModels;

public partial class SessionListViewModel : ObservableObject
{
    private readonly SessionStore _store;
    private readonly ICollectionView _sessionsView;

    public static readonly object TrueBox = true;
    public static readonly object FalseBox = false;
    public static readonly IValueConverter NullToVisibilityConverter = new NullToVisibilityConverterImpl();
    public static readonly IValueConverter BoolToVisibilityConverter = new BoolToVisibilityConverterImpl();
    public static readonly IValueConverter CountToVisibilityConverter = new CountToVisibilityConverterImpl();
    public static readonly IValueConverter OnlineToBrushConverter = new OnlineToBrushConverterImpl();
    public static readonly IValueConverter GroupHeaderConverter = new GroupHeaderConverterImpl();
    public static readonly IValueConverter LiveStatusToIconBrushConverter = new LiveStatusToIconBrushConverterImpl();

    public ObservableCollection<SessionInfo> Sessions { get; } = new();
    public ObservableCollection<SessionInfo> RecentSessions { get; } = new();

    public ICollectionView SessionsView => _sessionsView;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private SessionInfo? selectedSession;

    public ObservableCollection<TagChip> TagChips { get; } = new();

    public IEnumerable<string> AllTags => Sessions
        .SelectMany(s => s.Tags)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _activeTags = new(StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> CollapsedGroupNames = new();

    [ObservableProperty]
    private bool isGrouped = true;

    [ObservableProperty]
    private string summaryText = "";

    [ObservableProperty]
    private string? tagForGroupAction;

    public bool HasNoActiveTag => _activeTags.Count == 0;

    partial void OnIsGroupedChanged(bool value)
    {
        _sessionsView.GroupDescriptions.Clear();
        if (value) _sessionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SessionInfo.GroupKey)));
        _sessionsView.Refresh();
    }

    partial void OnSelectedSessionChanged(SessionInfo? value)
    {
        TagForGroupAction = value != null && value.Tags.Count > 0 ? value.Tags[0] : null;
        RefreshSummary();
    }

    public void RefreshTagFilterOptions()
    {
        var names = AllTags.ToList();
        _activeTags.RemoveWhere(t => !names.Contains(t, StringComparer.OrdinalIgnoreCase));
        TagChips.Clear();
        foreach (var n in names)
        {
            var chip = new TagChip(n, Sessions.Count(x => x.Tags.Contains(n, StringComparer.OrdinalIgnoreCase)));
            chip.SetActiveSilently(_activeTags.Contains(n));
            chip.ActiveChanged += OnChipActiveChanged;
            TagChips.Add(chip);
        }
        OnPropertyChanged(nameof(HasNoActiveTag));
        RefreshSummary();
    }

    private void OnChipActiveChanged(TagChip chip)
    {
        if (chip.IsActive) _activeTags.Add(chip.Name); else _activeTags.Remove(chip.Name);
        OnPropertyChanged(nameof(HasNoActiveTag));
        _sessionsView.Refresh();
        RefreshSummary();
    }

    [RelayCommand]
    public void ClearTagFilter()
    {
        _activeTags.Clear();
        foreach (var c in TagChips) c.SetActiveSilently(false);
        OnPropertyChanged(nameof(HasNoActiveTag));
        _sessionsView.Refresh();
        RefreshSummary();
    }

    public void RefreshSummary()
    {
        if (SelectedSession != null)
        {
            SummaryText = WNTerm.Services.LocalizationManager.Tr("Selected: ", "Đã chọn: ") + SelectedSession.DisplayName;
            return;
        }
        var visible = _sessionsView.Cast<SessionInfo>().ToList();
        int online = visible.Count(v => v.IsOnline == true);
        SummaryText = visible.Any(v => v.IsOnline != null)
            ? string.Format(WNTerm.Services.LocalizationManager.Tr("{0} VMs · {1} online", "{0} VM · {1} online"), visible.Count, online)
            : string.Format(WNTerm.Services.LocalizationManager.Tr("{0} VMs", "{0} VM"), visible.Count);
    }

    [RelayCommand]
    public void TogglePin(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;
        session.IsPinned = !session.IsPinned;
        _store.Save(Sessions);
        _sessionsView.Refresh();
    }

    [RelayCommand]
    public void ConnectAllWithTag()
    {
        if (string.IsNullOrEmpty(TagForGroupAction)) return;
        var targets = Sessions.Where(s => s.Tags.Contains(TagForGroupAction, StringComparer.OrdinalIgnoreCase)).ToList();
        if (targets.Count > 0) RequestConnectMultiple?.Invoke(targets);
    }

    // ===== Kiểm tra trạng thái online (TCP connect tới cổng SSH) =====
    private System.Windows.Threading.DispatcherTimer? _statusTimer;
    private bool _probing;

    public void StartStatusMonitor()
    {
        if (_statusTimer != null) return;
        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _statusTimer.Tick += async (_, _) => await ProbeAllAsync();
        _statusTimer.Start();
        _ = ProbeAllAsync();
    }

    public async System.Threading.Tasks.Task ProbeAllAsync()
    {
        if (_probing) return;
        _probing = true;
        try
        {
            var list = Sessions.ToList();
            using var gate = new System.Threading.SemaphoreSlim(8);
            bool changed = false;
            var tasks = list.Select(async s =>
            {
                await gate.WaitAsync();
                try
                {
                    var (ok, ms) = await ProbeAsync(s.Host, s.Port);
                    if (s.IsOnline != ok) changed = true;
                    s.PingMs = ok ? ms : null;
                    s.IsOnline = ok;
                }
                finally { gate.Release(); }
            });
            await System.Threading.Tasks.Task.WhenAll(tasks);
            foreach (var s in list) s.RefreshRelativeTime();
            if (changed) _sessionsView.Refresh();
            RefreshSummary();
        }
        catch { }
        finally { _probing = false; }
    }

    private static async System.Threading.Tasks.Task<(bool, int)> ProbeAsync(string host, int port)
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var client = new System.Net.Sockets.TcpClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return (true, (int)Math.Max(1, sw.ElapsedMilliseconds));
        }
        catch
        {
            return (false, 0);
        }
    }

    public event Action<SessionInfo>? RequestConnect;
    public event Action<IEnumerable<SessionInfo>>? RequestConnectMultiple;

    public SessionListViewModel(SessionStore? store = null)
    {
        _store = store ?? new SessionStore();

        _sessionsView = CollectionViewSource.GetDefaultView(Sessions);
        _sessionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SessionInfo.GroupKey)));
        _sessionsView.SortDescriptions.Add(new SortDescription(nameof(SessionInfo.GroupSortKey), ListSortDirection.Ascending));
        _sessionsView.SortDescriptions.Add(new SortDescription(nameof(SessionInfo.DisplayName), ListSortDirection.Ascending));
        _sessionsView.Filter = FilterSession;

        LoadSessions();
    }

    public void LoadSessions()
    {
        Sessions.Clear();
        var list = _store.Load(out bool recovered);
        foreach (var s in list)
        {
            Sessions.Add(s);
        }
        UpdateRecentSessions();
        RefreshTagFilterOptions();
    }

    partial void OnSearchTextChanged(string value)
    {
        _sessionsView.Refresh();
        RefreshSummary();
    }

    private bool FilterSession(object item)
    {
        if (item is not SessionInfo s) return false;
        // Nhiều tag được chọn => hiện VM thuộc BẤT KỲ tag nào (OR).
        if (_activeTags.Count > 0 && !s.Tags.Any(t => _activeTags.Contains(t))) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        string query = SearchText.Trim();
        if (query.StartsWith('#'))
        {
            string tq = query[1..];
            return s.Tags.Any(t => t.Contains(tq, StringComparison.OrdinalIgnoreCase));
        }
        return (s.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               (s.Host?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               (s.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               s.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public void UpdateRecentSessions()
    {
        RecentSessions.Clear();
        var recents = Sessions
            .Where(s => s.LastConnectedAt.HasValue)
            .OrderByDescending(s => s.LastConnectedAt)
            .Take(5);

        foreach (var r in recents)
        {
            RecentSessions.Add(r);
        }
    }

    [RelayCommand]
    public void AddSession()
    {
        var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
        var editorVm = new SessionEditorViewModel(null, existingGroups, AllTags);
        var dlg = new SessionEditorDialog(editorVm);
        dlg.Owner = Application.Current?.MainWindow;

        if (dlg.ShowDialog() == true && editorVm.ResultSession != null)
        {
            Sessions.Add(editorVm.ResultSession);
            _store.Save(Sessions);
            RefreshTagFilterOptions();
            _sessionsView.Refresh();

            if (editorVm.ConnectImmediately)
            {
                Connect(editorVm.ResultSession);
            }
        }
    }

    [RelayCommand]
    public void EditSession(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
        var editorVm = new SessionEditorViewModel(session, existingGroups, AllTags);
        var dlg = new SessionEditorDialog(editorVm);
        dlg.Owner = Application.Current?.MainWindow;

        if (dlg.ShowDialog() == true && editorVm.ResultSession != null)
        {
            int index = Sessions.IndexOf(session);
            if (index >= 0)
            {
                Sessions[index] = editorVm.ResultSession;
            }
            _store.Save(Sessions);
            UpdateRecentSessions();
            RefreshTagFilterOptions();
            _sessionsView.Refresh();

            if (editorVm.ConnectImmediately)
            {
                Connect(editorVm.ResultSession);
            }
        }
    }

        [RelayCommand]
    public void CreateGroup()
    {
        var inputDlg = new InputDialog(WNTerm.Services.LocalizationManager.Tr("Create new group", "Tạo nhóm mới"), WNTerm.Services.LocalizationManager.Get("Str_EnterGroupName"));
        inputDlg.Owner = Application.Current?.MainWindow;

        if (inputDlg.ShowDialog() != true) return;
        string groupName = inputDlg.InputText.Trim();
        if (string.IsNullOrWhiteSpace(groupName)) return;

        if (SelectedSession != null)
        {
            SelectedSession.Group = groupName;
            _store.Save(Sessions);
            _sessionsView.Refresh();
        }
        else
        {
            var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
            var editorVm = new SessionEditorViewModel(null, existingGroups, AllTags)
            {
                Group = groupName
            };
            var dlg = new SessionEditorDialog(editorVm)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true && editorVm.ResultSession != null)
            {
                Sessions.Add(editorVm.ResultSession);
                _store.Save(Sessions);
                _sessionsView.Refresh();

                if (editorVm.ConnectImmediately)
                {
                    Connect(editorVm.ResultSession);
                }
            }
        }
    }

    [RelayCommand]
    public void MoveToGroup(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var dlg = new InputDialog(WNTerm.Services.LocalizationManager.Get("Str_MoveToGroupTitle"), WNTerm.Services.LocalizationManager.Get("Str_EnterGroupName"), session.Group)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() == true && dlg.InputText != null)
        {
            session.Group = dlg.InputText.Trim();
            _store.Save(Sessions);
            _sessionsView.Refresh();
        }
    }

    public void MoveSessionsToGroup(IEnumerable<SessionInfo> sessions, string newGroup)
    {
        foreach (var s in sessions)
        {
            s.Group = newGroup.Trim();
        }
        _store.Save(Sessions);
        _sessionsView.Refresh();
    }

    public void RenameGroup(string oldGroupName, string newGroupName)
    {
        if (string.IsNullOrWhiteSpace(newGroupName)) return;
        var targets = Sessions.Where(s => s.EffectiveGroup.Equals(oldGroupName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var s in targets)
        {
            s.Group = newGroupName.Trim();
        }
        _store.Save(Sessions);
        _sessionsView.Refresh();
    }

    public void ConnectAllInGroup(string groupName)
    {
        var targets = Sessions.Where(s => s.EffectiveGroup.Equals(groupName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count > 0)
        {
            RequestConnectMultiple?.Invoke(targets);
        }
    }

    [RelayCommand]
    public void DuplicateSession(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var clone = session.Clone();
        Sessions.Add(clone);
        _store.Save(Sessions);
        _sessionsView.Refresh();
    }

    [RelayCommand]
    public void DeleteSession(object? parameter)
    {
        List<SessionInfo> toDelete = new();
        if (parameter is System.Collections.IList list)
        {
            toDelete = list.OfType<SessionInfo>().ToList();
        }
        else if (parameter is SessionInfo single)
        {
            toDelete.Add(single);
        }
        else if (SelectedSession != null)
        {
            toDelete.Add(SelectedSession);
        }

        if (toDelete.Count == 0) return;

        string message = toDelete.Count == 1
            ? LocalizationManager.Get("Str_ConfirmDeletePrompt", toDelete[0].DisplayName)
            : LocalizationManager.Get("Str_ConfirmDeleteMultiplePrompt", toDelete.Count) + "\n" + string.Join("\n", toDelete.Take(5).Select(s => $"- {s.DisplayName}"));

        var confirm = MessageBox.Show(message, LocalizationManager.Get("Str_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            foreach (var item in toDelete)
            {
                Sessions.Remove(item);
            }
            _store.Save(Sessions);
            UpdateRecentSessions();
            RefreshTagFilterOptions();
            _sessionsView.Refresh();
        }
    }

    [RelayCommand]
    public void Connect(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        RequestConnect?.Invoke(session);
    }

    [RelayCommand]
    public void ConnectMultiple(System.Collections.IList? list)
    {
        if (list == null) return;
        var selected = list.OfType<SessionInfo>().ToList();
        if (selected.Count == 0) return;

        if (selected.Count == 1)
        {
            Connect(selected[0]);
        }
        else
        {
            RequestConnectMultiple?.Invoke(selected);
        }
    }

    private class NullToVisibilityConverterImpl : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private class CountToVisibilityConverterImpl : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value is int n && n > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private class OnlineToBrushConverterImpl : IValueConverter
    {
        private static readonly SolidColorBrush On = Freeze(0x52, 0xC4, 0x1A);
        private static readonly SolidColorBrush Off = Freeze(0x8C, 0x8C, 0x8C);
        private static readonly SolidColorBrush Unknown = Freeze(0x55, 0x55, 0x55);
        private static SolidColorBrush Freeze(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? (b ? On : Off) : Unknown;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    // Header nhóm: "3/8 online" (hoặc "8 VM" nếu chưa có dữ liệu ping).
    private class GroupHeaderConverterImpl : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not System.Collections.IEnumerable items) return "";
            var list = items.OfType<SessionInfo>().ToList();
            return list.Any(v => v.IsOnline != null)
                ? $"{list.Count(v => v.IsOnline == true)}/{list.Count} online"
                : string.Format(WNTerm.Services.LocalizationManager.Tr("{0} VMs", "{0} VM"), list.Count);
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private class BoolToVisibilityConverterImpl : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    // Tô màu icon VM theo trạng thái kết nối "sống" (SessionInfo.LiveStatus), dùng
    // cùng bảng màu với chấm trạng thái ở tab (TerminalTabViewModel.UpdateStatusBrush)
    // để nhất quán trong toàn bộ ứng dụng: xanh = đang kết nối, vàng = đang kết nối/
    // reconnect, xám = chưa kết nối (off).
    private class LiveStatusToIconBrushConverterImpl : IValueConverter
    {
        private static readonly SolidColorBrush ConnectedBrush = CreateFrozen(0x52, 0xC4, 0x1A);
        private static readonly SolidColorBrush ConnectingBrush = CreateFrozen(0xFA, 0xAD, 0x14);
        private static readonly SolidColorBrush OffBrush = CreateFrozen(0x8C, 0x8C, 0x8C);

        private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value as ConnectionStatus?) switch
            {
                ConnectionStatus.Connected => ConnectedBrush,
                ConnectionStatus.Connecting => ConnectingBrush,
                ConnectionStatus.Reconnecting => ConnectingBrush,
                _ => OffBrush
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

public partial class TagChip : ObservableObject
{
    public string Name { get; }
    public int Count { get; }
    public string Label => $"{Name} {Count}";
    public event Action<TagChip>? ActiveChanged;
    private bool _silent;

    [ObservableProperty]
    private bool isActive;

    public TagChip(string name, int count) { Name = name; Count = count; }

    partial void OnIsActiveChanged(bool value)
    {
        if (!_silent) ActiveChanged?.Invoke(this);
    }

    public void SetActiveSilently(bool value)
    {
        _silent = true;
        IsActive = value;
        _silent = false;
    }
}
