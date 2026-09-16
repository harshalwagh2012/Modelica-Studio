namespace ModelicaStudio.Domain.Graphics;

public sealed class ModelicaCoordinateTransformer : IModelicaCoordinateTransformer
{
    public CanvasPoint ModelicaToCanvas(
        ModelicaPoint point,
        ModelicaCoordinateSystem coordinateSystem,
        double canvasWidth,
        double canvasHeight)
    {
        var transform = CreateTransform(coordinateSystem, canvasWidth, canvasHeight);
        return new CanvasPoint(
            transform.OffsetX + ((point.X - coordinateSystem.Extent.MinimumX) * transform.ScaleX),
            transform.OffsetY + ((coordinateSystem.Extent.MaximumY - point.Y) * transform.ScaleY));
    }

    public ModelicaPoint CanvasToModelica(
        CanvasPoint point,
        ModelicaCoordinateSystem coordinateSystem,
        double canvasWidth,
        double canvasHeight)
    {
        var transform = CreateTransform(coordinateSystem, canvasWidth, canvasHeight);
        return new ModelicaPoint(
            coordinateSystem.Extent.MinimumX + ((point.X - transform.OffsetX) / transform.ScaleX),
            coordinateSystem.Extent.MaximumY - ((point.Y - transform.OffsetY) / transform.ScaleY));
    }

    private static Transform CreateTransform(
        ModelicaCoordinateSystem coordinateSystem,
        double canvasWidth,
        double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        if (!double.IsFinite(canvasWidth) || !double.IsFinite(canvasHeight) || canvasWidth <= 0 || canvasHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasWidth), "Canvas dimensions must be finite and positive.");
        }

        var modelWidth = coordinateSystem.Extent.Width;
        var modelHeight = coordinateSystem.Extent.Height;
        if (modelWidth <= 0 || modelHeight <= 0)
        {
            throw new ArgumentException("The Modelica coordinate extent must have positive width and height.", nameof(coordinateSystem));
        }

        var scaleX = canvasWidth / modelWidth;
        var scaleY = canvasHeight / modelHeight;
        if (!coordinateSystem.PreserveAspectRatio)
        {
            return new Transform(scaleX, scaleY, 0, 0);
        }

        var scale = Math.Min(scaleX, scaleY);
        return new Transform(
            scale,
            scale,
            (canvasWidth - (modelWidth * scale)) / 2,
            (canvasHeight - (modelHeight * scale)) / 2);
    }

    private readonly record struct Transform(double ScaleX, double ScaleY, double OffsetX, double OffsetY);
}
