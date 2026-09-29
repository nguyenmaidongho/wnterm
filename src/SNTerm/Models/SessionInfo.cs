using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using SNTerm.Connections;

namespace SNTerm.Models;

public class SessionInfo : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public bool SavePassword { get; set; } = true;
    public string? EncryptedPassword { get; set; }
    public string? KeyFilePath { get; set; }
    public string? EncryptedPassphrase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConnectedAt { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Username}@{Host}" : Name;

    [JsonIgnore]
    // Không kèm Port để tránh lộ port khi hiển thị (tooltip, thanh trạng thái...).
    public string Subtitle => $"{Username}@{Host}";

    [JsonIgnore]
    public string EffectiveGroup => string.IsNullOrWhiteSpace(Group) ? "Chưa phân nhóm" : Group.Trim();

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

    public SessionInfo Clone()
    {
        return new SessionInfo
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? "" : $"{Name} (bản sao)",
            Group = Group,
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
