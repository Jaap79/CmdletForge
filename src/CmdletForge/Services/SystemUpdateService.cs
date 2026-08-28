using System.Diagnostics;

namespace CmdletForge.Services;

public static class SystemUpdateService
{
    public static async Task<string> GetPowerShellVersionAsync(string executable, CancellationToken cancellationToken = default)
    {
        var result = await PowerShellProcess.RunEncodedAsync(executable, "$PSVersionTable.PSVersion.ToString()", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(result.StandardError);
        return result.StandardOutput.Trim();
    }

    public static void StartPowerShellUpdate()
    {
        var start = new ProcessStartInfo
        {
            FileName = "winget.exe",
            UseShellExecute = true
        };
        start.ArgumentList.Add("upgrade");
        start.ArgumentList.Add("--id");
        start.ArgumentList.Add("Microsoft.PowerShell");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("--accept-source-agreements");
        start.ArgumentList.Add("--accept-package-agreements");
        Process.Start(start);
    }
}
