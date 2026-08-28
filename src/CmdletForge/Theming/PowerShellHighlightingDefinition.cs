using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace CmdletForge.Theming;

public sealed class PowerShellHighlightingDefinition : IHighlightingDefinition
{
    private readonly Dictionary<string, HighlightingColor> _colors = new(StringComparer.OrdinalIgnoreCase);

    public PowerShellHighlightingDefinition(EditorColors colors)
    {
        MainRuleSet = new HighlightingRuleSet();
        AddColor("Keyword", colors.Keyword);
        AddColor("Command", colors.Command);
        AddColor("Variable", colors.Variable);
        AddColor("String", colors.String);
        AddColor("Comment", colors.Comment);
        AddColor("Number", colors.Number);
        AddColor("Operator", colors.Operator);
        AddColor("Type", colors.Type);

        MainRuleSet.Spans.Add(Span(@"<#", @"#>", "Comment", multiline: true));
        MainRuleSet.Spans.Add(Span(@"#", @"$", "Comment", multiline: false));
        MainRuleSet.Spans.Add(Span("'", "'", "String", multiline: false));
        MainRuleSet.Spans.Add(Span("\"", "\"", "String", multiline: false));

        AddRule(@"\b(begin|break|catch|class|continue|data|define|do|dynamicparam|else|elseif|end|enum|exit|filter|finally|for|foreach|from|function|hidden|if|in|param|process|return|static|switch|throw|trap|try|until|using|var|while|workflow)\b", "Keyword");
        AddRule(@"\b(Get|Set|New|Remove|Add|Clear|Copy|Move|Rename|Test|Start|Stop|Restart|Enable|Disable|Import|Export|Install|Update|Uninstall|Connect|Disconnect|Invoke|Write|Read|Convert|ConvertTo|ConvertFrom|Select|Where|ForEach|Measure|Compare|Out|Format)-[A-Za-z][A-Za-z0-9-]*\b", "Command");
        AddRule(@"\$(?:global:|script:|local:|private:|env:)?(?:[A-Za-z_][A-Za-z0-9_]*|\{[^}]+\}|\?|\^|\$)", "Variable");
        AddRule(@"\[[A-Za-z_][A-Za-z0-9_.]*(?:\[\])?\]", "Type");
        AddRule(@"(?<![\w])(?:0x[0-9A-Fa-f]+|\d+(?:\.\d+)?)\b", "Number");
        AddRule(@"-(?:eq|ne|gt|ge|lt|le|like|notlike|match|notmatch|contains|notcontains|in|notin|is|isnot|and|or|xor|not|band|bor|bxor|bnot|shl|shr)\b|[|&=+*/%!<>?-]", "Operator");
    }

    public string Name => "PowerShell";
    public HighlightingRuleSet MainRuleSet { get; }
    public IEnumerable<HighlightingColor> NamedHighlightingColors => _colors.Values;
    public IDictionary<string, string>? Properties => null;
    public HighlightingColor? GetNamedColor(string name) => _colors.GetValueOrDefault(name);
    public HighlightingRuleSet? GetNamedRuleSet(string name) => null;

    private void AddColor(string name, Color color) => _colors[name] = new HighlightingColor
    {
        Name = name,
        Foreground = new SimpleHighlightingBrush(color)
    };

    private void AddRule(string pattern, string color) => MainRuleSet.Rules.Add(new HighlightingRule
    {
        Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        Color = _colors[color]
    });

    private HighlightingSpan Span(string start, string end, string color, bool multiline) => new()
    {
        StartExpression = new Regex(Regex.Escape(start), RegexOptions.Compiled),
        EndExpression = new Regex(multiline ? Regex.Escape(end) : end, RegexOptions.Compiled),
        SpanColor = _colors[color],
        SpanColorIncludesStart = true,
        SpanColorIncludesEnd = true
    };
}
