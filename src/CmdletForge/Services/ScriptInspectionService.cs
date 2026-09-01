using System.Security.Cryptography;
using System.Text;
using System.Management.Automation.Language;
using CmdletForge.Models;

namespace CmdletForge.Services;

public static class ScriptInspectionService
{
    public static ScriptInspection Inspect(
        string script,
        Encoding encoding,
        string newLine,
        string? filePath,
        bool isDirty,
        ScriptBlockAst? ast = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(encoding);

        var functions = ast is null ? DiscoverFunctions(script) : DiscoverFunctions(ast);
        var usesSavedFile = !isDirty && filePath is not null && File.Exists(filePath);
        byte[] bytes;
        try
        {
            bytes = usesSavedFile
                ? File.ReadAllBytes(filePath!)
                : EncodeForSave(script, encoding, newLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            usesSavedFile = false;
            bytes = EncodeForSave(script, encoding, newLine);
        }

        return new ScriptInspection(
            functions,
            CountLines(script),
            script.Length,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            usesSavedFile);
    }

    public static IReadOnlyList<ScriptFunctionInfo> DiscoverFunctions(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var ast = Parser.ParseInput(script, out _, out _);
        return DiscoverFunctions(ast);
    }

    public static IReadOnlyList<ScriptFunctionInfo> DiscoverFunctions(ScriptBlockAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        return ast.FindAll(node => node is FunctionDefinitionAst, searchNestedScriptBlocks: true)
            .Cast<FunctionDefinitionAst>()
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var definitions = group.OrderBy(definition => definition.Extent.StartOffset).ToArray();
                var first = definitions[0];
                var kind = first.IsWorkflow ? "workflow" : first.IsFilter ? "filter" : "function";
                return new ScriptFunctionInfo(
                    first.Name,
                    kind,
                    Math.Max(0, first.Extent.StartOffset),
                    Math.Max(1, first.Extent.StartLineNumber),
                    Math.Max(1, first.Extent.StartColumnNumber),
                    definitions.Length);
            })
            .OrderBy(function => function.StartOffset)
            .ToArray();
    }

    private static byte[] EncodeForSave(string script, Encoding encoding, string newLine)
    {
        var normalized = script.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", newLine, StringComparison.Ordinal);
        var content = encoding.GetBytes(normalized);
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0)
            return content;

        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static int CountLines(string script)
    {
        if (script.Length == 0)
            return 1;

        var count = 1;
        for (var index = 0; index < script.Length; index++)
        {
            if (script[index] == '\n')
                count++;
            else if (script[index] == '\r')
            {
                count++;
                if (index + 1 < script.Length && script[index + 1] == '\n')
                    index++;
            }
        }
        return count;
    }
}
