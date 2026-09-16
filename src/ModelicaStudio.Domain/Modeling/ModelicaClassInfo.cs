namespace ModelicaStudio.Domain.Modeling;

public enum ModelicaClassKind
{
    Unknown,
    Package,
    Model,
    Block,
    Connector,
    Record,
    Function,
    Type,
    Class,
}

public sealed record ModelicaClassInfo(
    string FullName,
    string Name,
    ModelicaClassKind Kind,
    bool IsPartial = false)
{
    public IReadOnlyList<string> Children { get; init; } = [];
}
