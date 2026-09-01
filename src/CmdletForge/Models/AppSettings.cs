using CmdletForge.Theming;

namespace CmdletForge.Models;

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public EditorPalette Palette { get; set; } = EditorPalette.Forge;
    public bool WordWrap { get; set; }
    public bool CrtOverlay { get; set; } = true;
    public bool ScriptInspectorVisible { get; set; } = true;
    public double ScriptInspectorWidth { get; set; } = 310;
    public double FontSize { get; set; } = 14;
    public string PreferredPowerShell { get; set; } = "pwsh.exe";
    public List<string> RecentFiles { get; set; } = [];
}
