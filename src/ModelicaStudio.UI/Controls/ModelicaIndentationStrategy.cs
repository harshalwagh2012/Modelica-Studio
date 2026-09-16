using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;

namespace ModelicaStudio.UI.Controls;

public sealed partial class ModelicaIndentationStrategy(int indentationSize = 2) : IIndentationStrategy
{
    public void IndentLine(TextDocument document, DocumentLine line)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(line);

        var previous = line.PreviousLine;
        while (previous is not null && string.IsNullOrWhiteSpace(GetLineText(document, previous)))
        {
            previous = previous.PreviousLine;
        }

        var previousText = previous is null ? string.Empty : GetLineText(document, previous);
        var currentText = GetLineText(document, line);
        var indentation = LeadingWhitespace(previousText);
        if (OpensIndentedSection(previousText.Trim()))
        {
            indentation += new string(' ', indentationSize);
        }

        if (ClosesIndentedSection(currentText.TrimStart()) && indentation.Length >= indentationSize)
        {
            indentation = indentation[..^indentationSize];
        }

        var currentIndentationLength = currentText.Length - currentText.TrimStart().Length;
        document.Replace(line.Offset, currentIndentationLength, indentation);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (beginLine < 1 || endLine < beginLine || endLine > document.LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(beginLine));
        }

        for (var lineNumber = beginLine; lineNumber <= endLine; lineNumber++)
        {
            IndentLine(document, document.GetLineByNumber(lineNumber));
        }
    }

    private static string GetLineText(TextDocument document, DocumentLine line) =>
        document.GetText(line.Offset, line.Length);

    private static string LeadingWhitespace(string value) =>
        value[..(value.Length - value.TrimStart().Length)];

    private static bool OpensIndentedSection(string value)
    {
        if (value.Length == 0 || value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        return SectionHeader().IsMatch(value)
            || value.EndsWith(" then", StringComparison.Ordinal)
            || value.EndsWith(" loop", StringComparison.Ordinal);
    }

    private static bool ClosesIndentedSection(string value) =>
        value.StartsWith("end ", StringComparison.Ordinal)
        || value.StartsWith("end;", StringComparison.Ordinal)
        || value.StartsWith("else", StringComparison.Ordinal);

    [GeneratedRegex("^(?:algorithm|block|class|connector|equation|function|model|package|protected|public|record)(?:\\s|$)")]
    private static partial Regex SectionHeader();
}
