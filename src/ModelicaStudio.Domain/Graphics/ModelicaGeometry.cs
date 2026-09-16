namespace ModelicaStudio.Domain.Graphics;

public readonly record struct ModelicaPoint(double X, double Y);

public readonly record struct CanvasPoint(double X, double Y);

public static class ModelicaGraphicTransform
{
    public static ModelicaPoint Apply(ModelicaPoint point, ModelicaPoint origin, double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees), "Rotation must be finite.");
        }

        var radians = rotationDegrees * (Math.PI / 180d);
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new ModelicaPoint(
            origin.X + (point.X * cosine) - (point.Y * sine),
            origin.Y + (point.X * sine) + (point.Y * cosine));
    }
}

public readonly record struct ModelicaExtent(ModelicaPoint First, ModelicaPoint Second)
{
    public double Width => Math.Abs(Second.X - First.X);
    public double Height => Math.Abs(Second.Y - First.Y);
    public double MinimumX => Math.Min(First.X, Second.X);
    public double MaximumX => Math.Max(First.X, Second.X);
    public double MinimumY => Math.Min(First.Y, Second.Y);
    public double MaximumY => Math.Max(First.Y, Second.Y);
}

public sealed record ModelicaCoordinateSystem(
    ModelicaExtent Extent,
    bool PreserveAspectRatio = true)
{
    public static ModelicaCoordinateSystem Default { get; } = new(
        new ModelicaExtent(new ModelicaPoint(-100, -100), new ModelicaPoint(100, 100)));
}

public interface IModelicaCoordinateTransformer
{
    CanvasPoint ModelicaToCanvas(
        ModelicaPoint point,
        ModelicaCoordinateSystem coordinateSystem,
        double canvasWidth,
        double canvasHeight);

    ModelicaPoint CanvasToModelica(
        CanvasPoint point,
        ModelicaCoordinateSystem coordinateSystem,
        double canvasWidth,
        double canvasHeight);
}
