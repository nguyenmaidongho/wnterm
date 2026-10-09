using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WNTerm.Models;

namespace WNTerm.Services;

public class SessionFileEnvelope
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sessions")]
    public List<SessionInfo> Sessions { get; set; } = new();
}

public class SessionStore
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private string? _lastDailyBackupDate;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SessionStore(AppPaths? paths = null)
    {
        _paths = paths ?? AppPaths.Default;
        Trash = new TrashStore(_paths);
    }

    public List<SessionInfo> Load(out bool recoveredFromCorruption)
    {
        recoveredFromCorruption = false;
        lock (_lock)
        {
            if (!File.Exists(_paths.SessionsFile))
            {
                return new List<SessionInfo>();
            }

            try
            {
                string json = ReadAllTextRetry(_paths.SessionsFile);
                var envelope = JsonSerializer.Deserialize<SessionFileEnvelope>(json, JsonOptions);
                return envelope?.Sessions ?? new List<SessionInfo>();
            }
            catch (JsonException)
            {
                // Chỉ coi là hỏng khi nội dung sai định dạng (file đang bị khóa bởi antivirus/OneDrive không phải là hỏng).
                recoveredFromCorruption = true;
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string corruptPath = Path.Combine(_paths.DataDir, $"sessions.corrupt-{timestamp}.json");
                try
                {
                    File.Move(_paths.SessionsFile, corruptPath, true);
                }
                catch { }

                // Khôi phục từ bản sao lưu mới nhất (theo thời điểm ghi, gồm cả bản hằng ngày và bản trước khi import).
                var backups = Directory.GetFiles(_paths.BackupsDir, "sessions-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc);

                foreach (var backup in backups)
                {
                    try
                    {
                        var envelope = JsonSerializer.Deserialize<SessionFileEnvelope>(File.ReadAllText(backup), JsonOptions);
                        if (envelope?.Sessions != null)
                        {
                            Write(envelope.Sessions); // giữ nguyên UpdatedAt của bản sao lưu
                            return envelope.Sessions;
                        }
                    }
                    catch { }
                }

                return new List<SessionInfo>();
            }
        }
    }

    public void UpdateSession(SessionInfo updatedSession)
    {
        lock (_lock)
        {
            var sessions = Load(out _);
            int index = sessions.FindIndex(s => s.Id == updatedSession.Id);
            if (index >= 0)
            {
                sessions[index] = updatedSession;
            }
            else
            {
                sessions.Add(updatedSession);
            }
            Save(sessions);
        }
    }
    /// <summary>
    /// Lưu thay đổi của người dùng: tự đóng dấu UpdatedAt cho VM mới/sửa nội dung, và VM bị bỏ khỏi danh sách
    /// được chuyển vào thùng rác (khôi phục được) thay vì mất hẳn.
    /// </summary>
    public void Save(IEnumerable<SessionInfo> sessions)
    {
        bool changed;
        lock (_lock)
        {
            var list = sessions.ToList();
            var old = ReadFileQuiet();
            var now = DateTime.UtcNow;
            changed = false;

            foreach (var s in list)
            {
                if (!old.TryGetValue(s.Id, out var prev) || prev.ContentFingerprint != s.ContentFingerprint || s.UpdatedAt == null)
                {
                    if (prev == null || prev.ContentFingerprint != s.ContentFingerprint) { s.UpdatedAt = now; changed = true; }
                    else s.UpdatedAt = prev.UpdatedAt ?? prev.CreatedAt;
                }
            }

            var ids = list.Select(s => s.Id).ToHashSet();
            var removed = old.Values.Where(s => !ids.Contains(s.Id)).ToList();
            if (removed.Count > 0) { Trash.Add(removed, now); changed = true; }
            // VM vừa được thêm lại (khôi phục) thì bỏ khỏi thùng rác.
            var added = list.Where(s => !old.ContainsKey(s.Id)).Select(s => s.Id).ToList();
            if (added.Count > 0) Trash.Remove(added);

            Write(list);
        }
        if (changed) ChangedByUser?.Invoke();
    }

    /// <summary>Ghi danh sách do đồng bộ/khôi phục tạo ra (giữ nguyên UpdatedAt, không tự đẩy vào thùng rác) rồi báo UI nạp lại.</summary>
    public void SaveSynced(IEnumerable<SessionInfo> sessions)
    {
        lock (_lock) Write(sessions.ToList());
        ReplacedExternally?.Invoke();
    }

    /// <summary>Phát khi người dùng thêm/sửa/xóa VM (để tự đồng bộ).</summary>
    public event Action? ChangedByUser;

    /// <summary>Phát khi đồng bộ/khôi phục ghi đè danh sách VM — UI phải nạp lại ngay để không lưu đè danh sách cũ.</summary>
    public event Action? ReplacedExternally;

    public TrashStore Trash { get; }

    /// <summary>Khôi phục VM từ thùng rác vào danh sách.</summary>
    public int RestoreFromTrash(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        var entries = Trash.Items().Where(e => set.Contains(e.Id) && e.Session != null).ToList();
        if (entries.Count == 0) return 0;
        lock (_lock)
        {
            var list = Load(out _);
            foreach (var e in entries)
            {
                list.RemoveAll(s => s.Id == e.Id);
                e.Session!.UpdatedAt = null; // Save sẽ đóng dấu "bây giờ" → thắng dấu xóa trên các máy khác
                list.Add(e.Session);
            }
            Save(list);
        }
        ReplacedExternally?.Invoke();
        return entries.Count;
    }

    /// <summary>Đọc file, thử lại vài lần nếu đang bị tiến trình khác khóa tạm (antivirus, OneDrive...).</summary>
    private static string ReadAllTextRetry(string path)
    {
        for (int i = 0; ; i++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (i < 5) { System.Threading.Thread.Sleep(150); }
        }
    }

    private Dictionary<Guid, SessionInfo> ReadFileQuiet()
    {
        try
        {
            if (!File.Exists(_paths.SessionsFile)) return new();
            var env = JsonSerializer.Deserialize<SessionFileEnvelope>(File.ReadAllText(_paths.SessionsFile), JsonOptions);
            var d = new Dictionary<Guid, SessionInfo>();
            foreach (var s in env?.Sessions ?? new()) d[s.Id] = s;
            return d;
        }
        catch { return new(); }
    }

    private void Write(List<SessionInfo> sessions)
    {
        PerformDailyBackupIfNeeded();

        var envelope = new SessionFileEnvelope
        {
            Version = 1,
            Sessions = sessions
        };

        string json = JsonSerializer.Serialize(envelope, JsonOptions);
        string tempFile = _paths.SessionsFile + ".tmp";

        File.WriteAllText(tempFile, json);
        File.Move(tempFile, _paths.SessionsFile, true);
    }

    public string? BackupBeforeImport()
    {
        lock (_lock)
        {
            if (!File.Exists(_paths.SessionsFile)) return null;

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = Path.Combine(_paths.BackupsDir, $"sessions-before-import-{timestamp}.json");
            File.Copy(_paths.SessionsFile, backupPath, true);
            return backupPath;
        }
    }

    private void PerformDailyBackupIfNeeded()
    {
        string today = DateTime.Now.ToString("yyyyMMdd");
        if (_lastDailyBackupDate == today) return;

        if (File.Exists(_paths.SessionsFile))
        {
            string backupPath = Path.Combine(_paths.BackupsDir, $"sessions-{today}.json");
            if (!File.Exists(backupPath))
            {
                File.Copy(_paths.SessionsFile, backupPath, true);
            }
            _lastDailyBackupDate = today;

            CleanOldBackups();
        }
    }

    private void CleanOldBackups()
    {
        try
        {
            var dailyBackups = Directory.GetFiles(_paths.BackupsDir, "sessions-20*.json")
                .OrderByDescending(f => f)
                .ToList();

            foreach (var oldFile in dailyBackups.Skip(10))
            {
                try { File.Delete(oldFile); } catch { }
            }
        }
        catch { }
    }
}

