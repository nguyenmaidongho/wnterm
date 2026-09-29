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
            Name = string.IsNullOrWhiteSpace(Name) ? "" : $"{Name} (bản sao)",
            Group = Group,
            Tags = new List<string>(Tags),
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
