namespace CmdletForge.Models;

public enum ScriptParameterInputKind
{
    Text,
    Switch,
    Boolean,
    Array,
    Unsupported
}

public sealed record ScriptParameterDefinition(
    string Name,
    string TypeName,
    ScriptParameterInputKind InputKind,
    string? DefaultExpression,
    string? UnsupportedReason = null);
