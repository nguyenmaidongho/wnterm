using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.ViewModels;

namespace WNTerm.App.Views;

/// <summary>
/// Lệnh đã lưu: chạm một dòng để gửi vào terminal đang mở. Có hai phạm vi: dùng chung (mọi VM) và riêng của một VM.
/// Không có tab đang kết nối thì vẫn quản lý (thêm/sửa/xóa) được, nút Gửi bị tắt.
/// </summary>
public sealed class SnippetsDialog : DialogView<bool?>
{
    private readonly SnippetStore _store;
    private readonly IReadOnlyList<SessionInfo> _sessions;
    private readonly TerminalTabViewModel? _tab;
    private readonly TextBox _search = new() { PlaceholderText = LocalizationManager.Tr("Search snippets", "Tìm lệnh") };
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly Button _saveLast;

    public SnippetsDialog(SnippetStore store, IReadOnlyList<SessionInfo> sessions, TerminalTabViewModel? tab)
    {
        _store = store;
        _sessions = sessions;
        _tab = tab;
        Title = LocalizationManager.Tr("Snippets", "Lệnh đã lưu");
        DialogWidth = 560;

        var add = new Button { Content = LocalizationManager.Tr("+ New", "+ Thêm"), Classes = { "primary" } };
        add.Click += async (_, _) => await EditAsync(null, null);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _search.Margin = new Thickness(0, 0, 8, 0);
        top.Children.Add(_search);
        Grid.SetColumn(add, 1);
        top.Children.Add(add);
        _search.TextChanged += (_, _) => Reload();

        _saveLast = new Button { Content = LocalizationManager.Tr("Save last command as snippet", "Lưu lệnh vừa gõ thành snippet") };
        _saveLast.Click += async (_, _) => await EditAsync(null, _tab?.InputTracker.LastCommand);
        var close = new Button { Content = LocalizationManager.Tr("Close", "Đóng") };
        close.Click += (_, _) => Close(false);

        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 11 };
        hint.Text = tab == null
            ? LocalizationManager.Tr("Open a connected terminal to send a snippet. You can still add, edit and delete here.",
                "Mở một terminal đang kết nối để gửi lệnh. Ở đây vẫn thêm/sửa/xóa được.")
            : !tab.CanSendSnippet
                ? LocalizationManager.Tr("This terminal is not connected, so Send is disabled.", "Terminal này chưa kết nối nên chưa gửi được.")
                : LocalizationManager.Tr("Tap a snippet to send it to ", "Chạm một lệnh để gửi vào ") + tab.Title;

        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                top,
                hint,
                new ScrollViewer { MaxHeight = 380, MinHeight = 100, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _rows },
                _status,
                new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { Wm(_saveLast), Wm(close) } }
            }
        };
        UpdateSaveLast();
        Reload();
    }

    public override void OnCancelRequested() => Close(false);

    private static Button Wm(Button b) { b.Margin = new Thickness(4, 2, 0, 2); return b; }

    private void UpdateSaveLast()
    {
        string? last = _tab?.InputTracker.LastCommand;
        _saveLast.IsEnabled = !string.IsNullOrWhiteSpace(last);
        ToolTip.SetTip(_saveLast, _saveLast.IsEnabled ? last
            : LocalizationManager.Tr("No reliable last command (history/Tab completion was used, or nothing typed yet).",
                "Chưa có lệnh đáng tin (đã dùng lịch sử/Tab hoặc chưa gõ gì)."));
    }

    private string VmName(string? vmId) =>
        _sessions.FirstOrDefault(s => string.Equals(s.Id.ToString(), vmId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? "";

    private List<Snippet> Visible()
    {
        var all = _store.Load();
        List<Snippet> list;
        if (_tab != null) list = SnippetStore.ForVm(all, _tab.Session.Id.ToString());
        else
        {
            // Không có tab: mục dùng chung + mục của các VM còn tồn tại (VM đã xóa thì ẩn, dữ liệu vẫn giữ).
            var alive = _sessions.Select(s => s.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            list = all.Where(s => s.IsGlobal).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .Concat(all.Where(s => !s.IsGlobal && alive.Contains(s.VmId!)).OrderBy(s => VmName(s.VmId), StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
                .ToList();
        }
        var tokens = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0)
            list = list.Where(s => tokens.All(t =>
                s.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || s.Command.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                VmName(s.VmId).Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        return list;
    }

    private void Reload()
    {
        _rows.Children.Clear();
        var list = Visible();
        foreach (var s in list) _rows.Children.Add(BuildRow(s));
        bool searching = !string.IsNullOrWhiteSpace(_search.Text);
        _status.Text = list.Count > 0 ? "" : searching
            ? LocalizationManager.Tr("No matching snippets.", "Không có lệnh nào khớp.")
            : LocalizationManager.Tr("No snippets yet. Tap \"+ New\" to save a command.", "Chưa có lệnh nào. Bấm \"+ Thêm\" để lưu một lệnh.");
    }

    private static Control Icon(string key, double size = 14)
    {
        var p = new Avalonia.Controls.Shapes.Path { Classes = { "ico" }, Width = size, Height = size };
        if (Application.Current is { } app && app.TryGetResource(key, null, out var g) && g is Geometry geo) p.Data = geo;
        return p;
    }

    private Control BuildRow(Snippet s)
    {
        string scope = s.IsGlobal ? LocalizationManager.Tr("All VMs", "Mọi VM") : VmName(s.VmId);
        string preview = s.Command.Replace("\r\n", "\n").Split('\n')[0];
        if (s.Command.Contains('\n')) preview += " …";

        var title = new TextBlock { Text = string.IsNullOrWhiteSpace(s.Name) ? preview : s.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var cmd = new TextBlock
        {
            Text = preview, FontSize = 11, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = new FontFamily("Consolas, Menlo, monospace")
        };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0),
            Background = Dialogs.ThemeBrush("ThemeButtonHover"),
            Child = new TextBlock { Text = scope, FontSize = 10, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "muted" } }
        };
        var edit = new Button { Classes = { "icon" }, Content = Icon("IconEdit"), Padding = new Thickness(8) };
        ToolTip.SetTip(edit, LocalizationManager.Tr("Edit", "Sửa"));
        edit.Click += async (_, e) => { e.Handled = true; await EditAsync(s, null); };
        var del = new Button { Classes = { "icon" }, Content = Icon("IconTrash"), Padding = new Thickness(8) };
        ToolTip.SetTip(del, LocalizationManager.Tr("Delete", "Xóa"));
        del.Click += async (_, e) => { e.Handled = true; await DeleteAsync(s); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(8, 4, 2, 4) };
        grid.Children.Add(new StackPanel { Children = { title, cmd }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(badge, 1); grid.Children.Add(badge);
        Grid.SetColumn(edit, 2); grid.Children.Add(edit);
        Grid.SetColumn(del, 3); grid.Children.Add(del);

        var row = new Border
        {
            CornerRadius = new CornerRadius(6), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
            BorderThickness = new Thickness(1), BorderBrush = Dialogs.ThemeBrush("ThemeBorderBrush"), Child = grid
        };
        bool canSend = _tab?.CanSendSnippet == true;
        if (canSend)
            row.Tapped += (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) != null) return;
                Send(s);
            };
        else row.Opacity = 0.85;
        return row;
    }

    private void Send(Snippet s)
    {
        if (_tab?.SendSnippet(s) == true) Close(true);
        else _status.Text = LocalizationManager.Tr("The terminal is not connected.", "Terminal chưa kết nối.");
    }

    private async Task DeleteAsync(Snippet s)
    {
        if (!await Dialogs.ConfirmAsync(LocalizationManager.Tr("Delete snippet", "Xóa lệnh"),
                string.Format(LocalizationManager.Tr("Delete \"{0}\"? It will also be removed on your other devices.", "Xóa \"{0}\"? Lệnh cũng sẽ bị xóa trên các thiết bị khác."),
                    string.IsNullOrWhiteSpace(s.Name) ? s.Command : s.Name), DialogIcon.Warning)) return;
        _store.Delete(s.Id);
        Reload();
    }

    private async Task EditAsync(Snippet? existing, string? prefillCommand)
    {
        var dlg = new SnippetEditorDialog(existing, _sessions, _tab?.Session, prefillCommand);
        var result = await Dialogs.ShowAsync(dlg);
        if (result == null) return;
        _store.Upsert(result);
        UpdateSaveLast();
        Reload();
    }
}

/// <summary>Thêm/sửa một snippet: Tên, Lệnh (nhiều dòng), Phạm vi, tự bấm Enter.</summary>
public sealed class SnippetEditorDialog : DialogView<Snippet?>
{
    private sealed record ScopeOption(string Label, string? VmId)
    {
        public override string ToString() => Label;
    }

    private readonly Snippet? _existing;
    private readonly TextBox _name = new() { PlaceholderText = LocalizationManager.Tr("Name (optional)", "Tên (không bắt buộc)") };
    private readonly TextBox _command = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 200,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"), PlaceholderText = LocalizationManager.Tr("Command to send", "Lệnh cần gửi")
    };
    private readonly ComboBox _scope = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _enter = new() { Content = LocalizationManager.Tr("Press Enter automatically", "Tự bấm Enter"), IsChecked = true };
    private readonly TextBlock _error = new() { Foreground = Dialogs.ThemeBrush("ThemeDangerBrush"), TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public SnippetEditorDialog(Snippet? existing, IReadOnlyList<SessionInfo> sessions, SessionInfo? currentVm, string? prefillCommand)
    {
        _existing = existing;
        Title = existing == null ? LocalizationManager.Tr("New snippet", "Lệnh mới") : LocalizationManager.Tr("Edit snippet", "Sửa lệnh");
        DialogWidth = 520;

        var options = new List<ScopeOption> { new(LocalizationManager.Tr("All VMs", "Mọi VM"), null) };
        string only = LocalizationManager.Tr("Only: ", "Chỉ: ");
        if (currentVm != null) options.Add(new(only + currentVm.DisplayName, currentVm.Id.ToString()));
        else foreach (var s in sessions.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)) options.Add(new(only + s.DisplayName, s.Id.ToString()));
        if (existing is { IsGlobal: false } && !options.Any(o => string.Equals(o.VmId, existing.VmId, StringComparison.OrdinalIgnoreCase)))
        {
            var vm = sessions.FirstOrDefault(s => string.Equals(s.Id.ToString(), existing.VmId, StringComparison.OrdinalIgnoreCase));
            options.Add(new(only + (vm?.DisplayName ?? LocalizationManager.Tr("(VM not found)", "(không thấy VM)")), existing.VmId));
        }
        _scope.ItemsSource = options;
        _scope.SelectedItem = options.FirstOrDefault(o => existing != null && string.Equals(o.VmId, existing.VmId, StringComparison.OrdinalIgnoreCase)) ?? options[0];

        if (existing != null)
        {
            _name.Text = existing.Name;
            _command.Text = existing.Command;
            _enter.IsChecked = existing.AutoEnter;
        }
        else if (!string.IsNullOrEmpty(prefillCommand)) _command.Text = prefillCommand;

        var ok = new Button { Content = LocalizationManager.Tr("Save", "Lưu"), Classes = { "primary" }, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = LocalizationManager.Tr("Cancel", "Hủy"), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Label(LocalizationManager.Tr("Name", "Tên")), _name,
                Label(LocalizationManager.Tr("Command", "Lệnh")), _command,
                Label(LocalizationManager.Tr("Scope", "Phạm vi")), _scope,
                _enter,
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } }
            }
        };
    }

    public override void OnCancelRequested() => Close(null);

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "muted" }, FontSize = 12 };

    private void Save()
    {
        string cmd = (_command.Text ?? "").Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t');
        if (string.IsNullOrWhiteSpace(cmd))
        {
            _error.Text = LocalizationManager.Tr("Enter the command to save.", "Hãy nhập lệnh cần lưu.");
            _error.IsVisible = true;
            return;
        }
        string name = (_name.Text ?? "").Trim();
        if (name.Length == 0)
        {
            string first = cmd.Split('\n')[0].Trim();
            name = first.Length > 40 ? first[..40] + "…" : first;
        }
        var sn = _existing?.Clone() ?? new Snippet { Id = Guid.NewGuid().ToString() };
        sn.Name = name;
        sn.Command = cmd;
        sn.VmId = (_scope.SelectedItem as ScopeOption)?.VmId;
        sn.AutoEnter = _enter.IsChecked != false;
        Close(sn);
    }
}
