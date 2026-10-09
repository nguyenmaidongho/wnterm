using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using WNTerm.Models;

namespace WNTerm.Services;

/// <summary>Một VM đã xóa. Session = null nghĩa là đã xóa vĩnh viễn, chỉ còn "dấu xóa" để đồng bộ sang máy khác.</summary>
public class TrashEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public DateTime DeletedAt { get; set; }
    public SessionInfo? Session { get; set; }

    /// <summary>Chỉ bỏ ở máy này (vd. khi "lấy bản máy chủ thay cho máy này") — không lan việc xóa sang máy khác.</summary>
    public bool LocalOnly { get; set; }

    public string DeletedAtText => DeletedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// Thùng rác VM (trash.json): xóa VM không mất ngay mà nằm ở đây <see cref="KeepDays"/> ngày để khôi phục.
/// Đồng thời giữ "dấu xóa" (tombstone) để đồng bộ: máy khác biết VM đã bị xóa và cũng chuyển vào thùng rác của nó.
/// </summary>
public class TrashStore
{
    public const int KeepDays = 30;          // giữ dữ liệu VM đã xóa
    public const int TombstoneDays = 180;    // giữ dấu xóa để đồng bộ

    private readonly AppPaths _paths;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TrashStore(AppPaths? paths = null) => _paths = paths ?? AppPaths.Default;

    public List<TrashEntry> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_paths.TrashFile)) return new();
                var list = JsonSerializer.Deserialize<List<TrashEntry>>(File.ReadAllText(_paths.TrashFile), JsonOptions) ?? new();
                return Purge(list);
            }
            catch { return new(); }
        }
    }

    public void Save(List<TrashEntry> entries)
    {
        lock (_lock)
        {
            string tmp = _paths.TrashFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Purge(entries), JsonOptions));
            File.Move(tmp, _paths.TrashFile, true);
        }
    }

    /// <summary>Các VM còn khôi phục được (mới xóa trước).</summary>
    public List<TrashEntry> Items() => Load().Where(e => e.Session != null).OrderByDescending(e => e.DeletedAt).ToList();

    public int Count => Load().Count(e => e.Session != null);

    public void Add(IEnumerable<SessionInfo> sessions, DateTime deletedAtUtc, bool localOnly = false)
    {
        lock (_lock)
        {
            var list = Load();
            foreach (var s in sessions)
            {
                list.RemoveAll(e => e.Id == s.Id);
                list.Add(new TrashEntry
                {
                    Id = s.Id, Name = s.DisplayName, Subtitle = s.Subtitle, DeletedAt = deletedAtUtc,
                    Session = s, LocalOnly = localOnly
                });
            }
            Save(list);
        }
    }

    /// <summary>Bỏ khỏi thùng rác (đã khôi phục, hoặc máy khác đã khôi phục/sửa lại VM này).</summary>
    public void Remove(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        if (set.Count == 0) return;
        lock (_lock)
        {
            var list = Load();
            if (list.RemoveAll(e => set.Contains(e.Id)) > 0) Save(list);
        }
    }

    /// <summary>Xóa vĩnh viễn dữ liệu VM (vẫn giữ dấu xóa để máy khác không đẩy VM này quay lại).</summary>
    public void Forget(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        lock (_lock)
        {
            var list = Load();
            foreach (var e in list.Where(e => set.Contains(e.Id))) e.Session = null;
            Save(list);
        }
    }

    private static List<TrashEntry> Purge(List<TrashEntry> list)
    {
        var now = DateTime.UtcNow;
        foreach (var e in list.Where(e => e.Session != null && (now - e.DeletedAt).TotalDays > KeepDays)) e.Session = null;
        list.RemoveAll(e => (now - e.DeletedAt).TotalDays > TombstoneDays || (e.Session == null && e.LocalOnly));
        return list;
    }
}
