using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Data;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WNTerm.Models;
using WNTerm.ViewModels;
using WNTerm.Services;

namespace WNTerm.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        double targetWidth = ViewModel.LeftColumnWidth;
        if (targetWidth < 140 || targetWidth > 600) targetWidth = 400;
        LeftColumnDef.Width = new GridLength(targetWidth, GridUnitType.Pixel);

        ViewModel.Tabs.CollectionChanged += OnTabsCollectionChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateTerminalViews();
        ViewModel.RunAutoCloudBackupIfDue();
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (TerminalTabViewModel tab in e.OldItems)
            {
                TerminalContainerGrid.Children.Remove(tab.TerminalControl);
            }
        }

        UpdateTerminalViews();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            UpdateTerminalViews();
        }
    }

    private void UpdateTerminalViews()
    {
        foreach (var tab in ViewModel.Tabs)
        {
            if (!TerminalContainerGrid.Children.Contains(tab.TerminalControl))
            {
                TerminalContainerGrid.Children.Add(tab.TerminalControl);
            }

            tab.TerminalControl.Visibility = (tab == ViewModel.SelectedTab)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (ViewModel.SelectedTab != null)
        {
            ViewModel.SelectedTab.TerminalControl.PostFocus();
        }
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is TerminalTabViewModel tab)
        {
            ViewModel.SelectedTab = tab;
        }
    }

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed && sender is FrameworkElement el && el.DataContext is TerminalTabViewModel tab)
        {
            ViewModel.CloseTab(tab);
            e.Handled = true;
        }
    }

    private void SessionsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SessionsListBox.SelectedItem is SessionInfo session)
        {
            ViewModel.SessionList.Connect(session);
        }
    }

    private void SessionsListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (SessionsListBox.SelectedItems.Count > 1)
            {
                ViewModel.SessionList.ConnectMultiple(SessionsListBox.SelectedItems);
            }
            else if (SessionsListBox.SelectedItem is SessionInfo session)
            {
                ViewModel.SessionList.Connect(session);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            if (SessionsListBox.SelectedItem is SessionInfo session)
            {
                ViewModel.SessionList.EditSession(session);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (SessionsListBox.SelectedItems.Count > 0)
            {
                ViewModel.SessionList.DeleteSession(SessionsListBox.SelectedItems);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (SessionsListBox.SelectedItem is SessionInfo session)
            {
                ViewModel.SessionList.DuplicateSession(session);
            }
            e.Handled = true;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.CanCloseWindow())
        {
            e.Cancel = true;
            return;
        }

        double finalLeftWidth = LeftColumnDef.ActualWidth >= 140 ? LeftColumnDef.ActualWidth : 400;
        ViewModel.SaveWindowState(ActualWidth, ActualHeight, finalLeftWidth);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Any(f => f.EndsWith(".wnterm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mxtsessions", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var targetFile = files?.FirstOrDefault(f => f.EndsWith(".wnterm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mxtsessions", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(targetFile))
            {
                ViewModel.Import(targetFile);
                e.Handled = true;
            }
        }
    }



    private void ListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            if (!item.IsSelected)
            {
                SessionsListBox.SelectedItems.Clear();
                item.IsSelected = true;
            }
            SessionsListBox.SelectedItem = item.DataContext;
            item.Focus();
        }
    }

    private void SessionsListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        DependencyObject? dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep != SessionsListBox)
        {
            if (dep is ListBoxItem)
            {
                return;
            }
            dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(dep)
                : LogicalTreeHelper.GetParent(dep);
        }

        e.Handled = true;
    }

    private void ContextMenuConnect_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListBox.SelectedItems.Count > 1)
        {
            ViewModel.SessionList.ConnectMultiple(SessionsListBox.SelectedItems);
        }
        else if (SessionsListBox.SelectedItem is SessionInfo session)
        {
            ViewModel.SessionList.Connect(session);
        }
    }

    private void ContextMenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListBox.SelectedItem is SessionInfo session)
        {
            ViewModel.SessionList.EditSession(session);
        }
    }

    private void ContextMenuMoveToGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = SessionsListBox.SelectedItems.OfType<SessionInfo>().ToList();
        if (selected.Count > 1)
        {
            string currentGroup = selected[0].Group;
            var dlg = new InputDialog(LocalizationManager.Get("Str_MoveToGroupTitle"), LocalizationManager.Get("Str_EnterGroupName"), currentGroup)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && dlg.InputText != null)
            {
                ViewModel.SessionList.MoveSessionsToGroup(selected, dlg.InputText);
            }
        }
        else if (SessionsListBox.SelectedItem is SessionInfo session)
        {
            ViewModel.SessionList.MoveToGroup(session);
        }
    }

    private void ContextMenuExport_Click(object sender, RoutedEventArgs e)
    {
        var selected = SessionsListBox.SelectedItems.OfType<SessionInfo>().ToList();
        if (selected.Count > 1)
        {
            ViewModel.Export(selected);
        }
        else if (SessionsListBox.SelectedItem is SessionInfo session)
        {
            ViewModel.Export(session);
        }
    }

    private void ContextMenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListBox.SelectedItems.Count > 0)
        {
            ViewModel.SessionList.DeleteSession(SessionsListBox.SelectedItems);
        }
        else if (SessionsListBox.SelectedItem is SessionInfo session)
        {
            ViewModel.SessionList.DeleteSession(session);
        }
    }

    private void GroupConnectAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el)
        {
            string currentName = (el.DataContext as CollectionViewGroup)?.Name?.ToString()
                                ?? el.DataContext?.ToString()
                                ?? "";
            if (!string.IsNullOrEmpty(currentName))
            {
                ViewModel.SessionList.ConnectAllInGroup(currentName);
            }
        }
    }

    private void GroupRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el)
        {
            string currentName = (el.DataContext as CollectionViewGroup)?.Name?.ToString()
                                ?? el.DataContext?.ToString()
                                ?? "";
            if (!string.IsNullOrEmpty(currentName))
            {
                var dlg = new InputDialog(LocalizationManager.Get("Str_RenameGroupTitle"), LocalizationManager.Get("Str_EnterNewGroupName"), currentName)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
                {
                    ViewModel.SessionList.RenameGroup(currentName, dlg.InputText);
                }
            }
        }
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Tab)
        {
            ViewModel.SelectNextTab();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Tab)
        {
            ViewModel.SelectPrevTab();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            if (ViewModel.SelectedTab != null)
            {
                ViewModel.CloseTab(ViewModel.SelectedTab);
                e.Handled = true;
            }
        }
    }
}