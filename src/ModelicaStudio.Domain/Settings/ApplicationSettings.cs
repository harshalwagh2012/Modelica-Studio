namespace ModelicaStudio.Domain.Settings;

public sealed record ApplicationSettings
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public string? OpenModelicaExecutablePath { get; init; }
    public IReadOnlyList<string> RecentProjectPaths { get; init; } = [];
}
