using System;

namespace WNTerm.Models;

public enum SftpItemType
{
    ParentDirectory,
    Directory,
    DirectorySymlink,
    FileSymlink,
    File
}

public class SftpItem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsParentDirectory { get; set; }
    public bool IsSymbolicLink { get; set; }
    public string? LinkTarget { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Permissions { get; set; } = "";
    public int UserId { get; set; }
    public int GroupId { get; set; }

    public SftpItemType ItemType
    {
        get
        {
            if (IsParentDirectory || Name == "..") return SftpItemType.ParentDirectory;
            if (IsDirectory && IsSymbolicLink) return SftpItemType.DirectorySymlink;
            if (IsDirectory) return SftpItemType.Directory;
            if (IsSymbolicLink) return SftpItemType.FileSymlink;
            return SftpItemType.File;
        }
    }

    private string? _owner;
    public string Owner
    {
        get => (IsParentDirectory || Name == "..") ? "" : (!string.IsNullOrEmpty(_owner) ? _owner : (UserId == 0 ? "root" : UserId.ToString()));
        set => _owner = value;
    }

    private string? _group;
    public string Group
    {
        get => (IsParentDirectory || Name == "..") ? "" : (!string.IsNullOrEmpty(_group) ? _group : (GroupId == 0 ? "root" : GroupId.ToString()));
        set => _group = value;
    }

    public string Icon
    {
        get
        {
            if (IsParentDirectory || Name == "..")
            {
                return "⤴";
            }
            if (IsSymbolicLink)
            {
                return IsDirectory ? "📁🔗" : "📄🔗";
            }
            return IsDirectory ? "📁" : "📄";
        }
    }

    public string SizeFormatted
    {
        get
        {
            if (IsParentDirectory || Name == "..") return "";
            if (IsDirectory) return "";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    public string LastModifiedFormatted => (IsParentDirectory || Name == "..") ? "" : LastModified.ToString("yyyy-MM-dd HH:mm");
}
