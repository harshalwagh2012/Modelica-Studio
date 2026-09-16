using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaDiagramHitTesterTests
{
    [Fact]
    public void HitTest_SelectsRotatedComponentInsidePlacement()
    {
        var component = CreateComponent(new ModelicaTransformation(
            new ModelicaPoint(20, 0),
            new ModelicaExtent(new ModelicaPoint(-10, -5), new ModelicaPoint(10, 5)),
            90));
        var instance = CreateInstance([component], []);

        var inside = ModelicaDiagramHitTester.HitTest(instance, new ModelicaPoint(20, 8), 2, false);
        var outside = ModelicaDiagramHitTester.HitTest(instance, new ModelicaPoint(35, 0), 2, false);

        Assert.Same(component, inside.Component);
        Assert.False(outside.HasSelection);
    }

    [Fact]
    public void HitTest_SelectsConnectionWithinTolerance()
    {
        var line = new ModelicaGraphicPrimitive
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(5, 10)),
            Rotation = new ModelicaAnnotationValue<double>(90),
            Points = [new ModelicaPoint(0, 0), new ModelicaPoint(20, 0)],
            RawJson = "{}",
        };
        var connection = new ModelicaDiagramConnection("a.y", "b.u", line, false, "Demo", "{}");
        var instance = CreateInstance([], [connection]);

        var hit = ModelicaDiagramHitTester.HitTest(instance, new ModelicaPoint(6.5, 22), 2, false);
        var miss = ModelicaDiagramHitTester.HitTest(instance, new ModelicaPoint(9, 22), 2, false);

        Assert.Same(connection, hit.Connection);
        Assert.False(miss.HasSelection);
    }

    [Fact]
    public void HitTest_DoesNotSelectConnectionInIconView()
    {
        var line = new ModelicaGraphicPrimitive
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(0),
            Points = [new ModelicaPoint(0, 0), new ModelicaPoint(20, 0)],
            RawJson = "{}",
        };
        var connection = new ModelicaDiagramConnection("a", "b", line, false, "Demo", "{}");

        var hit = ModelicaDiagramHitTester.HitTest(CreateInstance([], [connection]), new ModelicaPoint(10, 0), 2, true);

        Assert.False(hit.HasSelection);
    }

    [Fact]
    public void HitTest_SelectsUnannotatedConnectionUsingConnectorAnchors()
    {
        var first = CreateComponent(ModelicaTransformation.Default with
        {
            Origin = new ModelicaPoint(-40, 0),
        }) with
        {
            Name = "a",
            TypeName = "Demo.Signal",
            Restriction = "connector",
        };
        var second = CreateComponent(ModelicaTransformation.Default with
        {
            Origin = new ModelicaPoint(40, 0),
        }) with
        {
            Name = "b",
            TypeName = "Demo.Signal",
            Restriction = "connector",
        };
        var connection = new ModelicaDiagramConnection(
            "a.signal",
            "b.signal",
            null,
            false,
            "Demo",
            "{}");

        var hit = ModelicaDiagramHitTester.HitTest(
            CreateInstance([first, second], [connection]),
            new ModelicaPoint(0, 1),
            2,
            false);

        Assert.Same(connection, hit.Connection);
    }

    [Fact]
    public void SelectComponentsInBox_SelectsIntersectingRotatedPlacements()
    {
        var first = CreateComponent(new ModelicaTransformation(
            new ModelicaPoint(20, 0),
            new ModelicaExtent(new ModelicaPoint(-10, -5), new ModelicaPoint(10, 5)),
            45));
        var second = CreateComponent(ModelicaTransformation.Default) with { Name = "controller" };
        var selection = new ModelicaExtent(
            new ModelicaPoint(25, 4),
            new ModelicaPoint(35, 15));

        var selected = ModelicaDiagramHitTester.SelectComponentsInBox(
            CreateInstance([first, second], []),
            selection,
            showIcon: false);

        Assert.Collection(selected, component => Assert.Same(first, component));
    }

    [Fact]
    public void SelectComponentsInBox_SelectsComponentContainingEntireBox()
    {
        var component = CreateComponent(ModelicaTransformation.Default);
        var selection = new ModelicaExtent(
            new ModelicaPoint(-2, -2),
            new ModelicaPoint(2, 2));

        var selected = ModelicaDiagramHitTester.SelectComponentsInBox(
            CreateInstance([component], []),
            selection,
            showIcon: false);

        Assert.Collection(selected, item => Assert.Same(component, item));
    }

    private static ModelicaComponentInstance CreateComponent(ModelicaTransformation transformation)
    {
        var icon = new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []);
        return new ModelicaComponentInstance(
            "plant",
            "Demo.Plant",
            "model",
            new ModelicaComponentPrefixes(),
            new ModelicaPlacement(true, transformation, false, null),
            new ModelicaGraphicalAnnotationSnapshot("Demo.Plant", "model", icon, null, [], [], "{}"),
            false,
            "Demo",
            "{}");
    }

    private static ModelicaModelInstanceSnapshot CreateInstance(
        IReadOnlyList<ModelicaComponentInstance> components,
        IReadOnlyList<ModelicaDiagramConnection> connections)
    {
        var view = new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []);
        var annotation = new ModelicaGraphicalAnnotationSnapshot("Demo", "model", view, view, [], [], "{}");
        return new ModelicaModelInstanceSnapshot("Demo", "model", annotation, components, connections, [], "{}");
    }
}
