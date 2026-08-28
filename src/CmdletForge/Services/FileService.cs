using System.Text;

namespace CmdletForge.Services;

public sealed record TextFile(string Text, Encoding Encoding, string NewLine);

public static class FileService
{
    public static TextFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new TextFile(text, encoding, newLine);
    }

    public static void Write(string path, string text, Encoding encoding, string newLine)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", newLine, StringComparison.Ordinal);
        File.WriteAllText(path, normalized, encoding);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.GetPreamble()))
            return (new UTF8Encoding(true), Encoding.UTF8.GetPreamble().Length);
        if (bytes.StartsWith(Encoding.Unicode.GetPreamble()))
            return (Encoding.Unicode, Encoding.Unicode.GetPreamble().Length);
        if (bytes.StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
            return (Encoding.BigEndianUnicode, Encoding.BigEndianUnicode.GetPreamble().Length);
        return (new UTF8Encoding(false), 0);
    }
}
