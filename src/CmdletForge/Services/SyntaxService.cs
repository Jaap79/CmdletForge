using System.Management.Automation.Language;
using CmdletForge.Models;

namespace CmdletForge.Services;

public sealed record PowerShellParseResult(
    ScriptBlockAst Ast,
    IReadOnlyList<Token> Tokens,
    IReadOnlyList<SyntaxDiagnostic> Diagnostics);

public static class SyntaxService
{
    public static IReadOnlyList<SyntaxDiagnostic> Analyze(string script)
        => Parse(script).Diagnostics;

    public static PowerShellParseResult Parse(string script)
    {
        var ast = Parser.ParseInput(script, out var tokens, out var errors);
        var diagnostics = errors
            .Select(error => new SyntaxDiagnostic(
                DiagnosticSeverity.Error,
                error.Message,
                Math.Max(0, error.Extent.StartOffset),
                Math.Max(1, error.Extent.EndOffset - error.Extent.StartOffset),
                Math.Max(1, error.Extent.StartLineNumber),
                Math.Max(1, error.Extent.StartColumnNumber)))
            .ToArray();
        return new PowerShellParseResult(ast, tokens, diagnostics);
    }
}
