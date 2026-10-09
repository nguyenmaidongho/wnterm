using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;
using WNTerm.ViewModels;

namespace WNTerm.App.Views;

public partial class SessionEditorDialog : DialogView<bool?>
{
    private readonly SessionEditorViewModel _viewModel;
    private bool _isPasswordRevealed;

    public SessionEditorDialog() : this(new SessionEditorViewModel()) { }

    public SessionEditorDialog(SessionEditorViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        _viewModel = viewModel;
        DataContext = _viewModel;
        Title = _viewModel.DialogTitle;
        DialogWidth = 500;

        _viewModel.RequestClose += success => Close(success);

        this.FindControl<Control>("TagsPanel")!.IsVisible = _viewModel.ExistingTags.Count > 0;
        this.FindControl<ItemsControl>("TagsList")!.AddHandler(Button.ClickEvent, ExistingTag_Click);

        this.FindControl<Button>("RevealBtn")!.Click += TogglePasswordVisibility_Click;
        this.FindControl<Button>("BrowseBtn")!.Click += BrowseKeyFile_Click;

        var port = this.FindControl<TextBox>("PortBox")!;
        port.AddHandler(TextInputEvent, PortTextBox_TextInput, RoutingStrategies.Tunnel);
        port.GotFocus += (_, _) => port.SelectAll();

        // Giới hạn chiều cao vùng cuộn để nút ở đáy luôn thấy được (WPF: MaxHeight = 95% màn hình).
        AttachedToVisualTree += (_, _) =>
        {
            double h = Ui.Main?.Bounds.Height ?? 0;
            this.FindControl<ScrollViewer>("Scroller")!.MaxHeight = h > 0 ? System.Math.Max(240, h - 230) : 520;
        };
    }

    public override void OnCancelRequested() => Close(false);

    private void TogglePasswordVisibility_Click(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("PasswordBox")!;
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            if (string.IsNullOrEmpty(box.Text))
                box.Text = _viewModel.GetStoredPasswordPlain() ?? "";
            box.PasswordChar = '\0';
            box.Focus();
            box.CaretIndex = box.Text?.Length ?? 0;
        }
        else
        {
            box.PasswordChar = '•';
            box.Focus();
        }
    }

    private async void BrowseKeyFile_Click(object? sender, RoutedEventArgs e)
    {
        // Điện thoại: file key được chép vào thư mục keys của app (đường dẫn content:// không dùng lại được).
        var path = await Ui.PickOpenFileToAsync(
            LocalizationManager.Tr("Select SSH key file", "Chọn file SSH Key"),
            AppPaths.Default.KeysDir,
            "Key file",
            "*.pem", "*.key", "*.ppk", "id_*");
        if (path != null) _viewModel.KeyFilePath = path;
    }

    // Ô Port: chỉ cho nhập số.
    private void PortTextBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text != null && !e.Text.All(char.IsDigit)) e.Handled = true;
    }

    // Bấm vào một tag đã có để thêm vào ô Tags (bỏ qua nếu đã có).
    private void ExistingTag_Click(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Visual v) return;
        var btn = v as Button ?? v.FindAncestorOfType<Button>();
        if (btn?.Content is not string tag) return;
        var tags = SessionInfo.ParseTags(_viewModel.TagsText);
        if (tags.Exists(t => t.Equals(tag, System.StringComparison.OrdinalIgnoreCase))) return;
        tags.Add(tag);
        _viewModel.TagsText = string.Join(", ", tags);
    }
}
