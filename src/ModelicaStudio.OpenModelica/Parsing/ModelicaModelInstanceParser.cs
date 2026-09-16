using System.Text.Json;
using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.OpenModelica.Parsing;

public static class ModelicaModelInstanceParser
{
    public static ModelicaModelInstanceSnapshot Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("OpenModelica model-instance JSON must contain an object at its root.");
            }

            var className = RequiredString(root, "name");
            var restriction = OptionalString(root, "restriction") ?? "unknown";
            var graphics = ModelInstanceAnnotationParser.Parse(json);
            var components = new List<ModelicaComponentInstance>();
            var connections = new List<ModelicaDiagramConnection>();
            var issues = new List<ModelicaAnnotationIssue>();
            CollectContents(root, className, false, components, connections, issues);
            issues.AddRange(graphics.Issues);

            return new ModelicaModelInstanceSnapshot(
                className,
                restriction,
                graphics,
                components,
                connections,
                issues,
                json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("OpenModelica returned invalid model-instance JSON.", exception);
        }
    }

    private static void CollectContents(
        JsonElement model,
        string declaringClass,
        bool isInherited,
        ICollection<ModelicaComponentInstance> components,
        ICollection<ModelicaDiagramConnection> connections,
        ICollection<ModelicaAnnotationIssue> issues)
    {
        if (model.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in elements.EnumerateArray())
            {
                if (OptionalString(element, "$kind") != "extends"
                    || !element.TryGetProperty("baseClass", out var baseClass)
                    || baseClass.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                CollectContents(
                    baseClass,
                    OptionalString(baseClass, "name") ?? "<unknown base class>",
                    true,
                    components,
                    connections,
                    issues);
            }

            foreach (var element in elements.EnumerateArray())
            {
                if (OptionalString(element, "$kind") != "component")
                {
                    continue;
                }

                try
                {
                    components.Add(ParseComponent(element, declaringClass, isInherited));
                }
                catch (Exception exception) when (exception is FormatException or InvalidOperationException)
                {
                    issues.Add(new ModelicaAnnotationIssue(
                        $"A component in '{declaringClass}' could not be parsed: {exception.Message}",
                        element.GetRawText()));
                }
            }
        }

        if (!model.TryGetProperty("connections", out var connectionValues)
            || connectionValues.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var connection in connectionValues.EnumerateArray())
        {
            try
            {
                connections.Add(ParseConnection(connection, declaringClass, isInherited));
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                issues.Add(new ModelicaAnnotationIssue(
                    $"A connection in '{declaringClass}' could not be parsed: {exception.Message}",
                    connection.GetRawText()));
            }
        }
    }

    private static ModelicaComponentInstance ParseComponent(
        JsonElement component,
        string declaringClass,
        bool isInherited)
    {
        var typeName = "<unknown>";
        var restriction = "unknown";
        ModelicaGraphicalAnnotationSnapshot? typeGraphics = null;
        IReadOnlyList<ModelicaComponentInstance> typeComponents = [];
        var typePrefixes = new ModelicaComponentPrefixes();
        string? rootTypeName = null;
        if (component.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String)
            {
                typeName = type.GetString() ?? typeName;
                restriction = "type";
                rootTypeName = typeName;
            }
            else if (type.ValueKind == JsonValueKind.Object)
            {
                typeName = OptionalString(type, "name") ?? typeName;
                restriction = OptionalString(type, "restriction") ?? restriction;
                typeGraphics = ModelInstanceAnnotationParser.Parse(type.GetRawText());
                typePrefixes = ParsePrefixes(type);
                typeComponents = ParseTypeComponents(type, typeName);
                rootTypeName = FindRootTypeName(type) ?? typeName;
            }
        }

        return new ModelicaComponentInstance(
            RequiredString(component, "name"),
            typeName,
            restriction,
            ParsePrefixes(component),
            ParsePlacement(component),
            typeGraphics,
            isInherited,
            declaringClass,
            component.GetRawText())
        {
            TypeComponents = typeComponents,
            Dimensions = ParseDimensions(component),
            RootTypeName = rootTypeName,
            TypePrefixes = typePrefixes,
        };
    }

    private static IReadOnlyList<ModelicaComponentInstance> ParseTypeComponents(
        JsonElement type,
        string declaringClass)
    {
        var components = new List<ModelicaComponentInstance>();
        CollectTypeComponents(type, declaringClass, false, components);
        return components;
    }

    private static void CollectTypeComponents(
        JsonElement type,
        string declaringClass,
        bool isInherited,
        ICollection<ModelicaComponentInstance> components)
    {
        if (!type.TryGetProperty("elements", out var elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (OptionalString(element, "$kind") == "extends"
                && element.TryGetProperty("baseClass", out var baseClass)
                && baseClass.ValueKind == JsonValueKind.Object)
            {
                CollectTypeComponents(
                    baseClass,
                    OptionalString(baseClass, "name") ?? declaringClass,
                    true,
                    components);
            }
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (OptionalString(element, "$kind") == "component")
            {
                components.Add(ParseComponent(element, declaringClass, isInherited));
            }
        }
    }

    private static IReadOnlyList<string> ParseDimensions(JsonElement component)
    {
        if (!component.TryGetProperty("dims", out var dimensions)
            || dimensions.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var dimensionValues = dimensions.TryGetProperty("typed", out var typed)
            && typed.ValueKind == JsonValueKind.Array
                ? typed
                : dimensions.TryGetProperty("absyn", out var absyn)
                    && absyn.ValueKind == JsonValueKind.Array
                        ? absyn
                        : default;
        if (dimensionValues.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return dimensionValues.EnumerateArray().Select(FormatDimension).ToArray();
    }

    private static string FormatDimension(JsonElement dimension) => dimension.ValueKind switch
    {
        JsonValueKind.String => dimension.GetString() ?? string.Empty,
        JsonValueKind.Number => dimension.GetRawText(),
        _ when OptionalString(dimension, "$kind") == "cref" => FormatExpression(dimension),
        _ => dimension.GetRawText(),
    };

    private static string? FindRootTypeName(JsonElement type)
    {
        if (OptionalString(type, "restriction") != "type"
            || !type.TryGetProperty("elements", out var elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return OptionalString(type, "name");
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (OptionalString(element, "$kind") != "extends"
                || !element.TryGetProperty("baseClass", out var baseClass))
            {
                continue;
            }

            return baseClass.ValueKind switch
            {
                JsonValueKind.String => baseClass.GetString(),
                JsonValueKind.Object => FindRootTypeName(baseClass),
                _ => null,
            };
        }

        return OptionalString(type, "name");
    }

    private static ModelicaComponentPrefixes ParsePrefixes(JsonElement component)
    {
        if (!component.TryGetProperty("prefixes", out var prefixes)
            || prefixes.ValueKind != JsonValueKind.Object)
        {
            return new ModelicaComponentPrefixes();
        }

        return new ModelicaComponentPrefixes(
            Boolean(prefixes, "public", true),
            Boolean(prefixes, "final"),
            Boolean(prefixes, "inner"),
            Boolean(prefixes, "outer"),
            Boolean(prefixes, "replaceable"),
            Boolean(prefixes, "redeclare"),
            OptionalString(prefixes, "connector"),
            OptionalString(prefixes, "variability"),
            OptionalString(prefixes, "direction"));
    }

    private static ModelicaPlacement? ParsePlacement(JsonElement component)
    {
        if (!component.TryGetProperty("annotation", out var annotation)
            || annotation.ValueKind != JsonValueKind.Object
            || !annotation.TryGetProperty("Placement", out var placement)
            || placement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ModelicaPlacement(
            Boolean(placement, "visible", true),
            placement.TryGetProperty("transformation", out var transformation)
                ? ParseTransformation(transformation)
                : ModelicaTransformation.Default,
            Boolean(placement, "iconVisible", true),
            placement.TryGetProperty("iconTransformation", out var iconTransformation)
                ? ParseTransformation(iconTransformation)
                : null);
    }

    private static ModelicaTransformation ParseTransformation(JsonElement transformation)
    {
        if (transformation.ValueKind != JsonValueKind.Object)
        {
            return ModelicaTransformation.Default;
        }

        return new ModelicaTransformation(
            transformation.TryGetProperty("origin", out var origin)
                ? ParsePoint(origin, ModelicaTransformation.Default.Origin)
                : ModelicaTransformation.Default.Origin,
            transformation.TryGetProperty("extent", out var extent)
                ? ParseExtent(extent, ModelicaTransformation.Default.Extent)
                : ModelicaTransformation.Default.Extent,
            transformation.TryGetProperty("rotation", out var rotation) && rotation.TryGetDouble(out var angle)
                ? angle
                : ModelicaTransformation.Default.Rotation);
    }

    private static ModelicaDiagramConnection ParseConnection(
        JsonElement connection,
        string declaringClass,
        bool isInherited)
    {
        if (!connection.TryGetProperty("lhs", out var left)
            || !connection.TryGetProperty("rhs", out var right))
        {
            throw new FormatException("The connection is missing an endpoint.");
        }

        return new ModelicaDiagramConnection(
            FormatExpression(left),
            FormatExpression(right),
            ParseConnectionLine(connection),
            isInherited,
            declaringClass,
            connection.GetRawText());
    }

    private static ModelicaGraphicPrimitive? ParseConnectionLine(JsonElement connection)
    {
        if (!connection.TryGetProperty("annotation", out var annotation)
            || annotation.ValueKind != JsonValueKind.Object
            || !annotation.TryGetProperty("Line", out var line)
            || line.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var points = line.TryGetProperty("points", out var pointsJson)
            ? ParsePoints(pointsJson)
            : [];
        return new ModelicaGraphicPrimitive
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(Boolean(line, "visible", true)),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(line.TryGetProperty("origin", out var origin)
                ? ParsePoint(origin, new ModelicaPoint(0, 0))
                : new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(
                line.TryGetProperty("rotation", out var rotation) && rotation.TryGetDouble(out var angle) ? angle : 0),
            Points = points,
            Style = new ModelicaGraphicStyle
            {
                LineColor = line.TryGetProperty("color", out var color)
                    ? ParseColor(color)
                    : ModelicaColor.Black,
                LinePattern = line.TryGetProperty("pattern", out var pattern)
                    ? ParseEnum(pattern, ModelicaLinePattern.Solid)
                    : ModelicaLinePattern.Solid,
                LineThickness = line.TryGetProperty("thickness", out var thickness)
                    && thickness.TryGetDouble(out var width)
                        ? width
                        : 0.25,
            },
            Arrows = line.TryGetProperty("arrow", out var arrows)
                ? ParseArrows(arrows)
                : [ModelicaArrow.None, ModelicaArrow.None],
            ArrowSize = line.TryGetProperty("arrowSize", out var arrowSize)
                && arrowSize.TryGetDouble(out var size)
                    ? size
                    : 3,
            Smooth = line.TryGetProperty("smooth", out var smooth)
                ? ParseEnum(smooth, ModelicaSmooth.None)
                : ModelicaSmooth.None,
            RawJson = line.GetRawText(),
        };
    }

    private static IReadOnlyList<ModelicaArrow> ParseArrows(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [ModelicaArrow.None, ModelicaArrow.None];
        }

        var values = element.EnumerateArray()
            .Select(value => ParseEnum(value, ModelicaArrow.None))
            .Take(2)
            .ToList();
        while (values.Count < 2)
        {
            values.Add(ModelicaArrow.None);
        }

        return values;
    }

    private static string FormatExpression(JsonElement expression)
    {
        if (expression.ValueKind == JsonValueKind.Object
            && OptionalString(expression, "$kind") == "cref"
            && expression.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            return string.Join('.', parts.EnumerateArray().Select(FormatCrefPart));
        }

        return expression.ValueKind == JsonValueKind.String
            ? expression.GetString() ?? string.Empty
            : expression.GetRawText();
    }

    private static string FormatCrefPart(JsonElement part)
    {
        var name = OptionalString(part, "name") ?? "?";
        if (!part.TryGetProperty("subscripts", out var subscripts)
            || subscripts.ValueKind != JsonValueKind.Array)
        {
            return name;
        }

        return name + "[" + string.Join(',', subscripts.EnumerateArray().Select(FormatSubscript)) + "]";
    }

    private static string FormatSubscript(JsonElement subscript) => subscript.ValueKind switch
    {
        JsonValueKind.Number => subscript.GetRawText(),
        JsonValueKind.String => subscript.GetString() ?? string.Empty,
        _ when OptionalString(subscript, "$kind") == "cref" => FormatExpression(subscript),
        _ => subscript.GetRawText(),
    };

    private static IReadOnlyList<ModelicaPoint> ParsePoints(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(item => ParsePoint(item, new ModelicaPoint(0, 0))).ToArray()
            : [];

    private static ModelicaPoint ParsePoint(JsonElement element, ModelicaPoint fallback)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var values = element.EnumerateArray().ToArray();
        return values.Length == 2 && values[0].TryGetDouble(out var x) && values[1].TryGetDouble(out var y)
            ? new ModelicaPoint(x, y)
            : fallback;
    }

    private static ModelicaExtent ParseExtent(JsonElement element, ModelicaExtent fallback)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var values = element.EnumerateArray().ToArray();
        return values.Length == 2
            ? new ModelicaExtent(ParsePoint(values[0], fallback.First), ParsePoint(values[1], fallback.Second))
            : fallback;
    }

    private static ModelicaColor ParseColor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return ModelicaColor.Black;
        }

        var values = element.EnumerateArray().ToArray();
        return values.Length == 3
            && values[0].TryGetInt32(out var red)
            && values[1].TryGetInt32(out var green)
            && values[2].TryGetInt32(out var blue)
                ? new ModelicaColor(red, green, blue)
                : ModelicaColor.Black;
    }

    private static TEnum ParseEnum<TEnum>(JsonElement element, TEnum fallback)
        where TEnum : struct, Enum
    {
        var value = element.ValueKind == JsonValueKind.Object
            ? OptionalString(element, "name")
            : element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        var member = value?.Split('.').LastOrDefault();
        return Enum.TryParse<TEnum>(member, true, out var parsed) ? parsed : fallback;
    }

    private static bool Boolean(JsonElement element, string propertyName, bool fallback = false) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName)
        ?? throw new FormatException($"OpenModelica model-instance JSON is missing '{propertyName}'.");

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
