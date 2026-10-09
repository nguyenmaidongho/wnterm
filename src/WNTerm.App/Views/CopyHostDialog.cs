using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using WNTerm.App.Services;
using WNTerm.Services;

namespace WNTerm.App.Views;

/// <summary>Sao chép host: mặc định chỉ IP/host; tick để chép cả IP, Port, User, Pass.</summary>
public sealed class CopyHostDialog : DialogView<bool?>
{
    private readonly CheckBox _full = new() { IsChecked = false };

    public bool IncludeLogin => _full.IsChecked == true;

    public CopyHostDialog(string host, bool hasPassword)
    {
        Title = LocalizationManager.Tr("Copy host", "Sao chép host");
        DialogWidth = 420;

        _full.Content = LocalizationManager.Tr("Also copy login info (IP, Port, User, Password)", "Chép cả thông tin đăng nhập (IP, Port, User, Pass)");

        var root = new StackPanel { Spacing = 10 };
        root.Children.Add(new TextBlock { Text = host, Classes = { "muted" } });
        root.Children.Add(_full);
        if (!hasPassword)
            root.Children.Add(new TextBlock
            {
                Text = LocalizationManager.Tr("This VM has no saved password, so Password will be left empty.", "VM này chưa lưu mật khẩu nên phần Pass sẽ để trống."),
                Classes = { "muted" },
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

        var copy = new Button { Content = LocalizationManager.Tr("Copy", "Sao chép"), Classes = { "primary" }, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = LocalizationManager.Get("Str_Cancel"), IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        copy.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { cancel, copy }
        });

        Content = root;
    }

    public override void OnCancelRequested() => Close(false);
}
