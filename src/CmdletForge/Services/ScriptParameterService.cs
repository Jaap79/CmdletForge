using System.Management.Automation;
using System.Management.Automation.Language;
using System.Security;
using CmdletForge.Models;

namespace CmdletForge.Services;

public static class ScriptParameterService
{
    public static IReadOnlyList<ScriptParameterDefinition> Discover(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var ast = Parser.ParseInput(script, out _, out _);
        if (ast.ParamBlock is null)
            return [];

        return ast.ParamBlock.Parameters
            .Select(parameter => new ScriptParameterDefinition(
                parameter.Name.VariablePath.UserPath,
                DisplayType(parameter.StaticType),
                InputKind(parameter.StaticType),
                parameter.DefaultValue?.Extent.Text,
                UnsupportedReason(parameter.StaticType)))
            .ToArray();
    }

    private static ScriptParameterInputKind InputKind(Type type)
    {
        if (type == typeof(SecureString) || type == typeof(PSCredential))
            return ScriptParameterInputKind.Unsupported;
        if (type == typeof(SwitchParameter))
            return ScriptParameterInputKind.Switch;
        if (type == typeof(bool))
            return ScriptParameterInputKind.Boolean;
        if (type.IsArray)
            return ScriptParameterInputKind.Array;
        return ScriptParameterInputKind.Text;
    }

    private static string DisplayType(Type type)
    {
        if (type == typeof(object))
            return "object";
        if (type.IsArray)
            return $"{DisplayType(type.GetElementType() ?? typeof(object))}[]";
        return type.Name switch
        {
            "String" => "string",
            "Int32" => "int",
            "Int64" => "long",
            "Boolean" => "bool",
            "SwitchParameter" => "switch",
            _ => type.Name
        };
    }

    private static string? UnsupportedReason(Type type) => type == typeof(SecureString) || type == typeof(PSCredential)
        ? "Geheim- en credentialparameters worden in deze beta niet als tekst opgeslagen of doorgegeven. Vraag ze veilig op binnen het script."
        : null;
}
