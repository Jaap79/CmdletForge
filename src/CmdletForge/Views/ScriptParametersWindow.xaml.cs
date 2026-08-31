using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CmdletForge.Models;
using CmdletForge.Theming;

namespace CmdletForge.Views;

public partial class ScriptParametersWindow : Window
{
    private readonly ObservableCollection<ScriptParameterInput> _inputs;
    private readonly ThemeService _themeService;

    public IReadOnlyDictionary<string, object?> Parameters { get; private set; } = new Dictionary<string, object?>();

    public ScriptParametersWindow(
        IReadOnlyList<ScriptParameterDefinition> definitions,
        ThemeService themeService,
        string documentName)
    {
        InitializeComponent();
        _themeService = themeService;
        _inputs = new ObservableCollection<ScriptParameterInput>(definitions.Select(definition => new ScriptParameterInput(definition)));
        ParameterList.ItemsSource = _inputs;
        DocumentText.Text = documentName;
        SourceInitialized += (_, _) => _themeService.Apply(_themeService.CurrentTheme, _themeService.CurrentPalette, this);
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in _inputs.Where(input => input.Include && !input.ShowUnsupported))
            values[input.Name] = input.Value();

        Parameters = values;
        DialogResult = true;
    }
}

public sealed class ScriptParameterInput : INotifyPropertyChanged
{
    private bool _include;
    private string _textValue = string.Empty;
    private bool _booleanValue;

    public ScriptParameterInput(ScriptParameterDefinition definition)
    {
        Definition = definition;
        _include = false;
    }

    public ScriptParameterDefinition Definition { get; }
    public string Name => Definition.Name;
    public string TypeName => Definition.TypeName;
    public string? UnsupportedReason => Definition.UnsupportedReason;
    public string DefaultSummary => string.IsNullOrWhiteSpace(Definition.DefaultExpression) ? string.Empty : $"default: {Definition.DefaultExpression}";
    public bool CanToggleInclude => !ShowUnsupported;
    public bool ShowText => Definition.InputKind == ScriptParameterInputKind.Text;
    public bool ShowSwitch => Definition.InputKind == ScriptParameterInputKind.Switch;
    public bool ShowBoolean => Definition.InputKind == ScriptParameterInputKind.Boolean;
    public bool ShowArray => Definition.InputKind == ScriptParameterInputKind.Array;
    public bool ShowUnsupported => Definition.InputKind == ScriptParameterInputKind.Unsupported;

    public bool Include
    {
        get => _include;
        set => Set(ref _include, value);
    }

    public string TextValue
    {
        get => _textValue;
        set => Set(ref _textValue, value);
    }

    public bool BooleanValue
    {
        get => _booleanValue;
        set => Set(ref _booleanValue, value);
    }

    public object Value() => Definition.InputKind switch
    {
        ScriptParameterInputKind.Switch => true,
        ScriptParameterInputKind.Boolean => BooleanValue,
        ScriptParameterInputKind.Array => TextValue.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        _ => TextValue
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
