using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WNTerm.ViewModels;

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
    private int _batchDepth;

    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Bắt đầu một lô truyền file (nhiều file). Token trả về dùng chung cho cả lô: bấm Hủy là dừng cả lô,
    /// không chỉ file đang chạy. Gọi <see cref="EndBatch"/> khi xong (kể cả khi lỗi/hủy).
    /// </summary>
    public CancellationToken BeginBatch()
    {
        // Lô cũ đã bị hủy nhưng chưa kết thúc hẳn: lô mới phải có token riêng (chưa bị hủy).
        if (_batchDepth == 0 || _cts == null || _cts.IsCancellationRequested)
        {
            _cts = new CancellationTokenSource();
        }
        _batchDepth++;
        return _cts.Token;
    }

    public void EndBatch()
    {
        if (_batchDepth > 0) _batchDepth--;
        if (_batchDepth == 0) EndTransfer();
    }

    /// <summary>Hiện tiến trình cho 1 file trong lô. KHÔNG tạo token mới (để Hủy dừng được cả lô).</summary>
    public void StartTransfer(string fileName)
    {
        if (_cts == null || (_batchDepth == 0 && _cts.IsCancellationRequested))
        {
            _cts = new CancellationTokenSource();
        }
        CurrentFileName = fileName;
        ProgressPercentage = 0;
        SpeedFormatted = "0 KB/s";
        IsTransferring = true;
    }

    public void UpdateProgress(ulong transferred, ulong total, double bytesPerSec)
    {
        if (!IsTransferring) return;
        if (total > 0)
        {
            ProgressPercentage = (int)Math.Min(100, (transferred * 100) / total);
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
