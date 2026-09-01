using System.Text;
using System.IO;
using CmdletForge.Models;
using CmdletForge.Services;
using CmdletForge.Theming;

if (args.Contains("--module-smoke", StringComparer.Ordinal))
{
    var modules = await new ModuleService("pwsh.exe").GetModulesAsync();
    foreach (var module in modules)
        Console.WriteLine($"{module.Name}|{module.InstalledVersion}|{module.AvailableVersion}");
    return modules.Count == ModuleService.SuggestedModules.Count ? 0 : 1;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Valid PowerShell parses cleanly", () =>
    {
        Assert.Equal(0, SyntaxService.Analyze("param([string]$Name)\nWrite-Output $Name").Count);
        return Task.CompletedTask;
    }),
    ("Syntax error exposes exact location", () =>
    {
        var errors = SyntaxService.Analyze("if ($true {\n  'x'\n}");
        Assert.True(errors.Count > 0);
        var error = errors[0];
        Assert.True(error.Line >= 1);
        Assert.True(error.Column >= 1);
        Assert.True(error.Length >= 1);
        return Task.CompletedTask;
    }),
    ("Literal search returns exact offsets", () =>
    {
        var regex = TextSearchService.BuildRegex("Get-Item", new SearchOptions(false, false, false));
        var matches = TextSearchService.FindAll("Get-Item\nget-item X", regex);
        Assert.Equal(2, matches.Count);
        Assert.Equal(0, matches[0].Index);
        Assert.Equal(9, matches[1].Index);
        return Task.CompletedTask;
    }),
    ("Whole-word search excludes longer identifiers", () =>
    {
        var regex = TextSearchService.BuildRegex("item", new SearchOptions(false, true, false));
        var matches = TextSearchService.FindAll("item itemized $item", regex);
        Assert.Equal(2, matches.Count);
        Assert.Equal(0, matches[0].Index);
        Assert.Equal(15, matches[1].Index);
        return Task.CompletedTask;
    }),
    ("Regex replacement preserves capture groups", () =>
    {
        var regex = TextSearchService.BuildRegex(@"(Get)-(Item)", new SearchOptions(true, false, true));
        Assert.Equal("Item:Get", regex.Replace("Get-Item", "$2:$1"));
        return Task.CompletedTask;
    }),
    ("UTF-8 no-BOM and LF are preserved", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "utf8-lf.ps1");
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes("'é'\n'ß'\n"));
        var file = FileService.Read(path);
        Assert.Equal("utf-8", file.Encoding.WebName);
        Assert.Equal("\n", file.NewLine);
        FileService.Write(path, file.Text + "'x'\n", file.Encoding, file.NewLine);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        return Task.CompletedTask;
    }),
    ("UTF-16 BOM is detected", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "utf16.ps1");
        File.WriteAllText(path, "$x = 1\r\n", Encoding.Unicode);
        var file = FileService.Read(path);
        Assert.Equal("utf-16", file.Encoding.WebName);
        Assert.Equal("\r\n", file.NewLine);
        return Task.CompletedTask;
    }),
    ("Invalid module name is rejected before execution", async () =>
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new ModuleService("does-not-run.exe").InstallAsync("Az; Remove-Item C:\\"));
    }),
    ("Encoded PowerShell command round-trips", () =>
    {
        var command = "'héllo' | Write-Output";
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(PowerShellProcess.EncodeCommand(command)));
        Assert.Equal(command, decoded);
        return Task.CompletedTask;
    }),
    ("PowerShell child process captures stdout and stderr", async () =>
    {
        var result = await PowerShellProcess.RunEncodedAsync("pwsh.exe", "Write-Output 'ok'; [Console]::Error.WriteLine('err')");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ok", result.StandardOutput);
        Assert.Contains("err", result.StandardError);
    }),
    ("Script parameters are discovered with types and metadata", () =>
    {
        var parameters = ScriptParameterService.Discover("""
            param(
                [Parameter(Mandatory=$true)] [string] $Name,
                [switch] $Force,
                [bool] $Enabled,
                [string[]] $Tags,
                [int] $Count = 3
            )
            """);
        Assert.Equal(5, parameters.Count);
        Assert.Equal("Name", parameters[0].Name);
        Assert.Equal(ScriptParameterInputKind.Text, parameters[0].InputKind);
        Assert.Equal(ScriptParameterInputKind.Switch, parameters[1].InputKind);
        Assert.Equal(ScriptParameterInputKind.Boolean, parameters[2].InputKind);
        Assert.Equal(ScriptParameterInputKind.Array, parameters[3].InputKind);
        Assert.Equal("3", parameters[4].DefaultExpression!);
        return Task.CompletedTask;
    }),
    ("Credential parameter types are deliberately unsupported", () =>
    {
        var parameters = ScriptParameterService.Discover("param([securestring]$Secret, [pscredential]$Credential)");
        Assert.Equal(2, parameters.Count);
        Assert.True(parameters.All(parameter => parameter.InputKind == ScriptParameterInputKind.Unsupported));
        Assert.True(parameters.All(parameter => !string.IsNullOrWhiteSpace(parameter.UnsupportedReason)));
        return Task.CompletedTask;
    }),
    ("Problem items distinguish syntax from execution", () =>
    {
        var syntax = ProblemItem.FromSyntax(new SyntaxDiagnostic(DiagnosticSeverity.Error, "missing }", 12, 1, 3, 8));
        var execution = ProblemItem.FromExecution("parameter-runner-2d30e09fd14f4b71bba1a3039b7be4bf.ps1: runtime failure");
        Assert.Equal("SYNTAX", syntax.SourceLabel);
        Assert.True(syntax.CanNavigate);
        Assert.Equal("R3, T8", syntax.Location);
        Assert.Equal("UITVOER", execution.SourceLabel);
        Assert.False(execution.CanNavigate);
        Assert.Equal("runtime failure", execution.Message);
        return Task.CompletedTask;
    }),
    ("Parameter execution preserves values as data", async () =>
    {
        using var terminal = new TerminalSession();
        var messages = new List<TerminalMessage>();
        terminal.MessageReceived += (_, message) => messages.Add(message);
        var marker = "two words; $(Write-Output INJECTION)";
        await terminal.ExecuteScriptWithParametersAsync(
            "param([Parameter(Mandatory)][string]$Name,[switch]$Force,[string[]]$Tags)\nWrite-Output \"$Name|$Force|$($Tags -join ',')\"",
            new Dictionary<string, object?>
            {
                ["Name"] = marker,
                ["Force"] = true,
                ["Tags"] = new[] { "alpha", "two words" }
            });
        var output = string.Join("\n", messages.Where(message => message.Stream == TerminalStream.Output).Select(message => message.Text));
        Assert.Contains($"{marker}|True|alpha,two words", output);
        Assert.False(messages.Any(message => message.Stream != TerminalStream.Output && message.Text.Contains(marker, StringComparison.Ordinal)));
    }),
    ("Parameterized runtime failures are emitted as errors", async () =>
    {
        using var terminal = new TerminalSession();
        var messages = new List<TerminalMessage>();
        terminal.MessageReceived += (_, message) => messages.Add(message);
        await terminal.ExecuteScriptWithParametersAsync("throw 'runtime-problem-marker'", new Dictionary<string, object?>());
        Assert.True(messages.Any(message => message.Stream == TerminalStream.Error
                                            && message.Text.Contains("runtime-problem-marker", StringComparison.Ordinal)));
    }),
    ("Script inspector discovers unique real functions", () =>
    {
        var functions = ScriptInspectionService.DiscoverFunctions("""
            # function Comment-Fake {}
            $text = 'function String-Fake {}'
            function Get-Thing { 'first' }
            function Outer-Thing {
                function Inner-Thing { 'nested' }
            }
            function get-thing { 'second definition' }
            filter Select-Thing { $_ }
            """);
        Assert.Equal(4, functions.Count);
        Assert.Equal("Get-Thing", functions[0].Name);
        Assert.Equal(2, functions[0].DefinitionCount);
        Assert.Equal("Outer-Thing", functions[1].Name);
        Assert.Equal("Inner-Thing", functions[2].Name);
        Assert.Equal("filter", functions[3].Kind);
        Assert.True(functions.All(function => function.Line >= 1 && function.StartOffset >= 0));
        return Task.CompletedTask;
    }),
    ("Script inspector remains useful while a function is being typed", () =>
    {
        var functions = ScriptInspectionService.DiscoverFunctions("function Test-Live {\n    Write-Output 'busy'");
        var function = Assert.Single(functions);
        Assert.Equal("Test-Live", function.Name);
        Assert.Equal(1, function.Line);
        return Task.CompletedTask;
    }),
    ("Script inspector hashes the saved file bytes", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "inspector-hash.ps1");
        var encoding = new UTF8Encoding(false);
        var text = "function Test-Hash {\n    'é'\n}\n";
        FileService.Write(path, text, encoding, "\n");
        var inspection = ScriptInspectionService.Inspect(text, encoding, "\n", path, false);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        Assert.True(inspection.UsesSavedFile);
        Assert.Equal(expected, inspection.Sha256);
        Assert.Equal(File.ReadAllBytes(path).LongLength, inspection.ByteCount);
        Assert.Equal(4, inspection.LineCount);
        Assert.Equal(text.Length, inspection.CharacterCount);
        return Task.CompletedTask;
    }),
    ("Dirty script metadata uses current editor contents", () =>
    {
        var inspection = ScriptInspectionService.Inspect("function Current {}", new UTF8Encoding(false), "\r\n", null, true);
        Assert.False(inspection.UsesSavedFile);
        Assert.Equal(1, inspection.LineCount);
        Assert.Equal("function Current {}".Length, inspection.CharacterCount);
        Assert.Equal(1, inspection.Functions.Count);
        Assert.Equal(64, inspection.Sha256.Length);
        return Task.CompletedTask;
    }),
    ("Dirty metadata matches a future UTF-16 save", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "inspector-utf16.ps1");
        var text = "function Test-Unicode {\n    'é'\n}\n";
        var dirty = ScriptInspectionService.Inspect(text, Encoding.Unicode, "\r\n", null, true);
        FileService.Write(path, text, Encoding.Unicode, "\r\n");
        var savedBytes = File.ReadAllBytes(path);
        var savedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(savedBytes));
        Assert.Equal(savedHash, dirty.Sha256);
        Assert.Equal(savedBytes.LongLength, dirty.ByteCount);
        return Task.CompletedTask;
    }),
    ("Save path appends the selected extension", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var resolution = SavePathService.Resolve(root, "new-script", ".ps1");
        Assert.True(resolution.IsValid);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "new-script.ps1"), resolution.Path!);
        return Task.CompletedTask;
    }),
    ("Save path preserves an explicit extension", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var resolution = SavePathService.Resolve(root, "module.psm1", ".ps1");
        Assert.True(resolution.IsValid);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "module.psm1"), resolution.Path!);
        return Task.CompletedTask;
    }),
    ("Save path rejects traversal in the file name", () =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-fixtures");
        Directory.CreateDirectory(root);
        var resolution = SavePathService.Resolve(root, "..\\outside.ps1", ".ps1");
        Assert.False(resolution.IsValid);
        Assert.True(resolution.Path is null);
        return Task.CompletedTask;
    }),
    ("Every editor palette has light and dark contrast", () =>
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        foreach (var palette in Enum.GetValues<EditorPalette>())
        {
            var service = new ThemeService();
            typeof(ThemeService).GetProperty(nameof(ThemeService.CurrentTheme))!.SetValue(service, theme);
            typeof(ThemeService).GetProperty(nameof(ThemeService.CurrentPalette))!.SetValue(service, palette);
            var colors = service.GetEditorColors();
            Assert.True(Contrast(colors.Background, colors.Foreground) >= 4.5, $"{theme}/{palette} contrast too low");
        }
        return Task.CompletedTask;
    }),
    ("PowerShell folding finds nested multiline blocks", () =>
    {
        var script = "if ($true) {\n  foreach ($item in 1..2) {\n    $item\n  }\n}";
        var regions = PowerShellFoldingService.FindRegions(script);
        Assert.Equal(2, regions.Count);
        Assert.Equal(1, regions[0].StartLine);
        Assert.Equal(5, regions[0].EndLine);
        Assert.Equal(2, regions[1].StartLine);
        Assert.Equal(4, regions[1].EndLine);
        Assert.True(regions.All(region => region.EndOffset > region.StartOffset));
        return Task.CompletedTask;
    }),
    ("PowerShell folding ignores braces in strings comments and single lines", () =>
    {
        var script = "$text = '{not a block}'\n# { also not a block }\nif ($true) { 'single line' }\nfunction Test {\n  'fold me'\n}";
        var regions = PowerShellFoldingService.FindRegions(script);
        Assert.Equal(1, regions.Count);
        Assert.Equal(4, regions[0].StartLine);
        Assert.Equal(6, regions[0].EndLine);
        Assert.Contains("ingeklapt", regions[0].DisplayText);
        return Task.CompletedTask;
    })
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL  {test.Name}\n      {ex}");
    }
}

Console.WriteLine($"\n{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static double Contrast(System.Windows.Media.Color a, System.Windows.Media.Color b)
{
    static double L(byte value)
    {
        var c = value / 255d;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
    var l1 = 0.2126 * L(a.R) + 0.7152 * L(a.G) + 0.0722 * L(a.B);
    var l2 = 0.2126 * L(b.R) + 0.7152 * L(b.G) + 0.0722 * L(b.B);
    return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
}

static class Assert
{
    public static void True(bool condition, string message = "Expected true.") { if (!condition) throw new InvalidOperationException(message); }
    public static void False(bool condition, string message = "Expected false.") => True(!condition, message);
    public static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    public static T Single<T>(IReadOnlyList<T> values)
    {
        Equal(1, values.Count);
        return values[0];
    }
    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{expected}' not found in '{actual}'.");
    }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
