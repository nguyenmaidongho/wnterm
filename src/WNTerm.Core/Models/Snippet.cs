using System;
using System.Text.Json.Serialization;

namespace WNTerm.Models;

/// <summary>Lệnh đã lưu ("snippet"). VmId = null nghĩa là dùng chung cho mọi VM.</summary>
public class Snippet
{
    // Id/VmId là chuỗi (không phải Guid) để vault do thiết bị khác ghi (PWA...) không bao giờ làm lỗi giải mã.
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("vmId")] public string? VmId { get; set; }
    [JsonPropertyName("autoEnter")] public bool AutoEnter { get; set; } = true;
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }

    [JsonIgnore] public DateTime EffectiveUpdatedAt => UpdatedAt ?? CreatedAt ?? DateTime.MinValue;
    [JsonIgnore] public bool IsGlobal => string.IsNullOrWhiteSpace(VmId);
    [JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Command);

    /// <summary>Nội dung (không gồm thời điểm) — để biết có thật sự bị sửa không.</summary>
    [JsonIgnore]
    public string ContentFingerprint => string.Join("\u0001", Name, Command, (VmId ?? "").ToLowerInvariant(), AutoEnter ? "1" : "0");

    public Snippet Clone() => (Snippet)MemberwiseClone();
}

/// <summary>Dấu xóa snippet (để thiết bị khác cũng xóa).</summary>
public class SnippetTombstone
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("deletedAt")] public DateTime DeletedAt { get; set; }
}
