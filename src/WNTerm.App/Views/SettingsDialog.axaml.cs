using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

public partial class SettingsDialog : DialogView<bool?>
{
    private readonly AppSettings _settings;

    public SettingsDialog() : this(new AppSettings()) { }

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Title = LocalizationManager.Get("Str_SettingsTitle");
        DialogWidth = 540;

        LanguageCombo.SelectedIndex = string.Equals(_settings.Language, "vi", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        SetComboText(FontFamilyCombo, _settings.FontFamily);
        SetComboText(FontSizeCombo, _settings.FontSize.ToString());
        SetComboText(ThemeCombo, _settings.Theme);

        CopyOnSelectCheck.IsChecked = _settings.CopyOnSelect;
        ConfirmMultilineCheck.IsChecked = _settings.ConfirmMultilinePaste;
        KeepAliveBox.Text = _settings.KeepAliveSeconds.ToString();
        ShowHiddenFilesCheck.IsChecked = _settings.ShowHiddenFiles;
        CustomEditorBox.Text = _settings.CustomEditorPath;
        RightClickCombo.SelectedIndex = _settings.RightClickAction == "Menu" ? 1 : 0;

        this.FindControl<Button>("BrowseBtn")!.Click += BrowseEditor_Click;
        this.FindControl<Button>("SaveBtn")!.Click += Save_Click;
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close(false);

        AttachedToVisualTree += (_, _) =>
        {
            double h = Ui.Main?.Bounds.Height ?? 0;
            this.FindControl<ScrollViewer>("Scroller")!.MaxHeight = h > 0 ? Math.Max(240, h - 230) : 620;
        };
    }

    public override void OnCancelRequested() => Close(false);


    private static void SetComboText(ComboBox combo, string text)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private async void BrowseEditor_Click(object? sender, RoutedEventArgs e)
    {
        var title = LocalizationManager.Get("Str_SelectEditorTitle");
        var path = OperatingSystem.IsWindows()
            ? await Ui.PickOpenFileAsync(title, "Executable", "*.exe")
            : await Ui.PickOpenFileAsync(title);
        if (path != null) CustomEditorBox.Text = path;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var selectedLang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
        _settings.Language = selectedLang;
        LocalizationManager.ApplyLanguage(selectedLang);

        _settings.FontFamily = (FontFamilyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "JetBrains Mono";

        if (int.TryParse((FontSizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int size))
            _settings.FontSize = size;

        _settings.Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dark";
        AppPlatform.ApplyTheme(_settings.Theme);
        _settings.CopyOnSelect = CopyOnSelectCheck.IsChecked == true;
        _settings.ConfirmMultilinePaste = ConfirmMultilineCheck.IsChecked == true;
        _settings.ShowHiddenFiles = ShowHiddenFilesCheck.IsChecked == true;
        _settings.CustomEditorPath = (CustomEditorBox.Text ?? "").Trim();

        _settings.RightClickAction = (RightClickCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Paste";

        if (int.TryParse(KeepAliveBox.Text, out int keepAlive) && keepAlive >= 5)
            _settings.KeepAliveSeconds = keepAlive;

        Close(true);
    }
}
