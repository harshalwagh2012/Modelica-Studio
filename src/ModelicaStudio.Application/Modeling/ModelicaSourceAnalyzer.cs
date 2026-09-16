using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Application.Modeling;

public sealed class ModelicaSourceAnalyzer : IModelicaSourceAnalyzer
{
    private static readonly IReadOnlyDictionary<string, ModelicaSourceSymbolKind> ClassKeywords =
        new Dictionary<string, ModelicaSourceSymbolKind>(StringComparer.Ordinal)
        {
            ["model"] = ModelicaSourceSymbolKind.Model,
            ["package"] = ModelicaSourceSymbolKind.Package,
            ["block"] = ModelicaSourceSymbolKind.Block,
            ["connector"] = ModelicaSourceSymbolKind.Connector,
            ["record"] = ModelicaSourceSymbolKind.Record,
            ["function"] = ModelicaSourceSymbolKind.Function,
            ["class"] = ModelicaSourceSymbolKind.Class,
            ["type"] = ModelicaSourceSymbolKind.Type,
        };

    private static readonly HashSet<string> DeclarationQualifiers = new(StringComparer.Ordinal)
    {
        "constant",
        "discrete",
        "each",
        "final",
        "flow",
        "input",
        "inner",
        "outer",
        "output",
        "parameter",
        "redeclare",
        "replaceable",
        "stream",
    };

    private static readonly HashSet<string> IndexedDeclarationQualifiers = new(StringComparer.Ordinal)
    {
        "constant",
        "input",
        "output",
        "parameter",
    };

    public ModelicaSourceAnalysis Analyze(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var issues = new List<ModelicaSourceAnalysisIssue>();
        var tokens = Tokenize(source, issues);
        var symbols = new List<ModelicaSourceSymbol>();
        var classStack = new Stack<ModelicaSourceSymbol>();
        string? withinPackage = null;

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Text == "within")
            {
                withinPackage = ReadQualifiedName(tokens, index + 1, out _);
                continue;
            }

            if (token.Text == "end")
            {
                if (index + 1 < tokens.Count
                    && classStack.TryPeek(out var currentClass)
                    && string.Equals(
                        UnquoteIdentifier(tokens[index + 1].Text),
                        currentClass.Name,
                        StringComparison.Ordinal))
                {
                    classStack.Pop();
                }

                continue;
            }

            if (ClassKeywords.TryGetValue(token.Text, out var classKind)
                && !IsPrecededBy(tokens, index, "end")
                && index + 1 < tokens.Count
                && tokens[index + 1].Kind == TokenKind.Identifier)
            {
                var nameToken = tokens[index + 1];
                var symbol = new ModelicaSourceSymbol(
                    UnquoteIdentifier(nameToken.Text),
                    classKind,
                    token.Line,
                    token.Column,
                    classStack.Count);
                symbols.Add(symbol);
                if (classKind != ModelicaSourceSymbolKind.Type
                    && !IsShortClassDefinition(tokens, index + 2, nameToken.Line))
                {
                    classStack.Push(symbol);
                }

                index++;
                continue;
            }

            if (token.Text == "extends")
            {
                var baseClass = ReadQualifiedName(tokens, index + 1, out var endIndex);
                if (baseClass is not null)
                {
                    symbols.Add(new ModelicaSourceSymbol(
                        baseClass,
                        ModelicaSourceSymbolKind.Extends,
                        token.Line,
                        token.Column,
                        classStack.Count));
                    index = Math.Max(index, endIndex);
                }

                continue;
            }

            if (token.Text is "equation" or "algorithm")
            {
                var isInitial = IsPrecededBy(tokens, index, "initial");
                symbols.Add(new ModelicaSourceSymbol(
                    isInitial ? $"initial {token.Text}" : token.Text,
                    token.Text == "equation"
                        ? ModelicaSourceSymbolKind.EquationSection
                        : ModelicaSourceSymbolKind.AlgorithmSection,
                    token.Line,
                    token.Column,
                    classStack.Count));
                continue;
            }

            if (IndexedDeclarationQualifiers.Contains(token.Text)
                && !IsPrecededByQualifier(tokens, index))
            {
                if (TryReadDeclaration(tokens, index, classStack.Count, out var declaration, out var endIndex))
                {
                    symbols.Add(declaration);
                    index = Math.Max(index, endIndex);
                }
            }
        }

        foreach (var unclosedClass in classStack)
        {
            issues.Add(new ModelicaSourceAnalysisIssue(
                $"Class '{unclosedClass.Name}' has no matching end clause in the current source snapshot.",
                unclosedClass.Line,
                unclosedClass.Column));
        }

        return new ModelicaSourceAnalysis(withinPackage, symbols, issues);
    }

    private static bool TryReadDeclaration(
        IReadOnlyList<Token> tokens,
        int startIndex,
        int nestingLevel,
        out ModelicaSourceSymbol declaration,
        out int endIndex)
    {
        var qualifiers = new HashSet<string>(StringComparer.Ordinal);
        var index = startIndex;
        while (index < tokens.Count && DeclarationQualifiers.Contains(tokens[index].Text))
        {
            qualifiers.Add(tokens[index].Text);
            index++;
        }

        var typeName = ReadQualifiedName(tokens, index, out var typeEndIndex);
        index = typeEndIndex + 1;
        SkipArrayDimensions(tokens, ref index);
        if (typeName is null || index >= tokens.Count || tokens[index].Kind != TokenKind.Identifier)
        {
            declaration = default!;
            endIndex = startIndex;
            return false;
        }

        var nameToken = tokens[index];
        var kind = qualifiers.Contains("parameter")
            ? ModelicaSourceSymbolKind.Parameter
            : qualifiers.Contains("input")
                ? ModelicaSourceSymbolKind.Input
                : qualifiers.Contains("output")
                    ? ModelicaSourceSymbolKind.Output
                    : ModelicaSourceSymbolKind.Constant;
        declaration = new ModelicaSourceSymbol(
            UnquoteIdentifier(nameToken.Text),
            kind,
            nameToken.Line,
            nameToken.Column,
            nestingLevel,
            typeName);
        endIndex = index;
        return true;
    }

    private static void SkipArrayDimensions(IReadOnlyList<Token> tokens, ref int index)
    {
        if (index >= tokens.Count || tokens[index].Text != "[")
        {
            return;
        }

        var depth = 0;
        while (index < tokens.Count)
        {
            depth += tokens[index].Text switch
            {
                "[" => 1,
                "]" => -1,
                _ => 0,
            };
            index++;
            if (depth == 0)
            {
                return;
            }
        }
    }

    private static bool IsShortClassDefinition(
        IReadOnlyList<Token> tokens,
        int startIndex,
        int declarationLine)
    {
        for (var index = startIndex; index < tokens.Count && tokens[index].Line == declarationLine; index++)
        {
            if (tokens[index].Text == "=")
            {
                return true;
            }

            if (tokens[index].Text == ";")
            {
                return false;
            }
        }

        return false;
    }

    private static string? ReadQualifiedName(
        IReadOnlyList<Token> tokens,
        int startIndex,
        out int endIndex)
    {
        endIndex = startIndex - 1;
        if (startIndex >= tokens.Count || tokens[startIndex].Kind != TokenKind.Identifier)
        {
            return null;
        }

        var parts = new List<string> { UnquoteIdentifier(tokens[startIndex].Text) };
        endIndex = startIndex;
        while (endIndex + 2 < tokens.Count
               && tokens[endIndex + 1].Text == "."
               && tokens[endIndex + 2].Kind == TokenKind.Identifier)
        {
            parts.Add(UnquoteIdentifier(tokens[endIndex + 2].Text));
            endIndex += 2;
        }

        return string.Join('.', parts);
    }

    private static bool IsPrecededBy(IReadOnlyList<Token> tokens, int index, string value) =>
        index > 0 && tokens[index - 1].Text == value;

    private static bool IsPrecededByQualifier(IReadOnlyList<Token> tokens, int index) =>
        index > 0 && DeclarationQualifiers.Contains(tokens[index - 1].Text);

    private static string UnquoteIdentifier(string value) =>
        value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
            ? value[1..^1]
            : value;

    private static List<Token> Tokenize(string source, ICollection<ModelicaSourceAnalysisIssue> issues)
    {
        var tokens = new List<Token>();
        var offset = 0;
        var line = 1;
        var column = 1;
        while (offset < source.Length)
        {
            var current = source[offset];
            if (char.IsWhiteSpace(current))
            {
                Advance(source, ref offset, ref line, ref column);
                continue;
            }

            if (current == '/' && Peek(source, offset + 1) == '/')
            {
                while (offset < source.Length && source[offset] is not '\r' and not '\n')
                {
                    Advance(source, ref offset, ref line, ref column);
                }

                continue;
            }

            if (current == '/' && Peek(source, offset + 1) == '*')
            {
                var startLine = line;
                var startColumn = column;
                Advance(source, ref offset, ref line, ref column);
                Advance(source, ref offset, ref line, ref column);
                var terminated = false;
                while (offset < source.Length)
                {
                    if (source[offset] == '*' && Peek(source, offset + 1) == '/')
                    {
                        Advance(source, ref offset, ref line, ref column);
                        Advance(source, ref offset, ref line, ref column);
                        terminated = true;
                        break;
                    }

                    Advance(source, ref offset, ref line, ref column);
                }

                if (!terminated)
                {
                    issues.Add(new ModelicaSourceAnalysisIssue(
                        "Unterminated block comment in the current source snapshot.",
                        startLine,
                        startColumn));
                }

                continue;
            }

            if (current == '"')
            {
                SkipDelimited(source, '"', "string", issues, ref offset, ref line, ref column);
                continue;
            }

            if (current == '\'')
            {
                var tokenOffset = offset;
                var tokenLine = line;
                var tokenColumn = column;
                if (SkipDelimited(source, '\'', "quoted identifier", issues, ref offset, ref line, ref column))
                {
                    tokens.Add(new Token(
                        TokenKind.Identifier,
                        source[tokenOffset..offset],
                        tokenLine,
                        tokenColumn));
                }

                continue;
            }

            if (current == '_' || char.IsLetter(current))
            {
                var tokenOffset = offset;
                var tokenLine = line;
                var tokenColumn = column;
                do
                {
                    Advance(source, ref offset, ref line, ref column);
                }
                while (offset < source.Length && (source[offset] == '_' || char.IsLetterOrDigit(source[offset])));
                tokens.Add(new Token(
                    TokenKind.Identifier,
                    source[tokenOffset..offset],
                    tokenLine,
                    tokenColumn));
                continue;
            }

            if (char.IsDigit(current))
            {
                while (offset < source.Length
                       && (char.IsLetterOrDigit(source[offset]) || source[offset] is '.' or '+' or '-'))
                {
                    Advance(source, ref offset, ref line, ref column);
                }

                continue;
            }

            tokens.Add(new Token(TokenKind.Symbol, current.ToString(), line, column));
            Advance(source, ref offset, ref line, ref column);
        }

        return tokens;
    }

    private static bool SkipDelimited(
        string source,
        char delimiter,
        string description,
        ICollection<ModelicaSourceAnalysisIssue> issues,
        ref int offset,
        ref int line,
        ref int column)
    {
        var startLine = line;
        var startColumn = column;
        Advance(source, ref offset, ref line, ref column);
        while (offset < source.Length)
        {
            if (source[offset] == '\\' && offset + 1 < source.Length)
            {
                Advance(source, ref offset, ref line, ref column);
                Advance(source, ref offset, ref line, ref column);
                continue;
            }

            if (source[offset] == delimiter)
            {
                Advance(source, ref offset, ref line, ref column);
                return true;
            }

            Advance(source, ref offset, ref line, ref column);
        }

        issues.Add(new ModelicaSourceAnalysisIssue(
            $"Unterminated {description} in the current source snapshot.",
            startLine,
            startColumn));
        return false;
    }

    private static char Peek(string source, int offset) =>
        offset >= 0 && offset < source.Length ? source[offset] : '\0';

    private static void Advance(string source, ref int offset, ref int line, ref int column)
    {
        if (source[offset] == '\r')
        {
            offset++;
            if (offset < source.Length && source[offset] == '\n')
            {
                offset++;
            }

            line++;
            column = 1;
            return;
        }

        if (source[offset] == '\n')
        {
            offset++;
            line++;
            column = 1;
            return;
        }

        offset++;
        column++;
    }

    private enum TokenKind
    {
        Identifier,
        Symbol,
    }

    private sealed record Token(TokenKind Kind, string Text, int Line, int Column);
}
