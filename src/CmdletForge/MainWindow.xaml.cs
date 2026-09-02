using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CmdletForge.Models;
using CmdletForge.Services;
using CmdletForge.Theming;
using CmdletForge.Views;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using System.Management.Automation.Language;
using Microsoft.Win32;

namespace CmdletForge;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly ThemeService _themeService = new();
    private readonly TerminalSession _terminal = new();
    private readonly DispatcherTimer _syntaxTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DocumentState _document = new();
    private readonly FoldingManager _foldingManager;
    private IReadOnlyList<SyntaxDiagnostic> _syntaxDiagnostics = [];
    private readonly List<ProblemItem> _executionProblems = [];
    private AppSettings _settings;
    private bool _suppressDirty;
    private bool _collapseNewFoldings = true;
    private double _lastInspectorWidth = 310;
    private readonly List<TerminalEntry> _terminalHistory = [];
    private AnsiTextStyle _terminalOutputStyle;
    private AnsiTextStyle _terminalErrorStyle;

    private static readonly string[] AnsiBrushKeys =
    [
        "AnsiBlackBrush", "AnsiRedBrush", "AnsiGreenBrush", "AnsiYellowBrush",
        "AnsiBlueBrush", "AnsiMagentaBrush", "AnsiCyanBrush", "AnsiWhiteBrush",
        "AnsiBrightBlackBrush", "AnsiBrightRedBrush", "AnsiBrightGreenBrush", "AnsiBrightYellowBrush",
        "AnsiBrightBlueBrush", "AnsiBrightMagentaBrush", "AnsiBrightCyanBrush", "AnsiBrightWhiteBrush"
    ];

    public MainWindow()
    {
        InitializeComponent();
        _foldingManager = FoldingManager.Install(Editor.TextArea);
        ConfigureEditorMargins();
        _settings = _settingsService.Load();

        _syntaxTimer.Tick += (_, _) =>
        {
            _syntaxTimer.Stop();
            RefreshAnalysis();
        };
        _terminal.MessageReceived += Terminal_MessageReceived;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();

        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => ApplyTheme();
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        ConfigureFromSettings();
        SetDocumentText(DefaultScript());
        UpdateWindowTitle();
    }

    public void OpenFromCommandLine(string path) => OpenDocument(path);

    public void ApplyUpdateAndExit(StagedUpdate update)
    {
        if (!ConfirmDiscardOrSave())
            return;
        if (MessageBox.Show(this,
                $"Cmdlet Forge wordt afgesloten en vervangen door versie {update.Info.LatestVersion}. Doorgaan?",
                "Update installeren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        AppUpdateService.ApplyAfterExit(update);
        _document.IsDirty = false;
        Application.Current.Shutdown();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme();
        try
        {
            await _terminal.StartAsync(_settings.PreferredPowerShell);
            var version = await SystemUpdateService.GetPowerShellVersionAsync(_settings.PreferredPowerShell);
            PowerShellStatus.Text = $"PowerShell {version}";
        }
        catch (Exception ex)
        {
            AppLog.Error("PowerShell-terminal kon niet starten.", ex);
            AppendTerminal(new TerminalMessage(TerminalStream.Error, $"Terminal kon niet starten: {ex.Message}"));
            PowerShellStatus.Text = "PowerShell niet beschikbaar";
        }
    }

    private void ConfigureFromSettings()
    {
        ThemeCombo.SelectedIndex = _settings.Theme == AppTheme.Dark ? 0 : 1;
        PaletteCombo.SelectedIndex = (int)_settings.Palette;
        DarkModeMenu.IsChecked = _settings.Theme == AppTheme.Dark;
        WordWrapMenu.IsChecked = _settings.WordWrap;
        CrtMenu.IsChecked = _settings.CrtOverlay;
        _lastInspectorWidth = Math.Clamp(_settings.ScriptInspectorWidth, 220, 560);
        SetInspectorVisibility(_settings.ScriptInspectorVisible);
        Editor.WordWrap = _settings.WordWrap;
        Editor.FontSize = Math.Clamp(_settings.FontSize, 10, 28);
        CrtOverlay.IsActive = _settings.CrtOverlay;
        RefreshRecentFilesMenu();
    }

    private void ApplyTheme()
    {
        _themeService.Apply(_settings.Theme, _settings.Palette, this);
        var colors = _themeService.GetEditorColors();
        Editor.Background = new SolidColorBrush(colors.Background);
        Editor.Foreground = new SolidColorBrush(colors.Foreground);
        Editor.TextArea.SelectionBrush = new SolidColorBrush(colors.Selection);
        Editor.TextArea.SelectionForeground = new SolidColorBrush(colors.Foreground);
        Editor.LineNumbersForeground = (Brush)FindResource("TextMutedBrush");
        Editor.SyntaxHighlighting = new PowerShellHighlightingDefinition(colors);
        FoldingMargin.SetFoldingMarkerBrush(Editor.TextArea, (Brush)FindResource("TextMutedBrush"));
        FoldingMargin.SetFoldingMarkerBackgroundBrush(Editor.TextArea, (Brush)FindResource("SurfaceAltBrush"));
        FoldingMargin.SetSelectedFoldingMarkerBrush(Editor.TextArea, (Brush)FindResource("OnAccentBrush"));
        FoldingMargin.SetSelectedFoldingMarkerBackgroundBrush(Editor.TextArea, (Brush)FindResource("AccentBrush"));
        RenderTerminalHistory();
    }

    private void ConfigureEditorMargins()
    {
        foreach (var lineNumbers in Editor.TextArea.LeftMargins.OfType<LineNumberMargin>())
            lineNumbers.Margin = new Thickness(5, 0, 5, 0);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardOrSave())
        {
            e.Cancel = true;
            return;
        }

        _settings.FontSize = Editor.FontSize;
        if (InspectorPanel.Visibility == Visibility.Visible && InspectorColumn.ActualWidth >= 220)
            _lastInspectorWidth = InspectorColumn.ActualWidth;
        _settings.ScriptInspectorVisible = InspectorPanel.Visibility == Visibility.Visible;
        _settings.ScriptInspectorWidth = _lastInspectorWidth;
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            AppLog.Error("Instellingen konden niet worden opgeslagen.", ex);
        }

        _terminal.Dispose();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl && e.Key == Key.N) { NewFile(); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { OpenFile(); e.Handled = true; }
        else if (ctrl && !shift && e.Key == Key.S) { SaveDocument(); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.S) { SaveDocumentAs(); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { ShowSearch(false); e.Handled = true; }
        else if (ctrl && e.Key == Key.H) { ShowSearch(true); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.I) { SetInspectorVisibility(InspectorPanel.Visibility != Visibility.Visible); e.Handled = true; }
        else if (ctrl && e.Key == Key.G) { GoToLineBox.Focus(); GoToLineBox.SelectAll(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Enter) { RunSelection(); e.Handled = true; }
        else if (ctrl && !shift && e.Key == Key.F5) { RunScriptWithParameters(); e.Handled = true; }
        else if (e.Key == Key.F5 && shift) { _ = RestartTerminalAsync(); e.Handled = true; }
        else if (e.Key == Key.F5) { RunScript(); e.Handled = true; }
        else if (e.Key == Key.F3) { FindNext(shift); e.Handled = true; }
        else if (e.Key == Key.Escape && SearchPanel.Visibility == Visibility.Visible) { SearchPanel.Visibility = Visibility.Collapsed; Editor.Focus(); e.Handled = true; }
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Add && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Editor.FontSize = Math.Min(28, Editor.FontSize + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Subtract && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Editor.FontSize = Math.Max(10, Editor.FontSize - 1);
            e.Handled = true;
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (!_suppressDirty)
            _document.IsDirty = true;
        UpdateWindowTitle();
        _syntaxTimer.Stop();
        _syntaxTimer.Start();
    }

    private void RefreshAnalysis()
    {
        var analysis = SyntaxService.Parse(Editor.Text);
        RefreshDiagnostics(analysis.Diagnostics);
        RefreshFoldings(analysis.Tokens);
        if (InspectorPanel.Visibility == Visibility.Visible)
            RefreshInspection(analysis.Ast);
    }

    private void RefreshDiagnostics(IReadOnlyList<SyntaxDiagnostic> diagnostics)
    {
        _syntaxDiagnostics = diagnostics;
        RefreshProblems();
        SyntaxStatus.Text = _syntaxDiagnostics.Count == 0 ? "Syntax: in orde" : $"Syntax: {_syntaxDiagnostics.Count} fout(en)";
        SyntaxStatus.Foreground = _syntaxDiagnostics.Count == 0
            ? (Brush)FindResource("GoodBrush")
            : (Brush)FindResource("DangerBrush");
    }

    private void RefreshProblems()
    {
        var problems = _syntaxDiagnostics
            .Select(ProblemItem.FromSyntax)
            .Concat(_executionProblems)
            .ToArray();
        ProblemsList.ItemsSource = problems;
        ProblemsTab.Header = problems.Length == 0 ? "PROBLEMEN" : $"PROBLEMEN ({problems.Length})";
    }

    private void BeginExecution()
    {
        _executionProblems.Clear();
        RefreshProblems();
        BottomTabs.SelectedIndex = 0;
    }

    private void AddExecutionProblem(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        var problem = ProblemItem.FromExecution(message);
        if (string.IsNullOrWhiteSpace(problem.Message))
            return;
        if (_executionProblems.LastOrDefault()?.Message == problem.Message)
            return;
        _executionProblems.Add(problem);
        if (_executionProblems.Count > 200)
            _executionProblems.RemoveAt(0);
        RefreshProblems();
        BottomTabs.SelectedItem = ProblemsTab;
    }

    private void RefreshFoldings(IReadOnlyList<Token> tokens)
    {
        var collapseNewFoldings = _collapseNewFoldings;
        var foldings = PowerShellFoldingService.FindRegions(tokens)
            .Select(region => new NewFolding(region.StartOffset, region.EndOffset)
            {
                Name = region.DisplayText,
                DefaultClosed = collapseNewFoldings
            });
        _foldingManager.UpdateFoldings(foldings, -1);
        if (collapseNewFoldings)
        {
            foreach (var folding in _foldingManager.AllFoldings)
                folding.IsFolded = true;
        }
        _collapseNewFoldings = false;
    }

    private void RefreshInspection(ScriptBlockAst? ast = null)
    {
        var inspection = ScriptInspectionService.Inspect(
            Editor.Text,
            _document.Encoding,
            _document.NewLine,
            _document.FilePath,
            _document.IsDirty,
            ast);

        FunctionsList.ItemsSource = inspection.Functions;
        FunctionsHeader.Text = inspection.Functions.Count == 0
            ? "FUNCTIES"
            : $"FUNCTIES ({inspection.Functions.Count})";

        InspectorFilePath.Text = _document.FilePath ?? "Nog niet opgeslagen";
        InspectorFilePath.ToolTip = _document.FilePath;
        InspectorCounts.Text = $"{inspection.LineCount:N0} regels · {inspection.CharacterCount:N0} tekens\n{inspection.ByteCount:N0} bytes";
        InspectorEncoding.Text = _document.Encoding.GetPreamble().Length == 0
            ? $"{_document.Encoding.WebName} · geen BOM"
            : $"{_document.Encoding.WebName} · BOM";
        InspectorHash.Text = inspection.Sha256;
        InspectorHash.ToolTip = inspection.Sha256;
        InspectorHashScope.Text = inspection.UsesSavedFile
            ? "Hash van het opgeslagen bestand op schijf."
            : "Hash van de actuele editorinhoud zoals deze zou worden opgeslagen.";
        UpdateInspectorSaveState();
    }

    private void UpdateInspectorSaveState()
    {
        if (_document.IsDirty)
        {
            InspectorSaveState.Text = "Niet opgeslagen sinds laatste wijziging";
            InspectorSaveStateMarker.Background = (Brush)FindResource("WarningBrush");
        }
        else if (_document.FilePath is null)
        {
            InspectorSaveState.Text = "Nieuw, nog niet opgeslagen";
            InspectorSaveStateMarker.Background = (Brush)FindResource("NeutralBrush");
        }
        else
        {
            InspectorSaveState.Text = "Opgeslagen";
            InspectorSaveStateMarker.Background = (Brush)FindResource("GoodBrush");
        }
    }

    private void SetInspectorVisibility(bool visible)
    {
        if (!visible && InspectorColumn.ActualWidth >= 220)
            _lastInspectorWidth = InspectorColumn.ActualWidth;

        ScriptInspectorMenu.IsChecked = visible;
        InspectorPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        InspectorSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        InspectorSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        InspectorColumn.MinWidth = visible ? 220 : 0;
        InspectorColumn.Width = visible
            ? new GridLength(Math.Clamp(_lastInspectorWidth, 220, 560))
            : new GridLength(0);
        _settings.ScriptInspectorVisible = visible;

        if (visible && IsInitialized)
            RefreshInspection();
    }

    private void SetAllFoldings(bool isFolded)
    {
        foreach (var folding in _foldingManager.AllFoldings)
            folding.IsFolded = isFolded;
        Editor.Focus();
    }

    private void ExpandFoldingsContaining(int offset)
    {
        foreach (var folding in _foldingManager.GetFoldingsContaining(offset).ToArray())
            folding.IsFolded = false;
    }

    private void ProblemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is not ProblemItem problem || !problem.CanNavigate)
            return;
        var offset = Math.Clamp(problem.StartOffset!.Value, 0, Editor.Document.TextLength);
        var length = Math.Clamp(problem.Length, 0, Editor.Document.TextLength - offset);
        ExpandFoldingsContaining(offset);
        Editor.Select(offset, length);
        Editor.CaretOffset = offset;
        Editor.ScrollToLine(problem.Line!.Value);
        Editor.Focus();
    }

    private void UpdateCaretStatus()
    {
        var line = Editor.TextArea.Caret.Line;
        var column = Editor.TextArea.Caret.Column;
        CaretStatus.Text = $"Regel {line}, teken {column}";
        GoToLineBox.Text = line.ToString();
        GoToColumnBox.Text = column.ToString();
    }

    private void NewFile()
    {
        if (!ConfirmDiscardOrSave())
            return;
        _document.FilePath = null;
        _document.Encoding = new UTF8Encoding(false);
        _document.NewLine = Environment.NewLine;
        SetDocumentText(DefaultScript());
        UpdateWindowTitle();
    }

    private void OpenFile()
    {
        if (!ConfirmDiscardOrSave())
            return;
        var dialog = new OpenFileDialog
        {
            Title = "PowerShell-script openen",
            Filter = "PowerShell-bestanden (*.ps1;*.psm1;*.psd1)|*.ps1;*.psm1;*.psd1|Alle bestanden (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            OpenDocument(dialog.FileName);
    }

    private void OpenDocument(string path)
    {
        try
        {
            var file = FileService.Read(path);
            _document.FilePath = Path.GetFullPath(path);
            _document.Encoding = file.Encoding;
            _document.NewLine = file.NewLine;
            SetDocumentText(file.Text);
            AddRecentFile(_document.FilePath);
            UpdateWindowTitle();
            Editor.Focus();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Bestand kon niet worden geopend: {path}", ex);
            MessageBox.Show(this, ex.Message, "Openen mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveDocument()
    {
        if (_document.FilePath is null)
            return SaveDocumentAs();
        try
        {
            FileService.Write(_document.FilePath, Editor.Text, _document.Encoding, _document.NewLine);
            _document.IsDirty = false;
            AddRecentFile(_document.FilePath);
            UpdateWindowTitle();
            RefreshInspection();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Bestand kon niet worden opgeslagen: {_document.FilePath}", ex);
            MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool SaveDocumentAs()
    {
        var dialog = new SaveFileWindow(_themeService, _document.DisplayName, _document.FilePath) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedPath is null)
            return false;
        _document.FilePath = dialog.SelectedPath;
        return SaveDocument();
    }

    private bool ConfirmDiscardOrSave()
    {
        if (!_document.IsDirty)
            return true;
        var result = MessageBox.Show(this,
            $"Wijzigingen in {_document.DisplayName} opslaan?",
            "Niet-opgeslagen wijzigingen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void SetDocumentText(string text)
    {
        _collapseNewFoldings = true;
        _suppressDirty = true;
        Editor.Text = text;
        Editor.CaretOffset = 0;
        _document.IsDirty = false;
        _suppressDirty = false;
        _syntaxTimer.Stop();
        RefreshAnalysis();
    }

    private void UpdateWindowTitle()
    {
        var dirty = _document.IsDirty ? " •" : string.Empty;
        Title = $"{_document.DisplayName}{dirty} — Cmdlet Forge";
        FileStatus.Text = $"{_document.DisplayName}{dirty} · {_document.Encoding.WebName}";
        UpdateInspectorSaveState();
    }

    private void FunctionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FunctionsList.SelectedItem is not ScriptFunctionInfo function)
            return;

        FunctionsList.SelectedItem = null;
        var offset = Math.Clamp(function.StartOffset, 0, Editor.Document.TextLength);
        ExpandFoldingsContaining(offset);
        Editor.Select(offset, 0);
        Editor.CaretOffset = offset;
        Editor.ScrollTo(function.Line, function.Column);
        GoToLineBox.Text = function.Line.ToString();
        GoToColumnBox.Text = function.Column.ToString();
        Editor.Focus();
    }

    private void AddRecentFile(string path)
    {
        _settings.RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        RefreshRecentFilesMenu();
    }

    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        foreach (var path in _settings.RecentFiles.Where(File.Exists))
        {
            var item = new MenuItem { Header = path, ToolTip = path };
            item.Click += (_, _) =>
            {
                if (ConfirmDiscardOrSave())
                    OpenDocument(path);
            };
            RecentFilesMenu.Items.Add(item);
        }
        RecentFilesMenu.IsEnabled = RecentFilesMenu.Items.Count > 0;
    }

    private void ShowSearch(bool replace)
    {
        SearchPanel.Visibility = Visibility.Visible;
        ReplaceControls.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(Editor.SelectedText) && !Editor.SelectedText.Contains('\n'))
            FindBox.Text = Editor.SelectedText;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private Regex? BuildSearchRegex()
    {
        if (string.IsNullOrEmpty(FindBox.Text))
            return null;
        try
        {
            FindStatus.Text = string.Empty;
            return TextSearchService.BuildRegex(FindBox.Text, new SearchOptions(
                MatchCaseCheck.IsChecked == true,
                WholeWordCheck.IsChecked == true,
                RegexCheck.IsChecked == true));
        }
        catch (ArgumentException ex)
        {
            FindStatus.Text = $"Regexfout: {ex.Message}";
            return null;
        }
    }

    private void FindNext(bool backwards)
    {
        var regex = BuildSearchRegex();
        if (regex is null)
            return;
        var matches = regex.Matches(Editor.Text).Cast<Match>().Where(match => match.Length > 0).ToArray();
        if (matches.Length == 0)
        {
            FindStatus.Text = "Geen resultaten";
            return;
        }

        Match match;
        if (backwards)
            match = matches.LastOrDefault(item => item.Index < Editor.SelectionStart) ?? matches[^1];
        else
            match = matches.FirstOrDefault(item => item.Index >= Editor.SelectionStart + Editor.SelectionLength) ?? matches[0];

        SelectMatch(match);
        var index = Array.IndexOf(matches, match) + 1;
        FindStatus.Text = $"{index} / {matches.Length}";
    }

    private void SelectMatch(Match match)
    {
        ExpandFoldingsContaining(match.Index);
        Editor.Select(match.Index, match.Length);
        Editor.CaretOffset = match.Index + match.Length;
        var line = Editor.Document.GetLineByOffset(match.Index).LineNumber;
        Editor.ScrollToLine(line);
        Editor.Focus();
    }

    private void ReplaceOne()
    {
        var regex = BuildSearchRegex();
        if (regex is null)
            return;
        var selected = Editor.SelectedText;
        var match = regex.Match(selected);
        if (match.Success && match.Index == 0 && match.Length == selected.Length)
            Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength, match.Result(ReplaceBox.Text));
        FindNext(false);
    }

    private void ReplaceAll()
    {
        var regex = BuildSearchRegex();
        if (regex is null)
            return;
        var original = Editor.Text;
        var count = regex.Matches(original).Cast<Match>().Count(match => match.Length > 0);
        if (count == 0)
        {
            FindStatus.Text = "Geen resultaten";
            return;
        }
        Editor.Document.Text = regex.Replace(original, ReplaceBox.Text);
        FindStatus.Text = $"{count} vervangen";
    }

    private void GoTo()
    {
        if (!int.TryParse(GoToLineBox.Text, out var requestedLine) || !int.TryParse(GoToColumnBox.Text, out var requestedColumn))
        {
            MessageBox.Show(this, "Gebruik gehele getallen voor regel en teken.", "Spring naar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var lineNumber = Math.Clamp(requestedLine, 1, Editor.Document.LineCount);
        var line = Editor.Document.GetLineByNumber(lineNumber);
        var column = Math.Clamp(requestedColumn, 1, line.Length + 1);
        var offset = line.Offset + column - 1;
        ExpandFoldingsContaining(offset);
        Editor.CaretOffset = offset;
        Editor.ScrollTo(lineNumber, column);
        Editor.Focus();
    }

    private void RunSelection()
    {
        var script = string.IsNullOrWhiteSpace(Editor.SelectedText) ? Editor.Text : Editor.SelectedText;
        _ = ExecuteScriptAsync(script);
    }

    private void RunScript()
    {
        if (!ConfirmRunnableScript())
            return;
        _ = ExecuteScriptAsync(Editor.Text);
    }

    private bool ConfirmRunnableScript()
    {
        var diagnostics = SyntaxService.Analyze(Editor.Text);
        if (diagnostics.Count > 0 && MessageBox.Show(this,
                $"Het script bevat {diagnostics.Count} syntaxfout(en). Toch uitvoeren?",
                "Syntaxfouten",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            BottomTabs.SelectedIndex = 1;
            return false;
        }
        return true;
    }

    private void RunScriptWithParameters()
    {
        if (!ConfirmRunnableScript())
            return;

        var definitions = ScriptParameterService.Discover(Editor.Text);
        if (definitions.Count == 0)
        {
            MessageBox.Show(this,
                "Er is geen statisch param(...) blok gevonden. Gebruik F5 voor uitvoering zonder parameters.",
                "Geen parameters gevonden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ScriptParametersWindow(definitions, _themeService, _document.DisplayName) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var workingDirectory = _document.FilePath is null ? null : Path.GetDirectoryName(_document.FilePath);
        _ = ExecuteScriptWithParametersAsync(Editor.Text, dialog.Parameters, workingDirectory);
    }

    private async Task ExecuteScriptAsync(string script)
    {
        BeginExecution();
        try
        {
            await _terminal.ExecuteScriptTextAsync(script);
        }
        catch (Exception ex)
        {
            AppLog.Error("Scriptuitvoering kon niet worden gestart.", ex);
            AppendTerminal(new TerminalMessage(TerminalStream.Error, ex.Message));
        }
    }

    private async Task ExecuteScriptWithParametersAsync(
        string script,
        IReadOnlyDictionary<string, object?> parameters,
        string? workingDirectory)
    {
        BeginExecution();
        try
        {
            await _terminal.ExecuteScriptWithParametersAsync(script, parameters, workingDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Error("Parameterscript kon niet worden gestart.", ex);
            AppendTerminal(new TerminalMessage(TerminalStream.Error, ex.Message));
        }
    }

    private async Task RestartTerminalAsync()
    {
        try
        {
            AppendTerminal(new TerminalMessage(TerminalStream.System, "Actief proces wordt afgebroken..."));
            ResetTerminalFormatting();
            await _terminal.RestartAsync();
        }
        catch (Exception ex)
        {
            AppendTerminal(new TerminalMessage(TerminalStream.Error, ex.Message));
        }
    }

    private async void TerminalRun_Click(object sender, RoutedEventArgs e)
    {
        var command = TerminalInput.Text.Trim();
        if (command.Length == 0)
            return;
        TerminalInput.Clear();
        BeginExecution();
        try
        {
            await _terminal.ExecuteAsync(command);
        }
        catch (Exception ex)
        {
            AppendTerminal(new TerminalMessage(TerminalStream.Error, ex.Message));
        }
    }

    private void TerminalInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TerminalRun_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Terminal_MessageReceived(object? sender, TerminalMessage message) =>
        Dispatcher.BeginInvoke(() => AppendTerminal(message));

    private void AppendTerminal(TerminalMessage message)
    {
        var initialStyle = message.Stream switch
        {
            TerminalStream.Output => _terminalOutputStyle,
            TerminalStream.Error => _terminalErrorStyle,
            _ => default
        };
        var parsed = AnsiTextParser.Parse(message.Text, initialStyle);
        if (message.Stream == TerminalStream.Output)
            _terminalOutputStyle = parsed.FinalStyle;
        else if (message.Stream == TerminalStream.Error)
            _terminalErrorStyle = parsed.FinalStyle;

        if (message.Stream == TerminalStream.Error && !string.IsNullOrWhiteSpace(parsed.PlainText))
            AddExecutionProblem(parsed.PlainText);
        if (parsed.Segments.Count == 0)
            return;

        var entry = new TerminalEntry(message.Stream, parsed.Segments);
        _terminalHistory.Add(entry);
        AppendTerminalVisual(entry);
        while (_terminalHistory.Count > 2500)
        {
            _terminalHistory.RemoveAt(0);
            if (TerminalOutput.Document.Blocks.FirstBlock is { } firstBlock)
                TerminalOutput.Document.Blocks.Remove(firstBlock);
        }
        TerminalOutput.ScrollToEnd();
    }

    private void AppendTerminalVisual(TerminalEntry entry)
    {
        var fallbackBrushKey = entry.Stream switch
        {
            TerminalStream.Input => "AccentBrush",
            TerminalStream.Error => "DangerBrush",
            TerminalStream.System => "TextMutedBrush",
            _ => "TextBrush"
        };
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 18
        };

        foreach (var segment in entry.Segments)
        {
            var style = segment.Style;
            var effectiveFallbackBrushKey = style.Dim ? "TextMutedBrush" : fallbackBrushKey;
            var run = new Run(segment.Text)
            {
                FontWeight = style.Bold ? FontWeights.Bold : style.Dim ? FontWeights.Light : FontWeights.Normal,
                FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal
            };

            if (style.Underline || style.Strikethrough)
            {
                var decorations = new TextDecorationCollection();
                if (style.Underline)
                    decorations.Add(TextDecorations.Underline[0]);
                if (style.Strikethrough)
                    decorations.Add(TextDecorations.Strikethrough[0]);
                run.TextDecorations = decorations;
            }

            if (style.Inverse)
            {
                SetTerminalBrush(run, TextElement.ForegroundProperty, style.Background, "SurfaceBrush");
                SetTerminalBrush(run, TextElement.BackgroundProperty, style.Foreground, effectiveFallbackBrushKey);
            }
            else
            {
                SetTerminalBrush(run, TextElement.ForegroundProperty, style.Foreground, effectiveFallbackBrushKey);
                if (style.Background is { } background)
                    SetTerminalBrush(run, TextElement.BackgroundProperty, background, "SurfaceBrush");
            }
            paragraph.Inlines.Add(run);
        }

        TerminalOutput.Document.Blocks.Add(paragraph);
    }

    private static void SetTerminalBrush(Run run, DependencyProperty property, AnsiColor? color, string fallbackBrushKey)
    {
        if (color is null)
        {
            run.SetResourceReference(property, fallbackBrushKey);
            return;
        }

        if (color.Value.Mode == AnsiColorMode.Indexed && color.Value.Value < AnsiBrushKeys.Length)
        {
            run.SetResourceReference(property, AnsiBrushKeys[color.Value.Value]);
            return;
        }

        run.SetValue(property, new SolidColorBrush(ToMediaColor(color.Value)));
    }

    private static Color ToMediaColor(AnsiColor color)
    {
        if (color.Mode == AnsiColorMode.Rgb)
            return Color.FromRgb((byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value);

        var index = Math.Clamp(color.Value, 0, 255);
        if (index < 16)
            return Colors.White;
        if (index >= 232)
        {
            var gray = (byte)(8 + ((index - 232) * 10));
            return Color.FromRgb(gray, gray, gray);
        }

        var cubeIndex = index - 16;
        ReadOnlySpan<byte> levels = [0, 95, 135, 175, 215, 255];
        return Color.FromRgb(
            levels[cubeIndex / 36],
            levels[(cubeIndex % 36) / 6],
            levels[cubeIndex % 6]);
    }

    private void RenderTerminalHistory()
    {
        TerminalOutput.Document.Blocks.Clear();
        foreach (var entry in _terminalHistory)
            AppendTerminalVisual(entry);
        TerminalOutput.ScrollToEnd();
    }

    private void ClearTerminal()
    {
        _terminalHistory.Clear();
        ResetTerminalFormatting();
        TerminalOutput.Document.Blocks.Clear();
    }

    private void ResetTerminalFormatting()
    {
        _terminalOutputStyle = default;
        _terminalErrorStyle = default;
    }

    private static string DefaultScript() => "# Cmdlet Forge\r\n# Schrijf PowerShell, controleer de syntax en voer uit met F5.\r\n\r\n$PSVersionTable.PSVersion\r\n";

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || ThemeCombo.SelectedItem is not ComboBoxItem item)
            return;
        _settings.Theme = string.Equals(item.Tag?.ToString(), "Light", StringComparison.Ordinal) ? AppTheme.Light : AppTheme.Dark;
        DarkModeMenu.IsChecked = _settings.Theme == AppTheme.Dark;
        ApplyTheme();
    }

    private void PaletteCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || PaletteCombo.SelectedItem is not ComboBoxItem item || !Enum.TryParse<EditorPalette>(item.Tag?.ToString(), out var palette))
            return;
        _settings.Palette = palette;
        ApplyTheme();
    }

    private void DarkModeMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = DarkModeMenu.IsChecked ? AppTheme.Dark : AppTheme.Light;
        ThemeCombo.SelectedIndex = _settings.Theme == AppTheme.Dark ? 0 : 1;
        ApplyTheme();
    }

    private void WordWrapMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.WordWrap = WordWrapMenu.IsChecked;
        Editor.WordWrap = _settings.WordWrap;
    }

    private void CrtMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.CrtOverlay = CrtMenu.IsChecked;
        CrtOverlay.IsActive = _settings.CrtOverlay;
    }

    private void ScriptInspectorMenu_Click(object sender, RoutedEventArgs e) =>
        SetInspectorVisibility(ScriptInspectorMenu.IsChecked);

    private void HideScriptInspector_Click(object sender, RoutedEventArgs e) => SetInspectorVisibility(false);

    private void ManageModules_Click(object sender, RoutedEventArgs e) =>
        new ModuleManagerWindow(new ModuleService(_settings.PreferredPowerShell), _themeService) { Owner = this }.ShowDialog();

    private void ManageUpdates_Click(object sender, RoutedEventArgs e) =>
        new UpdateWindow(_settings.PreferredPowerShell, _themeService) { Owner = this }.ShowDialog();

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppLog.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", AppLog.LogDirectory) { UseShellExecute = true });
    }

    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        $"Cmdlet Forge {AppInfo.Version.ToString(3)}\n\nNative PowerShell-workbench voor Windows.\nScripts worden altijd uitgevoerd in een apart pwsh-proces.",
        "Over Cmdlet Forge", MessageBoxButton.OK, MessageBoxImage.Information);

    private void GoToBox_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { GoTo(); e.Handled = true; } }
    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; } }
    private void FindBox_TextChanged(object sender, TextChangedEventArgs e) { if (SearchPanel.Visibility == Visibility.Visible) FindNext(false); }
    private void NewFile_Click(object sender, RoutedEventArgs e) => NewFile();
    private void OpenFile_Click(object sender, RoutedEventArgs e) => OpenFile();
    private void SaveFile_Click(object sender, RoutedEventArgs e) => SaveDocument();
    private void SaveAsFile_Click(object sender, RoutedEventArgs e) => SaveDocumentAs();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Undo_Click(object sender, RoutedEventArgs e) => Editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Editor.Redo();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Editor.SelectAll();
    private void ShowFind_Click(object sender, RoutedEventArgs e) => ShowSearch(false);
    private void ShowReplace_Click(object sender, RoutedEventArgs e) => ShowSearch(true);
    private void FocusGoTo_Click(object sender, RoutedEventArgs e) { GoToLineBox.Focus(); GoToLineBox.SelectAll(); }
    private void CloseSearch_Click(object sender, RoutedEventArgs e) { SearchPanel.Visibility = Visibility.Collapsed; Editor.Focus(); }
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(false);
    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindNext(true);
    private void ReplaceOne_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceAll();
    private void GoTo_Click(object sender, RoutedEventArgs e) => GoTo();
    private void RunSelection_Click(object sender, RoutedEventArgs e) => RunSelection();
    private void RunScript_Click(object sender, RoutedEventArgs e) => RunScript();
    private void RunScriptWithParameters_Click(object sender, RoutedEventArgs e) => RunScriptWithParameters();
    private async void RestartTerminal_Click(object sender, RoutedEventArgs e) => await RestartTerminalAsync();
    private void ClearTerminal_Click(object sender, RoutedEventArgs e) => ClearTerminal();
    private void CollapseAllFoldings_Click(object sender, RoutedEventArgs e) => SetAllFoldings(true);
    private void ExpandAllFoldings_Click(object sender, RoutedEventArgs e) => SetAllFoldings(false);

    private sealed record TerminalEntry(TerminalStream Stream, IReadOnlyList<AnsiTextSegment> Segments);
}
