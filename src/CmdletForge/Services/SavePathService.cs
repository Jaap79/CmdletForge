namespace CmdletForge.Services;

public static class SavePathService
{
    public static SavePathResolution Resolve(string directory, string fileName, string? defaultExtension)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return SavePathResolution.Failure("Vul een bestandsnaam in.");

        var normalizedName = fileName.Trim();
        if (normalizedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || normalizedName.Contains(Path.DirectorySeparatorChar)
            || normalizedName.Contains(Path.AltDirectorySeparatorChar))
        {
            return SavePathResolution.Failure("De bestandsnaam bevat ongeldige tekens. Kies de map via het padveld hierboven.");
        }

        if (string.IsNullOrEmpty(Path.GetExtension(normalizedName)) && !string.IsNullOrWhiteSpace(defaultExtension))
        {
            var extension = defaultExtension.StartsWith('.') ? defaultExtension : $".{defaultExtension}";
            normalizedName += extension;
        }

        try
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (!Directory.Exists(fullDirectory))
                return SavePathResolution.Failure("Deze map bestaat niet of is niet bereikbaar.");

            return SavePathResolution.Success(Path.Combine(fullDirectory, normalizedName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return SavePathResolution.Failure(ex.Message);
        }
    }
}

public sealed record SavePathResolution(bool IsValid, string? Path, string? Error)
{
    public static SavePathResolution Success(string path) => new(true, path, null);
    public static SavePathResolution Failure(string error) => new(false, null, error);
}
