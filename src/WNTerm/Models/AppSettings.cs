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
    public int CloudKeepCount { get; set; } = 30;
    public DateTime? LastCloudBackupUtc { get; set; }

    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public double LeftColumnWidth { get; set; } = 400;
}