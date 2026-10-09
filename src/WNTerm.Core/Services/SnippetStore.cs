using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WNTerm.Models;

namespace WNTerm.Services;

public class SnippetFileEnvelope
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("snippets")] public List<Snippet> Snippets { get; set; } = new();
    [JsonPropertyName("deleted")] public List<SnippetTombstone> Deleted { get; set; } = new();
}

/// <summary>
/// Lệnh đã lưu (snippets.json cạnh sessions.json). Save (người dùng) tự đóng dấu UpdatedAt cho mục mới/sửa và ghi dấu xóa
/// cho mục bị bỏ → ChangedByUser (tự đồng bộ). SaveSynced (đồng bộ/khôi phục) giữ nguyên dấu thời gian → ReplacedExternally.
/// </summary>
public class SnippetStore
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Đồng hồ (UTC) — cho phép test đặt thời điểm.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public SnippetStore(AppPaths? paths = null) => _paths = paths ?? AppPaths.Default;

    /// <summary>Phát khi người dùng thêm/sửa/xóa snippet (để tự đồng bộ).</summary>
    public event Action? ChangedByUser;

    /// <summary>Phát khi đồng bộ/khôi phục ghi đè danh sách — UI phải nạp lại (trên luồng UI).</summary>
    public event Action? ReplacedExternally;

    public List<Snippet> Load() => ReadFile().Snippets;

    public List<SnippetTombstone> LoadTombstones() => ReadFile().Deleted;

    private SnippetFileEnvelope ReadFile()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_paths.SnippetsFile)) return new();
                var env = JsonSerializer.Deserialize<SnippetFileEnvelope>(ReadAllTextRetry(_paths.SnippetsFile), JsonOptions) ?? new();
                env.Snippets ??= new();
                env.Deleted ??= new();
                var now = Clock();
                env.Deleted.RemoveAll(t => (now - t.DeletedAt).TotalDays > TrashStore.TombstoneDays);
                return env;
            }
            catch { return new(); }
        }
    }

    private static string ReadAllTextRetry(string path)
    {
        for (int i = 0; ; i++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (i < 5) { System.Threading.Thread.Sleep(150); }
        }
    }

    private void Write(List<Snippet> snippets, List<SnippetTombstone> tombs)
    {
        var env = new SnippetFileEnvelope { Snippets = snippets, Deleted = tombs };
        string tmp = _paths.SnippetsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(env, JsonOptions));
        File.Move(tmp, _paths.SnippetsFile, true);
    }

    /// <summary>Lưu thay đổi của người dùng (danh sách đầy đủ): đóng dấu UpdatedAt cho mục mới/sửa, ghi dấu xóa cho mục bị bỏ.</summary>
    public void Save(IEnumerable<Snippet> snippets)
    {
        bool changed = false;
        lock (_lock)
        {
            var list = snippets.Where(s => !string.IsNullOrWhiteSpace(s.Id)).ToList();
            var env = ReadFile();
            var old = env.Snippets.GroupBy(s => s.Id.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            var now = Clock();

            foreach (var s in list)
            {
                s.CreatedAt ??= now;
                if (!old.TryGetValue(s.Id.ToLowerInvariant(), out var prev) || prev.ContentFingerprint != s.ContentFingerprint)
                { s.UpdatedAt = now; changed = true; }
                else s.UpdatedAt = prev.UpdatedAt ?? prev.CreatedAt;
            }

            var ids = list.Select(s => s.Id.ToLowerInvariant()).ToHashSet();
            var tombs = env.Deleted.Where(t => !ids.Contains(t.Id.ToLowerInvariant())).ToList();
            foreach (var gone in old.Values.Where(s => !ids.Contains(s.Id.ToLowerInvariant())))
            {
                tombs.RemoveAll(t => t.Id.Equals(gone.Id, StringComparison.OrdinalIgnoreCase));
                tombs.Add(new SnippetTombstone { Id = gone.Id.ToLowerInvariant(), DeletedAt = now });
                changed = true;
            }
            Write(list, tombs);
        }
        if (changed) ChangedByUser?.Invoke();
    }

    /// <summary>Thêm hoặc sửa một snippet (theo Id).</summary>
    public void Upsert(Snippet snippet)
    {
        List<Snippet> list;
        lock (_lock)
        {
            list = Load();
            int i = list.FindIndex(s => s.Id.Equals(snippet.Id, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = snippet; else list.Add(snippet);
            Save(list);
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            var list = Load();
            if (list.RemoveAll(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0) Save(list);
        }
    }

    /// <summary>Ghi kết quả đồng bộ/khôi phục (giữ nguyên UpdatedAt và dấu xóa) rồi báo UI nạp lại.</summary>
    public void SaveSynced(IEnumerable<Snippet> snippets, IEnumerable<SnippetTombstone> tombstones)
    {
        lock (_lock) Write(snippets.ToList(), tombstones.ToList());
        ReplacedExternally?.Invoke();
    }

    /// <summary>Snippet hiển thị cho một VM: của VM đó trước, rồi dùng chung. VM không còn tồn tại thì chỉ còn mục dùng chung.</summary>
    public static List<Snippet> ForVm(IEnumerable<Snippet> all, string? vmId)
    {
        var own = string.IsNullOrWhiteSpace(vmId) ? new List<Snippet>() :
            all.Where(s => !s.IsGlobal && s.VmId!.Equals(vmId, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        own.AddRange(all.Where(s => s.IsGlobal).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase));
        return own;
    }
}
