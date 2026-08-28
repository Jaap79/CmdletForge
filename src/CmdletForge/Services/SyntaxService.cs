using System.Management.Automation.Language;
using CmdletForge.Models;

namespace CmdletForge.Services;

public static class SyntaxService
{
    public static IReadOnlyList<SyntaxDiagnostic> Analyze(string script)
    {
        Parser.ParseInput(script, out _, out var errors);
        return errors
            .Select(error => new SyntaxDiagnostic(
                DiagnosticSeverity.Error,
                error.Message,
                Math.Max(0, error.Extent.StartOffset),
                Math.Max(1, error.Extent.EndOffset - error.Extent.StartOffset),
                Math.Max(1, error.Extent.StartLineNumber),
                Math.Max(1, error.Extent.StartColumnNumber)))
            .ToArray();
    }
}
