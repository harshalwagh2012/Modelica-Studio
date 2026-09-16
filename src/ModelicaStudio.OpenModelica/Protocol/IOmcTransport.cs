using ModelicaStudio.Application.Engine;
using ModelicaStudio.OpenModelica.Engine;

namespace ModelicaStudio.OpenModelica.Protocol;

public interface IOmcTransport : IAsyncDisposable
{
    bool IsRunning { get; }

    event EventHandler? UnexpectedExit;

    Task StartAsync(
        OpenModelicaInstallation installation,
        OpenModelicaOptions options,
        CancellationToken cancellationToken = default);

    Task<string> ExecuteAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IOmcTransportFactory
{
    IOmcTransport Create();
}
