using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CmdletForge.Theming;

public sealed class ThemeService
{
    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["Window"] = "#14171C", ["Surface"] = "#1B1E24", ["SurfaceAlt"] = "#252A32",
        ["Control"] = "#20242B", ["Text"] = "#F1F4F6", ["TextMuted"] = "#9EA7B3", ["Border"] = "#353C47"
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["Window"] = "#F3F5F8", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#E9EDF2",
        ["Control"] = "#F8FAFC", ["Text"] = "#17202A", ["TextMuted"] = "#5B6572", ["Border"] = "#C9D0D8"
    };

    private static readonly IReadOnlyDictionary<string, string> Shared = new Dictionary<string, string>
    {
        ["Accent"] = "#FF982E", ["AccentHover"] = "#FFAE58", ["Good"] = "#32C48D",
        ["Warning"] = "#FFB547", ["Danger"] = "#FF5D6C", ["Neutral"] = "#6F7B8A", ["OnAccent"] = "#15181D"
    };

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public EditorPalette CurrentPalette { get; private set; } = EditorPalette.Forge;
    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme theme, EditorPalette palette, Window? window = null)
    {
        CurrentTheme = theme;
        CurrentPalette = palette;
        var colors = theme == AppTheme.Dark ? Dark : Light;
        var resources = Application.Current.Resources;

        foreach (var (name, value) in colors.Concat(Shared))
            resources[$"{name}Brush"] = new SolidColorBrush(Parse(value));

        resources["CrtScanlineBrush"] = new SolidColorBrush(Parse(
            theme == AppTheme.Dark ? "#3817202A" : "#1417202A"));
        resources["CrtPhosphorBrush"] = new SolidColorBrush(Parse(
            theme == AppTheme.Dark ? "#16FF982E" : "#0AC35E00"));

        resources[SystemColors.WindowBrushKey] = resources["ControlBrush"];
        resources[SystemColors.WindowTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.ControlBrushKey] = resources["ControlBrush"];
        resources[SystemColors.ControlTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.MenuBrushKey] = resources["SurfaceBrush"];
        resources[SystemColors.MenuTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["SurfaceAltBrush"];
        resources[SystemColors.HighlightTextBrushKey] = resources["TextBrush"];

        if (window is not null)
            ApplyNativeChrome(window, theme);

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public EditorColors GetEditorColors()
    {
        var dark = CurrentTheme == AppTheme.Dark;
        return CurrentPalette switch
        {
            EditorPalette.Oceanic => dark
                ? Make("#101820", "#E6F3F5", "#294657", "#82AAFF", "#4FD6BE", "#89DDFF", "#C3E88D", "#637777", "#F78C6C", "#89DDFF", "#FFCB6B", "#FF5370")
                : Make("#F5FAFC", "#193549", "#CFE8F3", "#3659A9", "#007F72", "#007F9F", "#4B7100", "#70838F", "#B34D00", "#006D85", "#9A5B00", "#C6283D"),
            EditorPalette.HighContrast => dark
                ? Make("#0B0B0B", "#FFFFFF", "#4A4A00", "#FFFF00", "#00FFFF", "#00FFFF", "#7CFF7C", "#B8B8B8", "#FFB86C", "#FFFFFF", "#FFFF00", "#FF5D6C")
                : Make("#FFFFFF", "#000000", "#FFE88A", "#5B2C83", "#005B5B", "#005B5B", "#1B5E20", "#555555", "#8A3D00", "#000000", "#704800", "#B00020"),
            _ => dark
                ? Make("#14171C", "#F1F4F6", "#3D4654", "#FF982E", "#70D6FF", "#D9A7FF", "#A8E6A1", "#7F8996", "#FFD166", "#E5E9F0", "#70D6FF", "#FF5D6C")
                : Make("#FFFFFF", "#17202A", "#FFD6AD", "#C35E00", "#006A8A", "#7A3E9D", "#247A3D", "#687380", "#9A6200", "#263238", "#006A8A", "#C6283D")
        };
    }

    private static EditorColors Make(params string[] colors) => new(
        Parse(colors[0]), Parse(colors[1]), Parse(colors[2]), Parse(colors[3]), Parse(colors[4]), Parse(colors[5]),
        Parse(colors[6]), Parse(colors[7]), Parse(colors[8]), Parse(colors[9]), Parse(colors[10]), Parse(colors[11]));

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static void ApplyNativeChrome(Window window, AppTheme theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var dark = theme == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        SetColor(handle, 35, theme == AppTheme.Dark ? Parse("#14171C") : Parse("#FFFFFF"));
        SetColor(handle, 36, theme == AppTheme.Dark ? Parse("#F1F4F6") : Parse("#17202A"));
        SetColor(handle, 34, theme == AppTheme.Dark ? Parse("#353C47") : Parse("#C9D0D8"));
    }

    private static void SetColor(IntPtr handle, int attribute, Color color)
    {
        var colorRef = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
