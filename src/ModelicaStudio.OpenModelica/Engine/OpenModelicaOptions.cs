namespace ModelicaStudio.OpenModelica.Engine;

public sealed record OpenModelicaOptions
{
    public string? OmcPath { get; init; }
    public string? WorkingDirectory { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan SimulationTimeout { get; init; } = TimeSpan.FromMinutes(10);
}
