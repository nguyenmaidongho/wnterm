using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SNTerm.ViewModels;

public partial class TransferQueueViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isTransferring;

    [ObservableProperty]
    private string currentFileName = "";

    [ObservableProperty]
    private int progressPercentage;

    [ObservableProperty]
    private string speedFormatted = "";

    private CancellationTokenSource? _cts;

    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    public void StartTransfer(string fileName)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        CurrentFileName = fileName;
        ProgressPercentage = 0;
        SpeedFormatted = "0 KB/s";
        IsTransferring = true;
    }

    public void UpdateProgress(ulong transferred, ulong total, double bytesPerSec)
    {
        if (total > 0)
        {
            ProgressPercentage = (int)((transferred * 100) / total);
        }

        SpeedFormatted = bytesPerSec switch
        {
            >= 1024 * 1024 => $"{bytesPerSec / (1024 * 1024):F1} MB/s",
            >= 1024 => $"{bytesPerSec / 1024:F1} KB/s",
            _ => $"{bytesPerSec:F0} B/s"
        };
    }

    public void EndTransfer()
    {
        IsTransferring = false;
        CurrentFileName = "";
        ProgressPercentage = 0;
        SpeedFormatted = "";
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        EndTransfer();
    }
}
