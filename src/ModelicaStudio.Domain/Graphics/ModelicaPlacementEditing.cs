namespace ModelicaStudio.Domain.Graphics;

public enum ModelicaPlacementEditKind
{
    Move,
    Resize,
    Rotate,
}

public enum ModelicaPlacementHandle
{
    First,
    SecondXFirstY,
    Second,
    FirstXSecondY,
    Rotation,
}

public sealed record ModelicaComponentTransformationEdit(
    string ComponentName,
    ModelicaTransformation Transformation);

public static class ModelicaPlacementEditor
{
    public const double DefaultRotationHandleOffset = 12;

    public static IReadOnlyDictionary<ModelicaPlacementHandle, ModelicaPoint> GetHandlePoints(
        ModelicaTransformation transformation,
        double rotationHandleOffset = DefaultRotationHandleOffset)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        if (!double.IsFinite(rotationHandleOffset) || rotationHandleOffset <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotationHandleOffset),
                "The rotation handle offset must be finite and positive.");
        }

        var extent = transformation.Extent;
        var rotationHandle = new ModelicaPoint(
            (extent.First.X + extent.Second.X) / 2,
            extent.MaximumY + rotationHandleOffset);

        return new Dictionary<ModelicaPlacementHandle, ModelicaPoint>
        {
            [ModelicaPlacementHandle.First] = TransformLocal(
                transformation,
                extent.First),
            [ModelicaPlacementHandle.SecondXFirstY] = TransformLocal(
                transformation,
                new ModelicaPoint(extent.Second.X, extent.First.Y)),
            [ModelicaPlacementHandle.Second] = TransformLocal(
                transformation,
                extent.Second),
            [ModelicaPlacementHandle.FirstXSecondY] = TransformLocal(
                transformation,
                new ModelicaPoint(extent.First.X, extent.Second.Y)),
            [ModelicaPlacementHandle.Rotation] = TransformLocal(transformation, rotationHandle),
        };
    }

    public static ModelicaPlacementHandle? HitTestHandle(
        ModelicaTransformation transformation,
        ModelicaPoint point,
        double tolerance,
        double rotationHandleOffset = DefaultRotationHandleOffset)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be finite and non-negative.");
        }

        var toleranceSquared = tolerance * tolerance;
        foreach (var handle in GetHandlePoints(transformation, rotationHandleOffset)
                     .OrderByDescending(pair => pair.Key == ModelicaPlacementHandle.Rotation))
        {
            var deltaX = point.X - handle.Value.X;
            var deltaY = point.Y - handle.Value.Y;
            if ((deltaX * deltaX) + (deltaY * deltaY) <= toleranceSquared)
            {
                return handle.Key;
            }
        }

        return null;
    }

    public static ModelicaTransformation Resize(
        ModelicaTransformation transformation,
        ModelicaPlacementHandle handle,
        ModelicaPoint point,
        ModelicaPoint grid)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        if (handle == ModelicaPlacementHandle.Rotation)
        {
            throw new ArgumentException("The rotation handle cannot resize a placement.", nameof(handle));
        }

        ValidateGrid(grid);
        var localPoint = InverseTransform(transformation, point);
        var snappedPoint = new ModelicaPoint(
            Snap(localPoint.X, grid.X),
            Snap(localPoint.Y, grid.Y));
        var first = transformation.Extent.First;
        var second = transformation.Extent.Second;

        switch (handle)
        {
            case ModelicaPlacementHandle.First:
                first = new ModelicaPoint(
                    PreventCollapse(snappedPoint.X, second.X, first.X, grid.X),
                    PreventCollapse(snappedPoint.Y, second.Y, first.Y, grid.Y));
                break;
            case ModelicaPlacementHandle.SecondXFirstY:
                second = second with
                {
                    X = PreventCollapse(snappedPoint.X, first.X, second.X, grid.X),
                };
                first = first with
                {
                    Y = PreventCollapse(snappedPoint.Y, second.Y, first.Y, grid.Y),
                };
                break;
            case ModelicaPlacementHandle.Second:
                second = new ModelicaPoint(
                    PreventCollapse(snappedPoint.X, first.X, second.X, grid.X),
                    PreventCollapse(snappedPoint.Y, first.Y, second.Y, grid.Y));
                break;
            case ModelicaPlacementHandle.FirstXSecondY:
                first = first with
                {
                    X = PreventCollapse(snappedPoint.X, second.X, first.X, grid.X),
                };
                second = second with
                {
                    Y = PreventCollapse(snappedPoint.Y, first.Y, second.Y, grid.Y),
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(handle), handle, "Unknown placement handle.");
        }

        return transformation with { Extent = new ModelicaExtent(first, second) };
    }

    public static ModelicaTransformation RotateTowards(
        ModelicaTransformation transformation,
        ModelicaPoint point,
        double snapDegrees = 15)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        if (!double.IsFinite(snapDegrees) || snapDegrees <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapDegrees), "The rotation snap must be finite and positive.");
        }

        var deltaX = point.X - transformation.Origin.X;
        var deltaY = point.Y - transformation.Origin.Y;
        if (Math.Abs(deltaX) < double.Epsilon && Math.Abs(deltaY) < double.Epsilon)
        {
            return transformation;
        }

        var pointerAngle = Math.Atan2(deltaY, deltaX) * (180d / Math.PI);
        var rotation = Snap(pointerAngle - 90, snapDegrees);
        return transformation with { Rotation = NormalizeDegrees(rotation) };
    }

    public static ModelicaTransformation RotateBy(
        ModelicaTransformation transformation,
        double deltaDegrees)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        if (!double.IsFinite(deltaDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaDegrees), "The rotation delta must be finite.");
        }

        return transformation with { Rotation = NormalizeDegrees(transformation.Rotation + deltaDegrees) };
    }

    private static ModelicaPoint TransformLocal(
        ModelicaTransformation transformation,
        ModelicaPoint localPoint) =>
        ModelicaGraphicTransform.Apply(localPoint, transformation.Origin, transformation.Rotation);

    private static ModelicaPoint InverseTransform(
        ModelicaTransformation transformation,
        ModelicaPoint point)
    {
        var translated = new ModelicaPoint(
            point.X - transformation.Origin.X,
            point.Y - transformation.Origin.Y);
        return ModelicaGraphicTransform.Apply(translated, new ModelicaPoint(0, 0), -transformation.Rotation);
    }

    private static double PreventCollapse(double value, double opposite, double previous, double minimumDistance)
    {
        if (Math.Abs(value - opposite) >= minimumDistance)
        {
            return value;
        }

        var direction = Math.Sign(previous - opposite);
        if (direction == 0)
        {
            direction = Math.Sign(value - opposite);
        }

        return opposite + ((direction == 0 ? 1 : direction) * minimumDistance);
    }

    private static double Snap(double value, double interval) =>
        Math.Round(value / interval, MidpointRounding.AwayFromZero) * interval;

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static void ValidateGrid(ModelicaPoint grid)
    {
        if (!double.IsFinite(grid.X) || !double.IsFinite(grid.Y) || grid.X <= 0 || grid.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grid), "Grid intervals must be finite and positive.");
        }
    }
}
