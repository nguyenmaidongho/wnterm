using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly SettingsStore _settingsStore;

    [ObservableProperty]
    private string windowTitle = "SN Term";

    [ObservableProperty]
    private string statusMessage = "Sẵn sàng";

    [ObservableProperty]
    private double leftColumnWidth = 280;

    public SessionListViewModel SessionList { get; }

    public MainViewModel()
    {
        _paths = AppPaths.Default;
        _sessionStore = new SessionStore(_paths);
        _settingsStore = new SettingsStore(_paths);

        var settings = _settingsStore.Load();
        LeftColumnWidth = settings.LeftColumnWidth;

        SessionList = new SessionListViewModel(_sessionStore);
        SessionList.RequestConnect += OnRequestConnect;
        SessionList.RequestConnectMultiple += OnRequestConnectMultiple;
    }

    [RelayCommand]
    private void AddVm()
    {
        SessionList.AddSession();
    }

    private void OnRequestConnect(SessionInfo session)
    {
        StatusMessage = $"Sẵn sàng kết nối tới {session.DisplayName} ({session.Host}:{session.Port}) (Giai đoạn 2)";
    }

    private void OnRequestConnectMultiple(System.Collections.Generic.IEnumerable<SessionInfo> sessions)
    {
        StatusMessage = $"Sẵn sàng mở {System.Linq.Enumerable.Count(sessions)} tab kết nối (Giai đoạn 2)";
    }
}
