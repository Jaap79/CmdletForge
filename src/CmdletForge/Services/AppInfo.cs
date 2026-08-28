using System.Reflection;

namespace CmdletForge.Services;

public static class AppInfo
{
    public static Version Version
    {
        get
        {
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
            return new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(0, assemblyVersion.Build));
        }
    }

    public static string UpdateRepository =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "UpdateRepository")?.Value ?? string.Empty;
}
