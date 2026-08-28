using System.Windows.Media;

namespace CmdletForge.Theming;

public enum AppTheme
{
    Dark,
    Light
}

public enum EditorPalette
{
    Forge,
    Oceanic,
    HighContrast
}

public sealed record EditorColors(
    Color Background,
    Color Foreground,
    Color Selection,
    Color Keyword,
    Color Command,
    Color Variable,
    Color String,
    Color Comment,
    Color Number,
    Color Operator,
    Color Type,
    Color Error);
