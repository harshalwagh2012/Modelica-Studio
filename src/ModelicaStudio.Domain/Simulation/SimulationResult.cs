using ModelicaStudio.Domain.Diagnostics;

namespace ModelicaStudio.Domain.Simulation;

public sealed record SimulationResult(
    bool Success,
    string ModelName,
    string? ResultFile,
    TimeSpan Elapsed,
    string RawResponse,
    IReadOnlyList<CompilerDiagnostic> Diagnostics);

public sealed record SimulationSeries(
    string Name,
    string? Unit,
    IReadOnlyList<double> Time,
    IReadOnlyList<double> Values);
