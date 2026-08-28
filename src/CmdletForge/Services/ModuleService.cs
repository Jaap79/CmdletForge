using System.Text.Json;
using System.Text.RegularExpressions;
using CmdletForge.Models;

namespace CmdletForge.Services;

public sealed class ModuleService(string powerShellExecutable)
{
    private static readonly Regex SafeModuleName = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> SuggestedModules { get; } =
    [
        "Az",
        "Microsoft.Graph",
        "ExchangeOnlineManagement",
        "MicrosoftTeams",
        "PnP.PowerShell",
        "Microsoft.PowerShell.SecretManagement",
        "Microsoft.PowerShell.SecretStore",
        "PSScriptAnalyzer",
        "Microsoft.WinGet.Client"
    ];

    public async Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        var names = string.Join(',', SuggestedModules.Select(name => $"'{Escape(name)}'"));
        var command = $$"""
            $names = @({{names}})
            $installed = Get-Module -ListAvailable | Group-Object Name | ForEach-Object {
              $_.Group | Sort-Object Version -Descending | Select-Object -First 1
            }
            $result = foreach ($name in $names) {
              $local = $installed | Where-Object Name -eq $name | Select-Object -First 1
              $remote = $null
              try {
                if (Get-Command Find-PSResource -ErrorAction SilentlyContinue) {
                  $remote = Find-PSResource -Name $name -Repository PSGallery -ErrorAction Stop | Select-Object -First 1
                } elseif (Get-Command Find-Module -ErrorAction SilentlyContinue) {
                  $remote = Find-Module -Name $name -Repository PSGallery -ErrorAction Stop | Select-Object -First 1
                }
              } catch {}
              [pscustomobject]@{
                Name = $name
                InstalledVersion = if ($local) { $local.Version.ToString() } else { '' }
                AvailableVersion = if ($remote) { $remote.Version.ToString() } else { '' }
                Description = if ($remote.Description) { $remote.Description } else { '' }
              }
            }
            $result | ConvertTo-Json -Depth 3 -Compress
            """;

        var result = await PowerShellProcess.RunEncodedAsync(powerShellExecutable, command, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(PreferError(result));

        return DeserializeModules(result.StandardOutput);
    }

    public async Task InstallAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        ValidateName(moduleName);
        var name = Escape(moduleName);
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            if (Get-Command Install-PSResource -ErrorAction SilentlyContinue) {
              Install-PSResource -Name '{{name}}' -Scope CurrentUser -Repository PSGallery -TrustRepository -Quiet
            } elseif (Get-Command Install-Module -ErrorAction SilentlyContinue) {
              Install-Module -Name '{{name}}' -Scope CurrentUser -Repository PSGallery -Force -AllowClobber -Confirm:$false
            } else {
              throw 'PSResourceGet en PowerShellGet ontbreken; installeer eerst een recente PowerShell-versie.'
            }
            """;
        await RunActionAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        ValidateName(moduleName);
        var name = Escape(moduleName);
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            if (Get-Command Update-PSResource -ErrorAction SilentlyContinue) {
              Update-PSResource -Name '{{name}}' -Scope CurrentUser -Repository PSGallery -TrustRepository -Quiet -Confirm:$false
            } elseif (Get-Command Update-Module -ErrorAction SilentlyContinue) {
              Update-Module -Name '{{name}}' -Repository PSGallery -Force -Confirm:$false
            } else {
              throw 'Geen ondersteunde module-updateprovider gevonden.'
            }
            """;
        await RunActionAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunActionAsync(string command, CancellationToken cancellationToken)
    {
        var result = await PowerShellProcess.RunEncodedAsync(powerShellExecutable, command, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(PreferError(result));
    }

    private static IReadOnlyList<ModuleInfo> DeserializeModules(string json)
    {
        var trimmed = json.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return [];

        using var document = JsonDocument.Parse(trimmed);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : [document.RootElement];

        return elements.Select(element => new ModuleInfo(
            GetString(element, "Name"),
            GetString(element, "InstalledVersion"),
            GetString(element, "AvailableVersion"),
            GetString(element, "Description"))).ToArray();
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void ValidateName(string moduleName)
    {
        if (!SafeModuleName.IsMatch(moduleName))
            throw new ArgumentException("Ongeldige modulenaam.", nameof(moduleName));
    }

    private static string PreferError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim();
}
