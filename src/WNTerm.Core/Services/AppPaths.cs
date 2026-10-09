using System;
using System.IO;

namespace WNTerm.Services;

public class AppPaths
{
    public static AppPaths Default { get; } = new();

    public string DataDir { get; }
    public string LocalDir { get; }

    public string SessionsFile => Path.Combine(DataDir, "sessions.json");
    public string SettingsFile => Path.Combine(DataDir, "settings.json");
    public string KnownHostsFile => Path.Combine(DataDir, "known_hosts.json");
    public string SnippetsFile => Path.Combine(DataDir, "snippets.json");
    public string TrashFile => Path.Combine(DataDir, "trash.json");
    public string KeysDir => Path.Combine(DataDir, "keys");
    public string BackupsDir => Path.Combine(DataDir, "backups");
    public string LogsDir => Path.Combine(LocalDir, "logs");
    public string WebView2Dir => Path.Combine(LocalDir, "WebView2");

    public AppPaths(string? dataDir = null, string? localDir = null)
    {
        DataDir = dataDir ?? Environment.GetEnvironmentVariable("WNTERM_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WNTerm");
        LocalDir = localDir ?? Environment.GetEnvironmentVariable("WNTERM_LOCAL_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WNTerm");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(KeysDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
