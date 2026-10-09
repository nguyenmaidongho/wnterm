using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using WNTerm.Connections;

namespace WNTerm.Models;

public class SessionInfo : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public bool IsPinned { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public bool SavePassword { get; set; } = true;
    public string? EncryptedPassword { get; set; }
    public string? KeyFilePath { get; set; }
    public string? EncryptedPassphrase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>Lần sửa nội dung gần nhất (UTC) — SessionStore tự đặt; dùng khi gộp đồng bộ (bản sửa sau thắng).</summary>
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore]
    public DateTime EffectiveUpdatedAt => UpdatedAt ?? CreatedAt;

    /// <summary>Đại diện phần nội dung cần đồng bộ (không gồm thời điểm kết nối, trạng thái chạy...).</summary>
    [JsonIgnore]
    public string ContentFingerprint => string.Join("\u001f", Name, Group, string.Join(",", Tags), IsPinned, Host, Port, Username,
        SavePassword, EncryptedPassword ?? "", EncryptedPassphrase ?? "", KeyFilePath ?? "");

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Username}@{Host}" : Name;

    [JsonIgnore]
    // Không kèm Port để tránh lộ port khi hiển thị (tooltip, thanh trạng thái...).
    public string Subtitle => $"{Username}@{Host}";

    [JsonIgnore]
    public string EffectiveGroup => string.IsNullOrWhiteSpace(Group) ? WNTerm.Services.LocalizationManager.Tr("Ungrouped", "Chưa phân nhóm") : Group.Trim();

    [JsonIgnore]
    public string PrimaryTag => Tags.Count > 0 ? Tags[0] : WNTerm.Services.LocalizationManager.Tr("No tag", "Chưa có tag");

    /// <summary>Khóa nhóm hiển thị trong danh sách: ghim lên đầu, sau đó theo tag đầu tiên.</summary>
    [JsonIgnore]
    public string GroupKey => IsPinned ? PinnedGroupName : PrimaryTag;

    public static string PinnedGroupName => WNTerm.Services.LocalizationManager.Tr("★ Pinned", "★ Ghim");

    [JsonIgnore]
    public string GroupSortKey => IsPinned ? "0" : (Tags.Count > 0 ? "1|" + Tags[0].ToLowerInvariant() : "2");

    private bool? _isOnline;
    private int? _pingMs;

    /// <summary>Kết quả kiểm tra TCP tới cổng SSH (null = chưa biết). Chỉ trong phiên chạy.</summary>
    [JsonIgnore]
    public bool? IsOnline
    {
        get => _isOnline;
        set { if (_isOnline == value) return; _isOnline = value; Notify(nameof(IsOnline)); Notify(nameof(PingText)); }
    }

    [JsonIgnore]
    public int? PingMs
    {
        get => _pingMs;
        set { if (_pingMs == value) return; _pingMs = value; Notify(nameof(PingMs)); Notify(nameof(PingText)); }
    }

    [JsonIgnore]
    public string PingText => IsOnline switch { true => PingMs is int ms ? $"{ms} ms" : "online", false => "offline", _ => "" };

    [JsonIgnore]
    public string LastConnectedText
    {
        get
        {
            if (LastConnectedAt is not DateTime t) return "";
            var d = DateTime.UtcNow - t.ToUniversalTime();
            if (d.TotalMinutes < 1) return WNTerm.Services.LocalizationManager.Tr("just now", "vừa xong");
            if (d.TotalHours < 1) return string.Format(WNTerm.Services.LocalizationManager.Tr("{0} min ago", "{0} phút trước"), (int)d.TotalMinutes);
            if (d.TotalDays < 1) return string.Format(WNTerm.Services.LocalizationManager.Tr("{0} h ago", "{0} giờ trước"), (int)d.TotalHours);
            if (d.TotalDays < 2) return WNTerm.Services.LocalizationManager.Tr("yesterday", "hôm qua");
            if (d.TotalDays < 30) return string.Format(WNTerm.Services.LocalizationManager.Tr("{0} days ago", "{0} ngày trước"), (int)d.TotalDays);
            return t.ToLocalTime().ToString("dd/MM/yyyy");
        }
    }

    public void RefreshRelativeTime() => Notify(nameof(LastConnectedText));

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [JsonIgnore]
    public string TagsText => string.Join(", ", Tags);

    [JsonIgnore]
    public bool IsKeyFileMissing => !string.IsNullOrEmpty(KeyFilePath) && !System.IO.File.Exists(KeyFilePath);

    private ConnectionStatus? _liveStatus;

    /// <summary>
    /// Trạng thái kết nối hiện tại (chỉ trong phiên chạy, không lưu ra file).
    /// Được MainViewModel cập nhật dựa trên (các) tab terminal đang mở cho VM này,
    /// dùng để tô màu icon on/off trong danh sách VM.
    /// </summary>
    [JsonIgnore]
    public ConnectionStatus? LiveStatus
    {
        get => _liveStatus;
        set
        {
            if (_liveStatus == value) return;
            _liveStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveStatus)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static List<string> ParseTags(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length > 0 && !result.Exists(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)))
                result.Add(t);
        }
        return result;
    }

    public SessionInfo Clone()
    {
        return new SessionInfo
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? "" : $"{Name} " + WNTerm.Services.LocalizationManager.Tr("(copy)", "(bản sao)"),
            Group = Group,
            Tags = new List<string>(Tags),
            IsPinned = IsPinned,
            Host = Host,
            Port = Port,
            Username = Username,
            SavePassword = SavePassword,
            EncryptedPassword = EncryptedPassword,
            KeyFilePath = KeyFilePath,
            EncryptedPassphrase = EncryptedPassphrase,
            CreatedAt = DateTime.UtcNow,
            LastConnectedAt = null
        };
    }
}
