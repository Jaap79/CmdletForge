namespace CmdletForge.Models;

public sealed record UpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseName,
    Uri ReleasePage,
    Uri ExecutableDownload,
    Uri ChecksumDownload)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

public sealed record StagedUpdate(UpdateInfo Info, string ExecutablePath, string Sha256);
