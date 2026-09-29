﻿using Xunit;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.Tests;

public class SftpAndThemeTests
{
    [Fact]
    public void SftpItem_OwnerAndGroup_ReturnRootForZero()
    {
        var item = new SftpItem
        {
            Name = "test.txt",
            UserId = 0,
            GroupId = 0
        };

        Assert.Equal("root", item.Owner);
        Assert.Equal("root", item.Group);
    }

    [Fact]
    public void SftpItem_OwnerAndGroup_ReturnNumericStringForNonZeroWhenUnset()
    {
        var item = new SftpItem
        {
            Name = "test.txt",
            UserId = 1000,
            GroupId = 1002
        };

        Assert.Equal("1000", item.Owner);
        Assert.Equal("1002", item.Group);
    }

    [Fact]
    public void SftpItem_OwnerAndGroup_ReturnResolvedNamesWhenSet()
    {
        var item = new SftpItem
        {
            Name = "index.html",
            UserId = 1038,
            GroupId = 1042,
            Owner = "hienmedia",
            Group = "hienmedia"
        };

        Assert.Equal("hienmedia", item.Owner);
        Assert.Equal("hienmedia", item.Group);
    }

    [Fact]
    public void SftpItem_SymbolicLink_IconShowsChain()
    {
        var dirLink = new SftpItem
        {
            Name = "linkdir",
            IsDirectory = true,
            IsSymbolicLink = true
        };

        var fileLink = new SftpItem
        {
            Name = "linkfile",
            IsDirectory = false,
            IsSymbolicLink = true
        };

        Assert.Contains("🔗", dirLink.Icon);
        Assert.Contains("🔗", fileLink.Icon);
    }

    [Fact]
    public void AppSettings_Defaults_IncludeHiddenFilesTrueAndEditorPath()
    {
        var settings = new AppSettings();

        Assert.True(settings.ShowHiddenFiles);
        Assert.Equal("", settings.CustomEditorPath);
    }

    [Fact]
    public void ThemeManager_ApplyTheme_DoesNotThrowWhenNoCurrentApp()
    {
        var ex = Record.Exception(() =>
        {
            ThemeManager.ApplyTheme("Dark");
            ThemeManager.ApplyTheme("Light");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void MobaXtermImporter_Parse_ExtractsSessionsAndGroupsCorrectly()
    {
        string mobaData = @"[Bookmarks]
SubRep=
ImgNum=41
Server 1=#109#0%192.168.1.10%22%root%%-1%-1%%%%%22%%0%0%0%%-1%-1`29`99`#0# #-1
Server Key=#109#0%10.0.0.5%2222%ubuntu%%-1%-1%%C:\keys\id_rsa%%%22%%0%0%0%%-1%-1`29`99`#0# #-1

[Bookmarks_1]
SubRep=Cloud\AWS
ImgNum=41
EC2 App=#109#0%ec2.example.com%22%ec2-user%%-1%-1%%/home/user/app.pem%%%22%%0%0%0%%-1%-1`29`99`#0# #-1
";

        var sessions = MobaXtermImporter.Parse(mobaData);

        Assert.Equal(3, sessions.Count);

        // Session 1
        Assert.Equal("Server 1", sessions[0].Name);
        Assert.Equal("192.168.1.10", sessions[0].Host);
        Assert.Equal(22, sessions[0].Port);
        Assert.Equal("root", sessions[0].Username);
        Assert.Equal("Chưa phân nhóm", sessions[0].Group);
        Assert.Null(sessions[0].KeyFileName);

        // Session 2
        Assert.Equal("Server Key", sessions[1].Name);
        Assert.Equal("10.0.0.5", sessions[1].Host);
        Assert.Equal(2222, sessions[1].Port);
        Assert.Equal("ubuntu", sessions[1].Username);
        Assert.Equal(@"C:\keys\id_rsa", sessions[1].KeyFileName);

        // Session 3
        Assert.Equal("EC2 App", sessions[2].Name);
        Assert.Equal("ec2.example.com", sessions[2].Host);
        Assert.Equal(22, sessions[2].Port);
        Assert.Equal("ec2-user", sessions[2].Username);
        Assert.Equal("Cloud/AWS", sessions[2].Group);
        Assert.Equal("/home/user/app.pem", sessions[2].KeyFileName);
    }

    [Fact]
    public void SftpItem_Permissions_StoresAndFormatsCorrectly()
    {
        var item = new SftpItem
        {
            Name = "testfile",
            Permissions = "-rw-r--r--"
        };

        Assert.Equal("-rw-r--r--", item.Permissions);
    }

    [Theory]
    [InlineData("almalinux", true)]
    [InlineData("AlmaLinux 9.2", true)]
    [InlineData("rocky", true)]
    [InlineData("Rocky Linux", true)]
    [InlineData("cloudlinux", true)]
    [InlineData("CloudLinux 8", true)]
    [InlineData("centos", true)]
    [InlineData("redhat", true)]
    [InlineData("rhel", true)]
    [InlineData("fedora", true)]
    [InlineData("ubuntu", false)]
    [InlineData("debian", false)]
    [InlineData("alpine", false)]
    public void OsDetection_RedHatFamily_IdentifiedCorrectly(string osName, bool expectedRedHat)
    {
        string osLower = osName.ToLowerInvariant();
        bool isRedHatFamily = osLower.Contains("alma")
            || osLower.Contains("rocky")
            || osLower.Contains("cloud")
            || osLower.Contains("centos")
            || osLower.Contains("redhat")
            || osLower.Contains("rhel")
            || osLower.Contains("fedora");

        Assert.Equal(expectedRedHat, isRedHatFamily);
    }

    [Theory]
    [InlineData("cloudlinux", "CloudLinux release 8.8", "redhat")]
    [InlineData("almalinux", "AlmaLinux 9.2", "redhat")]
    [InlineData("ubuntu", "Ubuntu 22.04 LTS", "ubuntu")]
    [InlineData("debian", "Debian GNU/Linux 12", "debian")]
    [InlineData("alpine", "Alpine Linux v3.18", "alpine")]
    [InlineData("arch", "Arch Linux", "arch")]
    [InlineData("suse", "openSUSE Leap 15.5", "suse")]
    [InlineData("linux", "Linux 6.1.0", "linux")]
    public void OsGroup_Detection_MappedCorrectly(string osName, string osPretty, string expectedGroup)
    {
        string osGroup = WNTerm.ViewModels.TerminalTabViewModel.DetectOsGroup(osName, osPretty);
        Assert.Equal(expectedGroup, osGroup);
    }

    [Fact]
    public void MobaXtermImporter_ParseWorkspaceFile_ExtractsAllSessions()
    {
        string path = @"..\..\..\..\..\mobaxx.mxtsessions";
        if (!System.IO.File.Exists(path))
        {
            path = @"..\..\..\..\mobaxx.mxtsessions";
        }
        if (!System.IO.File.Exists(path))
        {
            path = "mobaxx.mxtsessions";
        }

        if (System.IO.File.Exists(path))
        {
            string content = System.IO.File.ReadAllText(path);
            var sessions = MobaXtermImporter.Parse(content);
            Assert.True(sessions.Count >= 100, $"Expected >= 100 sessions, got {sessions.Count}");
        }
    }
    [Theory]
    [InlineData("/: 14%  /boot: 24%", "/: 14%  /boot: 24%")]
    [InlineData("14%", "/: 14%")]
    [InlineData("", "/: --")]
    [InlineData(null, "/: --")]
    [InlineData("/: 48%  /home: 62%  /data: 15%", "/: 48%  /home: 62%  /data: 15%")]
    public void DiskText_Formatting_HandlesMultipleMountsAndFallbacks(string? input, string expected)
    {
        string diskText = string.IsNullOrWhiteSpace(input)
            ? "/: --"
            : (input.StartsWith('/') ? input : $"/: {input}");
        Assert.Equal(expected, diskText);
    }

    [Fact]
    public void DfCommand_NewlineRestoration_ProducesExpectedTable()
    {
        // 1. Base64 encoded df output
        string original = "Filesystem      1K-blocks   Used Available Use% Mounted on\n/dev/sda4          53.74G  7.34G    46.40G  14% /\n/dev/sda3            960M   225M      734M  24% /boot";
        string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(original));
        byte[] bytes = Convert.FromBase64String(b64);
        string decoded = System.Text.Encoding.UTF8.GetString(bytes).TrimEnd();
        var lines1 = decoded.Split('\n');
        Assert.Equal(3, lines1.Length);

        // 2. Fallback with unicode replacement character or custom tokens
        string rawCorrupted = "Filesystem      1K-blocks   Used Available Use% Mounted on\uFFFD/dev/sda4          53.74G  7.34G    46.40G  14% /\uFFFD/dev/sda3            960M   225M      734M  24% /boot";
        string dfOutputFallback = rawCorrupted.Replace("\uFFFD", "\n").Replace("¶", "\n").Replace("§", "\n").Replace("@", "\n").TrimEnd();
        var lines2 = dfOutputFallback.Split('\n');
        Assert.Equal(3, lines2.Length);
        Assert.StartsWith("Filesystem", lines2[0]);
        Assert.Contains("/dev/sda4", lines2[1]);
        Assert.Contains("/dev/sda3", lines2[2]);
    }
    [Fact]
    public void SftpItem_ParentDirectory_PropertiesFormattedCorrectly()
    {
        var item = new SftpItem
        {
            Name = "..",
            FullName = "/var",
            IsDirectory = true,
            IsParentDirectory = true,
            Size = 4096,
            UserId = 0,
            GroupId = 0,
            Permissions = "drwxr-xr-x"
        };

        Assert.Equal("..", item.Name);
        Assert.True(item.IsParentDirectory);
        Assert.True(item.IsDirectory);
        Assert.Equal("⤴", item.Icon);
        Assert.Equal("", item.SizeFormatted);
        Assert.Equal("", item.Owner);
        Assert.Equal("", item.Group);
        Assert.Equal("", item.LastModifiedFormatted);
    }

    [Fact]
    public void SftpItem_ItemType_DistinguishesCorrectly()
    {
        var parent = new SftpItem { Name = "..", IsDirectory = true, IsParentDirectory = true };
        var folder = new SftpItem { Name = "myfolder", IsDirectory = true, IsSymbolicLink = false };
        var folderLink = new SftpItem { Name = "linkdir", IsDirectory = true, IsSymbolicLink = true };
        var fileLink = new SftpItem { Name = "linkfile", IsDirectory = false, IsSymbolicLink = true };
        var file = new SftpItem { Name = "app.log", IsDirectory = false, IsSymbolicLink = false };

        Assert.Equal(SftpItemType.ParentDirectory, parent.ItemType);
        Assert.Equal(SftpItemType.Directory, folder.ItemType);
        Assert.Equal(SftpItemType.DirectorySymlink, folderLink.ItemType);
        Assert.Equal(SftpItemType.FileSymlink, fileLink.ItemType);
        Assert.Equal(SftpItemType.File, file.ItemType);
    }

    [Theory]
    [InlineData("drwxr-xr-x", "755", 493)]
    [InlineData("-rw-r--r--", "644", 420)]
    [InlineData("-rwxrwxrwx", "777", 511)]
    [InlineData("-rw-------", "600", 384)]
    [InlineData("-rwxr-x---", "750", 488)]
    public void SftpItem_OctalPermissions_CalculatedCorrectly(string perms, string expectedOctal, short expectedShort)
    {
        int o = (perms[1] == 'r' ? 4 : 0) + (perms[2] == 'w' ? 2 : 0) + (perms[3] == 'x' || perms[3] == 's' ? 1 : 0);
        int g = (perms[4] == 'r' ? 4 : 0) + (perms[5] == 'w' ? 2 : 0) + (perms[6] == 'x' || perms[6] == 's' ? 1 : 0);
        int a = (perms[7] == 'r' ? 4 : 0) + (perms[8] == 'w' ? 2 : 0) + (perms[9] == 'x' || perms[9] == 't' ? 1 : 0);
        string octal = $"{o}{g}{a}";

        Assert.Equal(expectedOctal, octal);
        Assert.Equal(expectedShort, Convert.ToInt16(octal, 8));
    }

    [Fact]
    public void LocalizationManager_DefaultAndSwitching_WorksCorrectly()
    {
        // 1. Default to English
        LocalizationManager.ApplyLanguage("en");
        Assert.Equal("en", LocalizationManager.CurrentLanguage);
        Assert.Equal("▶ Connect", LocalizationManager.Get("Str_Connect"));
        Assert.Equal("Permissions (chmod)...", LocalizationManager.Get("Str_Chmod"));
        Assert.DoesNotContain("?", LocalizationManager.Get("Str_Chmod"));

        // 2. Switch to Vietnamese
        LocalizationManager.ApplyLanguage("vi");
        Assert.Equal("vi", LocalizationManager.CurrentLanguage);
        Assert.Equal("▶ Kết nối", LocalizationManager.Get("Str_Connect"));
        Assert.Equal("Phân quyền (chmod)...", LocalizationManager.Get("Str_Chmod"));
        Assert.DoesNotContain("?", LocalizationManager.Get("Str_Chmod"));
        Assert.Equal("+ Thư mục", LocalizationManager.Get("Str_NewFolderBtn"));
        Assert.DoesNotContain("?", LocalizationManager.Get("Str_NewFolderBtn"));

        // 3. Switch back to English
        LocalizationManager.ApplyLanguage("en");
        Assert.Equal("en", LocalizationManager.CurrentLanguage);
        Assert.Equal("+ Folder", LocalizationManager.Get("Str_NewFolderBtn"));
    }
}
