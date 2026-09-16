using ModelicaStudio.Domain.Simulation;

namespace ModelicaStudio.Domain.Projects;

public sealed record ModelicaProject
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required string Name { get; init; }
    public string? RootPackage { get; init; }
    public IReadOnlyList<string> ModelFiles { get; init; } = [];
    public IReadOnlyList<string> AdditionalLibraryPaths { get; init; } = [];
    public IReadOnlyList<string> RecentlyOpenedModels { get; init; } = [];
    public IReadOnlyDictionary<string, SimulationConfiguration> SimulationConfigurations { get; init; } =
        new Dictionary<string, SimulationConfiguration>(StringComparer.Ordinal);
    public ProjectUiState UiState { get; init; } = new();
}

public sealed record ProjectUiState
{
    public double LibraryPanelWidth { get; init; } = 260;
    public double InspectorPanelWidth { get; init; } = 320;
    public double MessagesPanelHeight { get; init; } = 190;
    public IReadOnlyList<string> OpenModels { get; init; } = [];
    public string? ActiveModel { get; init; }
}

public sealed record ModelicaProjectDocument(string ProjectFilePath, ModelicaProject Project)
{
    public string ProjectDirectory => Path.GetDirectoryName(ProjectFilePath)
        ?? throw new InvalidOperationException("The project file does not have a parent directory.");
}
