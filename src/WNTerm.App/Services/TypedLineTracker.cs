using System.Text;

namespace WNTerm.App.Services;

/// <summary>
/// Theo dõi dòng lệnh người dùng gõ vào terminal để có nút "Lưu lệnh vừa gõ". Chỉ tin được khi dòng gồm toàn ký tự gõ trực tiếp
/// (+ Backspace); mũi tên/lịch sử, Tab (tự hoàn thành), chuỗi escape hay phím Ctrl bất kỳ → dòng đó "không tin cậy".
/// Lớp thuần (không phụ thuộc UI) để dễ kiểm thử.
/// </summary>
public sealed class TypedLineTracker
{
    private const int MaxLine = 2000;
    private readonly StringBuilder _line = new();
    private bool _unreliable;

    /// <summary>Lệnh đã Enter gần nhất và đáng tin; null nếu chưa có hoặc dòng gần nhất không tin cậy.</summary>
    public string? LastCommand { get; private set; }

    /// <summary>Dòng đang gõ dở có còn đáng tin không.</summary>
    public bool CurrentLineReliable => !_unreliable;

    /// <summary>Nhập một đoạn dữ liệu đúng như gửi cho SSH (một phím, một chuỗi escape, hoặc đoạn dán).</summary>
    public void Feed(string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        if (data.IndexOf('\u001b') >= 0) { _unreliable = true; return; } // mũi tên, Home/End, Delete, dán có bracketed-paste...

        for (int i = 0; i < data.Length; i++)
        {
            char c = data[i];
            switch (c)
            {
                case '\r':
                    if (i + 1 < data.Length && data[i + 1] == '\n') i++;
                    Commit();
                    break;
                case '\n':
                    Commit();
                    break;
                case '\b':
                case '\u007f':
                    if (_line.Length > 0) _line.Length--;
                    else if (_unreliable) { /* đã không chắc, giữ nguyên */ }
                    break;
                case '\u0003': // Ctrl+C: hủy dòng
                case '\u0015': // Ctrl+U: xóa cả dòng
                    Reset();
                    break;
                case '\u000c': // Ctrl+L: xóa màn hình, dòng giữ nguyên
                    break;
                default:
                    if (c < ' ') _unreliable = true;   // Tab, Ctrl+R/W/A/E..., hoàn thành/lịch sử/di chuyển con trỏ
                    else if (_line.Length < MaxLine) _line.Append(c);
                    else _unreliable = true;
                    break;
            }
        }
    }

    /// <summary>Bỏ dòng đang gõ (vd. sau khi gửi lệnh bằng nút).</summary>
    public void Reset()
    {
        _line.Clear();
        _unreliable = false;
    }

    /// <summary>Đánh dấu dòng hiện tại không tin cậy (vd. có thứ gì đó được chèn vào dòng không qua bàn phím).</summary>
    public void MarkUnreliable() => _unreliable = true;

    private void Commit()
    {
        string text = _line.ToString().Trim();
        bool ok = !_unreliable;
        Reset();
        if (!ok) { LastCommand = null; return; }
        if (text.Length == 0) return; // Enter trên dòng trống: giữ lệnh trước đó
        LastCommand = text;
    }
}
