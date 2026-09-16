using ModelicaStudio.Domain.Settings;

namespace ModelicaStudio.Application.Settings;

public interface IApplicationSettingsService
{
    string SettingsFilePath { get; }

    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
