namespace ModelicaStudio.Domain.Graphics;

public sealed record ModelicaDiagramHit(
    ModelicaComponentInstance? Component,
    ModelicaDiagramConnection? Connection)
{
    public static ModelicaDiagramHit None { get; } = new(null, null);
    public bool HasSelection => Component is not null || Connection is not null;
}

public static class ModelicaDiagramHitTester
{
    public static ModelicaDiagramHit HitTest(
        ModelicaModelInstanceSnapshot? instance,
        ModelicaPoint point,
        double connectionTolerance,
        bool showIcon)
    {
        if (!double.IsFinite(connectionTolerance) || connectionTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionTolerance),
                "Connection tolerance must be finite and non-negative.");
        }

        if (instance is null)
        {
            return ModelicaDiagramHit.None;
        }

        foreach (var component in instance.Components.Reverse())
        {
            var transformation = SelectTransformation(component.Placement, showIcon);
            if (transformation is null
                || component.TypeGraphics?.Icon is null
                || !Contains(GetPlacementPolygon(transformation), point))
            {
                continue;
            }

            return new ModelicaDiagramHit(component, null);
        }

        if (!showIcon)
        {
            foreach (var connection in instance.Connections.Reverse())
            {
                if (ConnectionContains(instance, connection, point, connectionTolerance))
                {
                    return new ModelicaDiagramHit(null, connection);
                }
            }
        }

        return ModelicaDiagramHit.None;
    }

    public static IReadOnlyList<ModelicaPoint> GetPlacementPolygon(ModelicaTransformation transformation)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        var extent = transformation.Extent;
        return
        [
            ModelicaGraphicTransform.Apply(extent.First, transformation.Origin, transformation.Rotation),
            ModelicaGraphicTransform.Apply(
                new ModelicaPoint(extent.Second.X, extent.First.Y),
                transformation.Origin,
                transformation.Rotation),
            ModelicaGraphicTransform.Apply(extent.Second, transformation.Origin, transformation.Rotation),
            ModelicaGraphicTransform.Apply(
                new ModelicaPoint(extent.First.X, extent.Second.Y),
                transformation.Origin,
                transformation.Rotation),
        ];
    }

    public static IReadOnlyList<ModelicaComponentInstance> SelectComponentsInBox(
        ModelicaModelInstanceSnapshot? instance,
        ModelicaExtent selectionBox,
        bool showIcon)
    {
        if (instance is null || selectionBox.Width <= 0 || selectionBox.Height <= 0)
        {
            return [];
        }

        return instance.Components
            .Where(component =>
            {
                var transformation = SelectTransformation(component.Placement, showIcon);
                return transformation is not null
                    && component.TypeGraphics?.Icon is not null
                    && PolygonIntersectsBox(GetPlacementPolygon(transformation), selectionBox);
            })
            .ToArray();
    }

    private static ModelicaTransformation? SelectTransformation(ModelicaPlacement? placement, bool showIcon)
    {
        if (placement is null)
        {
            return null;
        }

        return showIcon
            ? placement.IconVisible ? placement.IconTransformation : null
            : placement.Visible ? placement.Transformation : null;
    }

    private static bool ConnectionContains(
        ModelicaModelInstanceSnapshot instance,
        ModelicaDiagramConnection connection,
        ModelicaPoint point,
        double tolerance)
    {
        if (connection.Line is { Visible.StaticValue: false })
        {
            return false;
        }

        var points = ModelicaConnectionEditor.GetConnectionPoints(instance, connection);
        if (points.Count < 2)
        {
            return false;
        }

        for (var index = 1; index < points.Count; index++)
        {
            if (DistanceToSegment(point, points[index - 1], points[index]) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<ModelicaPoint> polygon, ModelicaPoint point)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var first = polygon[previous];
            var second = polygon[current];
            if (DistanceToSegment(point, first, second) <= 1e-9)
            {
                return true;
            }

            var crossesRay = (second.Y > point.Y) != (first.Y > point.Y)
                && point.X < ((first.X - second.X) * (point.Y - second.Y) / (first.Y - second.Y)) + second.X;
            if (crossesRay)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool PolygonIntersectsBox(IReadOnlyList<ModelicaPoint> polygon, ModelicaExtent box)
    {
        if (polygon.Any(point => PointInsideBox(point, box)))
        {
            return true;
        }

        var corners = new[]
        {
            new ModelicaPoint(box.MinimumX, box.MinimumY),
            new ModelicaPoint(box.MaximumX, box.MinimumY),
            new ModelicaPoint(box.MaximumX, box.MaximumY),
            new ModelicaPoint(box.MinimumX, box.MaximumY),
        };
        if (corners.Any(point => Contains(polygon, point)))
        {
            return true;
        }

        for (var polygonIndex = 0; polygonIndex < polygon.Count; polygonIndex++)
        {
            var polygonStart = polygon[polygonIndex];
            var polygonEnd = polygon[(polygonIndex + 1) % polygon.Count];
            for (var boxIndex = 0; boxIndex < corners.Length; boxIndex++)
            {
                if (SegmentsIntersect(
                        polygonStart,
                        polygonEnd,
                        corners[boxIndex],
                        corners[(boxIndex + 1) % corners.Length]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool PointInsideBox(ModelicaPoint point, ModelicaExtent box) =>
        point.X >= box.MinimumX
        && point.X <= box.MaximumX
        && point.Y >= box.MinimumY
        && point.Y <= box.MaximumY;

    private static bool SegmentsIntersect(
        ModelicaPoint firstStart,
        ModelicaPoint firstEnd,
        ModelicaPoint secondStart,
        ModelicaPoint secondEnd)
    {
        var firstA = Cross(firstStart, firstEnd, secondStart);
        var firstB = Cross(firstStart, firstEnd, secondEnd);
        var secondA = Cross(secondStart, secondEnd, firstStart);
        var secondB = Cross(secondStart, secondEnd, firstEnd);
        const double epsilon = 1e-9;
        if (Math.Abs(firstA) <= epsilon && PointOnSegment(secondStart, firstStart, firstEnd)
            || Math.Abs(firstB) <= epsilon && PointOnSegment(secondEnd, firstStart, firstEnd)
            || Math.Abs(secondA) <= epsilon && PointOnSegment(firstStart, secondStart, secondEnd)
            || Math.Abs(secondB) <= epsilon && PointOnSegment(firstEnd, secondStart, secondEnd))
        {
            return true;
        }

        return (firstA > 0) != (firstB > 0) && (secondA > 0) != (secondB > 0);
    }

    private static double Cross(ModelicaPoint start, ModelicaPoint end, ModelicaPoint point) =>
        ((end.X - start.X) * (point.Y - start.Y))
        - ((end.Y - start.Y) * (point.X - start.X));

    private static bool PointOnSegment(ModelicaPoint point, ModelicaPoint start, ModelicaPoint end) =>
        point.X >= Math.Min(start.X, end.X) - 1e-9
        && point.X <= Math.Max(start.X, end.X) + 1e-9
        && point.Y >= Math.Min(start.Y, end.Y) - 1e-9
        && point.Y <= Math.Max(start.Y, end.Y) + 1e-9;

    private static double DistanceToSegment(ModelicaPoint point, ModelicaPoint start, ModelicaPoint end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        if (lengthSquared <= double.Epsilon)
        {
            return Math.Sqrt(SquaredDistance(point, start));
        }

        var projection = Math.Clamp(
            (((point.X - start.X) * segmentX) + ((point.Y - start.Y) * segmentY)) / lengthSquared,
            0,
            1);
        var closest = new ModelicaPoint(start.X + (projection * segmentX), start.Y + (projection * segmentY));
        return Math.Sqrt(SquaredDistance(point, closest));
    }

    private static double SquaredDistance(ModelicaPoint first, ModelicaPoint second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return (x * x) + (y * y);
    }
}
