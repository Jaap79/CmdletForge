using System.Diagnostics;

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
    private string _executable = "pwsh.exe";

    public event EventHandler<TerminalMessage>? MessageReceived;
    public bool IsRunning => _process is { HasExited: false };

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
            process = _process;

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

    public async Task RestartAsync() => await StartAsync(_executable).ConfigureAwait(false);

    public Task StopAsync()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }

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
}
