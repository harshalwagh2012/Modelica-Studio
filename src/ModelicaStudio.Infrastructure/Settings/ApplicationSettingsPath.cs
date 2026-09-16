using ModelicaStudio.Domain;

namespace ModelicaStudio.Infrastructure.Settings;

public static class ApplicationSettingsPath
{
    public static string GetDefault()
    {
        var root = OperatingSystem.IsLinux()
            ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            : null;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("The operating system did not provide an application settings directory.");
        }

        return Path.Combine(Path.GetFullPath(root), ProductInfo.Name, "settings.json");
    }
}
