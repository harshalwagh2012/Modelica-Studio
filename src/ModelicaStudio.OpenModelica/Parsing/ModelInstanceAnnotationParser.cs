using System.Text.Json;
using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.OpenModelica.Parsing;

public static class ModelInstanceAnnotationParser
{
    public static ModelicaGraphicalAnnotationSnapshot Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("OpenModelica annotation JSON must contain an object at its root.");
            }

            var issues = new List<ModelicaAnnotationIssue>();
            var inherited = new List<ModelicaInheritedGraphicalAnnotation>();
            ParseInheritedAnnotations(root, inherited, issues);
            TryGetAnnotation(root, out var annotation);
            return new ModelicaGraphicalAnnotationSnapshot(
                RequiredString(root, "name"),
                OptionalString(root, "restriction") ?? "unknown",
                ParseView(annotation, "Icon", issues),
                ParseView(annotation, "Diagram", issues),
                inherited,
                issues,
                json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("OpenModelica returned invalid model-instance annotation JSON.", exception);
        }
    }

    private static void ParseInheritedAnnotations(
        JsonElement model,
        ICollection<ModelicaInheritedGraphicalAnnotation> inherited,
        ICollection<ModelicaAnnotationIssue> issues)
    {
        if (!model.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (OptionalString(element, "$kind") != "extends"
                || !element.TryGetProperty("baseClass", out var baseClass)
                || baseClass.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            TryGetAnnotation(baseClass, out var annotation);
            inherited.Add(new ModelicaInheritedGraphicalAnnotation(
                OptionalString(baseClass, "name") ?? "<unknown base class>",
                ParseView(annotation, "Icon", issues),
                ParseView(annotation, "Diagram", issues)));
            ParseInheritedAnnotations(baseClass, inherited, issues);
        }
    }

    private static ModelicaGraphicalView? ParseView(
        JsonElement? annotation,
        string viewName,
        ICollection<ModelicaAnnotationIssue> issues)
    {
        if (annotation is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty(viewName, out var view)
            || view.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var coordinateSystem = ParseCoordinateSystem(view);
        var graphics = new List<ModelicaGraphicPrimitive>();
        if (view.TryGetProperty("graphics", out var graphicsJson) && graphicsJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var graphic in graphicsJson.EnumerateArray())
            {
                graphics.Add(ParseGraphic(graphic, issues));
            }
        }

        return new ModelicaGraphicalView(coordinateSystem, graphics);
    }

    private static ModelicaGraphicalCoordinateSystem ParseCoordinateSystem(JsonElement view)
    {
        var result = new ModelicaGraphicalCoordinateSystem();
        if (!view.TryGetProperty("coordinateSystem", out var coordinate)
            || coordinate.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var extent = coordinate.TryGetProperty("extent", out var extentJson)
            ? ParseExtent(extentJson)
            : ModelicaCoordinateSystem.Default.Extent;
        var preserveAspectRatio = coordinate.TryGetProperty("preserveAspectRatio", out var preserveJson)
            && preserveJson.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? preserveJson.GetBoolean()
                : ModelicaCoordinateSystem.Default.PreserveAspectRatio;
        var initialScale = coordinate.TryGetProperty("initialScale", out var scaleJson)
            && scaleJson.TryGetDouble(out var parsedScale)
                ? parsedScale
                : 0.1;
        var grid = coordinate.TryGetProperty("grid", out var gridJson)
            ? ParsePoint(gridJson)
            : new ModelicaPoint(2, 2);
        return new ModelicaGraphicalCoordinateSystem
        {
            Coordinates = new ModelicaCoordinateSystem(extent, preserveAspectRatio),
            InitialScale = initialScale,
            Grid = grid,
        };
    }

    private static ModelicaGraphicPrimitive ParseGraphic(
        JsonElement graphicJson,
        ICollection<ModelicaAnnotationIssue> issues)
    {
        var rawJson = graphicJson.GetRawText();
        if (graphicJson.TryGetProperty("$error", out var error))
        {
            issues.Add(new ModelicaAnnotationIssue(error.GetString() ?? "OpenModelica reported an annotation error.", rawJson));
        }

        if (graphicJson.TryGetProperty("value", out var wrappedValue))
        {
            graphicJson = wrappedValue;
        }

        var name = OptionalString(graphicJson, "name") ?? "Unknown";
        if (OptionalString(graphicJson, "$kind") == "call")
        {
            return ParseCallGraphic(graphicJson, name, rawJson);
        }

        if (!graphicJson.TryGetProperty("elements", out var elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return UnknownGraphic(rawJson);
        }

        var values = elements.EnumerateArray().ToArray();
        var kind = ParseGraphicKind(name);
        var visible = ParseValue(ValueAt(values, 0), true, ParseBoolean);
        var origin = ParseValue(ValueAt(values, 1), new ModelicaPoint(0, 0), ParsePoint);
        var rotation = ParseValue(ValueAt(values, 2), 0d, ParseDouble);
        var style = ParseStyle(kind, values);
        var primitive = new ModelicaGraphicPrimitive
        {
            Kind = kind,
            Visible = visible,
            Origin = origin,
            Rotation = rotation,
            Style = style,
            RawJson = rawJson,
        };

        return kind switch
        {
            ModelicaGraphicKind.Rectangle => primitive with
            {
                BorderPattern = ParseEnum(ValueAt(values, 8), ModelicaBorderPattern.Unknown),
                Extent = ParseValue(ValueAt(values, 9), ModelicaCoordinateSystem.Default.Extent, ParseExtent),
                Radius = ParseDoubleOrDefault(ValueAt(values, 10)),
            },
            ModelicaGraphicKind.Ellipse => primitive with
            {
                Extent = ParseValue(ValueAt(values, 8), ModelicaCoordinateSystem.Default.Extent, ParseExtent),
                StartAngle = ParseDoubleOrDefault(ValueAt(values, 9)),
                EndAngle = ParseDoubleOrDefault(ValueAt(values, 10), 360),
                EllipseClosure = ParseEnum(ValueAt(values, 11), ModelicaEllipseClosure.Unknown),
            },
            ModelicaGraphicKind.Line => primitive with
            {
                Points = ParsePoints(ValueAt(values, 3)),
                Arrows = ParseArrows(ValueAt(values, 7)),
                ArrowSize = ParseDoubleOrDefault(ValueAt(values, 8), 3),
                Smooth = ParseEnum(ValueAt(values, 9), ModelicaSmooth.None),
            },
            ModelicaGraphicKind.Polygon => primitive with
            {
                Points = ParsePoints(ValueAt(values, 3)),
            },
            ModelicaGraphicKind.Text => primitive with
            {
                Extent = ParseValue(ValueAt(values, 8), ModelicaCoordinateSystem.Default.Extent, ParseExtent),
                TextString = ParseStringOrDefault(ValueAt(values, 9)),
                FontSize = ParseDoubleOrDefault(ValueAt(values, 10)),
                TextColor = ParseColor(ValueAt(values, 11)),
                FontName = ParseStringOrDefault(ValueAt(values, 12)),
                TextStyles = ParseTextStyles(ValueAt(values, 13)),
                TextAlignment = ParseEnum(ValueAt(values, 14), ModelicaTextAlignment.Unknown),
            },
            ModelicaGraphicKind.Bitmap => primitive with
            {
                Extent = ParseValue(ValueAt(values, 3), ModelicaCoordinateSystem.Default.Extent, ParseExtent),
                FileName = ParseStringOrDefault(ValueAt(values, 4)),
                ImageSource = ParseStringOrDefault(ValueAt(values, 5)),
            },
            _ => primitive,
        };
    }

    private static ModelicaGraphicPrimitive ParseCallGraphic(JsonElement graphic, string name, string rawJson)
    {
        var extent = new ModelicaAnnotationValue<ModelicaExtent>(ModelicaCoordinateSystem.Default.Extent);
        if (graphic.TryGetProperty("namedArgs", out var namedArgs)
            && namedArgs.ValueKind == JsonValueKind.Object
            && namedArgs.TryGetProperty("extent", out var extentJson))
        {
            extent = ParseValue(extentJson, ModelicaCoordinateSystem.Default.Extent, ParseExtent);
        }

        return new ModelicaGraphicPrimitive
        {
            Kind = ParseGraphicKind(name),
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(0),
            Extent = extent,
            RawJson = rawJson,
        };
    }

    private static ModelicaGraphicStyle ParseStyle(ModelicaGraphicKind kind, JsonElement[] values)
    {
        if (kind is ModelicaGraphicKind.Line or ModelicaGraphicKind.Polygon)
        {
            var offset = kind == ModelicaGraphicKind.Line ? 0 : 1;
            return new ModelicaGraphicStyle
            {
                LineColor = ParseColor(ValueAt(values, 4)),
                FillColor = kind == ModelicaGraphicKind.Polygon
                    ? ParseColor(ValueAt(values, 5))
                    : ModelicaColor.Black,
                LinePattern = ParseEnum(ValueAt(values, 5 + offset), ModelicaLinePattern.Unknown),
                FillPattern = kind == ModelicaGraphicKind.Polygon
                    ? ParseEnum(ValueAt(values, 7), ModelicaFillPattern.Unknown)
                    : ModelicaFillPattern.None,
                LineThickness = ParseDoubleOrDefault(ValueAt(values, 6 + (offset * 2)), 0.25),
            };
        }

        if (kind == ModelicaGraphicKind.Bitmap || kind == ModelicaGraphicKind.Unknown)
        {
            return new ModelicaGraphicStyle();
        }

        return new ModelicaGraphicStyle
        {
            LineColor = ParseColor(ValueAt(values, 3)),
            FillColor = ParseColor(ValueAt(values, 4)),
            LinePattern = ParseEnum(ValueAt(values, 5), ModelicaLinePattern.Unknown),
            FillPattern = ParseEnum(ValueAt(values, 6), ModelicaFillPattern.Unknown),
            LineThickness = ParseDoubleOrDefault(ValueAt(values, 7), 0.25),
        };
    }

    private static ModelicaAnnotationValue<T> ParseValue<T>(
        JsonElement? element,
        T fallback,
        Func<JsonElement, T> parse)
    {
        if (element is null)
        {
            return new ModelicaAnnotationValue<T>(fallback);
        }

        var value = element.Value;
        if (value.ValueKind == JsonValueKind.Object
            && OptionalString(value, "$kind") == "call"
            && OptionalString(value, "name") == "DynamicSelect"
            && value.TryGetProperty("arguments", out var arguments)
            && arguments.ValueKind == JsonValueKind.Array)
        {
            var argumentValues = arguments.EnumerateArray().ToArray();
            if (argumentValues.Length == 0)
            {
                return new ModelicaAnnotationValue<T>(fallback, value.GetRawText());
            }

            try
            {
                return new ModelicaAnnotationValue<T>(parse(argumentValues[0]), value.GetRawText());
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                return new ModelicaAnnotationValue<T>(fallback, value.GetRawText());
            }
        }

        try
        {
            return new ModelicaAnnotationValue<T>(parse(value));
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return new ModelicaAnnotationValue<T>(fallback, value.GetRawText());
        }
    }
    private static ModelicaGraphicPrimitive UnknownGraphic(string rawJson) => new()
    {
        Kind = ModelicaGraphicKind.Unknown,
        Visible = new ModelicaAnnotationValue<bool>(true),
        Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
        Rotation = new ModelicaAnnotationValue<double>(0),
        RawJson = rawJson,
    };

    private static ModelicaGraphicKind ParseGraphicKind(string name) => name switch
    {
        "Rectangle" => ModelicaGraphicKind.Rectangle,
        "Ellipse" => ModelicaGraphicKind.Ellipse,
        "Line" => ModelicaGraphicKind.Line,
        "Polygon" => ModelicaGraphicKind.Polygon,
        "Text" => ModelicaGraphicKind.Text,
        "Bitmap" => ModelicaGraphicKind.Bitmap,
        _ => ModelicaGraphicKind.Unknown,
    };

    private static ModelicaPoint ParsePoint(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Expected a Modelica point array.");
        }

        var coordinates = element.EnumerateArray().ToArray();
        if (coordinates.Length != 2)
        {
            throw new FormatException("A Modelica point must have two coordinates.");
        }

        return new ModelicaPoint(ParseDouble(coordinates[0]), ParseDouble(coordinates[1]));
    }

    private static ModelicaExtent ParseExtent(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Expected a Modelica extent array.");
        }

        var points = element.EnumerateArray().ToArray();
        if (points.Length != 2)
        {
            throw new FormatException("A Modelica extent must have two points.");
        }

        return new ModelicaExtent(ParsePoint(points[0]), ParsePoint(points[1]));
    }

    private static IReadOnlyList<ModelicaPoint> ParsePoints(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return [];
        }

        return value.EnumerateArray().Select(ParsePoint).ToArray();
    }

    private static IReadOnlyList<ModelicaArrow> ParseArrows(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return [ModelicaArrow.None, ModelicaArrow.None];
        }

        var arrows = value.EnumerateArray()
            .Select(item => ParseEnum(item, ModelicaArrow.None))
            .Take(2)
            .ToList();
        while (arrows.Count < 2)
        {
            arrows.Add(ModelicaArrow.None);
        }

        return arrows;
    }

    private static ModelicaColor ParseColor(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return ModelicaColor.Black;
        }

        var channels = value.EnumerateArray().ToArray();
        if (channels.Length != 3
            || !channels[0].TryGetInt32(out var red)
            || !channels[1].TryGetInt32(out var green)
            || !channels[2].TryGetInt32(out var blue))
        {
            return ModelicaColor.Black;
        }

        return new ModelicaColor(red, green, blue);
    }

    private static TEnum ParseEnum<TEnum>(JsonElement? element, TEnum fallback)
        where TEnum : struct, Enum
    {
        var name = element is { ValueKind: JsonValueKind.Object } value
            ? OptionalString(value, "name")
            : element is { ValueKind: JsonValueKind.String } stringValue
                ? stringValue.GetString()
                : null;
        var member = name?.Split('.').LastOrDefault();
        return Enum.TryParse<TEnum>(member, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static IReadOnlyList<string> ParseTextStyles(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => OptionalString(item, "name")?.Split('.').LastOrDefault() ?? item.ToString())
            .ToArray();
    }

    private static bool ParseBoolean(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new FormatException("Expected a Boolean annotation value."),
    };

    private static double ParseDouble(JsonElement element) =>
        element.TryGetDouble(out var value)
            ? value
            : throw new FormatException("Expected a numeric annotation value.");

    private static double ParseDoubleOrDefault(JsonElement? element, double fallback = 0) =>
        element is { } value && value.TryGetDouble(out var parsed) ? parsed : fallback;

    private static string? ParseStringOrDefault(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? ValueAt(JsonElement[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : null;

    private static void TryGetAnnotation(JsonElement model, out JsonElement? annotation)
    {
        annotation = model.TryGetProperty("annotation", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName)
        ?? throw new FormatException($"OpenModelica annotation JSON is missing '{propertyName}'.");

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
