namespace ModelicaStudio.Application.Engine;

public interface IOpenModelicaInstallationLocator
{
    Task<OpenModelicaInstallation?> LocateAsync(
        string? configuredExecutable = null,
        CancellationToken cancellationToken = default);
}
