using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaConnectionEditorTests
{
    [Fact]
    public void GetConnectorEndpoints_ComposesNestedConnectorAndPlacementTransforms()
    {
        var connector = Component(
            "y",
            "Demo.Signal",
            "connector",
            new ModelicaTransformation(
                new ModelicaPoint(100, 0),
                new ModelicaExtent(new ModelicaPoint(-10, -10), new ModelicaPoint(10, 10)),
                0),
            typeChildren: [Variable("signal", "Real")]);
        var block = Component(
            "source",
            "Demo.Source",
            "block",
            new ModelicaTransformation(
                new ModelicaPoint(20, 30),
                new ModelicaExtent(new ModelicaPoint(-20, -10), new ModelicaPoint(20, 10)),
                90),
            typeChildren: [connector]);
        var instance = Snapshot([block]);

        var endpoint = Assert.Single(ModelicaConnectionEditor.GetConnectorEndpoints(instance));

        Assert.Equal("source.y", endpoint.Path);
        Assert.False(endpoint.IsOutside);
        Assert.Equal(new ModelicaPoint(20, 50), endpoint.Position);
    }

    [Fact]
    public void GetConnectorEndpoints_ExpandsFixedConnectorArrays()
    {
        var connector = Component(
            "ports",
            "Demo.Pin",
            "connector",
            ModelicaTransformation.Default,
            dimensions: ["2", "2"]);

        var endpoints = ModelicaConnectionEditor.GetConnectorEndpoints(Snapshot([connector]));

        Assert.Equal(
            ["ports[1,1]", "ports[1,2]", "ports[2,1]", "ports[2,2]"],
            endpoints.Select(endpoint => endpoint.Path));
    }

    [Fact]
    public void CreateOrthogonalRoute_RemovesDuplicateAndCollinearPoints()
    {
        var route = ModelicaConnectionEditor.CreateOrthogonalRoute(
            new ModelicaPoint(-20, 10),
            new ModelicaPoint(20, -10));

        Assert.Equal(
        [
            new ModelicaPoint(-20, 10),
            new ModelicaPoint(0, 10),
            new ModelicaPoint(0, -10),
            new ModelicaPoint(20, -10),
        ],
        route);

        Assert.Equal(
            [new ModelicaPoint(0, 0), new ModelicaPoint(10, 0)],
            ModelicaConnectionEditor.CreateOrthogonalRoute(new ModelicaPoint(0, 0), new ModelicaPoint(10, 0)));
    }

    [Fact]
    public void CheckCompatibility_UsesDuplicateDirectionAndStructuralChecks()
    {
        var outputA = Endpoint("a.y", "Demo.Signal", "output", false, [Variable("signal", "Real")]);
        var outputB = Endpoint("b.y", "Demo.Signal", "output", false, [Variable("signal", "Real")]);
        var input = Endpoint("u", "Demo.Signal", "input", true, [Variable("signal", "Real")]);
        var incompatible = Endpoint("heat", "Demo.Heat", null, true, [Variable("temperature", "Real")]);

        Assert.False(ModelicaConnectionEditor.CheckCompatibility(outputA, outputB).IsCompatible);
        Assert.True(ModelicaConnectionEditor.CheckCompatibility(outputA, input).IsCompatible);
        Assert.False(ModelicaConnectionEditor.CheckCompatibility(outputA, incompatible).IsCompatible);
        Assert.False(ModelicaConnectionEditor.CheckCompatibility(
            outputA,
            input,
            [new ModelicaDiagramConnection("u", "a.y", null, false, "Demo", "{}")]).IsCompatible);
    }

    [Fact]
    public void MoveOrthogonalSegment_KeepsEndpointsAndSnapsDogleg()
    {
        var route = ModelicaConnectionEditor.MoveOrthogonalSegment(
            [new ModelicaPoint(-20, 0), new ModelicaPoint(20, 0)],
            0,
            new ModelicaPoint(3, 11),
            new ModelicaPoint(2, 2));

        Assert.Equal(
        [
            new ModelicaPoint(-20, 0),
            new ModelicaPoint(-20, 12),
            new ModelicaPoint(20, 12),
            new ModelicaPoint(20, 0),
        ],
        route);

        var middle = ModelicaConnectionEditor.MoveOrthogonalSegment(
            [
                new ModelicaPoint(-20, 0),
                new ModelicaPoint(0, 0),
                new ModelicaPoint(0, 20),
                new ModelicaPoint(20, 20),
            ],
            1,
            new ModelicaPoint(7, 4),
            new ModelicaPoint(2, 2));
        Assert.Equal(new ModelicaPoint(8, 0), middle[1]);
        Assert.Equal(new ModelicaPoint(8, 20), middle[2]);
    }

    [Fact]
    public void GetConnectionPoints_RoutesUnannotatedMemberReferenceThroughConnectorAnchors()
    {
        var source = Component(
            "source",
            "Demo.Signal",
            "connector",
            ModelicaTransformation.Default with { Origin = new ModelicaPoint(-20, 0) });
        var sink = Component(
            "sink",
            "Demo.Signal",
            "connector",
            ModelicaTransformation.Default with { Origin = new ModelicaPoint(20, 0) });
        var instance = Snapshot([source, sink]);
        var connection = new ModelicaDiagramConnection(
            "source.signal",
            "sink.signal[1]",
            null,
            false,
            "Demo",
            "{}");

        Assert.Equal(
            [new ModelicaPoint(-20, 0), new ModelicaPoint(20, 0)],
            ModelicaConnectionEditor.GetConnectionPoints(instance, connection));
    }

    private static ModelicaConnectorEndpoint Endpoint(
        string path,
        string typeName,
        string? direction,
        bool outside,
        IReadOnlyList<ModelicaComponentInstance> typeChildren)
    {
        var connector = Component(
            path.Split('.').Last(),
            typeName,
            "connector",
            ModelicaTransformation.Default,
            typeChildren: typeChildren,
            prefixes: new ModelicaComponentPrefixes(Direction: direction));
        return new ModelicaConnectorEndpoint(path, connector, connector, new ModelicaPoint(0, 0), outside);
    }

    private static ModelicaComponentInstance Variable(string name, string rootType) =>
        Component(name, rootType, "type", null) with { RootTypeName = rootType };

    private static ModelicaComponentInstance Component(
        string name,
        string typeName,
        string restriction,
        ModelicaTransformation? transformation,
        IReadOnlyList<ModelicaComponentInstance>? typeChildren = null,
        IReadOnlyList<string>? dimensions = null,
        ModelicaComponentPrefixes? prefixes = null)
    {
        var icon = new ModelicaGraphicalView(
            new ModelicaGraphicalCoordinateSystem
            {
                Coordinates = new ModelicaCoordinateSystem(
                    new ModelicaExtent(new ModelicaPoint(-100, -100), new ModelicaPoint(100, 100))),
            },
            []);
        return new ModelicaComponentInstance(
            name,
            typeName,
            restriction,
            prefixes ?? new ModelicaComponentPrefixes(),
            transformation is null ? null : new ModelicaPlacement(true, transformation, true, transformation),
            new ModelicaGraphicalAnnotationSnapshot(typeName, restriction, icon, null, [], [], "{}"),
            false,
            "Demo",
            "{}")
        {
            TypeComponents = typeChildren ?? [],
            Dimensions = dimensions ?? [],
            RootTypeName = typeName,
        };
    }

    private static ModelicaModelInstanceSnapshot Snapshot(
        IReadOnlyList<ModelicaComponentInstance> components)
    {
        var diagram = new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []);
        var annotation = new ModelicaGraphicalAnnotationSnapshot("Demo", "model", null, diagram, [], [], "{}");
        return new ModelicaModelInstanceSnapshot("Demo", "model", annotation, components, [], [], "{}");
    }
}
