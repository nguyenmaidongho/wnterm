using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SNTerm.Models;
using SNTerm.ViewModels;

namespace SNTerm.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Tabs.CollectionChanged += OnTabsCollectionChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateTerminalViews();
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

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Any(f => f.EndsWith(".snterm", StringComparison.OrdinalIgnoreCase)))
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
            var sntermFile = files?.FirstOrDefault(f => f.EndsWith(".snterm", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(sntermFile))
            {
                ViewModel.Import(sntermFile);
                e.Handled = true;
            }
        }
    }

    private void ContextMenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListBox.SelectedItems.Count > 0)
        {
            ViewModel.SessionList.DeleteSession(SessionsListBox.SelectedItems);
        }
    }
}

