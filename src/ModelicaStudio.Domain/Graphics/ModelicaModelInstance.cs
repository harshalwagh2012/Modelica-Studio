namespace ModelicaStudio.Domain.Graphics;

public sealed record ModelicaTransformation(
    ModelicaPoint Origin,
    ModelicaExtent Extent,
    double Rotation)
{
    public static ModelicaTransformation Default { get; } = new(
        new ModelicaPoint(0, 0),
        new ModelicaExtent(new ModelicaPoint(-10, -10), new ModelicaPoint(10, 10)),
        0);
}

public sealed record ModelicaPlacement(
    bool Visible,
    ModelicaTransformation Transformation,
    bool IconVisible,
    ModelicaTransformation? IconTransformation);

public sealed record ModelicaComponentPrefixes(
    bool IsPublic = true,
    bool IsFinal = false,
    bool IsInner = false,
    bool IsOuter = false,
    bool IsReplaceable = false,
    bool IsRedeclare = false,
    string? Connector = null,
    string? Variability = null,
    string? Direction = null);

public sealed record ModelicaComponentInstance(
    string Name,
    string TypeName,
    string Restriction,
    ModelicaComponentPrefixes Prefixes,
    ModelicaPlacement? Placement,
    ModelicaGraphicalAnnotationSnapshot? TypeGraphics,
    bool IsInherited,
    string DeclaringClass,
    string RawJson)
{
    public IReadOnlyList<ModelicaComponentInstance> TypeComponents { get; init; } = [];
    public IReadOnlyList<string> Dimensions { get; init; } = [];
    public string? RootTypeName { get; init; }
    public ModelicaComponentPrefixes TypePrefixes { get; init; } = new();
}

public sealed record ModelicaDiagramConnection(
    string Left,
    string Right,
    ModelicaGraphicPrimitive? Line,
    bool IsInherited,
    string DeclaringClass,
    string RawJson);

public sealed record ModelicaModelInstanceSnapshot(
    string ClassName,
    string Restriction,
    ModelicaGraphicalAnnotationSnapshot ClassGraphics,
    IReadOnlyList<ModelicaComponentInstance> Components,
    IReadOnlyList<ModelicaDiagramConnection> Connections,
    IReadOnlyList<ModelicaAnnotationIssue> Issues,
    string RawJson);

public static class ModelicaPlacementTransform
{
    public static ModelicaPoint Apply(
        ModelicaPoint point,
        ModelicaExtent sourceExtent,
        ModelicaTransformation transformation)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        if (sourceExtent.Width <= 0 || sourceExtent.Height <= 0)
        {
            throw new ArgumentException("The source extent must have positive width and height.", nameof(sourceExtent));
        }

        if (!double.IsFinite(transformation.Rotation))
        {
            throw new ArgumentOutOfRangeException(nameof(transformation), "Rotation must be finite.");
        }

        var xFraction = (point.X - sourceExtent.First.X) / (sourceExtent.Second.X - sourceExtent.First.X);
        var yFraction = (point.Y - sourceExtent.First.Y) / (sourceExtent.Second.Y - sourceExtent.First.Y);
        var localPoint = new ModelicaPoint(
            transformation.Extent.First.X
                + (xFraction * (transformation.Extent.Second.X - transformation.Extent.First.X)),
            transformation.Extent.First.Y
                + (yFraction * (transformation.Extent.Second.Y - transformation.Extent.First.Y)));
        return ModelicaGraphicTransform.Apply(localPoint, transformation.Origin, transformation.Rotation);
    }
}
