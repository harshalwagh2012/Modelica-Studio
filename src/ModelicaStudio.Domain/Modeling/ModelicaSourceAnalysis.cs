namespace ModelicaStudio.Domain.Modeling;

public enum ModelicaSourceSymbolKind
{
    Model,
    Package,
    Block,
    Connector,
    Record,
    Function,
    Class,
    Type,
    Parameter,
    Input,
    Output,
    Constant,
    Extends,
    EquationSection,
    AlgorithmSection,
}

public sealed record ModelicaSourceSymbol(
    string Name,
    ModelicaSourceSymbolKind Kind,
    int Line,
    int Column,
    int NestingLevel,
    string? TypeName = null);

public sealed record ModelicaSourceAnalysisIssue(
    string Message,
    int Line,
    int Column);

public sealed record ModelicaSourceAnalysis(
    string? WithinPackage,
    IReadOnlyList<ModelicaSourceSymbol> Symbols,
    IReadOnlyList<ModelicaSourceAnalysisIssue> Issues)
{
    public static ModelicaSourceAnalysis Empty { get; } = new(null, [], []);
}
