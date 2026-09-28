using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNTerm.Models;
using SNTerm.Services;
using SNTerm.Views;

namespace SNTerm.ViewModels;

public partial class SessionListViewModel : ObservableObject
{
    private readonly SessionStore _store;
    private readonly ICollectionView _sessionsView;

    public static readonly object TrueBox = true;
    public static readonly object FalseBox = false;
    public static readonly IValueConverter NullToVisibilityConverter = new NullToVisibilityConverterImpl();
    public static readonly IValueConverter BoolToVisibilityConverter = new BoolToVisibilityConverterImpl();

    public ObservableCollection<SessionInfo> Sessions { get; } = new();
    public ObservableCollection<SessionInfo> RecentSessions { get; } = new();

    public ICollectionView SessionsView => _sessionsView;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private SessionInfo? selectedSession;

    public event Action<SessionInfo>? RequestConnect;
    public event Action<IEnumerable<SessionInfo>>? RequestConnectMultiple;

    public SessionListViewModel(SessionStore? store = null)
    {
        _store = store ?? new SessionStore();

        _sessionsView = CollectionViewSource.GetDefaultView(Sessions);
        _sessionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SessionInfo.EffectiveGroup)));
        _sessionsView.SortDescriptions.Add(new SortDescription(nameof(SessionInfo.EffectiveGroup), ListSortDirection.Ascending));
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
    }

    partial void OnSearchTextChanged(string value)
    {
        _sessionsView.Refresh();
    }

    private bool FilterSession(object item)
    {
        if (item is not SessionInfo s) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        string query = SearchText.Trim();
        return (s.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               (s.Host?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               (s.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
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
        var editorVm = new SessionEditorViewModel(null, existingGroups);
        var dlg = new SessionEditorDialog(editorVm);
        dlg.Owner = Application.Current?.MainWindow;

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

    [RelayCommand]
    public void EditSession(SessionInfo? session)
    {
        session ??= SelectedSession;
        if (session == null) return;

        var existingGroups = Sessions.Select(s => s.Group).Distinct().ToList();
        var editorVm = new SessionEditorViewModel(session, existingGroups);
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
            _sessionsView.Refresh();

            if (editorVm.ConnectImmediately)
            {
                Connect(editorVm.ResultSession);
            }
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
            ? $"Bạn có chắc chắn muốn xóa VM '{toDelete[0].DisplayName}'?"
            : $"Bạn có chắc chắn muốn xóa {toDelete.Count} VM đã chọn?\n" + string.Join("\n", toDelete.Take(5).Select(s => $"- {s.DisplayName}"));

        var confirm = MessageBox.Show(message, "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            foreach (var item in toDelete)
            {
                Sessions.Remove(item);
            }
            _store.Save(Sessions);
            UpdateRecentSessions();
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

    private class BoolToVisibilityConverterImpl : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
