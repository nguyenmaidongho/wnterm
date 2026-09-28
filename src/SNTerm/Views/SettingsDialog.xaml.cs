using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SNTerm.Models;

namespace SNTerm.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        Loaded += (s, e) =>
        {
            Activate();
            Focus();
        };

        MaxHeight = SystemParameters.WorkArea.Height * 0.95;

        // Populate controls
                if (string.Equals(_settings.Language, "vi", StringComparison.OrdinalIgnoreCase))
        {
            LanguageCombo.SelectedIndex = 1;
        }
        else
        {
            LanguageCombo.SelectedIndex = 0;
        }

        SetComboText(FontFamilyCombo, _settings.FontFamily);
        SetComboText(FontSizeCombo, _settings.FontSize.ToString());
        SetComboText(ThemeCombo, _settings.Theme);

        CopyOnSelectCheck.IsChecked = _settings.CopyOnSelect;
        ConfirmMultilineCheck.IsChecked = _settings.ConfirmMultilinePaste;
        KeepAliveBox.Text = _settings.KeepAliveSeconds.ToString();
        ShowHiddenFilesCheck.IsChecked = _settings.ShowHiddenFiles;
        CustomEditorBox.Text = _settings.CustomEditorPath;

        if (_settings.RightClickAction == "Menu")
        {
            RightClickCombo.SelectedIndex = 1;
        }
        else
        {
            RightClickCombo.SelectedIndex = 0;
        }
    }

    private void SetComboText(ComboBox combo, string text)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void BrowseEditor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = SNTerm.Services.LocalizationManager.Get("Str_SelectEditorTitle"),
            Filter = SNTerm.Services.LocalizationManager.Get("Str_ExeFilter")
        };
        if (dlg.ShowDialog(this) == true)
        {
            CustomEditorBox.Text = dlg.FileName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
                var selectedLang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
        _settings.Language = selectedLang;
        SNTerm.Services.LocalizationManager.ApplyLanguage(selectedLang);

        _settings.FontFamily = (FontFamilyCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "JetBrains Mono";

        if (int.TryParse((FontSizeCombo.SelectedItem as ComboBoxItem)?.Content.ToString(), out int size))
        {
            _settings.FontSize = size;
        }

        _settings.Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Dark";
        _settings.CopyOnSelect = CopyOnSelectCheck.IsChecked == true;
        _settings.ConfirmMultilinePaste = ConfirmMultilineCheck.IsChecked == true;
        _settings.ShowHiddenFiles = ShowHiddenFilesCheck.IsChecked == true;
        _settings.CustomEditorPath = CustomEditorBox.Text.Trim();

        var selectedAction = (RightClickCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _settings.RightClickAction = selectedAction ?? "Paste";

        if (int.TryParse(KeepAliveBox.Text, out int keepAlive) && keepAlive >= 5)
        {
            _settings.KeepAliveSeconds = keepAlive;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
