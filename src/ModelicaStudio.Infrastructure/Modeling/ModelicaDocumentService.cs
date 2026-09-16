using ModelicaStudio.Application.Files;
using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Infrastructure.Modeling;

public sealed class ModelicaDocumentService(IAtomicFileWriter atomicFileWriter) : IModelicaDocumentService
{
    public ModelDocument Create(NewModelicaClass definition, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var path = sourcePath is null ? null : Path.GetFullPath(sourcePath);
        return new ModelDocument(definition.Name, path, ModelicaSourceGenerator.Generate(definition), isNew: true);
    }

    public async Task<ModelDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Modelica source file was not found.", fullPath);
        }

        var source = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var className = ModelicaSourceGenerator.TryGetTopLevelClassName(source);
        var document = new ModelDocument(className ?? Path.GetFileNameWithoutExtension(fullPath), fullPath, source);
        if (className is null)
        {
            document.MarkInvalidSource();
        }

        return document;
    }

    public async Task SaveAsync(
        ModelDocument document,
        string? destinationPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = destinationPath ?? document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("A destination path is required for a new Modelica document.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
        {
            fullPath += ".mo";
        }

        await atomicFileWriter.WriteTextAsync(fullPath, document.Source, cancellationToken).ConfigureAwait(false);
        document.MarkSaved(fullPath);
    }
}
