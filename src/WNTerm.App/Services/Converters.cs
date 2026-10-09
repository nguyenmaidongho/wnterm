using Avalonia.Data.Converters;
using Avalonia.Media;
using WNTerm.Connections;

namespace WNTerm.App.Services;

/// <summary>Converter dùng trong XAML (thay cho các converter static của SessionListViewModel bản WPF).</summary>
public static class Converters
{
    private static readonly IBrush Connected = new SolidColorBrush(Color.FromRgb(0x52, 0xC4, 0x1A)).ToImmutable();
    private static readonly IBrush Connecting = new SolidColorBrush(Color.FromRgb(0xFA, 0xAD, 0x14)).ToImmutable();
    private static readonly IBrush Off = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)).ToImmutable();
    private static readonly IBrush OnlineUnknown = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)).ToImmutable();

    /// <summary>Màu icon VM theo trạng thái kết nối sống: xanh = đã kết nối, vàng = đang kết nối, xám = off.</summary>
    public static readonly FuncValueConverter<ConnectionStatus?, IBrush> LiveStatusToIconBrush = new(s => s switch
    {
        ConnectionStatus.Connected => Connected,
        ConnectionStatus.Connecting => Connecting,
        ConnectionStatus.Reconnecting => Connecting,
        _ => Off
    });

    /// <summary>Chấm online/offline (null = chưa biết).</summary>
    public static readonly FuncValueConverter<bool?, IBrush> OnlineToBrush = new(b =>
        b is bool v ? (v ? Connected : Off) : OnlineUnknown);

    /// <summary>Mũi tên header nhóm: mở = hướng xuống (0°), thu gọn = hướng sang phải (-90°).</summary>
    public static readonly FuncValueConverter<bool, double> CollapsedAngle = new(collapsed => collapsed ? -90 : 0);

    /// <summary>Vẽ chữ trạng thái kết nối (tab).</summary>
    public static readonly FuncValueConverter<string?, bool> NotEmpty = new(s => !string.IsNullOrEmpty(s));

    /// <summary>Số &gt; 0 (vd. hiện nút thùng rác khi có VM đã xóa).</summary>
    public static readonly FuncValueConverter<int, bool> Positive = new(n => n > 0);
}
