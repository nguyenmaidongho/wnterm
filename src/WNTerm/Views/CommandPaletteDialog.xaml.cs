using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WNTerm.Models;

namespace WNTerm.Views;

/// <summary>Thanh tìm nhanh (Ctrl+K): gõ vài chữ của tên/IP/tag, Enter để kết nối.</summary>
public partial class CommandPaletteDialog : Window
{
    private readonly List<SessionInfo> _all;

    public SessionInfo? Chosen { get; private set; }
    public bool OpenSftp { get; private set; }

    public CommandPaletteDialog(IEnumerable<SessionInfo> sessions)
    {
        InitializeComponent();
        _all = sessions.ToList();
        Loaded += (_, _) =>
        {
            if (Owner != null)
            {
                Left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
                Top = Owner.Top + 90;
            }
            QueryBox.Focus();
            Refresh();
        };
    }

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
        var tokens = QueryBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = _all.Where(s => Match(s, tokens))
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.LastConnectedAt ?? DateTime.MinValue)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        ResultsList.ItemsSource = list;
        if (list.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (Chosen == null && IsVisible) Close();
    }

    private void Accept(bool sftp)
    {
        if (ResultsList.SelectedItem is SessionInfo s)
        {
            Chosen = s;
            OpenSftp = sftp;
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                if (ResultsList.SelectedIndex < ResultsList.Items.Count - 1) ResultsList.SelectedIndex++;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (ResultsList.SelectedIndex > 0) ResultsList.SelectedIndex--;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept(false);
}
