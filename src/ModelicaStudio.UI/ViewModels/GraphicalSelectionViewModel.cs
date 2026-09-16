using System.Globalization;
using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.UI.ViewModels;

public sealed record InspectorPropertyViewModel(string Name, string Value);

public sealed record GraphicalSelectionViewModel(
    string Kind,
    string Title,
    string Subtitle,
    IReadOnlyList<InspectorPropertyViewModel> Properties)
{
    public static GraphicalSelectionViewModel FromComponent(
        ModelicaComponentInstance component,
        bool showIcon)
    {
        ArgumentNullException.ThrowIfNull(component);
        var properties = new List<InspectorPropertyViewModel>
        {
            new("Type", component.TypeName),
            new("Restriction", component.Restriction),
            new("Scope", component.IsInherited ? "Inherited" : "Local"),
            new("Declared in", component.DeclaringClass),
            new("Prefixes", FormatPrefixes(component.Prefixes)),
        };
        var placement = component.Placement;
        var transformation = showIcon ? placement?.IconTransformation : placement?.Transformation;
        if (transformation is not null)
        {
            properties.Add(new("Origin", FormatPoint(transformation.Origin)));
            properties.Add(new("Extent", FormatExtent(transformation.Extent)));
            properties.Add(new("Rotation", $"{FormatNumber(transformation.Rotation)}°"));
        }
        else
        {
            properties.Add(new("Placement", showIcon ? "No icon transformation" : "Unplaced"));
        }

        return new GraphicalSelectionViewModel(
            "COMPONENT",
            component.Name,
            component.TypeName,
            properties);
    }

    public static GraphicalSelectionViewModel FromConnection(ModelicaDiagramConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var line = connection.Line;
        var properties = new List<InspectorPropertyViewModel>
        {
            new("From", connection.Left),
            new("To", connection.Right),
            new("Scope", connection.IsInherited ? "Inherited" : "Local"),
            new("Declared in", connection.DeclaringClass),
            new("Route points", (line?.Points.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
        };
        if (line is not null)
        {
            properties.Add(new("Line pattern", line.Style.LinePattern.ToString()));
            properties.Add(new("Line thickness", FormatNumber(line.Style.LineThickness)));
            properties.Add(new("Arrows", string.Join(" → ", line.Arrows)));
            properties.Add(new("Arrow size", FormatNumber(line.ArrowSize)));
            properties.Add(new("Smoothing", line.Smooth.ToString()));
            properties.Add(new(
                "Line color",
                $"RGB {line.Style.LineColor.Red}, {line.Style.LineColor.Green}, {line.Style.LineColor.Blue}"));
        }

        return new GraphicalSelectionViewModel(
            "CONNECTION",
            $"{connection.Left} → {connection.Right}",
            connection.IsInherited ? "Inherited connection" : "Model connection",
            properties);
    }

    public static GraphicalSelectionViewModel FromComponents(
        IReadOnlyList<ModelicaComponentInstance> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var localCount = components.Count(static component => !component.IsInherited);
        var typeCount = components.Select(static component => component.TypeName).Distinct().Count();
        return new GraphicalSelectionViewModel(
            "COMPONENT GROUP",
            $"{components.Count} components",
            string.Join(", ", components.Select(static component => component.Name)),
            [
                new InspectorPropertyViewModel("Local", localCount.ToString(CultureInfo.InvariantCulture)),
                new InspectorPropertyViewModel(
                    "Inherited",
                    (components.Count - localCount).ToString(CultureInfo.InvariantCulture)),
                new InspectorPropertyViewModel("Distinct types", typeCount.ToString(CultureInfo.InvariantCulture)),
            ]);
    }

    private static string FormatPrefixes(ModelicaComponentPrefixes prefixes)
    {
        var values = new List<string>();
        if (!prefixes.IsPublic)
        {
            values.Add("protected");
        }

        AddWhen(values, prefixes.IsFinal, "final");
        AddWhen(values, prefixes.IsInner, "inner");
        AddWhen(values, prefixes.IsOuter, "outer");
        AddWhen(values, prefixes.IsReplaceable, "replaceable");
        AddWhen(values, prefixes.IsRedeclare, "redeclare");
        AddWhenPresent(values, prefixes.Connector);
        AddWhenPresent(values, prefixes.Variability);
        AddWhenPresent(values, prefixes.Direction);
        return values.Count == 0 ? "public" : string.Join(' ', values);
    }

    private static void AddWhen(ICollection<string> values, bool condition, string value)
    {
        if (condition)
        {
            values.Add(value);
        }
    }

    private static void AddWhenPresent(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static string FormatPoint(ModelicaPoint point) =>
        $"({FormatNumber(point.X)}, {FormatNumber(point.Y)})";

    private static string FormatExtent(ModelicaExtent extent) =>
        $"{FormatPoint(extent.First)} – {FormatPoint(extent.Second)}";

    private static string FormatNumber(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
}
