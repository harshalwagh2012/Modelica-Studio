using ModelicaStudio.Domain.Diagnostics;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.OpenModelica.Parsing;

namespace ModelicaStudio.OpenModelica.Tests;

public sealed class OmcResponseParserTests
{
    [Theory]
    [InlineData("true\n", true)]
    [InlineData("false", false)]
    public void ParseBoolean_ParsesOmcLiterals(string response, bool expected) =>
        Assert.Equal(expected, OmcResponseParser.ParseBoolean(response));

    [Fact]
    public void ParseStringList_PreservesQuotedCommas()
    {
        var result = OmcResponseParser.ParseStringList("{Modelica.Blocks,\"a,b\",Modelica.Fluid}");

        Assert.Equal(["Modelica.Blocks", "a,b", "Modelica.Fluid"], result);
    }

    [Fact]
    public void ParseNumericMatrix_ParsesScientificNotation()
    {
        var result = OmcResponseParser.ParseNumericMatrix("{{0,1.5,2e0},{3,-4.25,5E-2}}");

        Assert.Equal([0d, 1.5d, 2d], result[0]);
        Assert.Equal([3d, -4.25d, 0.05d], result[1]);
    }

    [Fact]
    public void ParseSimulationResultFile_ExtractsAndUnescapesPath()
    {
        var response = "record SimulationResult resultFile = \"/tmp/RC_Test_res.mat\", messages = \"\" end SimulationResult;";

        Assert.Equal("/tmp/RC_Test_res.mat", OmcResponseParser.ParseSimulationResultFile(response));
    }

    [Fact]
    public void ParseDiagnostics_ProducesStructuredCompilerLocation()
    {
        const string response = """
            {record OpenModelica.Scripting.ErrorMessage
              info = record OpenModelica.Scripting.SourceInfo
                filename = "/tmp/Broken.mo",
                readonly = false,
                lineStart = 4,
                columnStart = 7,
                lineEnd = 4,
                columnEnd = 12
              end OpenModelica.Scripting.SourceInfo;,
              message = "Unknown component: x",
              kind = .OpenModelica.Scripting.ErrorKind.scripting,
              level = .OpenModelica.Scripting.ErrorLevel.error,
              id = 101
            end OpenModelica.Scripting.ErrorMessage;}
            """;

        var diagnostic = Assert.Single(OmcResponseParser.ParseDiagnostics(response));

        Assert.Equal(CompilerDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Unknown component: x", diagnostic.Message);
        Assert.Equal("/tmp/Broken.mo", diagnostic.File);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(7, diagnostic.Column);
        Assert.Equal(4, diagnostic.EndLine);
        Assert.Equal(12, diagnostic.EndColumn);
        Assert.Equal(101, diagnostic.Id);
    }

    [Theory]
    [InlineData("\"package\"", ModelicaClassKind.Package)]
    [InlineData("\"expandable connector\"", ModelicaClassKind.Connector)]
    [InlineData("\"operator record\"", ModelicaClassKind.Record)]
    public void ParseClassKind_MapsRestrictions(string response, ModelicaClassKind expected) =>
        Assert.Equal(expected, OmcResponseParser.ParseClassKind(response));
}
