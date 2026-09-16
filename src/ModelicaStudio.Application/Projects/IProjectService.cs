using ModelicaStudio.Domain.Projects;

namespace ModelicaStudio.Application.Projects;

public interface IProjectService
{
    Task<ModelicaProjectDocument> CreateAsync(
        string projectFilePath,
        string name,
        string? rootPackage = null,
        CancellationToken cancellationToken = default);

    Task<ModelicaProjectDocument> LoadAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ModelicaProjectDocument project,
        CancellationToken cancellationToken = default);
}

public sealed class InvalidModelicaProjectException(string message, Exception? innerException = null)
    : Exception(message, innerException);
