namespace ModelicaStudio.Domain.Graphics;

public sealed record ModelicaConnectorEndpoint(
    string Path,
    ModelicaComponentInstance Owner,
    ModelicaComponentInstance Connector,
    ModelicaPoint Position,
    bool IsOutside);

public sealed record ModelicaConnectionCompatibility(bool IsCompatible, string? Reason = null);

public static class ModelicaConnectionEditor
{
    private const int MaximumExpandedConnectorEndpoints = 256;

    public static IReadOnlyList<ModelicaConnectorEndpoint> GetConnectorEndpoints(
        ModelicaModelInstanceSnapshot? instance,
        bool showIcon = false)
    {
        if (instance is null)
        {
            return [];
        }

        var endpoints = new List<ModelicaConnectorEndpoint>();
        foreach (var component in instance.Components)
        {
            var ownerTransformation = SelectTransformation(component.Placement, showIcon);
            if (ownerTransformation is null)
            {
                continue;
            }

            if (IsConnector(component))
            {
                foreach (var path in ExpandPath(component.Name, component.Dimensions))
                {
                    endpoints.Add(new ModelicaConnectorEndpoint(
                        path,
                        component,
                        component,
                        ownerTransformation.Origin,
                        true));
                }

                continue;
            }

            var sourceExtent = component.TypeGraphics?.Icon?.CoordinateSystem.Coordinates.Extent;
            if (sourceExtent is null)
            {
                continue;
            }

            foreach (var connector in component.TypeComponents.Where(IsConnector))
            {
                var connectorTransformation = SelectTransformation(connector.Placement, true);
                if (connectorTransformation is null)
                {
                    continue;
                }

                var position = ModelicaPlacementTransform.Apply(
                    connectorTransformation.Origin,
                    sourceExtent.Value,
                    ownerTransformation);
                var ownerPaths = ExpandPath(component.Name, component.Dimensions);
                var connectorPaths = ExpandPath(connector.Name, connector.Dimensions);
                foreach (var ownerPath in ownerPaths)
                {
                    foreach (var connectorPath in connectorPaths)
                    {
                        if (endpoints.Count >= MaximumExpandedConnectorEndpoints)
                        {
                            return endpoints;
                        }

                        endpoints.Add(new ModelicaConnectorEndpoint(
                            $"{ownerPath}.{connectorPath}",
                            component,
                            connector,
                            position,
                            false));
                    }
                }
            }
        }

        return endpoints;
    }

    public static ModelicaConnectorEndpoint? HitTestEndpoint(
        IEnumerable<ModelicaConnectorEndpoint> endpoints,
        ModelicaPoint point,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        return endpoints
            .Select(endpoint => (Endpoint: endpoint, Distance: Distance(endpoint.Position, point)))
            .Where(candidate => candidate.Distance <= tolerance)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Endpoint)
            .FirstOrDefault();
    }

    public static IReadOnlyList<ModelicaPoint> CreateOrthogonalRoute(
        ModelicaPoint start,
        ModelicaPoint end)
    {
        var middleX = (start.X + end.X) / 2d;
        return SimplifyRoute(
        [
            start,
            new ModelicaPoint(middleX, start.Y),
            new ModelicaPoint(middleX, end.Y),
            end,
        ]);
    }

    public static IReadOnlyList<ModelicaPoint> GetConnectionPoints(
        ModelicaModelInstanceSnapshot instance,
        ModelicaDiagramConnection connection)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Line is { Points.Count: >= 2 } line)
        {
            return line.Points.Select(point => ModelicaGraphicTransform.Apply(
                point,
                line.Origin.StaticValue,
                line.Rotation.StaticValue)).ToArray();
        }

        var endpoints = GetConnectorEndpoints(instance);
        var left = ResolveEndpoint(endpoints, connection.Left);
        var right = ResolveEndpoint(endpoints, connection.Right);
        return left is not null && right is not null && left.Position != right.Position
            ? CreateOrthogonalRoute(left.Position, right.Position)
            : [];
    }

    public static IReadOnlyList<ModelicaPoint> SimplifyRoute(IEnumerable<ModelicaPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new List<ModelicaPoint>();
        foreach (var point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(points), "Connection route points must be finite.");
            }

            if (result.Count > 0 && result[^1] == point)
            {
                continue;
            }

            result.Add(point);
            while (result.Count >= 3 && AreCollinear(result[^3], result[^2], result[^1]))
            {
                result.RemoveAt(result.Count - 2);
            }
        }

        if (result.Count < 2)
        {
            throw new ArgumentException("A connection route requires at least two distinct points.", nameof(points));
        }

        return result;
    }

    public static IReadOnlyList<ModelicaPoint> MoveOrthogonalSegment(
        IReadOnlyList<ModelicaPoint> points,
        int segmentIndex,
        ModelicaPoint target,
        ModelicaPoint grid)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2 || segmentIndex < 0 || segmentIndex >= points.Count - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        }

        var first = points[segmentIndex];
        var second = points[segmentIndex + 1];
        var horizontal = Math.Abs(first.Y - second.Y) <= 1e-9;
        var vertical = Math.Abs(first.X - second.X) <= 1e-9;
        if (!horizontal && !vertical)
        {
            throw new ArgumentException("Only orthogonal connection segments can be routed.", nameof(points));
        }

        var snapped = ModelicaGrid.Snap(target, grid);
        var route = points.ToList();
        if (points.Count == 2)
        {
            return horizontal
                ? SimplifyRoute(
                [
                    first,
                    new ModelicaPoint(first.X, snapped.Y),
                    new ModelicaPoint(second.X, snapped.Y),
                    second,
                ])
                : SimplifyRoute(
                [
                    first,
                    new ModelicaPoint(snapped.X, first.Y),
                    new ModelicaPoint(snapped.X, second.Y),
                    second,
                ]);
        }

        if (horizontal)
        {
            var coordinate = snapped.Y;
            if (segmentIndex == 0)
            {
                route[1] = route[1] with { Y = coordinate };
                route.Insert(1, new ModelicaPoint(route[0].X, coordinate));
            }
            else if (segmentIndex == points.Count - 2)
            {
                route[segmentIndex] = route[segmentIndex] with { Y = coordinate };
                route.Insert(segmentIndex + 1, new ModelicaPoint(route[^1].X, coordinate));
            }
            else
            {
                route[segmentIndex] = route[segmentIndex] with { Y = coordinate };
                route[segmentIndex + 1] = route[segmentIndex + 1] with { Y = coordinate };
            }
        }
        else
        {
            var coordinate = snapped.X;
            if (segmentIndex == 0)
            {
                route[1] = route[1] with { X = coordinate };
                route.Insert(1, new ModelicaPoint(coordinate, route[0].Y));
            }
            else if (segmentIndex == points.Count - 2)
            {
                route[segmentIndex] = route[segmentIndex] with { X = coordinate };
                route.Insert(segmentIndex + 1, new ModelicaPoint(coordinate, route[^1].Y));
            }
            else
            {
                route[segmentIndex] = route[segmentIndex] with { X = coordinate };
                route[segmentIndex + 1] = route[segmentIndex + 1] with { X = coordinate };
            }
        }

        return SimplifyRoute(route);
    }

    public static ModelicaConnectionCompatibility CheckCompatibility(
        ModelicaConnectorEndpoint left,
        ModelicaConnectorEndpoint right,
        IEnumerable<ModelicaDiagramConnection>? existingConnections = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (string.Equals(left.Path, right.Path, StringComparison.Ordinal))
        {
            return new ModelicaConnectionCompatibility(false, "A connector cannot be connected to itself.");
        }

        if (existingConnections?.Any(connection => SameEndpoints(
                connection.Left,
                connection.Right,
                left.Path,
                right.Path)) == true)
        {
            return new ModelicaConnectionCompatibility(false, "These connectors are already connected.");
        }

        var leftConnector = left.Connector;
        var rightConnector = right.Connector;
        if (IsExpandableConnector(leftConnector) || IsExpandableConnector(rightConnector))
        {
            return new ModelicaConnectionCompatibility(true);
        }

        if (!DirectionsAreCompatible(left, right))
        {
            return new ModelicaConnectionCompatibility(false, "The connector directions are not compatible.");
        }

        return TypesAreCompatible(leftConnector, rightConnector)
            ? new ModelicaConnectionCompatibility(true)
            : new ModelicaConnectionCompatibility(
                false,
                $"Connector types '{leftConnector.TypeName}' and '{rightConnector.TypeName}' are structurally incompatible.");
    }

    public static bool SameEndpoints(
        string left,
        string right,
        string expectedLeft,
        string expectedRight) =>
        string.Equals(left, expectedLeft, StringComparison.Ordinal)
        && string.Equals(right, expectedRight, StringComparison.Ordinal)
        || string.Equals(left, expectedRight, StringComparison.Ordinal)
        && string.Equals(right, expectedLeft, StringComparison.Ordinal);

    private static bool TypesAreCompatible(
        ModelicaComponentInstance left,
        ModelicaComponentInstance right)
    {
        if (left.TypeComponents.Count == 0 && right.TypeComponents.Count == 0)
        {
            return string.Equals(
                left.RootTypeName ?? left.TypeName,
                right.RootTypeName ?? right.TypeName,
                StringComparison.Ordinal);
        }

        if (left.TypeComponents.Count != right.TypeComponents.Count)
        {
            return false;
        }

        foreach (var leftChild in left.TypeComponents)
        {
            var rightChild = right.TypeComponents.FirstOrDefault(child =>
                string.Equals(child.Name, leftChild.Name, StringComparison.Ordinal));
            if (rightChild is null
                || !string.Equals(leftChild.Prefixes.Connector, rightChild.Prefixes.Connector, StringComparison.Ordinal)
                || !string.Equals(leftChild.Prefixes.Direction, rightChild.Prefixes.Direction, StringComparison.Ordinal)
                || !leftChild.Dimensions.SequenceEqual(rightChild.Dimensions, StringComparer.Ordinal)
                || !TypesAreCompatible(leftChild, rightChild))
            {
                return false;
            }
        }

        return true;
    }

    private static ModelicaConnectorEndpoint? ResolveEndpoint(
        IEnumerable<ModelicaConnectorEndpoint> endpoints,
        string reference) => endpoints
        .Where(endpoint => string.Equals(endpoint.Path, reference, StringComparison.Ordinal)
            || reference.StartsWith(endpoint.Path + ".", StringComparison.Ordinal))
        .OrderByDescending(endpoint => endpoint.Path.Length)
        .FirstOrDefault();

    private static bool DirectionsAreCompatible(
        ModelicaConnectorEndpoint left,
        ModelicaConnectorEndpoint right)
    {
        var leftDirection = EffectiveDirection(left.Connector);
        var rightDirection = EffectiveDirection(right.Connector);
        if (string.IsNullOrEmpty(leftDirection)
            || !string.Equals(leftDirection, rightDirection, StringComparison.Ordinal))
        {
            return true;
        }

        return leftDirection switch
        {
            "output" => left.IsOutside || right.IsOutside,
            "input" => !left.IsOutside || !right.IsOutside
                || !left.Connector.Prefixes.IsPublic
                || !right.Connector.Prefixes.IsPublic,
            _ => true,
        };
    }

    private static string? EffectiveDirection(ModelicaComponentInstance connector) =>
        connector.Prefixes.Direction ?? connector.TypePrefixes.Direction;

    private static IReadOnlyList<string> ExpandPath(
        string name,
        IReadOnlyList<string> dimensions)
    {
        if (dimensions.Count == 0)
        {
            return [name];
        }

        var sizes = new int[dimensions.Count];
        var total = 1;
        for (var index = 0; index < dimensions.Count; index++)
        {
            if (!int.TryParse(dimensions[index], out sizes[index]) || sizes[index] <= 0)
            {
                return [];
            }

            total *= sizes[index];
            if (total > MaximumExpandedConnectorEndpoints)
            {
                return [];
            }
        }

        var paths = new List<string>(total);
        var indices = Enumerable.Repeat(1, sizes.Length).ToArray();
        while (true)
        {
            paths.Add($"{name}[{string.Join(',', indices)}]");
            var dimension = sizes.Length - 1;
            while (dimension >= 0 && indices[dimension] == sizes[dimension])
            {
                indices[dimension] = 1;
                dimension--;
            }

            if (dimension < 0)
            {
                break;
            }

            indices[dimension]++;
        }

        return paths;
    }

    private static ModelicaTransformation? SelectTransformation(ModelicaPlacement? placement, bool showIcon)
    {
        if (placement is null)
        {
            return null;
        }

        return showIcon
            ? placement.IconVisible ? placement.IconTransformation ?? placement.Transformation : null
            : placement.Visible ? placement.Transformation : null;
    }

    private static bool IsConnector(ModelicaComponentInstance component) =>
        string.Equals(component.Restriction, "connector", StringComparison.OrdinalIgnoreCase)
        || IsExpandableConnector(component);

    private static bool IsExpandableConnector(ModelicaComponentInstance component) =>
        string.Equals(component.Restriction, "expandable connector", StringComparison.OrdinalIgnoreCase);

    private static bool AreCollinear(ModelicaPoint first, ModelicaPoint middle, ModelicaPoint last) =>
        Math.Abs(((middle.X - first.X) * (last.Y - first.Y))
            - ((middle.Y - first.Y) * (last.X - first.X))) <= 1e-9;

    private static double Distance(ModelicaPoint first, ModelicaPoint second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
