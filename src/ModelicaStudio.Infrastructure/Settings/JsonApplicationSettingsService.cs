using System.Text.Json;
using ModelicaStudio.Application.Files;
using ModelicaStudio.Application.Settings;
using ModelicaStudio.Domain.Settings;

namespace ModelicaStudio.Infrastructure.Settings;

public sealed class JsonApplicationSettingsService : IApplicationSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IAtomicFileWriter _fileWriter;

    public JsonApplicationSettingsService(IAtomicFileWriter fileWriter, string settingsFilePath)
    {
        _fileWriter = fileWriter;
        SettingsFilePath = Path.GetFullPath(settingsFilePath);
    }

    public string SettingsFilePath { get; }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new ApplicationSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (settings?.FormatVersion != ApplicationSettings.CurrentFormatVersion)
            {
                return new ApplicationSettings();
            }

            return Normalize(settings);
        }
        catch (JsonException)
        {
            // Preserve the unreadable file for recovery and start with safe defaults.
            return new ApplicationSettings();
        }
        catch (NotSupportedException)
        {
            return new ApplicationSettings();
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.FormatVersion != ApplicationSettings.CurrentFormatVersion)
        {
            throw new ArgumentException("The application settings format version is not supported.", nameof(settings));
        }

        var normalized = Normalize(settings);
        var json = JsonSerializer.Serialize(normalized, SerializerOptions) + Environment.NewLine;
        await _fileWriter.WriteTextAsync(SettingsFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        var executablePath = string.IsNullOrWhiteSpace(settings.OpenModelicaExecutablePath)
            ? null
            : Path.GetFullPath(settings.OpenModelicaExecutablePath);
        var recentProjects = (settings.RecentProjectPaths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Take(20)
            .ToArray();
        return settings with
        {
            OpenModelicaExecutablePath = executablePath,
            RecentProjectPaths = recentProjects,
        };
    }
}
