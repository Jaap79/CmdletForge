using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmdletForge.Models;

namespace CmdletForge.Services;

public sealed class AppUpdateService : IDisposable
{
    private static readonly Regex RepositoryPattern = new("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly HttpClient _http = new();

    public AppUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CmdletForge", AppInfo.Version.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public bool IsConfigured => RepositoryPattern.IsMatch(AppInfo.UpdateRepository);

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("De updatebron is niet in deze build geconfigureerd.");

        var api = new Uri($"https://api.github.com/repos/{AppInfo.UpdateRepository}/releases/latest");
        using var response = await _http.GetAsync(api, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V')
                  ?? throw new InvalidDataException("Release heeft geen geldige tag.");
        var normalized = tag.Split('-', 2)[0];
        if (!Version.TryParse(normalized, out var latest))
            throw new InvalidDataException($"Releaseversie '{tag}' is niet herkenbaar.");

        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var executable = FindAsset(assets, "CmdletForge-win-x64.exe");
        var checksum = FindAsset(assets, "CmdletForge-win-x64.exe.sha256");

        return new UpdateInfo(
            AppInfo.Version,
            latest,
            root.GetProperty("name").GetString() ?? $"v{tag}",
            new Uri(root.GetProperty("html_url").GetString() ?? throw new InvalidDataException("Releasepagina ontbreekt.")),
            new Uri(executable),
            new Uri(checksum));
    }

    public async Task<StagedUpdate> DownloadAndVerifyAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cmdlet Forge", "Updates", update.LatestVersion.ToString());
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, "CmdletForge-win-x64.exe");

        using (var response = await _http.GetAsync(update.ExecutableDownload, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(executablePath);
            var buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                if (length is > 0)
                    progress?.Report((double)total / length.Value);
            }
        }

        var checksumText = await _http.GetStringAsync(update.ChecksumDownload, cancellationToken).ConfigureAwait(false);
        var expected = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant();
        if (expected is null || !Regex.IsMatch(expected, "^[A-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Het release-checksumbestand is ongeldig.");

        await using var file = File.OpenRead(executablePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
        {
            File.Delete(executablePath);
            throw new InvalidDataException("SHA-256-controle van de update is mislukt; de download is verwijderd.");
        }

        return new StagedUpdate(update, executablePath, actual);
    }

    public static void ApplyAfterExit(StagedUpdate staged)
    {
        var currentExecutable = Environment.ProcessPath
                                ?? throw new InvalidOperationException("Het huidige executablepad is onbekend.");
        var scriptDirectory = Path.Combine(Path.GetTempPath(), "Cmdlet Forge");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, $"update-{Guid.NewGuid():N}.ps1");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $source = '{{Escape(staged.ExecutablePath)}}'
            $target = '{{Escape(currentExecutable)}}'
            Wait-Process -Id {{Environment.ProcessId}} -Timeout 120 -ErrorAction SilentlyContinue
            Copy-Item -LiteralPath $source -Destination $target -Force
            Start-Process -FilePath $target
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        var start = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        Process.Start(start);
    }

    private static string FindAsset(IEnumerable<JsonElement> assets, string name)
    {
        foreach (var asset in assets)
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }
        throw new InvalidDataException($"Vereist releasebestand ontbreekt: {name}");
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    public void Dispose() => _http.Dispose();
}
