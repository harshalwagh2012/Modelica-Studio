using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.OpenModelica.Parsing;

namespace ModelicaStudio.OpenModelica.Tests;

public sealed class ModelInstanceAnnotationParserTests
{
    [Fact]
    public void Parse_BasicShapesProducesTypedIconAndDiagram()
    {
        var result = ParseFixture("basic-shapes.json");

        Assert.Equal("M", result.ClassName);
        Assert.Equal("model", result.Restriction);
        var rectangle = Assert.Single(Assert.IsType<ModelicaGraphicalView>(result.Icon).Graphics);
        Assert.Equal(ModelicaGraphicKind.Rectangle, rectangle.Kind);
        Assert.Equal(new ModelicaColor(28, 108, 200), rectangle.Style.LineColor);
        Assert.Equal(ModelicaLinePattern.Solid, rectangle.Style.LinePattern);
        Assert.Equal(ModelicaBorderPattern.None, rectangle.BorderPattern);
        Assert.Equal(new ModelicaPoint(-66, 78), rectangle.Extent?.StaticValue.First);
        Assert.Equal(new ModelicaPoint(70, -56), rectangle.Extent?.StaticValue.Second);

        var ellipse = Assert.Single(Assert.IsType<ModelicaGraphicalView>(result.Diagram).Graphics);
        Assert.Equal(ModelicaGraphicKind.Ellipse, ellipse.Kind);
        Assert.Equal(ModelicaEllipseClosure.Chord, ellipse.EllipseClosure);
        Assert.Equal(360, ellipse.EndAngle);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_InheritedIconPreservesBaseClassAndDynamicVisibility()
    {
        var result = ParseFixture("inherited-icon.json");

        Assert.True(Assert.IsType<ModelicaGraphicalView>(result.Icon).CoordinateSystem.Coordinates.PreserveAspectRatio);
        var inherited = Assert.Single(result.InheritedAnnotations);
        Assert.Equal("Icons.Example", inherited.BaseClassName);
        var inheritedIcon = Assert.IsType<ModelicaGraphicalView>(inherited.Icon);
        Assert.False(inheritedIcon.CoordinateSystem.Coordinates.PreserveAspectRatio);
        var ellipse = Assert.Single(inheritedIcon.Graphics);
        Assert.True(ellipse.Visible.StaticValue);
        Assert.True(ellipse.Visible.IsDynamic);
        Assert.Contains("cref", ellipse.Visible.DynamicExpressionJson, StringComparison.Ordinal);
        Assert.Equal(ModelicaFillPattern.Solid, ellipse.Style.FillPattern);
    }

    [Fact]
    public void Parse_DynamicSelectUsesStaticBranchAndPreservesExpression()
    {
        var result = ParseFixture("dynamic-select.json");

        var rectangle = Assert.Single(Assert.IsType<ModelicaGraphicalView>(result.Icon).Graphics);
        Assert.True(rectangle.Extent?.IsDynamic);
        Assert.Equal(new ModelicaPoint(20, 20), rectangle.Extent?.StaticValue.Second);
        Assert.Contains("DynamicSelect", rectangle.Extent?.DynamicExpressionJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TextAndCompilerErrorAreBothPreserved()
    {
        var result = ParseFixture("text-and-error.json");

        Assert.Equal(2, Assert.IsType<ModelicaGraphicalView>(result.Icon).Graphics.Count);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("Variable x", issue.Message, StringComparison.Ordinal);
        var text = result.Icon.Graphics[1];
        Assert.Equal(ModelicaGraphicKind.Text, text.Kind);
        Assert.Equal("str", text.TextString);
        Assert.True(text.TextColor.UsesInheritedColor);
        Assert.Equal(ModelicaTextAlignment.Center, text.TextAlignment);
    }

    [Fact]
    public void Parse_InvalidJsonFailsWithFormatException() =>
        Assert.Throws<FormatException>(() => ModelInstanceAnnotationParser.Parse("{not-json"));

    [Fact]
    public void Parse_PolygonUsesModelicaRecordFieldOrder()
    {
        const string json = """
            {
              "name": "P",
              "restriction": "model",
              "annotation": {
                "Icon": {
                  "graphics": [{
                    "$kind": "record",
                    "name": "Polygon",
                    "elements": [
                      true,
                      [0, 0],
                      0,
                      [[-10, -10], [10, -10], [0, 10]],
                      [1, 2, 3],
                      [4, 5, 6],
                      { "$kind": "enum", "name": "LinePattern.Dash" },
                      { "$kind": "enum", "name": "FillPattern.Solid" },
                      0.75,
                      { "$kind": "enum", "name": "Smooth.None" }
                    ]
                  }]
                }
              }
            }
            """;

        var polygon = Assert.Single(Assert.IsType<ModelicaGraphicalView>(
            ModelInstanceAnnotationParser.Parse(json).Icon).Graphics);

        Assert.Equal(3, polygon.Points.Count);
        Assert.Equal(new ModelicaColor(1, 2, 3), polygon.Style.LineColor);
        Assert.Equal(new ModelicaColor(4, 5, 6), polygon.Style.FillColor);
        Assert.Equal(ModelicaLinePattern.Dash, polygon.Style.LinePattern);
        Assert.Equal(ModelicaFillPattern.Solid, polygon.Style.FillPattern);
        Assert.Equal(0.75, polygon.Style.LineThickness);
    }

    [Fact]
    public void Parse_EmptyDynamicSelectFallsBackWithoutLosingExpression()
    {
        const string json = """
            {
              "name": "M",
              "annotation": {
                "Icon": {
                  "graphics": [{
                    "$kind": "record",
                    "name": "Rectangle",
                    "elements": [
                      { "$kind": "call", "name": "DynamicSelect", "arguments": [] },
                      [0, 0], 0, [0, 0, 0], [255, 255, 255],
                      { "$kind": "enum", "name": "LinePattern.Solid" },
                      { "$kind": "enum", "name": "FillPattern.Solid" },
                      0.25,
                      { "$kind": "enum", "name": "BorderPattern.None" },
                      [[-10, -10], [10, 10]], 0
                    ]
                  }]
                }
              }
            }
            """;

        var rectangle = Assert.Single(Assert.IsType<ModelicaGraphicalView>(
            ModelInstanceAnnotationParser.Parse(json).Icon).Graphics);

        Assert.True(rectangle.Visible.StaticValue);
        Assert.True(rectangle.Visible.IsDynamic);
    }

    private static ModelicaGraphicalAnnotationSnapshot ParseFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Annotations", name);
        return ModelInstanceAnnotationParser.Parse(File.ReadAllText(path));
    }
}
