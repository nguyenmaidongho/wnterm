using System;

namespace SNTerm.Models;

public class SftpItem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Permissions { get; set; } = "";

    public string Icon => IsDirectory ? "📁" : "📄";

    public string SizeFormatted
    {
        get
        {
            if (IsDirectory) return "";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    public string LastModifiedFormatted => LastModified.ToString("yyyy-MM-dd HH:mm");
}
