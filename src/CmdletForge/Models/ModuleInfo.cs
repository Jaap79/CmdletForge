namespace CmdletForge.Models;

public sealed record ModuleInfo(string Name, string InstalledVersion, string AvailableVersion, string Description)
{
    public bool IsInstalled => !string.IsNullOrWhiteSpace(InstalledVersion);
    public bool HasUpdate => Version.TryParse(InstalledVersion, out var installed)
                             && Version.TryParse(AvailableVersion, out var available)
                             && available > installed;
}
