using System.Text;

namespace CmdletForge.Models;

public sealed class DocumentState
{
    public string? FilePath { get; set; }
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    public string NewLine { get; set; } = Environment.NewLine;
    public bool IsDirty { get; set; }
    public string DisplayName => FilePath is null ? "Naamloos.ps1" : Path.GetFileName(FilePath);
}
