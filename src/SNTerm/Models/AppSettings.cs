using System.Collections.Generic;

namespace SNTerm.Models;

public class AppSettings
{
    public string FontFamily { get; set; } = "JetBrains Mono";
    public int FontSize { get; set; } = 14;
    public string Theme { get; set; } = "Dark";
    public bool CopyOnSelect { get; set; } = true;
    public string RightClickAction { get; set; } = "Paste";
    public bool ConfirmMultilinePaste { get; set; } = true;
    public int Scrollback { get; set; } = 10000;
    public bool CursorBlink { get; set; } = true;
    public int KeepAliveSeconds { get; set; } = 30;
    public bool ShowHiddenFiles { get; set; } = false;
    public List<string> CollapsedGroups { get; set; } = new();
    public string LastExportFolder { get; set; } = "";
    public string LastImportFolder { get; set; } = "";
    public int MaxParallelConnects { get; set; } = 4;

    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public double LeftColumnWidth { get; set; } = 280;
}
