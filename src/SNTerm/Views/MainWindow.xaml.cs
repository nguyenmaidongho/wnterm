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

    private void ContextMenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SessionsListBox.SelectedItems.Count > 0)
        {
            ViewModel.SessionList.DeleteSession(SessionsListBox.SelectedItems);
        }
    }
}
