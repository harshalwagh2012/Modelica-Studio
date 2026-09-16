using AvaloniaEdit.Document;
using ModelicaStudio.UI.Controls;

namespace ModelicaStudio.UI.Tests;

public sealed class ModelicaIndentationStrategyTests
{
    [Fact]
    public void IndentLine_IndentsBodyAfterClassHeader()
    {
        var document = new TextDocument("model Demo\nReal x;\nend Demo;");
        var strategy = new ModelicaIndentationStrategy();

        strategy.IndentLine(document, document.GetLineByNumber(2));

        Assert.Equal("  Real x;", document.GetText(document.GetLineByNumber(2)));
    }

    [Fact]
    public void IndentLine_DedentsEndClause()
    {
        var document = new TextDocument("model Demo\n  Real x;\n    end Demo;");
        var strategy = new ModelicaIndentationStrategy();

        strategy.IndentLine(document, document.GetLineByNumber(3));

        Assert.Equal("end Demo;", document.GetText(document.GetLineByNumber(3)));
    }

    [Fact]
    public void IndentLine_IndentsAfterControlClause()
    {
        var document = new TextDocument("  if x > 0 then\ny := 1;\n  end if;");
        var strategy = new ModelicaIndentationStrategy();

        strategy.IndentLine(document, document.GetLineByNumber(2));

        Assert.Equal("    y := 1;", document.GetText(document.GetLineByNumber(2)));
    }
}
