using System.Management.Automation.Language;

namespace CmdletForge.Services;

public sealed record PowerShellFoldRegion(
    int StartOffset,
    int EndOffset,
    int StartLine,
    int EndLine,
    int HiddenLineCount,
    string DisplayText);

public static class PowerShellFoldingService
{
    public static IReadOnlyList<PowerShellFoldRegion> FindRegions(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Parser.ParseInput(script, out var tokens, out _);
        return FindRegions(tokens);
    }

    public static IReadOnlyList<PowerShellFoldRegion> FindRegions(IEnumerable<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var openings = new Stack<Token>();
        var regions = new List<PowerShellFoldRegion>();

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.LCurly)
            {
                openings.Push(token);
                continue;
            }

            if (token.Kind != TokenKind.RCurly || openings.Count == 0)
                continue;

            var opening = openings.Pop();
            var startOffset = opening.Extent.EndOffset;
            var endOffset = token.Extent.StartOffset;
            var hiddenLineCount = token.Extent.StartLineNumber - opening.Extent.EndLineNumber;
            if (endOffset <= startOffset || hiddenLineCount < 1)
                continue;

            var label = hiddenLineCount == 1
                ? " … 1 regel ingeklapt … "
                : $" … {hiddenLineCount} regels ingeklapt … ";
            regions.Add(new PowerShellFoldRegion(
                startOffset,
                endOffset,
                opening.Extent.StartLineNumber,
                token.Extent.EndLineNumber,
                hiddenLineCount,
                label));
        }

        return regions.OrderBy(region => region.StartOffset).ToArray();
    }
}
