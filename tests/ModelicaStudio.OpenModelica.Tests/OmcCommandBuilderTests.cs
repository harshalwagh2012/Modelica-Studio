using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.OpenModelica.Commands;

namespace ModelicaStudio.OpenModelica.Tests;

public sealed class OmcCommandBuilderTests
{
    [Fact]
    public void LoadFile_EscapesBackslashesQuotesAndNewlines()
    {
        var command = OmcCommandBuilder.LoadFile("C:\\models\\a\"b\n.mo");

        Assert.Equal("loadFile(\"C:\\\\models\\\\a\\\"b\\n.mo\")", command);
    }

    [Theory]
    [InlineData("Modelica")]
    [InlineData("Modelica.Electrical.Analog.Basic.Resistor")]
    public void Identifier_AllowsQualifiedModelicaNames(string value) =>
        Assert.Equal(value, OmcCommandBuilder.Identifier(value));

    [Theory]
    [InlineData("Modelica);quit()")]
    [InlineData("Modelica name")]
    [InlineData("")]
    public void Identifier_RejectsCommandInjection(string value) =>
        Assert.Throws<ArgumentException>(() => OmcCommandBuilder.Identifier(value));

    [Fact]
    public void Simulate_UsesInvariantTypedOptions()
    {
        var configuration = new SimulationConfiguration
        {
            StartTime = 0.25,
            StopTime = 2.5,
            NumberOfIntervals = 250,
            Tolerance = 1e-7,
            Method = "dassl",
            OutputFormat = "mat",
        };

        var command = OmcCommandBuilder.Simulate("BouncingBall", configuration);

        Assert.Equal(
            "simulate(BouncingBall, startTime=0.25, stopTime=2.5, numberOfIntervals=250, tolerance=1E-07, method=\"dassl\", outputFormat=\"mat\")",
            command);
    }

    [Fact]
    public void GetModelInstanceAnnotation_UsesValidatedClassAndGraphicalFilters()
    {
        var command = OmcCommandBuilder.GetModelInstanceAnnotation("Modelica.Blocks.Sources.Sine");

        Assert.Equal(
            "getModelInstanceAnnotation(Modelica.Blocks.Sources.Sine, {\"Icon\",\"Diagram\"}, false)",
            command);
    }

    [Fact]
    public void GetModelInstance_UsesValidatedClassAndCompactJson()
    {
        var command = OmcCommandBuilder.GetModelInstance("Modelica.Blocks.Sources.Sine");

        Assert.Equal(
            "getModelInstance(Modelica.Blocks.Sources.Sine, prettyPrint=false)",
            command);
    }

    [Fact]
    public void IsPartial_UsesValidatedClassName() =>
        Assert.Equal("isPartial(Modelica.PartialMedium)", OmcCommandBuilder.IsPartial("Modelica.PartialMedium"));

    [Fact]
    public void ComponentAuthoring_UsesCurrentTypedScriptingSignatures()
    {
        var placement = new ModelicaPlacement(
            true,
            ModelicaTransformation.Default with { Origin = new ModelicaPoint(12, -8) },
            false,
            null);

        Assert.Equal(
            "getDefaultComponentName(Modelica.Electrical.Analog.Basic.Resistor)",
            OmcCommandBuilder.GetDefaultComponentName("Modelica.Electrical.Analog.Basic.Resistor"));
        Assert.Equal(
            "addComponent(resistor1, Modelica.Electrical.Analog.Basic.Resistor, Demo.System, annotate=Placement(visible=true, transformation=transformation(origin={12,-8}, extent={{-10,-10},{10,10}}, rotation=0), iconVisible=false))",
            OmcCommandBuilder.AddComponent(
                "Demo.System",
                "resistor1",
                "Modelica.Electrical.Analog.Basic.Resistor",
                placement));
        Assert.Equal(
            "deleteComponent(resistor1, Demo.System)",
            OmcCommandBuilder.DeleteComponent("Demo.System", "resistor1"));
    }

    [Fact]
    public void ComponentAuthoring_RejectsUnsafeNames()
    {
        var placement = new ModelicaPlacement(true, ModelicaTransformation.Default, false, null);

        Assert.Throws<ArgumentException>(() => OmcCommandBuilder.AddComponent(
            "Demo",
            "x);quit()",
            "Demo.Part",
            placement));
        Assert.Throws<ArgumentException>(() => OmcCommandBuilder.DeleteComponent("Demo", "x.y"));
    }

    [Fact]
    public void ConnectionAuthoring_UsesValidatedReferencesAndCompleteLineAnnotation()
    {
        var line = ConnectionLine();

        Assert.Equal(
            "addConnection(source.y[1], plant.'heat port', Demo.System, annotate=Line(visible=true, origin={0,0}, rotation=0, points={{-10,5},{0,5},{0,-4},{12,-4}}, color={0,0,255}, pattern=LinePattern.Dash, thickness=0.5, arrow={Arrow.Open,Arrow.Filled}, arrowSize=4, smooth=Smooth.Bezier))",
            OmcCommandBuilder.AddConnection("Demo.System", "source.y[1]", "plant.'heat port'", line));
        Assert.Equal(
            "deleteConnection(source.y[1], plant.'heat port', Demo.System)",
            OmcCommandBuilder.DeleteConnection("Demo.System", "source.y[1]", "plant.'heat port'"));
        Assert.Equal(
            "updateConnectionAnnotation(Demo.System, \"source.y[1]\", \"plant.'heat port'\", \"annotate=Line(visible=true, origin={0,0}, rotation=0, points={{-10,5},{0,5},{0,-4},{12,-4}}, color={0,0,255}, pattern=LinePattern.Dash, thickness=0.5, arrow={Arrow.Open,Arrow.Filled}, arrowSize=4, smooth=Smooth.Bezier)\")",
            OmcCommandBuilder.UpdateConnectionAnnotation("Demo.System", "source.y[1]", "plant.'heat port'", line));
    }

    [Theory]
    [InlineData("x);quit()")]
    [InlineData("x[0]")]
    [InlineData("x[i]")]
    [InlineData("x..y")]
    public void ConnectionAuthoring_RejectsUnsafeReferences(string reference) =>
        Assert.Throws<ArgumentException>(() => OmcCommandBuilder.AddConnection(
            "Demo",
            reference,
            "safe.port",
            ConnectionLine()));

    [Fact]
    public void SetComponentPlacement_UsesCurrentCodeAnnotationSyntaxAndInvariantGeometry()
    {
        var placement = new ModelicaPlacement(
            true,
            new ModelicaTransformation(
                new ModelicaPoint(20.5, -30),
                new ModelicaExtent(new ModelicaPoint(10, -5), new ModelicaPoint(-10, 5)),
                90),
            false,
            new ModelicaTransformation(
                new ModelicaPoint(0, 1),
                new ModelicaExtent(new ModelicaPoint(-2, -3), new ModelicaPoint(2, 3)),
                0));

        var command = OmcCommandBuilder.SetComponentPlacement("Demo.System", "resistor1", placement);

        Assert.Equal(
            "setElementAnnotation(Demo.System.resistor1, $Code((Placement(visible=true, transformation=transformation(origin={20.5,-30}, extent={{10,-5},{-10,5}}, rotation=90), iconVisible=false, iconTransformation=transformation(origin={0,1}, extent={{-2,-3},{2,3}}, rotation=0)))))",
            command);
    }

    [Fact]
    public void SetComponentPlacement_RejectsUnsafeComponentName() =>
        Assert.Throws<ArgumentException>(() => OmcCommandBuilder.SetComponentPlacement(
            "Demo",
            "x);quit()",
            new ModelicaPlacement(true, ModelicaTransformation.Default, false, null)));

    [Fact]
    public void SaveClass_UsesValidatedClassName() =>
        Assert.Equal("save(Demo.System)", OmcCommandBuilder.SaveClass("Demo.System"));

    [Fact]
    public void LoadString_UsesReplaceSemanticsAndEscapesSourceAndPath()
    {
        var command = OmcCommandBuilder.LoadString(
            "model A\n  String s = \"x\";\nend A;",
            "C:\\models\\A.mo");

        Assert.Equal(
            "loadString(\"model A\\n  String s = \\\"x\\\";\\nend A;\", \"C:\\\\models\\\\A.mo\", \"UTF-8\", merge=false, uses=true, notify=false)",
            command);
    }

    [Fact]
    public void LoadClassContentString_UsesValidatedTargetAndOffsets()
    {
        var command = OmcCommandBuilder.LoadClassContentString(
            "Demo.Part plant annotation(Placement());\nequation\nconnect(plant.a, plant.b);",
            "Demo.System",
            10,
            -4);

        Assert.Equal(
            "loadClassContentString(\"Demo.Part plant annotation(Placement());\\nequation\\nconnect(plant.a, plant.b);\", Demo.System, 10, -4)",
            command);
    }

    private static ModelicaGraphicPrimitive ConnectionLine() => new()
    {
        Kind = ModelicaGraphicKind.Line,
        Visible = new ModelicaAnnotationValue<bool>(true),
        Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
        Rotation = new ModelicaAnnotationValue<double>(0),
        Points =
        [
            new ModelicaPoint(-10, 5),
            new ModelicaPoint(0, 5),
            new ModelicaPoint(0, -4),
            new ModelicaPoint(12, -4),
        ],
        Style = new ModelicaGraphicStyle
        {
            LineColor = new ModelicaColor(0, 0, 255),
            LinePattern = ModelicaLinePattern.Dash,
            LineThickness = 0.5,
        },
        Arrows = [ModelicaArrow.Open, ModelicaArrow.Filled],
        ArrowSize = 4,
        Smooth = ModelicaSmooth.Bezier,
        RawJson = "{}",
    };
}
