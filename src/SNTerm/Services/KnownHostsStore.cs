using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using SNTerm.Models;

namespace SNTerm.Services;

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
            _hosts.Clear();
            if (!File.Exists(_paths.KnownHostsFile)) return;

            try
            {
                string json = File.ReadAllText(_paths.KnownHostsFile);
                var list = JsonSerializer.Deserialize<List<KnownHost>>(json, JsonOptions);
                if (list != null)
                {
                    foreach (var h in list)
                    {
                        _hosts[h.HostKey] = h;
                    }
                }
            }
            catch { }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
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
