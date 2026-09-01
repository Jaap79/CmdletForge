namespace CmdletForge.Models;

public sealed record ScriptFunctionInfo(
    string Name,
    string Kind,
    int StartOffset,
    int Line,
    int Column,
    int DefinitionCount)
{
    public string Location => $"R{Line}, T{Column}";

    public string Detail => DefinitionCount == 1
        ? Kind
        : $"{Kind} · {DefinitionCount} definities";
}

public sealed record ScriptInspection(
    IReadOnlyList<ScriptFunctionInfo> Functions,
    int LineCount,
    int CharacterCount,
    long ByteCount,
    string Sha256,
    bool UsesSavedFile);
