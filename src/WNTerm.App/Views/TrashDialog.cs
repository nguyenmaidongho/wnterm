using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using WNTerm.App.Services;
using WNTerm.Services;

namespace WNTerm.App.Views;

/// <summary>Thùng rác VM: khôi phục hoặc xóa vĩnh viễn các VM đã xóa (giữ <see cref="TrashStore.KeepDays"/> ngày).</summary>
public sealed class TrashDialog : DialogView<bool?>
{
    private readonly SessionStore _store;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple, MaxHeight = 360, MinHeight = 120 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };

    public TrashDialog(SessionStore store)
    {
        _store = store;
        Title = LocalizationManager.Tr("Recycle bin", "Thùng rác");
        DialogWidth = 520;

        _list.ItemTemplate = new FuncDataTemplate<TrashEntry>((e, _) =>
        {
            if (e == null) return new TextBlock();
            return new StackPanel
            {
                Margin = new Avalonia.Thickness(2, 3),
                Children =
                {
                    new TextBlock { Text = e.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = e.Subtitle + "  ·  " + LocalizationManager.Tr("deleted ", "xóa lúc ") + e.DeletedAtText,
                        Classes = { "muted" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
        });

        var restore = new Button { Content = LocalizationManager.Tr("Restore", "Khôi phục"), Classes = { "primary" } };
        restore.Click += (_, _) => Restore();
        var all = new Button { Content = LocalizationManager.Tr("Select all", "Chọn tất cả") };
        all.Click += (_, _) => _list.SelectAll();
        var purge = new Button { Content = LocalizationManager.Tr("Delete forever", "Xóa vĩnh viễn") };
        purge.Click += async (_, _) => await PurgeAsync();
        var close = new Button { Content = LocalizationManager.Tr("Close", "Đóng") };
        close.Click += (_, _) => Close(true);

        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = string.Format(LocalizationManager.Tr(
                        "Deleted VMs (on this device or synced from your other devices) are kept here for {0} days.",
                        "VM đã xóa (ở máy này hoặc đồng bộ từ máy khác của bạn) được giữ ở đây {0} ngày."), TrashStore.KeepDays),
                    TextWrapping = TextWrapping.Wrap, Classes = { "muted" }
                },
                _list,
                _status,
                new WrapPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { WithMargin(all), WithMargin(purge), WithMargin(restore), WithMargin(close) }
                }
            }
        };
        Reload();
    }

    private static Button WithMargin(Button b)
    {
        b.Margin = new Avalonia.Thickness(4, 2, 0, 2);
        return b;
    }

    private void Reload()
    {
        var items = _store.Trash.Items();
        _list.ItemsSource = items;
        _status.Text = items.Count == 0 ? LocalizationManager.Tr("The recycle bin is empty.", "Thùng rác trống.") : "";
    }

    private List<TrashEntry> Selected() => _list.SelectedItems?.OfType<TrashEntry>().ToList() ?? new();

    private void Restore()
    {
        var sel = Selected();
        if (sel.Count == 0) { _status.Text = LocalizationManager.Tr("Select VMs to restore.", "Chọn VM cần khôi phục."); return; }
        int n = _store.RestoreFromTrash(sel.Select(e => e.Id));
        Reload();
        _status.Text = string.Format(LocalizationManager.Tr("Restored {0} VMs.", "Đã khôi phục {0} VM."), n);
    }

    private async Task PurgeAsync()
    {
        var sel = Selected();
        if (sel.Count == 0) { _status.Text = LocalizationManager.Tr("Select VMs to delete.", "Chọn VM cần xóa."); return; }
        if (!await Dialogs.ConfirmAsync(LocalizationManager.Tr("Delete forever", "Xóa vĩnh viễn"),
                string.Format(LocalizationManager.Tr("Permanently delete {0} VMs? This cannot be undone on this device.",
                    "Xóa vĩnh viễn {0} VM? Không hoàn tác được trên máy này."), sel.Count), DialogIcon.Warning)) return;
        _store.Trash.Forget(sel.Select(e => e.Id));
        Reload();
    }
}
