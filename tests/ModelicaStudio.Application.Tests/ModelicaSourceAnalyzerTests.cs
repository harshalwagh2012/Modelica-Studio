using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Application.Tests;

public sealed class ModelicaSourceAnalyzerTests
{
    private readonly ModelicaSourceAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_IndexesScopeClassDeclarationsAndSections()
    {
        const string source = """
            within Controls.Examples;
            model Plant
              extends Modelica.Icons.Example;
              parameter Modelica.Units.SI.Time stopTime = 10;
              input Real u;
              output Real y;
            equation
              y = u;
            end Plant;
            """;

        var result = _analyzer.Analyze(source);

        Assert.Equal("Controls.Examples", result.WithinPackage);
        Assert.Collection(
            result.Symbols,
            symbol => AssertSymbol(symbol, "Plant", ModelicaSourceSymbolKind.Model, null),
            symbol => AssertSymbol(symbol, "Modelica.Icons.Example", ModelicaSourceSymbolKind.Extends, null),
            symbol => AssertSymbol(symbol, "stopTime", ModelicaSourceSymbolKind.Parameter, "Modelica.Units.SI.Time"),
            symbol => AssertSymbol(symbol, "u", ModelicaSourceSymbolKind.Input, "Real"),
            symbol => AssertSymbol(symbol, "y", ModelicaSourceSymbolKind.Output, "Real"),
            symbol => AssertSymbol(symbol, "equation", ModelicaSourceSymbolKind.EquationSection, null));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Analyze_IgnoresKeywordsInsideCommentsAndStrings()
    {
        const string source = """
            // model Fake
            model RealModel "package AlsoFake"
              String text = "end RealModel; model StillFake";
              /* function Hidden
                 end Hidden; */
            end RealModel;
            """;

        var result = _analyzer.Analyze(source);

        var model = Assert.Single(result.Symbols);
        AssertSymbol(model, "RealModel", ModelicaSourceSymbolKind.Model, null);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Analyze_IndexesNestedAndShortClassesWithoutFalseUnclosedIssue()
    {
        const string source = """
            package P
              type Gain = Real(min = 0);
              model M
              algorithm
              end M;
            end P;
            """;

        var result = _analyzer.Analyze(source);

        Assert.Equal(["P", "Gain", "M", "algorithm"], result.Symbols.Select(static symbol => symbol.Name));
        Assert.Equal([0, 1, 1, 2], result.Symbols.Select(static symbol => symbol.NestingLevel));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Analyze_InvalidSnapshotReturnsUsefulIssuesWithoutThrowing()
    {
        const string source = "model Broken\n  /* unfinished";

        var result = _analyzer.Analyze(source);

        Assert.Equal("Broken", Assert.Single(result.Symbols).Name);
        Assert.Equal(2, result.Issues.Count);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("Unterminated block comment", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Message.Contains("no matching end clause", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_QuotedIdentifiersAndArrayDeclarationsAreIndexed()
    {
        const string source = """
            model 'Controlled plant'
              parameter Real[3] gains = {1, 2, 3};
            end 'Controlled plant';
            """;

        var result = _analyzer.Analyze(source);

        Assert.Equal("Controlled plant", result.Symbols[0].Name);
        AssertSymbol(result.Symbols[1], "gains", ModelicaSourceSymbolKind.Parameter, "Real");
        Assert.Empty(result.Issues);
    }

    private static void AssertSymbol(
        ModelicaSourceSymbol symbol,
        string name,
        ModelicaSourceSymbolKind kind,
        string? typeName)
    {
        Assert.Equal(name, symbol.Name);
        Assert.Equal(kind, symbol.Kind);
        Assert.Equal(typeName, symbol.TypeName);
        Assert.True(symbol.Line > 0);
        Assert.True(symbol.Column > 0);
    }
}
