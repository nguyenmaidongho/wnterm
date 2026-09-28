using CommunityToolkit.Mvvm.ComponentModel;

namespace SNTerm.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "SN Term";

    [ObservableProperty]
    private string statusMessage = "Sẵn sàng";

    [ObservableProperty]
    private double leftColumnWidth = 280;
}
