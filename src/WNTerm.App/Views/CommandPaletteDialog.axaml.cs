using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

/// <summary>Thanh tìm nhanh (Ctrl+K): gõ vài chữ của tên/IP/tag, Enter để kết nối.</summary>
public partial class CommandPaletteDialog : DialogView<bool?>
{
    private static readonly IBrush OnBrush = new SolidColorBrush(Color.FromRgb(0x52, 0xC4, 0x1A));
    private static readonly IBrush OffBrush = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    public static readonly IValueConverter OnlineToBrush =
        new FuncValueConverter<bool?, IBrush>(v => v is bool b ? (b ? OnBrush : OffBrush) : UnknownBrush);

    private readonly List<SessionInfo> _all;
    private readonly TextBox _queryBox;
    private readonly ListBox _results;

    public SessionInfo? Chosen { get; private set; }
    public bool OpenSftp { get; private set; }
    public Snippet? ChosenSnippet { get; private set; }

    private readonly List<Snippet> _snippets;

    public CommandPaletteDialog() : this(Array.Empty<SessionInfo>(), Array.Empty<Snippet>()) { }

    /// <param name="snippets">Lệnh đã lưu áp dụng cho tab hiện tại (của VM đó trước, rồi dùng chung); gõ ">" để tìm.</param>
    public CommandPaletteDialog(IEnumerable<SessionInfo> sessions, IEnumerable<Snippet>? snippets = null)
    {
        AvaloniaXamlLoader.Load(this);
        Title = "";
        CloseOnBackdrop = true;
        DialogWidth = 560;
        _all = sessions.ToList();
        _snippets = snippets?.ToList() ?? new();
        this.FindControl<TextBlock>("SnippetHint")!.Text = _snippets.Count == 0 ? "" :
            LocalizationManager.Tr("Type > to search and send a saved snippet.", "Gõ > để tìm và gửi lệnh đã lưu.");
        _queryBox = this.FindControl<TextBox>("QueryBox")!;
        _results = this.FindControl<ListBox>("ResultsList")!;

        _queryBox.TextChanged += (_, _) => Refresh();
        _results.DoubleTapped += (_, _) => Accept(false);
        AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);

        Refresh();
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _queryBox.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public override void OnCancelRequested() => Close(false);

    private static bool Match(SessionInfo s, string[] tokens)
    {
        string hay = $"{s.DisplayName} {s.Username}@{s.Host} {string.Join(' ', s.Tags)}";
        foreach (var t in tokens)
        {
            if (t.StartsWith('#'))
            {
                string tag = t[1..];
                if (!s.Tags.Any(x => x.Contains(tag, StringComparison.OrdinalIgnoreCase))) return false;
            }
            else if (hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        return true;
    }

    private void Refresh()
    {
        string raw = _queryBox.Text ?? "";
        if (raw.StartsWith('>'))
        {
            var st = raw[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var snips = _snippets.Where(s => st.All(t => s.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || s.Command.Contains(t, StringComparison.OrdinalIgnoreCase))).Take(8).ToList();
            _results.ItemsSource = snips;
            if (snips.Count > 0) _results.SelectedIndex = 0;
            return;
        }
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = _all.Where(s => Match(s, tokens))
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.LastConnectedAt ?? DateTime.MinValue)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        _results.ItemsSource = list;
        if (list.Count > 0) _results.SelectedIndex = 0;
    }

    private void Accept(bool sftp)
    {
        if (_results.SelectedItem is Snippet sn)
        {
            ChosenSnippet = sn;
            Close(true);
        }
        else if (_results.SelectedItem is SessionInfo s)
        {
            Chosen = s;
            OpenSftp = sftp;
            Close(true);
        }
    }

    private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        int count = (_results.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
        switch (e.Key)
        {
            case Key.Escape:
                Close(false);
                e.Handled = true;
                break;
            case Key.Down:
                if (_results.SelectedIndex < count - 1) _results.SelectedIndex++;
                if (_results.SelectedItem != null) _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (_results.SelectedIndex > 0) _results.SelectedIndex--;
                if (_results.SelectedItem != null) _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept((e.KeyModifiers & KeyModifiers.Shift) != 0);
                e.Handled = true;
                break;
        }
    }
}
