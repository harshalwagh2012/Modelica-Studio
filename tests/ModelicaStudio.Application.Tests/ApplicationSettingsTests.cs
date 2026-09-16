using ModelicaStudio.Domain.Settings;
using ModelicaStudio.Infrastructure.Files;
using ModelicaStudio.Infrastructure.Settings;

namespace ModelicaStudio.Application.Tests;

public sealed class ApplicationSettingsTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "ModelicaStudio.ApplicationSettings.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveLoad_RoundTripsAndNormalizesSettings()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "settings.json");
        var executablePath = Path.Combine(_temporaryDirectory, "OpenModelica", "bin", "omc");
        var projects = Enumerable.Range(0, 25)
            .Select(index => Path.Combine(_temporaryDirectory, $"Project{index}.modelicaproj"))
            .Append(Path.Combine(_temporaryDirectory, "Project0.modelicaproj"))
            .ToArray();
        var service = new JsonApplicationSettingsService(new AtomicFileWriter(), settingsPath);

        await service.SaveAsync(new ApplicationSettings
        {
            OpenModelicaExecutablePath = executablePath,
            RecentProjectPaths = projects,
        });
        var loaded = await service.LoadAsync();

        Assert.Equal(Path.GetFullPath(executablePath), loaded.OpenModelicaExecutablePath);
        Assert.Equal(20, loaded.RecentProjectPaths.Count);
        Assert.Equal(Path.GetFullPath(projects[0]), loaded.RecentProjectPaths[0]);
        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Load_CorruptJsonPreservesFileAndReturnsDefaults()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var settingsPath = Path.Combine(_temporaryDirectory, "settings.json");
        const string corruptJson = "{ this is not json";
        await File.WriteAllTextAsync(settingsPath, corruptJson);
        var service = new JsonApplicationSettingsService(new AtomicFileWriter(), settingsPath);

        var loaded = await service.LoadAsync();

        Assert.Equal(ApplicationSettings.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Null(loaded.OpenModelicaExecutablePath);
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task Load_UnsupportedVersionPreservesFileAndReturnsDefaults()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var settingsPath = Path.Combine(_temporaryDirectory, "settings.json");
        const string unsupportedJson = "{\"formatVersion\":99,\"openModelicaExecutablePath\":\"/invalid/omc\"}";
        await File.WriteAllTextAsync(settingsPath, unsupportedJson);
        var service = new JsonApplicationSettingsService(new AtomicFileWriter(), settingsPath);

        var loaded = await service.LoadAsync();

        Assert.Null(loaded.OpenModelicaExecutablePath);
        Assert.Equal(unsupportedJson, await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task Save_UnsupportedVersionIsRejected()
    {
        var service = new JsonApplicationSettingsService(
            new AtomicFileWriter(),
            Path.Combine(_temporaryDirectory, "settings.json"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(new ApplicationSettings { FormatVersion = 99 }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
