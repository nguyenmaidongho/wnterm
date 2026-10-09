using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using WNTerm.Models;

namespace WNTerm.Services;

public enum HostKeyStatus
{
    Trusted,
    NewHost,
    Changed
}

public class KnownHostsStore
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private readonly Dictionary<string, KnownHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public KnownHostsStore(AppPaths? paths = null)
    {
        _paths = paths ?? AppPaths.Default;
        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            var list = ReadFile();
            // File hỏng/đang ghi dở → giữ nguyên dữ liệu trong bộ nhớ thay vì xóa sạch.
            if (list.Count == 0 && _hosts.Count > 0 && File.Exists(_paths.KnownHostsFile)) return;

            _hosts.Clear();
            foreach (var h in list)
            {
                if (!string.IsNullOrEmpty(h.HostKey)) _hosts[h.HostKey] = h;
            }
        }
    }

    private List<KnownHost> ReadFile()
    {
        try
        {
            if (!File.Exists(_paths.KnownHostsFile)) return new List<KnownHost>();
            string json = File.ReadAllText(_paths.KnownHostsFile);
            return JsonSerializer.Deserialize<List<KnownHost>>(json, JsonOptions) ?? new List<KnownHost>();
        }
        catch
        {
            return new List<KnownHost>();
        }
    }

    /// <summary>
    /// Ghi ra đĩa. Trước khi ghi, gộp các host đang có trên đĩa mà bộ nhớ chưa có (do một instance
    /// KnownHostsStore khác — vd. nút "Kiểm tra kết nối" — vừa thêm) để các instance không xóa của nhau.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                foreach (var h in ReadFile())
                {
                    if (!string.IsNullOrEmpty(h.HostKey) && !_hosts.ContainsKey(h.HostKey))
                        _hosts[h.HostKey] = h;
                }

                string json = JsonSerializer.Serialize(_hosts.Values.ToList(), JsonOptions);
                string tmp = _paths.KnownHostsFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _paths.KnownHostsFile, true);
            }
            catch { }
        }
    }

    public HostKeyStatus CheckHost(string host, int port, string fingerprintSha256)
    {
        string key = $"{host.Trim()}:{port}";
        lock (_lock)
        {
            // Chưa có trong bộ nhớ: có thể instance khác vừa lưu → nạp lại từ đĩa rồi xem lại.
            if (!_hosts.ContainsKey(key)) Load();

            if (_hosts.TryGetValue(key, out var existing))
            {
                return string.Equals(existing.FingerprintSha256, fingerprintSha256, StringComparison.OrdinalIgnoreCase)
                    ? HostKeyStatus.Trusted
                    : HostKeyStatus.Changed;
            }

            return HostKeyStatus.NewHost;
        }
    }

    public void AddOrUpdate(string host, int port, string algorithm, string fingerprintSha256)
    {
        string key = $"{host.Trim()}:{port}";
        lock (_lock)
        {
            // Nạp lại từ đĩa trước khi sửa: lấy các thay đổi mới nhất của instance khác.
            Load();
            _hosts[key] = new KnownHost
            {
                HostKey = key,
                Algorithm = algorithm,
                FingerprintSha256 = fingerprintSha256,
                AddedAt = DateTime.UtcNow
            };
            Save();
        }
    }
}
