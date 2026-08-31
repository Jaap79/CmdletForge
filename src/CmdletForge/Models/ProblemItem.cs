using System.Text.RegularExpressions;

namespace CmdletForge.Models;

public enum ProblemSource
{
    Syntax,
    Execution
}

public sealed record ProblemItem(
    ProblemSource Source,
    string Message,
    string Location,
    int? StartOffset = null,
    int Length = 0,
    int? Line = null)
{
    public string SourceLabel => Source == ProblemSource.Syntax ? "SYNTAX" : "UITVOER";
    public bool CanNavigate => Source == ProblemSource.Syntax && StartOffset.HasValue && Line.HasValue;

    public static ProblemItem FromSyntax(SyntaxDiagnostic diagnostic) => new(
        ProblemSource.Syntax,
        diagnostic.Message,
        diagnostic.Location,
        diagnostic.StartOffset,
        diagnostic.Length,
        diagnostic.Line);

    public static ProblemItem FromExecution(string message)
    {
        var cleaned = Regex.Replace(
            message.Trim(),
            @"^(?:.*[\\/])?(?:parameter-runner|run)-[0-9a-f]+\.ps1:\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new ProblemItem(ProblemSource.Execution, cleaned, "PowerShell");
    }
}
