using System;
using System.Text.Json.Serialization;

namespace SNTerm.Models;

public class SessionInfo
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
    public string Subtitle => $"{Username}@{Host}:{Port}";

    [JsonIgnore]
    public string EffectiveGroup => string.IsNullOrWhiteSpace(Group) ? "Chưa phân nhóm" : Group.Trim();

    [JsonIgnore]
    public bool IsKeyFileMissing => !string.IsNullOrEmpty(KeyFilePath) && !System.IO.File.Exists(KeyFilePath);

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
