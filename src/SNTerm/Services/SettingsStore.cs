using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using SNTerm.Models;

namespace SNTerm.Services;

public class SettingsStore
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SettingsStore(AppPaths? paths = null)
    {
        _paths = paths ?? AppPaths.Default;
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_paths.SettingsFile))
                return new AppSettings();

            try
            {
                string json = File.ReadAllText(_paths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                string tempFile = _paths.SettingsFile + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _paths.SettingsFile, true);
            }
            catch { }
        }
    }
}
