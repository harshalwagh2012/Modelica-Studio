namespace ModelicaStudio.Domain.Simulation;

public sealed record SimulationConfiguration
{
    public double StartTime { get; init; } = 0;
    public double StopTime { get; init; } = 1;
    public int NumberOfIntervals { get; init; } = 500;
    public double Tolerance { get; init; } = 1e-6;
    public string? Method { get; init; } = "dassl";
    public string OutputFormat { get; init; } = "mat";
    public string? VariableFilter { get; init; }
    public string? ResultFileName { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(StartTime) || !double.IsFinite(StopTime) || StopTime <= StartTime)
        {
            throw new ArgumentOutOfRangeException(nameof(StopTime), "Stop time must be finite and greater than start time.");
        }

        if (NumberOfIntervals <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(NumberOfIntervals), "The interval count must be positive.");
        }

        if (!double.IsFinite(Tolerance) || Tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Tolerance), "Tolerance must be finite and positive.");
        }

        if (string.IsNullOrWhiteSpace(OutputFormat))
        {
            throw new ArgumentException("An output format is required.", nameof(OutputFormat));
        }
    }
}
