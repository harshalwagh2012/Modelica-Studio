namespace ModelicaStudio.Domain.Graphics;

public static class ModelicaGrid
{
    public static ModelicaPoint Snap(ModelicaPoint point, ModelicaPoint spacing) => new(
        SnapCoordinate(point.X, spacing.X),
        SnapCoordinate(point.Y, spacing.Y));

    public static ModelicaPoint Move(ModelicaPoint point, ModelicaPoint spacing, int horizontalSteps, int verticalSteps) =>
        new(
            point.X + (ValidSpacing(spacing.X) * horizontalSteps),
            point.Y + (ValidSpacing(spacing.Y) * verticalSteps));

    private static double SnapCoordinate(double value, double spacing) =>
        double.IsFinite(value) && double.IsFinite(spacing) && spacing > 0
            ? Math.Round(value / spacing, MidpointRounding.AwayFromZero) * spacing
            : value;

    private static double ValidSpacing(double spacing) =>
        double.IsFinite(spacing) && spacing > 0 ? spacing : 0;
}
