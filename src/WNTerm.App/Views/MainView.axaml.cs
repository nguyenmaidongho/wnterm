using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.ViewModels;

namespace WNTerm.App.Views;

public partial class MainView : UserControl
{
    private readonly MainViewModel _vm;
    private readonly ListBox _list;
    private readonly Grid _terminalGrid;
    private bool _fixingSelection;

    public MainViewModel ViewModel => _vm;

    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
        _vm = new MainViewModel();
        DataContext = _vm;

        _list = this.FindControl<ListBox>("SessionsListBox")!;
        _terminalGrid = this.FindControl<Grid>("TerminalContainerGrid")!;

        Dialogs.Attach(this.FindControl<Panel>("Overlay")!);
        this.FindControl<TextBlock>("SnippetsToolbarText")!.Text = LocalizationManager.Tr("Snippets", "Lệnh đã lưu");
        this.FindControl<Button>("SnippetsKey")!.Content = LocalizationManager.Tr("Snippets", "Lệnh");
        // WebView là control native nên luôn vẽ đè lên mọi thứ: ẩn vùng terminal khi có hộp thoại.
        Dialogs.OpenChanged += open => Ui.Run(() => _terminalGrid.IsVisible = !open);

        AttachedToVisualTree += (_, _) => Ui.Main = TopLevel.GetTopLevel(this);

        var grid = this.FindControl<Grid>("MainGrid")!;
        double w = _vm.LeftColumnWidth;
        grid.ColumnDefinitions[0].Width = new GridLength(w < 140 || w > 600 ? 400 : w);

        _vm.Tabs.CollectionChanged += OnTabsChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;

        _list.SelectionChanged += OnListSelectionChanged;
        _list.DoubleTapped += OnListDoubleTapped;
        _list.Tapped += OnListTapped;
        _list.KeyDown += OnListKeyDown;
        _list.AddHandler(PointerPressedEvent, OnListPointerPressedTunnel, RoutingStrategies.Tunnel);
        this.FindControl<ContextMenu>("SessionMenu")!.Opening += OnSessionMenuOpening;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        // Kéo-thả file .wnterm / .mxtsessions vào cửa sổ để import.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        SizeChanged += (_, e) => UpdateCompact(e.NewSize.Width);
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()) UpdateCompact(0);

        UpdateSftpHost();
        UpdateTerminalViews();

        // Debug/test: tự mở VM theo tên khi đặt WNTERM_AUTOOPEN.
        var auto = Environment.GetEnvironmentVariable("WNTERM_AUTOOPEN");
        if (!string.IsNullOrEmpty(auto))
        {
            var target = _vm.SessionList.Sessions.FirstOrDefault(x => x.DisplayName == auto);
            if (target != null) Ui.Post(() => _vm.SessionList.Connect(target));
        }

        // Debug/test: tự mở hộp thoại khi đặt WNTERM_AUTODIALOG=settings|add|export|import|cloud|palette.
        var dlgName = Environment.GetEnvironmentVariable("WNTERM_AUTODIALOG");
        if (!string.IsNullOrEmpty(dlgName))
        {
            Ui.Post(() =>
            {
                switch (dlgName)
                {
                    case "settings": _ = _vm.OpenSettings(); break;
                    case "add": _ = _vm.SessionList.AddSession(); break;
                    case "export": _ = _vm.Export(); break;
                    case "cloud": _ = _vm.OpenCloudBackup(); break;
                    case "palette": _ = _vm.OpenPalette(); break;
                    case "snippets": _ = _vm.OpenSnippets(); break;
                }
            });
        }

        Loaded += (_, _) =>
        {
            _vm.RunAutoCloudBackupIfDue();
            if (!string.IsNullOrEmpty(WNTerm.App.App.StartupFile)) _ = _vm.Import(WNTerm.App.App.StartupFile);
            _vm.SessionList.StartStatusMonitor();
            _vm.Update.Start();
        };
    }

    /// <summary>Độ rộng cột trái hiện tại (lưu vào settings khi đóng).</summary>
    public double LeftPaneWidth => this.FindControl<Grid>("MainGrid")!.ColumnDefinitions[0].ActualWidth;

    private const string HomeUrl = "https://wnterm.webnow.vn/";

    /// <summary>Bấm logo → mở trang chủ WNTerm (trình duyệt mặc định; Android/iOS qua Launcher).</summary>
    private async void OnLogoPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher != null) { await launcher.LaunchUriAsync(new Uri(HomeUrl)); return; }
        }
        catch { }
        Ui.OpenWithShell(HomeUrl);
    }

    // ===== Chế độ gọn cho điện thoại =====

    private bool _compact;
    private int _page; // 0 = Sessions, 1 = Terminal, 2 = SFTP (chỉ dùng khi _compact)
    private TerminalTabViewModel? _keyTab;

    private void UpdateCompact(double width)
    {
        bool compact = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || width < 720;
        if (compact == _compact) return;
        _compact = compact;

        this.FindControl<Grid>("RootGrid")!.Classes.Set("compact", compact);
        this.FindControl<Border>("Logo")!.IsVisible = !compact;
        this.FindControl<Border>("LogoCompact")!.IsVisible = compact;
        // Điện thoại: Export/Import chuyển vào Cài đặt cho gọn thanh công cụ (Tìm nhanh vẫn giữ).
        this.FindControl<Button>("ExportBtn")!.IsVisible = !compact;
        this.FindControl<Button>("ImportBtn")!.IsVisible = !compact;
        var grid = this.FindControl<Grid>("MainGrid")!;
        if (compact)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[0].MinWidth = 0;
            grid.ColumnDefinitions[1].Width = new GridLength(0);
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            this.FindControl<Border>("LeftPane")!.MaxWidth = double.PositiveInfinity;
            this.FindControl<GridSplitter>("Splitter")!.IsVisible = false;
            ShowPage(_vm.SelectedTab != null ? 1 : 0);
        }
        else
        {
            double w = _vm.LeftColumnWidth;
            grid.ColumnDefinitions[0].Width = new GridLength(w < 140 || w > 600 ? 400 : w);
            grid.ColumnDefinitions[1].Width = new GridLength(5);
            grid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            this.FindControl<Border>("LeftPane")!.MaxWidth = 600;
            this.FindControl<GridSplitter>("Splitter")!.IsVisible = true;
            this.FindControl<Border>("LeftPane")!.IsVisible = true;
            this.FindControl<Grid>("RightPane")!.IsVisible = true;
            this.FindControl<Border>("NavBar")!.IsVisible = false;
            this.FindControl<Border>("KeyBar")!.IsVisible = false;
        }
    }

    private void ShowPage(int page)
    {
        if (!_compact) return;
        _page = page;
        var cols = this.FindControl<Grid>("MainGrid")!.ColumnDefinitions;
        cols[0].Width = page == 1 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        cols[2].Width = page == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        this.FindControl<Border>("LeftPane")!.IsVisible = page != 1;
        this.FindControl<Grid>("RightPane")!.IsVisible = page == 1;
        this.FindControl<Border>("NavBar")!.IsVisible = true;
        int leftIndex = page == 2 ? 1 : 0;
        if (_vm.SelectedLeftTabIndex != leftIndex) _vm.SelectedLeftTabIndex = leftIndex;

        this.FindControl<Button>("NavSessions")!.Classes.Set("active", page == 0);
        this.FindControl<Button>("NavTerminal")!.Classes.Set("active", page == 1);
        this.FindControl<Button>("NavSftp")!.Classes.Set("active", page == 2);
        UpdateKeyBar();
    }

    private void UpdateKeyBar()
        => this.FindControl<Border>("KeyBar")!.IsVisible = _compact && _page == 1 && _vm.SelectedTab != null;

    private void NavSessions_Click(object? sender, RoutedEventArgs e) => ShowPage(0);
    private void NavTerminal_Click(object? sender, RoutedEventArgs e) => ShowPage(1);
    private void NavSftp_Click(object? sender, RoutedEventArgs e) => ShowPage(2);

    private void SnippetsKey_Click(object? sender, RoutedEventArgs e) => _ = _vm.OpenSnippets();

    private void CtrlKey_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is { } tab)
            tab.CtrlArmed = this.FindControl<ToggleButton>("CtrlKey")!.IsChecked == true;
    }

    private async void Key_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { } tab || (sender as Button)?.Tag is not string key) return;
        if (key == "PASTE")
        {
            var text = await Ui.GetClipboardTextAsync();
            if (!string.IsNullOrEmpty(text)) tab.TerminalControl.PostPaste(text);
        }
        else
        {
            tab.SendSpecialKey(key);
        }
    }

    private void OnKeyTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalTabViewModel.CtrlArmed) && sender is TerminalTabViewModel t)
            Ui.Run(() => this.FindControl<ToggleButton>("CtrlKey")!.IsChecked = t.CtrlArmed);
    }

    // ===== Terminal views =====

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (TerminalTabViewModel tab in e.OldItems)
                _terminalGrid.Children.Remove(tab.TerminalControl);
        UpdateTerminalViews();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            UpdateTerminalViews();
            if (_keyTab != null) _keyTab.PropertyChanged -= OnKeyTabPropertyChanged;
            _keyTab = _vm.SelectedTab;
            if (_keyTab != null) _keyTab.PropertyChanged += OnKeyTabPropertyChanged;
            this.FindControl<ToggleButton>("CtrlKey")!.IsChecked = _keyTab?.CtrlArmed == true;
            if (_compact && _vm.SelectedTab != null) ShowPage(1);
            else UpdateKeyBar();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedLeftTabIndex) && _compact)
        {
            // Điện thoại: trang do người dùng chọn qua thanh điều hướng, không tự nhảy sang SFTP khi kết nối.
            int want = _page == 2 ? 1 : 0;
            if (_vm.SelectedLeftTabIndex != want) _vm.SelectedLeftTabIndex = want;
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentSftp))
            UpdateSftpHost();
    }

    private void UpdateTerminalViews()
    {
        foreach (var tab in _vm.Tabs)
        {
            if (!_terminalGrid.Children.Contains(tab.TerminalControl))
                _terminalGrid.Children.Add(tab.TerminalControl);
            tab.TerminalControl.IsVisible = tab == _vm.SelectedTab;
        }
        this.FindControl<TextBlock>("TerminalPlaceholder")!.IsVisible = _vm.Tabs.Count == 0;
        _vm.SelectedTab?.TerminalControl.PostFocus();
    }

    private void UpdateSftpHost()
    {
        var host = this.FindControl<ContentControl>("SftpHost")!;
        host.Content = _vm.CurrentSftp == null ? null : new SftpPanel { DataContext = _vm.CurrentSftp };
        this.FindControl<TextBlock>("NoVmText")!.IsVisible = _vm.CurrentSftp == null;
    }

    // ===== Dải tab =====

    private static TerminalTabViewModel? TabOf(object? sender) => (sender as Control)?.DataContext as TerminalTabViewModel;

    private void Tab_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var tab = TabOf(sender);
        if (tab == null) return;
        var props = e.GetCurrentPoint(sender as Visual).Properties;
        if (props.IsMiddleButtonPressed)
        {
            _vm.CloseTab(tab);
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            _vm.SelectedTab = tab;
        }
    }

    private void TabClose_Click(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab) _vm.CloseTab(tab);
        e.Handled = true;
    }

    private static TerminalTabViewModel? MenuTab(object? sender)
        => ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget?.DataContext as TerminalTabViewModel
           ?? (sender as Control)?.DataContext as TerminalTabViewModel;

    private void TabMenuDuplicate_Click(object? sender, RoutedEventArgs e) => _vm.DuplicateTab(MenuTab(sender));
    private void TabMenuClose_Click(object? sender, RoutedEventArgs e) => _vm.CloseTab(MenuTab(sender));
    private void TabMenuCloseOthers_Click(object? sender, RoutedEventArgs e) => _vm.CloseOtherTabs(MenuTab(sender));
    private void TabMenuCloseRight_Click(object? sender, RoutedEventArgs e) => _vm.CloseRightTabs(MenuTab(sender));
    private void TabMenuCloseAll_Click(object? sender, RoutedEventArgs e) => _vm.CloseAllTabs();

    // ===== Danh sách VM =====

    private List<SessionInfo> SelectedSessions() => _list.SelectedItems?.OfType<SessionInfo>().ToList() ?? new();

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_fixingSelection) return;
        // Header nhóm không được chọn.
        if (_list.SelectedItems != null && _list.SelectedItems.OfType<GroupHeaderRow>().Any())
        {
            _fixingSelection = true;
            try
            {
                foreach (var h in _list.SelectedItems.OfType<GroupHeaderRow>().ToList())
                    _list.SelectedItems.Remove(h);
            }
            finally { _fixingSelection = false; }
        }
        _vm.SessionList.SelectedSession = SelectedSessions().FirstOrDefault();
    }

    private void GroupHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: GroupHeaderRow header } && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            _vm.SessionList.ToggleGroup(header);
            e.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_compact) return; // điện thoại: kết nối bằng một lần chạm (OnListTapped)
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) != null) return; // nút thao tác nhanh
        if (_list.SelectedItem is SessionInfo session)
            _vm.SessionList.Connect(session);
    }

    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (!_compact) return;
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true);
        if (item?.DataContext is SessionInfo s && (e.Source as Visual)?.FindAncestorOfType<Button>(true) == null)
            _vm.SessionList.Connect(s);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var sel = SelectedSessions();
            if (sel.Count > 1) _vm.SessionList.ConnectMultiple(sel);
            else if (sel.Count == 1) _vm.SessionList.Connect(sel[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            if (_list.SelectedItem is SessionInfo s) _ = _vm.SessionList.EditSession(s);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            var sel = SelectedSessions();
            if (sel.Count > 0) _ = _vm.SessionList.DeleteSession(sel);
            e.Handled = true;
        }
        else if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
        {
            if (_list.SelectedItem is SessionInfo s) _vm.SessionList.DuplicateSession(s);
            e.Handled = true;
        }
    }

    // Chuột phải vào một hàng chưa chọn => chọn hàng đó trước khi mở menu.
    private void OnListPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsRightButtonPressed) return;
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true);
        if (item?.DataContext is SessionInfo && !item.IsSelected)
        {
            _list.SelectedItems?.Clear();
            item.IsSelected = true;
        }
    }

    private void OnSessionMenuOpening(object? sender, CancelEventArgs e)
    {
        // Chỉ hiện menu khi bấm vào một hàng VM (không phải vùng trống / header).
        if (SelectedSessions().Count == 0) e.Cancel = true;
    }

    private void ContextMenuConnect_Click(object? sender, RoutedEventArgs e)
    {
        var sel = SelectedSessions();
        if (sel.Count > 1) _vm.SessionList.ConnectMultiple(sel);
        else if (sel.Count == 1) _vm.SessionList.Connect(sel[0]);
    }

    private void ContextMenuEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is SessionInfo s) _ = _vm.SessionList.EditSession(s);
    }

    private void ContextMenuPin_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var s in SelectedSessions()) _vm.SessionList.TogglePin(s);
    }

    private void ContextMenuExport_Click(object? sender, RoutedEventArgs e)
    {
        var sel = SelectedSessions();
        if (sel.Count > 1) _ = _vm.Export(sel);
        else if (sel.Count == 1) _ = _vm.Export(sel[0]);
    }

    private void ContextMenuDelete_Click(object? sender, RoutedEventArgs e)
    {
        var sel = SelectedSessions();
        if (sel.Count > 0) _ = _vm.SessionList.DeleteSession(sel);
    }

    private static SessionInfo? RowSession(object? sender) => (sender as Control)?.DataContext as SessionInfo;

    private void QuickConnect_Click(object? sender, RoutedEventArgs e)
    {
        if (RowSession(sender) is { } s) _vm.SessionList.Connect(s);
        e.Handled = true;
    }

    private void QuickSftp_Click(object? sender, RoutedEventArgs e)
    {
        if (RowSession(sender) is { } s)
        {
            _vm.SessionList.Connect(s);
            _vm.SelectedLeftTabIndex = 1;
        }
        e.Handled = true;
    }

    private async void QuickCopy_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (RowSession(sender) is not { } s) return;
        var dlg = new CopyHostDialog(s.Host, !string.IsNullOrEmpty(s.EncryptedPassword));
        if (await Dialogs.ShowAsync(dlg) != true) return;

        string text = s.Host;
        if (dlg.IncludeLogin)
        {
            string pass = WNTerm.Services.SecretProtector.Decrypt(s.EncryptedPassword) ?? "";
            text = $"IP: {s.Host}{System.Environment.NewLine}Port: {s.Port}{System.Environment.NewLine}User: {s.Username}{System.Environment.NewLine}Pass: {pass}";
        }
        if (await Ui.SetClipboardTextAsync(text))
            _vm.StatusMessage = dlg.IncludeLogin
                ? WNTerm.Services.LocalizationManager.Tr("Copied login info for " + s.Host, "Đã copy thông tin đăng nhập " + s.Host)
                : string.Format(WNTerm.Services.LocalizationManager.Tr("Copied {0}", "Đã copy {0}"), s.Host);
    }

    private void QuickEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (RowSession(sender) is { } s) _ = _vm.SessionList.EditSession(s);
        e.Handled = true;
    }

    // ===== Phím tắt toàn cục =====

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (Dialogs.IsOpen) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K)
        {
            _ = _vm.OpenPalette();
            e.Handled = true;
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S)
        {
            _ = _vm.OpenSnippets();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Tab)
        {
            _vm.SelectNextTab();
            e.Handled = true;
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Tab)
        {
            _vm.SelectPrevTab();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W && _vm.SelectedTab != null)
        {
            _vm.CloseTab(_vm.SelectedTab);
            e.Handled = true;
        }
    }

    // ===== Kéo thả import =====

    private static bool IsImportFile(string path) =>
        path.EndsWith(".wnterm", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".mxtsessions", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> DroppedFiles(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!)
           ?? Enumerable.Empty<string>();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DroppedFiles(e).Any(IsImportFile) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var file = DroppedFiles(e).FirstOrDefault(IsImportFile);
        if (!string.IsNullOrEmpty(file))
        {
            _ = _vm.Import(file);
            e.Handled = true;
        }
    }
}
