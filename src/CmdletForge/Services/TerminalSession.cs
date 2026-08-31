using System.Diagnostics;
using System.Text.Json;

namespace CmdletForge.Services;

public enum TerminalStream
{
    Input,
    Output,
    Error,
    System
}

public sealed record TerminalMessage(TerminalStream Stream, string Text);

public sealed class TerminalSession : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private Process? _parameterProcess;
    private string _executable = "pwsh.exe";

    public event EventHandler<TerminalMessage>? MessageReceived;
    public bool IsRunning => _process is { HasExited: false } || _parameterProcess is { HasExited: false };

    public async Task StartAsync(string executable, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        _executable = executable;

        var process = new Process
        {
            EnableRaisingEvents = true,
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NoExit");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("-");

        process.OutputDataReceived += (_, args) => Emit(TerminalStream.Output, args.Data);
        process.ErrorDataReceived += (_, args) => Emit(TerminalStream.Error, args.Data);
        process.Exited += (_, _) =>
        {
            try
            {
                Emit(TerminalStream.System, $"PowerShell is gestopt (code {process.ExitCode}).");
            }
            catch (InvalidOperationException)
            {
                Emit(TerminalStream.System, "PowerShell is gestopt.");
            }
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Kon {executable} niet starten.");

            lock (_gate)
                _process = process;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.StandardInput.WriteLineAsync("$global:ProgressPreference='SilentlyContinue'; if ($PSStyle) { $PSStyle.OutputRendering='PlainText' }; [Console]::OutputEncoding=[Text.UTF8Encoding]::new()").ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            Emit(TerminalStream.System, $"Terminal gestart met {executable}.");
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public async Task ExecuteAsync(string command)
    {
        Process? process;
        lock (_gate)
        {
            if (_parameterProcess is { HasExited: false })
                throw new InvalidOperationException("Een script met parameters wordt al uitgevoerd. Stop dit proces eerst met Shift+F5.");
            process = _process;
        }

        if (process is null || process.HasExited)
            await StartAsync(_executable).ConfigureAwait(false);

        lock (_gate)
            process = _process;

        if (process is null)
            throw new InvalidOperationException("PowerShell-terminal is niet beschikbaar.");

        Emit(TerminalStream.Input, $"PS> {command}");
        await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    public async Task ExecuteScriptTextAsync(string script)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Cmdlet Forge");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"run-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(path, script, new UTF8Encoding(false)).ConfigureAwait(false);
        var quoted = path.Replace("'", "''", StringComparison.Ordinal);
        await ExecuteAsync($"try {{ & '{quoted}' }} finally {{ Remove-Item -LiteralPath '{quoted}' -Force -ErrorAction SilentlyContinue }}").ConfigureAwait(false);
    }

    public async Task ExecuteScriptWithParametersAsync(
        string script,
        IReadOnlyDictionary<string, object?> parameters,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(parameters);

        lock (_gate)
        {
            if (_parameterProcess is { HasExited: false })
                throw new InvalidOperationException("Er wordt al een parameterscript uitgevoerd.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "Cmdlet Forge");
        Directory.CreateDirectory(directory);
        var runId = Guid.NewGuid().ToString("N");
        var scriptPath = Path.Combine(directory, $"parameter-script-{runId}.ps1");
        var parametersPath = Path.Combine(directory, $"parameter-values-{runId}.json");
        var runnerPath = Path.Combine(directory, $"parameter-runner-{runId}.ps1");

        try
        {
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(parametersPath, JsonSerializer.Serialize(parameters), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(runnerPath, ParameterRunner, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DeleteTemporary(scriptPath);
            DeleteTemporary(parametersPath);
            DeleteTemporary(runnerPath);
            throw;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(runnerPath);
        process.StartInfo.ArgumentList.Add("-ScriptPath");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("-ParametersPath");
        process.StartInfo.ArgumentList.Add(parametersPath);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Kon {_executable} niet starten.");

            lock (_gate)
                _parameterProcess = process;

            var parameterSummary = parameters.Count == 0
                ? "zonder parameters"
                : $"met {string.Join(", ", parameters.Keys.Select(name => $"-{name}"))}";
            Emit(TerminalStream.Input, $"PS> script uitvoeren {parameterSummary}");
            Emit(TerminalStream.System, "Geïsoleerd parameterscript gestart. Waarden worden niet in de terminal of log weergegeven.");

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            Emit(TerminalStream.Output, output.TrimEnd());
            Emit(TerminalStream.Error, error.TrimEnd());
            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(error))
                Emit(TerminalStream.Error, $"Parameterscript is gestopt met exitcode {process.ExitCode}.");
            Emit(TerminalStream.System, $"Parameterscript gestopt (code {process.ExitCode}).");
        }
        catch (OperationCanceledException)
        {
            StopParameterProcess(process);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_parameterProcess, process))
                    _parameterProcess = null;
            }
            DeleteTemporary(scriptPath);
            DeleteTemporary(parametersPath);
            DeleteTemporary(runnerPath);
        }
    }

    public async Task RestartAsync() => await StartAsync(_executable).ConfigureAwait(false);

    public Task StopAsync()
    {
        Process? process;
        Process? parameterProcess;
        lock (_gate)
        {
            process = _process;
            _process = null;
            parameterProcess = _parameterProcess;
            _parameterProcess = null;
        }

        StopParameterProcess(parameterProcess);

        if (process is null)
            return Task.CompletedTask;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Terminalproces kon niet netjes worden gestopt: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }

        return Task.CompletedTask;
    }

    private void Emit(TerminalStream stream, string? text)
    {
        if (!string.IsNullOrEmpty(text))
            MessageReceived?.Invoke(this, new TerminalMessage(stream, text));
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private static void StopParameterProcess(Process? process)
    {
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Parameterscript kon niet netjes worden gestopt: {ex.Message}");
        }
    }

    private static void DeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Tijdelijk parameterbestand kon niet worden verwijderd: {ex.Message}");
        }
    }

    private const string ParameterRunner = """
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)] [string] $ScriptPath,
            [Parameter(Mandatory)] [string] $ParametersPath
        )

        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
        $parameters = Get-Content -Raw -LiteralPath $ParametersPath | ConvertFrom-Json -AsHashtable
        & $ScriptPath @parameters
        """;
}
