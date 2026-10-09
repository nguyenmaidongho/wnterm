using System;
using System.Collections.Generic;

namespace WNTerm.Models;

public class AppSettings
{
    public string FontFamily { get; set; } = "JetBrains Mono";
    public int FontSize { get; set; } = 14;
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "Dark";
    public bool CopyOnSelect { get; set; } = true;
    public string RightClickAction { get; set; } = "Paste";
    public bool ConfirmMultilinePaste { get; set; } = true;
    public int Scrollback { get; set; } = 10000;
    public bool CursorBlink { get; set; } = true;
    public int KeepAliveSeconds { get; set; } = 5;
    public bool ShowHiddenFiles { get; set; } = true;
    public string CustomEditorPath { get; set; } = "";
    public List<string> CollapsedGroups { get; set; } = new();
    public string LastExportFolder { get; set; } = "";
    public string LastImportFolder { get; set; } = "";
    public int MaxParallelConnects { get; set; } = 4;

    // Cloud backup (S3 / S3-compatible: AWS, Cloudflare R2, MinIO, Wasabi...)
    public string S3Endpoint { get; set; } = "";
    public string S3Region { get; set; } = "us-east-1";
    public string S3Bucket { get; set; } = "";
    public string S3Prefix { get; set; } = "wnterm-backups/";
    public string S3AccessKey { get; set; } = "";
    public string? S3SecretKeyEnc { get; set; }
    public bool S3PathStyle { get; set; } = true;
    public string? CloudBackupPasswordEnc { get; set; }
    public bool CloudAutoBackup { get; set; } = true;

    /// <summary>Chu kỳ tự backup (phút): 0 = tắt. -1 = chưa đặt (suy ra từ CloudAutoBackup cũ: bật = 1 ngày).</summary>
    public int CloudBackupIntervalMinutes { get; set; } = -1;

    /// <summary>Hash của sessions.json ở lần backup gần nhất, để bỏ qua khi dữ liệu không đổi.</summary>
    public string? LastCloudBackupHash { get; set; }

    /// <summary>Tự backup: bỏ qua nếu dữ liệu VM không đổi so với lần backup trước.</summary>
    public bool CloudBackupOnlyIfChanged { get; set; } = true;

    public int EffectiveCloudIntervalMinutes =>
        CloudBackupIntervalMinutes >= 0 ? CloudBackupIntervalMinutes : (CloudAutoBackup ? 1440 : 0);
    public int CloudKeepCount { get; set; } = 30;
    public DateTime? LastCloudBackupUtc { get; set; }

    // Tài khoản WNTerm (đồng bộ VM mã hóa đầu-cuối)
    public string AccountApiUrl { get; set; } = "";
    public string? AccountEmail { get; set; }
    public string? AccountTokenEnc { get; set; }
    public string? AccountVaultKeyEnc { get; set; }
    public int AccountKdfIterations { get; set; }
    public int AccountVaultVersion { get; set; }
    public DateTime? AccountLastSyncUtc { get; set; }

    /// <summary>Tự đồng bộ khi mở app, sau mỗi thay đổi và định kỳ (khi đã đăng nhập).</summary>
    public bool AccountAutoSync { get; set; } = true;

    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public double LeftColumnWidth { get; set; } = 400;
}