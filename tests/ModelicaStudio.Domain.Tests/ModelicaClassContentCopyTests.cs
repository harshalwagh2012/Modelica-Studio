using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaClassContentCopyTests
{
    [Fact]
    public void Create_PreservesCompleteDeclarationsAndInternalConnections()
    {
        const string source = """
            model Demo "plant appears in documentation"
              final replaceable Demo.Part plant(k=42, redeclare package Medium = Water)
                constrainedby Demo.Part "configured" annotation(Placement(transformation(origin={0,0})));
            protected
              input Demo.Controller 'control unit'(gain=2) annotation(Placement(transformation(origin={20,0})));
            equation
              connect(plant.port, 'control unit'.port) annotation(Line(points={{0,0},{20,0}}));
              plant.signal = 1;
            end Demo;
            """;
        var plant = CreateComponent("plant", true);
        var control = CreateComponent("control unit", false) with { Name = "control unit" };
        var connection = new ModelicaDiagramConnection(
            "plant.port",
            "'control unit'.port",
            null,
            false,
            "Demo",
            "{}");

        var content = ModelicaClassContentCopy.Create(source, [plant, control], [connection]);

        Assert.Contains("public\nfinal replaceable Demo.Part plant(k=42, redeclare package Medium = Water)", content);
        Assert.Contains("protected\ninput Demo.Controller 'control unit'(gain=2)", content);
        Assert.Contains("equation\nconnect(plant.port, 'control unit'.port) annotation", content);
        Assert.DoesNotContain("plant.signal = 1", content);
    }

    [Fact]
    public void Create_DoesNotCopyConnectionsToComponentsOutsideSelection()
    {
        const string source = """
            model Demo
              Demo.Part plant;
              Demo.Part sink;
            equation
              connect(plant.port, sink.port);
            end Demo;
            """;

        var content = ModelicaClassContentCopy.Create(
            source,
            [CreateComponent("plant", true)],
            [new ModelicaDiagramConnection("plant.port", "sink.port", null, false, "Demo", "{}")]);

        Assert.Equal("public\nDemo.Part plant;", content);
    }

    [Fact]
    public void Create_RejectsSharedDeclarationsInsteadOfDuplicatingUnselectedSiblings()
    {
        const string source = "model Demo\n  Demo.Part plant(k=1), sink(k=2);\nend Demo;";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ModelicaClassContentCopy.Create(source, [CreateComponent("plant", true)], []));

        Assert.Contains("shares a declaration", exception.Message, StringComparison.Ordinal);
    }

    private static ModelicaComponentInstance CreateComponent(string name, bool isPublic) => new(
        name,
        "Demo.Part",
        "model",
        new ModelicaComponentPrefixes(IsPublic: isPublic),
        new ModelicaPlacement(true, ModelicaTransformation.Default, false, null),
        null,
        false,
        "Demo",
        "{}");
}
