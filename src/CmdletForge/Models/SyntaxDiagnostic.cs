namespace CmdletForge.Models;

public enum DiagnosticSeverity
{
    Error,
    Warning
}

public sealed record SyntaxDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int StartOffset,
    int Length,
    int Line,
    int Column)
{
    public string Location => $"R{Line}, T{Column}";
}
