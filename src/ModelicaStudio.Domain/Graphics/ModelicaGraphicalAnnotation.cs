namespace ModelicaStudio.Domain.Graphics;

public enum ModelicaGraphicKind
{
    Unknown,
    Rectangle,
    Ellipse,
    Line,
    Polygon,
    Text,
    Bitmap,
}

public enum ModelicaLinePattern
{
    Unknown,
    None,
    Solid,
    Dash,
    Dot,
    DashDot,
    DashDotDot,
}

public enum ModelicaArrow
{
    Unknown,
    None,
    Open,
    Filled,
    Half,
}

public enum ModelicaSmooth
{
    Unknown,
    None,
    Bezier,
}

public enum ModelicaFillPattern
{
    Unknown,
    None,
    Solid,
    Horizontal,
    Vertical,
    Cross,
    Forward,
    Backward,
    CrossDiag,
    HorizontalCylinder,
    VerticalCylinder,
    Sphere,
}

public enum ModelicaBorderPattern
{
    Unknown,
    None,
    Raised,
    Sunken,
    Engraved,
}

public enum ModelicaEllipseClosure
{
    Unknown,
    None,
    Chord,
    Radial,
}

public enum ModelicaTextAlignment
{
    Unknown,
    Left,
    Center,
    Right,
}

public readonly record struct ModelicaColor(int Red, int Green, int Blue)
{
    public static ModelicaColor Black { get; } = new(0, 0, 0);
    public bool UsesInheritedColor => Red < 0 || Green < 0 || Blue < 0;
}

public sealed record ModelicaAnnotationValue<T>(T StaticValue, string? DynamicExpressionJson = null)
{
    public bool IsDynamic => DynamicExpressionJson is not null;
}

public sealed record ModelicaGraphicStyle
{
    public ModelicaColor LineColor { get; init; } = ModelicaColor.Black;
    public ModelicaColor FillColor { get; init; } = ModelicaColor.Black;
    public ModelicaLinePattern LinePattern { get; init; } = ModelicaLinePattern.Solid;
    public ModelicaFillPattern FillPattern { get; init; } = ModelicaFillPattern.None;
    public double LineThickness { get; init; } = 0.25;
}

public sealed record ModelicaGraphicPrimitive
{
    public required ModelicaGraphicKind Kind { get; init; }
    public required ModelicaAnnotationValue<bool> Visible { get; init; }
    public required ModelicaAnnotationValue<ModelicaPoint> Origin { get; init; }
    public required ModelicaAnnotationValue<double> Rotation { get; init; }
    public ModelicaGraphicStyle Style { get; init; } = new();
    public ModelicaAnnotationValue<ModelicaExtent>? Extent { get; init; }
    public IReadOnlyList<ModelicaPoint> Points { get; init; } = [];
    public ModelicaBorderPattern BorderPattern { get; init; }
    public ModelicaEllipseClosure EllipseClosure { get; init; }
    public double Radius { get; init; }
    public double StartAngle { get; init; }
    public double EndAngle { get; init; } = 360;
    public string? TextString { get; init; }
    public double FontSize { get; init; }
    public ModelicaColor TextColor { get; init; } = ModelicaColor.Black;
    public string? FontName { get; init; }
    public IReadOnlyList<string> TextStyles { get; init; } = [];
    public ModelicaTextAlignment TextAlignment { get; init; }
    public IReadOnlyList<ModelicaArrow> Arrows { get; init; } = [ModelicaArrow.None, ModelicaArrow.None];
    public double ArrowSize { get; init; } = 3;
    public ModelicaSmooth Smooth { get; init; } = ModelicaSmooth.None;
    public string? FileName { get; init; }
    public string? ImageSource { get; init; }
    public required string RawJson { get; init; }
}

public sealed record ModelicaGraphicalCoordinateSystem
{
    public ModelicaCoordinateSystem Coordinates { get; init; } = ModelicaCoordinateSystem.Default;
    public double InitialScale { get; init; } = 0.1;
    public ModelicaPoint Grid { get; init; } = new(2, 2);
}

public sealed record ModelicaGraphicalView(
    ModelicaGraphicalCoordinateSystem CoordinateSystem,
    IReadOnlyList<ModelicaGraphicPrimitive> Graphics);

public sealed record ModelicaInheritedGraphicalAnnotation(
    string BaseClassName,
    ModelicaGraphicalView? Icon,
    ModelicaGraphicalView? Diagram);

public sealed record ModelicaAnnotationIssue(string Message, string? RawJson = null);

public sealed record ModelicaGraphicalAnnotationSnapshot(
    string ClassName,
    string Restriction,
    ModelicaGraphicalView? Icon,
    ModelicaGraphicalView? Diagram,
    IReadOnlyList<ModelicaInheritedGraphicalAnnotation> InheritedAnnotations,
    IReadOnlyList<ModelicaAnnotationIssue> Issues,
    string RawJson);
