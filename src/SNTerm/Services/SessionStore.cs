using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SNTerm.Models;

namespace SNTerm.Services;

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
                string json = File.ReadAllText(_paths.SessionsFile);
                var envelope = JsonSerializer.Deserialize<SessionFileEnvelope>(json, JsonOptions);
                return envelope?.Sessions ?? new List<SessionInfo>();
            }
            catch (Exception)
            {
                recoveredFromCorruption = true;
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string corruptPath = Path.Combine(_paths.DataDir, $"sessions.corrupt-{timestamp}.json");
                try
                {
                    File.Move(_paths.SessionsFile, corruptPath, true);
                }
                catch { }

                // Try restore from latest backup
                var latestBackup = Directory.GetFiles(_paths.BackupsDir, "sessions-*.json")
                    .OrderByDescending(f => f)
                    .FirstOrDefault();

                if (latestBackup != null)
                {
                    try
                    {
                        string json = File.ReadAllText(latestBackup);
                        var envelope = JsonSerializer.Deserialize<SessionFileEnvelope>(json, JsonOptions);
                        if (envelope?.Sessions != null)
                        {
                            Save(envelope.Sessions);
                            return envelope.Sessions;
                        }
                    }
                    catch { }
                }

                return new List<SessionInfo>();
            }
        }
    }

    public void Save(IEnumerable<SessionInfo> sessions)
    {
        lock (_lock)
        {
            PerformDailyBackupIfNeeded();

            var envelope = new SessionFileEnvelope
            {
                Version = 1,
                Sessions = sessions.ToList()
            };

            string json = JsonSerializer.Serialize(envelope, JsonOptions);
            string tempFile = _paths.SessionsFile + ".tmp";

            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _paths.SessionsFile, true);
        }
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
