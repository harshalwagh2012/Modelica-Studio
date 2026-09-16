namespace ModelicaStudio.Application.Files;

public interface IAtomicFileWriter
{
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
}
