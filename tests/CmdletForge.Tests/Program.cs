using System.Text;
using System.IO;
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
