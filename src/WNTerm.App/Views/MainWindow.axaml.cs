using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WNTerm.ViewModels;

namespace WNTerm.App.Views;

public partial class MainWindow : Window
{
    private readonly MainView _main;
    private bool _allowClose;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _main = this.FindControl<MainView>("Main")!;

        var vm = _main.ViewModel;
        double w = vm.Settings.WindowWidth, h = vm.Settings.WindowHeight;
        if (w >= 800 && h >= 500) { Width = w; Height = h; }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WindowTitle)) Title = vm.WindowTitle;
        };

        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            Save();
            return;
        }
        // Hộp thoại xác nhận là async nên huỷ lần đóng này rồi đóng lại sau khi người dùng đồng ý.
        e.Cancel = true;
        if (await _main.ViewModel.CanCloseWindowAsync())
        {
            _allowClose = true;
            Close();
        }
    }

    private void Save()
    {
        double left = _main.LeftPaneWidth >= 140 ? _main.LeftPaneWidth : 400;
        _main.ViewModel.SaveWindowState(Width, Height, left);
    }
}
