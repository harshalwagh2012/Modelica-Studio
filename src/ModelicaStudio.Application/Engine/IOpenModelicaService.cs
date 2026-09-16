using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.Domain.Simulation;

namespace ModelicaStudio.Application.Engine;

public interface IOpenModelicaService : IAsyncDisposable
{
    OpenModelicaSessionState State { get; }

    event EventHandler<OpenModelicaStateChangedEventArgs>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    Task ConfigureExecutableAsync(string executablePath, CancellationToken cancellationToken = default);

    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    Task<bool> LoadModelAsync(string library, CancellationToken cancellationToken = default);

    Task<bool> LoadFileAsync(string path, CancellationToken cancellationToken = default);

    Task<ModelCheckResult> CheckModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelicaClassInfo>> GetClassNamesAsync(
        string parentClass,
        CancellationToken cancellationToken = default);

    Task<ModelicaGraphicalAnnotationSnapshot> GetGraphicalAnnotationAsync(
        string className,
        CancellationToken cancellationToken = default);

    Task<ModelicaModelInstanceSnapshot> GetModelInstanceAsync(
        string className,
        CancellationToken cancellationToken = default);

    Task<string> GetDefaultComponentNameAsync(
        string typeName,
        CancellationToken cancellationToken = default);

    Task<bool> AddComponentAsync(
        string className,
        string componentName,
        string typeName,
        ModelicaPlacement placement,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteComponentAsync(
        string className,
        string componentName,
        CancellationToken cancellationToken = default);

    Task<bool> AddConnectionAsync(
        string className,
        string from,
        string to,
        ModelicaGraphicPrimitive line,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteConnectionAsync(
        string className,
        string from,
        string to,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateConnectionAnnotationAsync(
        string className,
        string from,
        string to,
        ModelicaGraphicPrimitive line,
        CancellationToken cancellationToken = default);

    Task<bool> SetComponentPlacementAsync(
        string className,
        string componentName,
        ModelicaPlacement placement,
        CancellationToken cancellationToken = default);

    Task<bool> SaveClassAsync(
        string className,
        CancellationToken cancellationToken = default);

    Task<bool> LoadStringAsync(
        string source,
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<bool> LoadClassContentStringAsync(
        string content,
        string className,
        int offsetX,
        int offsetY,
        CancellationToken cancellationToken = default);

    Task<SimulationResult> SimulateAsync(
        string modelName,
        SimulationConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadSimulationResultVariablesAsync(
        string resultFile,
        CancellationToken cancellationToken = default);

    Task<SimulationSeries> ReadSimulationSeriesAsync(
        string resultFile,
        string variableName,
        CancellationToken cancellationToken = default);
}
