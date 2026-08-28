using System.Text.Json;
using System.Text.Json.Serialization;
using CmdletForge.Models;

namespace CmdletForge.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cmdlet Forge");

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                   ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("Instellingen konden niet worden geladen; standaardwaarden worden gebruikt.", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));

        if (File.Exists(SettingsPath))
            File.Replace(temporaryPath, SettingsPath, null);
        else
            File.Move(temporaryPath, SettingsPath);
    }
}
