using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Application.Modeling;

public interface IModelicaDocumentService
{
    ModelDocument Create(NewModelicaClass definition, string? sourcePath = null);

    Task<ModelDocument> OpenAsync(string path, CancellationToken cancellationToken = default);

    Task SaveAsync(
        ModelDocument document,
        string? destinationPath = null,
        CancellationToken cancellationToken = default);
}
