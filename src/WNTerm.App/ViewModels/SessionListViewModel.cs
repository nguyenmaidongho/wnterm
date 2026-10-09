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

/// <summary>Dòng tiêu đề nhóm trong danh sách VM (danh sách phẳng: header + VM, thay cho CollectionView.Group của WPF).</summary>
public sealed partial class GroupHeaderRow : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool isCollapsed;

    [ObservableProperty]
    private string countText = "";

    public GroupHeaderRow(string name) => Name = name;
}

public partial class SessionListViewModel : ObservableObject
{
    private readonly SessionStore _store;
    private readonly Dictionary<string, GroupHeaderRow> _headers = new(StringComparer.OrdinalIgnoreCase);
    private bool _rebuilding;

    public ObservableCollection<SessionInfo> Sessions { get; } = new();
    public ObservableCollection<SessionInfo> RecentSessions { get; } = new();

    /// <summary>Danh sách hiển thị: xen kẽ <see cref="GroupHeaderRow"/> và <see cref="SessionInfo"/> (đã lọc + sắp xếp).</summary>
    public ObservableCollection<object> Rows { get; } = new();

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

    [ObservableProperty]
    private bool hasSessions;

    public bool HasNoActiveTag => _activeTags.Count == 0;

    public event Action<SessionInfo>? RequestConnect;
    public event Action<IEnumerable<SessionInfo>>? RequestConnectMultiple;

    public SessionListViewModel(SessionStore? store = null)
    {
        _store = store ?? new SessionStore();
        LoadSessions();
    }

    partial void OnIsGroupedChanged(bool value) => Refresh();

    partial void OnSelectedSessionChanged(SessionInfo? value)
    {
        TagForGroupAction = value != null && value.Tags.Count > 0 ? value.Tags[0] : null;
        RefreshSummary();
    }

    partial void OnSearchTextChanged(string value)
    {
        Refresh();
    }

    // ===== Lọc / sắp xếp / nhóm =====

    private bool FilterSession(SessionInfo s)
    {
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

    public IReadOnlyList<SessionInfo> VisibleSessions => Rows.OfType<SessionInfo>().ToList();

    /// <summary>Tính lại danh sách hiển thị (thay cho ICollectionView.Refresh).</summary>
    public void Refresh()
    {
        if (_rebuilding) return;
        _rebuilding = true;
        try
        {
            var cmp = StringComparer.CurrentCultureIgnoreCase;
            var visible = Sessions
                .Where(FilterSession)
                .OrderBy(s => s.GroupSortKey, StringComparer.CurrentCulture)
                .ThenBy(s => s.DisplayName, cmp)
                .ToList();

            var desired = new List<object>();
            if (IsGrouped)
            {
                foreach (var g in visible.GroupBy(s => s.GroupKey, StringComparer.CurrentCulture))
                {
                    if (!_headers.TryGetValue(g.Key, out var header))
                        _headers[g.Key] = header = new GroupHeaderRow(g.Key);

                    header.IsCollapsed = CollapsedGroupNames.Contains(g.Key);
                    var list = g.ToList();
                    header.CountText = list.Any(v => v.IsOnline != null)
                        ? $"{list.Count(v => v.IsOnline == true)}/{list.Count} online"
                        : string.Format(LocalizationManager.Tr("{0} VMs", "{0} VM"), list.Count);

                    desired.Add(header);
                    if (!header.IsCollapsed) desired.AddRange(list);
                }
            }
            else
            {
                desired.AddRange(visible);
            }

            SyncRows(desired);
            HasSessions = Sessions.Count > 0;
        }
        finally
        {
            _rebuilding = false;
        }
        RefreshSummary();
    }

    /// <summary>Đồng bộ Rows theo danh sách mong muốn bằng thao tác nhỏ nhất để giữ nguyên lựa chọn/cuộn.</summary>
    private void SyncRows(List<object> desired)
    {
        var keep = new HashSet<object>(desired, ReferenceEqualityComparer.Instance);
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains(Rows[i])) Rows.RemoveAt(i);

        for (int i = 0; i < desired.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], desired[i])) continue;
            int existing = -1;
            for (int j = i + 1; j < Rows.Count; j++)
                if (ReferenceEquals(Rows[j], desired[i])) { existing = j; break; }
            if (existing >= 0) Rows.Move(existing, i);
            else Rows.Insert(i, desired[i]);
        }
    }

    public void ToggleGroup(GroupHeaderRow header)
    {
        if (CollapsedGroupNames.Contains(header.Name)) CollapsedGroupNames.Remove(header.Name);
        else CollapsedGroupNames.Add(header.Name);
        Refresh();
    }

    // ===== Tag chips =====

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
        Refresh();
    }

    private void OnChipActiveChanged(TagChip chip)
    {
        if (chip.IsActive) _activeTags.Add(chip.Name); else _activeTags.Remove(chip.Name);
        OnPropertyChanged(nameof(HasNoActiveTag));
        Refresh();
    }

    [RelayCommand]
    public void ClearTagFilter()
    {
        _activeTags.Clear();
        foreach (var c in TagChips) c.SetActiveSilently(false);
        OnPropertyChanged(nameof(HasNoActiveTag));
        Refresh();
    }

    public void RefreshSummary()
    {
        if (SelectedSession != null)
        {
            SummaryText = LocalizationManager.Tr("Selected: ", "Đã chọn: ") + SelectedSession.DisplayName;
            return;
        }
        var visible = VisibleSessions;
        int online = visible.Count(v => v.IsOnline == true);
        SummaryText = visible.Any(v => v.IsOnline != null)
            ? string.Format(LocalizationManager.Tr("{0} VMs · {1} online", "{0} VM · {1} online"), visible.Count, online)
            : string.Format(LocalizationManager.Tr("{0} VMs", "{0} VM"), visible.Count);
    }

    [RelayCommand]
    public void TogglePin(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;
        session.IsPinned = !session.IsPinned;
        _store.Save(Sessions);
        Refresh();
    }

    [RelayCommand]
    public void ConnectAllWithTag()
    {
        if (string.IsNullOrEmpty(TagForGroupAction)) return;
        var targets = Sessions.Where(s => s.Tags.Contains(TagForGroupAction, StringComparer.OrdinalIgnoreCase)).ToList();
        if (targets.Count > 0) RequestConnectMultiple?.Invoke(targets);
    }

    // ===== Kiểm tra trạng thái online (TCP connect tới cổng SSH) =====
    private DispatcherTimer? _statusTimer;
    private bool _probing;

    public void StartStatusMonitor()
    {
        if (_statusTimer != null) return;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _statusTimer.Tick += async (_, _) => await ProbeAllAsync();
        _statusTimer.Start();
        _ = ProbeAllAsync();
    }

    public async Task ProbeAllAsync()
    {
        if (_probing) return;
        _probing = true;
        try
        {
            var list = Sessions.ToList();
            using var gate = new SemaphoreSlim(8);
            bool changed = false;
            var tasks = list.Select(async s =>
            {
                await gate.WaitAsync();
                try
                {
                    var (ok, ms) = await ProbeAsync(s.Host, s.Port);
                    if (s.IsOnline != ok) changed = true;
                    // Cập nhật thuộc tính (đã bind) trên UI thread.
                    await Ui.RunAsync(() =>
                    {
                        s.PingMs = ok ? ms : null;
                        s.IsOnline = ok;
                        return Task.CompletedTask;
                    });
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
            foreach (var s in list) s.RefreshRelativeTime();
            if (changed) Refresh();
            RefreshSummary();
        }
        catch { }
        finally { _probing = false; }
    }

    private static async Task<(bool, int)> ProbeAsync(string host, int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
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

    // ===== Nạp / lưu =====

    public void LoadSessions()
    {
        Sessions.Clear();
        var list = _store.Load(out _);
        foreach (var s in list) Sessions.Add(s);
        UpdateRecentSessions();
        RefreshTagFilterOptions();
        RefreshTrashCount();
    }

    public void UpdateRecentSessions()
    {
        RecentSessions.Clear();
        var recents = Sessions
            .Where(s => s.LastConnectedAt.HasValue)
            .OrderByDescending(s => s.LastConnectedAt)
            .Take(5);
        foreach (var r in recents) RecentSessions.Add(r);
    }

    // ===== Thao tác =====

    [RelayCommand]
    public async Task AddSession()
    {
        var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
        var editorVm = new SessionEditorViewModel(null, existingGroups, AllTags);
        var dlg = new SessionEditorDialog(editorVm);

        if (await Dialogs.ShowAsync(dlg) == true && editorVm.ResultSession != null)
        {
            Sessions.Add(editorVm.ResultSession);
            _store.Save(Sessions);
            RefreshTagFilterOptions();

            if (editorVm.ConnectImmediately)
                Connect(editorVm.ResultSession);
        }
    }

    /// <summary>Kết nối nhanh: nhập vài thông số, mở tab ngay, KHÔNG thêm vào danh sách VM.</summary>
    [RelayCommand]
    public async Task QuickConnect()
    {
        var editorVm = new SessionEditorViewModel(null, null, null, quick: true);
        var dlg = new SessionEditorDialog(editorVm);
        if (await Dialogs.ShowAsync(dlg) == true && editorVm.ResultSession != null)
            RequestConnect?.Invoke(editorVm.ResultSession);
    }

    [RelayCommand]
    public async Task EditSession(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
        var editorVm = new SessionEditorViewModel(session, existingGroups, AllTags);
        var dlg = new SessionEditorDialog(editorVm);

        if (await Dialogs.ShowAsync(dlg) == true && editorVm.ResultSession != null)
        {
            int index = Sessions.IndexOf(session);
            if (index >= 0) Sessions[index] = editorVm.ResultSession;
            _store.Save(Sessions);
            UpdateRecentSessions();
            RefreshTagFilterOptions();

            if (editorVm.ConnectImmediately)
                Connect(editorVm.ResultSession);
        }
    }

    [RelayCommand]
    public async Task CreateGroup()
    {
        string? name = await Dialogs.PromptAsync(
            LocalizationManager.Tr("Create new group", "Tạo nhóm mới"),
            LocalizationManager.Get("Str_EnterGroupName"));
        string groupName = name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(groupName)) return;

        if (SelectedSession != null)
        {
            SelectedSession.Group = groupName;
            _store.Save(Sessions);
            Refresh();
        }
        else
        {
            var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
            var editorVm = new SessionEditorViewModel(null, existingGroups, AllTags) { Group = groupName };
            var dlg = new SessionEditorDialog(editorVm);

            if (await Dialogs.ShowAsync(dlg) == true && editorVm.ResultSession != null)
            {
                Sessions.Add(editorVm.ResultSession);
                _store.Save(Sessions);
                Refresh();

                if (editorVm.ConnectImmediately)
                    Connect(editorVm.ResultSession);
            }
        }
    }

    [RelayCommand]
    public async Task MoveToGroup(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        string? name = await Dialogs.PromptAsync(
            LocalizationManager.Get("Str_MoveToGroupTitle"),
            LocalizationManager.Get("Str_EnterGroupName"),
            session.Group);
        if (name != null)
        {
            session.Group = name.Trim();
            _store.Save(Sessions);
            Refresh();
        }
    }

    public void MoveSessionsToGroup(IEnumerable<SessionInfo> sessions, string newGroup)
    {
        foreach (var s in sessions) s.Group = newGroup.Trim();
        _store.Save(Sessions);
        Refresh();
    }

    public void RenameGroup(string oldGroupName, string newGroupName)
    {
        if (string.IsNullOrWhiteSpace(newGroupName)) return;
        var targets = Sessions.Where(s => s.EffectiveGroup.Equals(oldGroupName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var s in targets) s.Group = newGroupName.Trim();
        _store.Save(Sessions);
        Refresh();
    }

    public void ConnectAllInGroup(string groupName)
    {
        var targets = Sessions.Where(s => s.EffectiveGroup.Equals(groupName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count > 0) RequestConnectMultiple?.Invoke(targets);
    }

    [RelayCommand]
    public void DuplicateSession(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var clone = session.Clone();
        Sessions.Add(clone);
        _store.Save(Sessions);
        Refresh();
    }

    [RelayCommand]
    public async Task DeleteSession(object? parameter)
    {
        List<SessionInfo> toDelete = new();
        if (parameter is System.Collections.IList list)
            toDelete = list.OfType<SessionInfo>().ToList();
        else if (parameter is SessionInfo single)
            toDelete.Add(single);
        else if (SelectedSession != null)
            toDelete.Add(SelectedSession);

        if (toDelete.Count == 0) return;

        string message = toDelete.Count == 1
            ? LocalizationManager.Get("Str_ConfirmDeletePrompt", toDelete[0].DisplayName)
            : LocalizationManager.Get("Str_ConfirmDeleteMultiplePrompt", toDelete.Count) + "\n" + string.Join("\n", toDelete.Take(5).Select(s => $"- {s.DisplayName}"));
        message += "\n\n" + string.Format(LocalizationManager.Tr("Deleted VMs go to the Recycle bin and can be restored within {0} days.",
            "VM đã xóa nằm trong Thùng rác, khôi phục được trong {0} ngày."), TrashStore.KeepDays);

        if (await Dialogs.ConfirmAsync(LocalizationManager.Get("Str_ConfirmDeleteTitle"), message, DialogIcon.Warning))
        {
            foreach (var item in toDelete) Sessions.Remove(item);
            _store.Save(Sessions); // VM bị bỏ khỏi danh sách được SessionStore chuyển vào thùng rác
            UpdateRecentSessions();
            RefreshTagFilterOptions();
            RefreshTrashCount();
        }
    }

    // ===== Thùng rác =====

    [ObservableProperty]
    private int trashCount;

    public void RefreshTrashCount() => TrashCount = _store.Trash.Count;

    [RelayCommand]
    public async Task OpenTrash()
    {
        await Dialogs.ShowAsync(new TrashDialog(_store));
        RefreshTrashCount();
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

        if (selected.Count == 1) Connect(selected[0]);
        else RequestConnectMultiple?.Invoke(selected);
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
