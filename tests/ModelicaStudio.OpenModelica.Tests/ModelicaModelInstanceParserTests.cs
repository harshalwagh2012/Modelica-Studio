using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.OpenModelica.Parsing;

namespace ModelicaStudio.OpenModelica.Tests;

public sealed class ModelicaModelInstanceParserTests
{
    [Fact]
    public void Parse_ReadsPlacedComponentsNestedIconsAndPrefixes()
    {
        var snapshot = ReadFixture();

        Assert.Equal("Fixture.System", snapshot.ClassName);
        Assert.Equal(3, snapshot.Components.Count);
        var inherited = Assert.Single(snapshot.Components, component => component.Name == "inheritedSource");
        Assert.True(inherited.IsInherited);
        Assert.Equal("Fixture.BaseSystem", inherited.DeclaringClass);

        var plant = Assert.Single(snapshot.Components, component => component.Name == "plant");
        Assert.Equal("Fixture.Plant", plant.TypeName);
        Assert.Equal("model", plant.Restriction);
        Assert.True(plant.Prefixes.IsFinal);
        Assert.Single(Assert.IsType<ModelicaGraphicalView>(plant.TypeGraphics?.Icon).Graphics);
        var placement = Assert.IsType<ModelicaPlacement>(plant.Placement);
        Assert.Equal(new ModelicaPoint(20, 30), placement.Transformation.Origin);
        Assert.Equal(90, placement.Transformation.Rotation);
        Assert.Equal(new ModelicaPoint(-20, -10), placement.Transformation.Extent.First);
        var output = Assert.Single(plant.TypeComponents);
        Assert.Equal("output", output.Name);
        Assert.Equal("connector", output.Restriction);
        Assert.Equal("output", output.TypePrefixes.Direction);
        Assert.Equal("Real", Assert.Single(output.TypeComponents).RootTypeName);

        var port = Assert.Single(snapshot.Components, component => component.Name == "port");
        Assert.False(port.Prefixes.IsPublic);
        Assert.Equal("input", port.Prefixes.Direction);
        Assert.NotNull(port.Placement?.IconTransformation);
        Assert.Equal(["2"], Assert.Single(port.TypeComponents).Dimensions);
    }

    [Fact]
    public void Parse_ReadsConnectionEndpointsAndLineAnnotation()
    {
        var connection = Assert.Single(ReadFixture().Connections);

        Assert.Equal("plant.output", connection.Left);
        Assert.Equal("port.signal[1]", connection.Right);
        var line = Assert.IsType<ModelicaGraphicPrimitive>(connection.Line);
        Assert.Equal(4, line.Points.Count);
        Assert.Equal(new ModelicaColor(0, 0, 255), line.Style.LineColor);
        Assert.Equal(ModelicaLinePattern.Dash, line.Style.LinePattern);
        Assert.Equal(0.5, line.Style.LineThickness);
        Assert.Equal([ModelicaArrow.Open, ModelicaArrow.Filled], line.Arrows);
        Assert.Equal(4, line.ArrowSize);
        Assert.Equal(ModelicaSmooth.Bezier, line.Smooth);
    }

    [Fact]
    public void Parse_MissingPlacementRemainsUnplaced()
    {
        const string json = """
            {"name":"M","restriction":"model","elements":[
              {"$kind":"component","name":"x","type":"Real"}
            ]}
            """;

        var component = Assert.Single(ModelicaModelInstanceParser.Parse(json).Components);

        Assert.Null(component.Placement);
        Assert.Equal("type", component.Restriction);
    }

    [Fact]
    public void Parse_InvalidJsonReportsStableFormatException()
    {
        var exception = Assert.Throws<FormatException>(() => ModelicaModelInstanceParser.Parse("{broken"));

        Assert.Contains("invalid model-instance JSON", exception.Message, StringComparison.Ordinal);
    }

    private static ModelicaModelInstanceSnapshot ReadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Instances", "component-placement.json");
        return ModelicaModelInstanceParser.Parse(File.ReadAllText(path));
    }
}
