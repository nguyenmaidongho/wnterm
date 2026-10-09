using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using WNTerm.Services;

namespace WNTerm.App.Services;

/// <summary>
/// Lớp cơ sở cho mọi hộp thoại. Hiển thị bằng <c>await Dialogs.ShowAsync(dlg)</c>; trong hộp thoại gọi
/// <see cref="Close"/> để đóng và trả kết quả. (WPF: DialogResult true/false → ở đây TResult, thường bool?.)
/// </summary>
public abstract class DialogView<TResult> : UserControl
{
    private TaskCompletionSource<TResult?>? _tcs;

    /// <summary>Tiêu đề hiển thị ở đầu hộp thoại.</summary>
    public string Title { get; set; } = "";

    /// <summary>Độ rộng mong muốn (bị giới hạn theo màn hình, tự co trên điện thoại).</summary>
    public double DialogWidth { get; set; } = 480;

    /// <summary>Gọi khi hộp thoại được bấm Esc / click nền. Mặc định đóng với kết quả default.</summary>
    public virtual void OnCancelRequested() => Close(default);

    /// <summary>Chạm nền mờ có đóng hộp thoại không. Mặc định không — tránh mất dữ liệu đang nhập vì chạm nhầm.</summary>
    public bool CloseOnBackdrop { get; set; }

    internal Task<TResult?> Begin()
    {
        _tcs = new TaskCompletionSource<TResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _tcs.Task;
    }

    public void Close(TResult? result)
    {
        Dialogs.Remove(this);
        _tcs?.TrySetResult(result);
    }
}

public enum DialogIcon { Info, Warning, Error, Question }

/// <summary>Điểm vào duy nhất để hiện hộp thoại. Gắn với MainView qua <see cref="Attach"/>.</summary>
public static class Dialogs
{
    private static Panel? _overlay;
    private static readonly List<(Control view, Border layer, Action onCancel)> Stack = new();
    private static TopLevel? _backTopLevel;

    /// <summary>Phát khi có/không có hộp thoại đang mở (để ẩn WebView native đè lên overlay).</summary>
    public static event Action<bool>? OpenChanged;

    public static bool IsOpen => Stack.Count > 0;

    public static void Attach(Panel overlay)
    {
        _overlay = overlay;
        // Nút Back của Android (BackRequested của TopLevel): đóng hộp thoại trên cùng.
        overlay.AttachedToVisualTree += (_, _) => HookBack(TopLevel.GetTopLevel(overlay));
        overlay.DetachedFromVisualTree += (_, _) => HookBack(null);
        if (TopLevel.GetTopLevel(overlay) is { } top) HookBack(top);
    }

    private static void HookBack(TopLevel? top)
    {
        if (ReferenceEquals(_backTopLevel, top)) return;
        if (_backTopLevel != null) _backTopLevel.BackRequested -= OnBackRequested;
        _backTopLevel = top;
        if (top != null) top.BackRequested += OnBackRequested;
    }

    private static void OnBackRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CancelTop()) e.Handled = true;
    }

    /// <summary>Hủy (như bấm Esc) hộp thoại trên cùng. Trả false nếu không có hộp thoại nào.</summary>
    public static bool CancelTop()
    {
        if (Stack.Count == 0) return false;
        Stack[^1].onCancel();
        return true;
    }

    /// <summary>Lấy brush theo theme hiện tại (null nếu chưa có).</summary>
    public static IBrush? ThemeBrush(string key)
    {
        var app = Application.Current;
        return app != null && app.TryGetResource(key, app.ActualThemeVariant, out var v) ? v as IBrush : null;
    }

    public static Task<TResult?> ShowAsync<TResult>(DialogView<TResult> view)
    {
        var task = view.Begin();
        Ui.Run(() => Present(view, view.Title, view.DialogWidth, view.OnCancelRequested, view.CloseOnBackdrop));
        return task;
    }

    private static void Present(Control view, string title, double width, Action onCancel, bool closeOnBackdrop = false)
    {
        if (_overlay == null) throw new InvalidOperationException("Dialogs.Attach chưa được gọi.");

        var content = new StackPanel { Spacing = 14 };
        {
            // Tiêu đề + nút đóng ✕ (bấm = hủy, giống Esc) để hộp thoại nào cũng thoát được —
            // LUÔN có nút đóng, kể cả hộp thoại không có tiêu đề (vd. Ctrl+K trên điện thoại không có Esc).
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            if (!string.IsNullOrEmpty(title))
                header.Children.Add(new TextBlock { Text = title, Classes = { "h2" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

            var closeIcon = new Avalonia.Controls.Shapes.Path
            {
                Width = 10,
                Height = 10,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.6,
                StrokeLineCap = PenLineCap.Round
            };
            var app = Application.Current;
            if (app != null && app.TryGetResource("IconClose", null, out var geo) && geo is Geometry g) closeIcon.Data = g;
            closeIcon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new Avalonia.Data.Binding("Foreground") { RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) } });

            var close = new Button { Classes = { "icon" }, Content = closeIcon, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
            Avalonia.Controls.ToolTip.SetTip(close, "Esc");
            close.Click += (_, _) => onCancel();
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            content.Children.Add(header);
        }
        content.Children.Add(view);

        var card = new Border
        {
            Classes = { "dialog" },
            // Co theo màn hình (điện thoại): rộng tối đa = width mong muốn, nhỏ hơn thì ôm sát màn hình.
            MaxWidth = Math.Min(width, 900),
            MaxHeight = 760,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = content
            }
        };

        var layer = new Border
        {
            Background = ThemeBrush("ThemeOverlayBrush") ?? new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Child = card,
            Focusable = true
        };
        layer.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { onCancel(); e.Handled = true; }
        };
        // Chạm/click vào nền mờ (ngoài thẻ hộp thoại) = hủy hộp thoại trên cùng (chỉ hộp thoại cho phép, vd. Ctrl+K).
        layer.PointerPressed += (_, e) =>
        {
            if (!closeOnBackdrop || !ReferenceEquals(e.Source, layer)) return;
            if (Stack.Count > 0 && ReferenceEquals(Stack[^1].layer, layer))
            {
                onCancel();
                e.Handled = true;
            }
        };

        Stack.Add((view, layer, onCancel));
        _overlay.Children.Add(layer);
        _overlay.IsVisible = true;
        OpenChanged?.Invoke(true);
        layer.AttachedToVisualTree += (_, _) => FocusFirst(card);
    }

    private static void FocusFirst(Control root)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var first = root.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible && c is TextBox or ComboBox or Button);
            first?.Focus();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    internal static void Remove(Control view)
    {
        Ui.Run(() =>
        {
            var idx = Stack.FindIndex(s => ReferenceEquals(s.view, view));
            if (idx < 0) return;
            _overlay?.Children.Remove(Stack[idx].layer);
            Stack.RemoveAt(idx);
            if (Stack.Count == 0 && _overlay != null) _overlay.IsVisible = false;
            OpenChanged?.Invoke(Stack.Count > 0);
        });
    }

    // ===== Hộp thoại dựng sẵn =====

    public static Task MessageAsync(string title, string message, DialogIcon icon = DialogIcon.Info)
        => ShowAsync(new MessageDialog(title, message, icon, confirm: false));

    public static async Task<bool> ConfirmAsync(string title, string message, DialogIcon icon = DialogIcon.Question)
        => await ShowAsync(new MessageDialog(title, message, icon, confirm: true)) == true;

    /// <summary>Nhập 1 dòng văn bản (hoặc mật khẩu). Trả null nếu hủy.</summary>
    public static Task<string?> PromptAsync(string title, string label, string initial = "", bool isPassword = false)
        => ShowAsync(new PromptDialog(title, label, initial, isPassword));

    public static Task<HostKeyDecision> HostKeyAsync(HostKeyPromptInfo info)
        => HostKeyCoreAsync(info);

    private static async Task<HostKeyDecision> HostKeyCoreAsync(HostKeyPromptInfo info)
        => await ShowAsync(new HostKeyPromptDialog(info));
}

/// <summary>Thông báo / xác nhận Có–Không.</summary>
internal sealed class MessageDialog : DialogView<bool?>
{
    public MessageDialog(string title, string message, DialogIcon icon, bool confirm)
    {
        Title = title;
        DialogWidth = 440;

        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        if (confirm)
        {
            var no = new Button { Content = LocalizationManager.Tr("No", "Không"), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var yes = new Button { Content = LocalizationManager.Tr("Yes", "Có"), MinWidth = 80, Classes = { "primary" }, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            no.Click += (_, _) => Close(false);
            yes.Click += (_, _) => Close(true);
            buttons.Children.Add(no);
            buttons.Children.Add(yes);
        }
        else
        {
            var ok = new Button { Content = "OK", MinWidth = 80, Classes = { "primary" }, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            ok.Click += (_, _) => Close(true);
            buttons.Children.Add(ok);
        }

        Content = new StackPanel { Spacing = 16, Children = { body, buttons } };
    }

    public override void OnCancelRequested() => Close(false);
}

internal sealed class PromptDialog : DialogView<string?>
{
    public PromptDialog(string title, string label, string initial, bool isPassword)
    {
        Title = title;
        DialogWidth = 420;

        var box = new TextBox { Text = initial, PasswordChar = isPassword ? '•' : '\0' };
        var ok = new Button { Content = "OK", MinWidth = 80, Classes = { "primary" }, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        var cancel = new Button { Content = LocalizationManager.Tr("Cancel", "Hủy"), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close(box.Text ?? "");
        cancel.Click += (_, _) => Close(null);
        box.AttachedToVisualTree += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, Avalonia.Threading.DispatcherPriority.Loaded);
        };

        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                box,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } }
            }
        };
    }
}

internal sealed class HostKeyPromptDialog : DialogView<HostKeyDecision>
{
    public HostKeyPromptDialog(HostKeyPromptInfo info)
    {
        Title = LocalizationManager.Tr("Verify host key", "Xác nhận host key");
        DialogWidth = 520;

        string msg = info.IsChanged
            ? LocalizationManager.Tr(
                $"WARNING: the host key of {info.Host}:{info.Port} has CHANGED. This could be a man-in-the-middle attack.",
                $"CẢNH BÁO: host key của {info.Host}:{info.Port} đã THAY ĐỔI. Có thể đang bị tấn công man-in-the-middle.")
            : LocalizationManager.Tr(
                $"First connection to {info.Host}:{info.Port}. Verify the fingerprint:",
                $"Lần đầu kết nối {info.Host}:{info.Port}. Hãy kiểm tra vân tay:");

        var warn = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap };
        if (info.IsChanged) warn.Foreground = Dialogs.ThemeBrush("ThemeDangerBrush");

        var fp = new TextBox
        {
            Text = $"{info.KeyName}\n{info.Fingerprint}",
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace")
        };

        var cancel = new Button { Content = LocalizationManager.Tr("Cancel", "Hủy"), HorizontalContentAlignment = HorizontalAlignment.Center };
        var once = new Button { Content = LocalizationManager.Tr("Trust once", "Tin cậy 1 lần"), HorizontalContentAlignment = HorizontalAlignment.Center };
        var save = new Button { Content = LocalizationManager.Tr("Trust & save", "Tin cậy & lưu"), Classes = { "primary" }, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = !info.IsChanged };
        cancel.Click += (_, _) => Close(HostKeyDecision.Cancel);
        once.Click += (_, _) => Close(HostKeyDecision.TrustOnce);
        save.Click += (_, _) => Close(HostKeyDecision.TrustAndSave);

        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                warn, fp,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, once, save } }
            }
        };
    }

    public override void OnCancelRequested() => Close(HostKeyDecision.Cancel);
}
