using System.Text;

namespace CmdletForge.Services;

public enum AnsiColorMode
{
    Indexed,
    Rgb
}

public readonly record struct AnsiColor(AnsiColorMode Mode, int Value)
{
    public static AnsiColor FromIndex(int value) => new(AnsiColorMode.Indexed, Math.Clamp(value, 0, 255));
    public static AnsiColor FromRgb(int red, int green, int blue) => new(
        AnsiColorMode.Rgb,
        (Math.Clamp(red, 0, 255) << 16) | (Math.Clamp(green, 0, 255) << 8) | Math.Clamp(blue, 0, 255));
}

public readonly record struct AnsiTextStyle(
    AnsiColor? Foreground = null,
    AnsiColor? Background = null,
    bool Bold = false,
    bool Dim = false,
    bool Italic = false,
    bool Underline = false,
    bool Inverse = false,
    bool Strikethrough = false);

public sealed record AnsiTextSegment(string Text, AnsiTextStyle Style);

public sealed record AnsiParseResult(IReadOnlyList<AnsiTextSegment> Segments, AnsiTextStyle FinalStyle)
{
    public string PlainText => string.Concat(Segments.Select(segment => segment.Text));
}

public static class AnsiTextParser
{
    private const char Escape = '\u001b';
    private const char Csi = '\u009b';
    private const char Bell = '\u0007';

    public static AnsiParseResult Parse(string? text, AnsiTextStyle initialStyle = default)
    {
        if (string.IsNullOrEmpty(text))
            return new AnsiParseResult([], initialStyle);

        var segments = new List<AnsiTextSegment>();
        var buffer = new StringBuilder(text.Length);
        var style = initialStyle;

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            var value = buffer.ToString();
            buffer.Clear();
            if (segments.Count > 0 && segments[^1].Style == style)
                segments[^1] = segments[^1] with { Text = segments[^1].Text + value };
            else
                segments.Add(new AnsiTextSegment(value, style));
        }

        for (var index = 0; index < text.Length;)
        {
            var character = text[index];
            if (character == Escape)
            {
                Flush();
                if (index + 1 >= text.Length)
                {
                    index++;
                    continue;
                }

                if (text[index + 1] == '[' && TryReadCsi(text, index + 2, out var end, out var finalByte))
                {
                    if (finalByte == 'm')
                        style = ApplySgr(text.AsSpan(index + 2, end - index - 2), style);
                    index = end + 1;
                    continue;
                }

                if (text[index + 1] == ']')
                {
                    index = SkipOperatingSystemCommand(text, index + 2);
                    continue;
                }

                // Single-character escape commands are terminal controls, not text.
                index += Math.Min(2, text.Length - index);
                continue;
            }

            if (character == Csi)
            {
                Flush();
                if (TryReadCsi(text, index + 1, out var end, out var finalByte))
                {
                    if (finalByte == 'm')
                        style = ApplySgr(text.AsSpan(index + 1, end - index - 1), style);
                    index = end + 1;
                }
                else
                {
                    index++;
                }
                continue;
            }

            if (character == Bell || (char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            {
                index++;
                continue;
            }

            buffer.Append(character);
            index++;
        }

        Flush();
        return new AnsiParseResult(segments, style);
    }

    public static string ToPlainText(string? text) => Parse(text).PlainText;

    private static bool TryReadCsi(string text, int parametersStart, out int end, out char finalByte)
    {
        for (var index = parametersStart; index < text.Length; index++)
        {
            var character = text[index];
            if (character is >= '\x40' and <= '\x7e')
            {
                end = index;
                finalByte = character;
                return true;
            }
        }

        end = text.Length;
        finalByte = '\0';
        return false;
    }

    private static int SkipOperatingSystemCommand(string text, int contentStart)
    {
        for (var index = contentStart; index < text.Length; index++)
        {
            if (text[index] == Bell)
                return index + 1;
            if (text[index] == Escape && index + 1 < text.Length && text[index + 1] == '\\')
                return index + 2;
        }
        return text.Length;
    }

    private static AnsiTextStyle ApplySgr(ReadOnlySpan<char> parameterText, AnsiTextStyle style)
    {
        var raw = parameterText.ToString().Replace("::", ":", StringComparison.Ordinal).Replace(':', ';');
        var parts = raw.Length == 0 ? new[] { "0" } : raw.Split(';');
        var values = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
            values[index] = int.TryParse(parts[index], out var value) ? value : 0;

        for (var index = 0; index < values.Length; index++)
        {
            var code = values[index];
            switch (code)
            {
                case 0: style = default; break;
                case 1: style = style with { Bold = true }; break;
                case 2: style = style with { Dim = true }; break;
                case 3: style = style with { Italic = true }; break;
                case 4:
                case 21: style = style with { Underline = true }; break;
                case 7: style = style with { Inverse = true }; break;
                case 9: style = style with { Strikethrough = true }; break;
                case 22: style = style with { Bold = false, Dim = false }; break;
                case 23: style = style with { Italic = false }; break;
                case 24: style = style with { Underline = false }; break;
                case 27: style = style with { Inverse = false }; break;
                case 29: style = style with { Strikethrough = false }; break;
                case >= 30 and <= 37: style = style with { Foreground = AnsiColor.FromIndex(code - 30) }; break;
                case 39: style = style with { Foreground = null }; break;
                case >= 40 and <= 47: style = style with { Background = AnsiColor.FromIndex(code - 40) }; break;
                case 49: style = style with { Background = null }; break;
                case >= 90 and <= 97: style = style with { Foreground = AnsiColor.FromIndex(code - 90 + 8) }; break;
                case >= 100 and <= 107: style = style with { Background = AnsiColor.FromIndex(code - 100 + 8) }; break;
                case 38:
                    if (TryReadExtendedColor(values, ref index, out var foreground))
                        style = style with { Foreground = foreground };
                    break;
                case 48:
                    if (TryReadExtendedColor(values, ref index, out var background))
                        style = style with { Background = background };
                    break;
            }
        }

        return style;
    }

    private static bool TryReadExtendedColor(int[] values, ref int index, out AnsiColor color)
    {
        color = default;
        if (index + 2 < values.Length && values[index + 1] == 5)
        {
            color = AnsiColor.FromIndex(values[index + 2]);
            index += 2;
            return true;
        }
        if (index + 4 < values.Length && values[index + 1] == 2)
        {
            color = AnsiColor.FromRgb(values[index + 2], values[index + 3], values[index + 4]);
            index += 4;
            return true;
        }
        return false;
    }
}
