using System.Text;
using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Modeling;

public static class ModelicaClassContentCopy
{
    private static readonly HashSet<string> ClassKeywords = new(StringComparer.Ordinal)
    {
        "model",
        "package",
        "block",
        "connector",
        "record",
        "function",
        "class",
        "type",
    };

    public static string Create(
        string source,
        IReadOnlyList<ModelicaComponentInstance> components,
        IReadOnlyList<ModelicaDiagramConnection> connections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);
        if (components.Count == 0)
        {
            throw new ArgumentException("At least one component must be copied.", nameof(components));
        }

        var selectedNames = components.Select(static component => component.Name)
            .ToHashSet(StringComparer.Ordinal);
        var tokens = Tokenize(source);
        var statements = SplitStatements(tokens);
        var content = new StringBuilder();
        foreach (var component in components)
        {
            if (component.IsInherited)
            {
                throw new InvalidOperationException(
                    $"Inherited component '{component.Name}' must be duplicated in its declaring class.");
            }

            var declaration = ExtractComponentDeclaration(source, statements, component.Name);
            content.AppendLine(component.Prefixes.IsPublic ? "public" : "protected");
            content.AppendLine(declaration);
        }

        var internalConnections = connections.Where(connection =>
                !connection.IsInherited
                && selectedNames.Any(name => ModelicaConnectionEndpoint.ReferencesComponent(connection.Left, name))
                && selectedNames.Any(name => ModelicaConnectionEndpoint.ReferencesComponent(connection.Right, name)))
            .ToArray();
        if (internalConnections.Length > 0)
        {
            content.AppendLine("equation");
            foreach (var connection in internalConnections)
            {
                content.AppendLine(ExtractConnection(source, statements, connection));
            }
        }

        return content.ToString().TrimEnd();
    }

    private static string ExtractComponentDeclaration(
        string source,
        IReadOnlyList<IReadOnlyList<Token>> statements,
        string componentName)
    {
        var matches = new List<(IReadOnlyList<Token> Statement, int StartIndex)>();
        foreach (var statement in statements)
        {
            for (var index = 0; index < statement.Count; index++)
            {
                if (statement[index].Kind != TokenKind.Identifier
                    || !string.Equals(Unquote(statement[index].Text), componentName, StringComparison.Ordinal)
                    || !LooksLikeComponentName(statement, index, out var startIndex))
                {
                    continue;
                }

                matches.Add((statement, startIndex));
            }
        }

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(matches.Count == 0
                ? $"The exact declaration for '{componentName}' could not be located in the active source."
                : $"The declaration for '{componentName}' is ambiguous in the active source.");
        }

        var match = matches[0];
        var depths = CalculateDepths(match.Statement);
        if (match.Statement.Where((token, index) =>
                index >= match.StartIndex
                && token.Text == ","
                && depths[index] == 0).Any())
        {
            throw new InvalidOperationException(
                $"'{componentName}' shares a declaration with another component. Split the declaration before duplicating it.");
        }

        return source[match.Statement[match.StartIndex].Start..match.Statement[^1].End].Trim();
    }

    private static bool LooksLikeComponentName(
        IReadOnlyList<Token> statement,
        int nameIndex,
        out int declarationStart)
    {
        declarationStart = 0;
        var depths = CalculateDepths(statement);
        if (depths[nameIndex] != 0
            || nameIndex == 0
            || nameIndex + 1 >= statement.Count
            || statement[nameIndex + 1].Text == ".")
        {
            return false;
        }

        for (var index = 0; index < nameIndex; index++)
        {
            if (depths[index] != 0)
            {
                continue;
            }

            if (statement[index].Text is "equation" or "algorithm" or "connect" or "=")
            {
                return false;
            }

            if (statement[index].Text is "public" or "protected")
            {
                declarationStart = index + 1;
            }

            if (ClassKeywords.Contains(statement[index].Text)
                && index + 1 < nameIndex
                && statement[index + 1].Kind == TokenKind.Identifier)
            {
                declarationStart = index + 2;
                while (declarationStart < nameIndex
                       && statement[declarationStart].Kind == TokenKind.String)
                {
                    declarationStart++;
                }
            }
        }

        return statement.Skip(declarationStart).Take(nameIndex - declarationStart)
            .Any(static token => token.Kind == TokenKind.Identifier);
    }

    private static string ExtractConnection(
        string source,
        IReadOnlyList<IReadOnlyList<Token>> statements,
        ModelicaDiagramConnection connection)
    {
        foreach (var statement in statements)
        {
            var connectIndex = statement.TakeWhile(static token => token.Text is "equation" or "initial")
                .Count();
            if (connectIndex >= statement.Count || statement[connectIndex].Text != "connect")
            {
                continue;
            }

            if (!TryReadConnectionEndpoints(statement, connectIndex, out var left, out var right)
                || !(string.Equals(left, NormalizeEndpoint(connection.Left), StringComparison.Ordinal)
                    && string.Equals(right, NormalizeEndpoint(connection.Right), StringComparison.Ordinal)
                    || string.Equals(left, NormalizeEndpoint(connection.Right), StringComparison.Ordinal)
                    && string.Equals(right, NormalizeEndpoint(connection.Left), StringComparison.Ordinal)))
            {
                continue;
            }

            return source[statement[connectIndex].Start..statement[^1].End].Trim();
        }

        throw new InvalidOperationException(
            $"The exact connect({connection.Left}, {connection.Right}) equation could not be located in the active source.");
    }

    private static bool TryReadConnectionEndpoints(
        IReadOnlyList<Token> statement,
        int connectIndex,
        out string left,
        out string right)
    {
        left = string.Empty;
        right = string.Empty;
        if (connectIndex + 1 >= statement.Count || statement[connectIndex + 1].Text != "(")
        {
            return false;
        }

        var leftBuilder = new StringBuilder();
        var rightBuilder = new StringBuilder();
        var current = leftBuilder;
        var depth = 0;
        for (var index = connectIndex + 2; index < statement.Count; index++)
        {
            var token = statement[index];
            if (token.Text is "(" or "[" or "{")
            {
                depth++;
            }
            else if (token.Text is ")" or "]" or "}")
            {
                if (token.Text == ")" && depth == 0)
                {
                    left = leftBuilder.ToString();
                    right = rightBuilder.ToString();
                    return current == rightBuilder && left.Length > 0 && right.Length > 0;
                }

                depth--;
            }

            if (token.Text == "," && depth == 0 && current == leftBuilder)
            {
                current = rightBuilder;
                continue;
            }

            current.Append(token.Text);
        }

        return false;
    }

    private static IReadOnlyList<int> CalculateDepths(IReadOnlyList<Token> statement)
    {
        var result = new int[statement.Count];
        var depth = 0;
        for (var index = 0; index < statement.Count; index++)
        {
            result[index] = depth;
            depth += statement[index].Text switch
            {
                "(" or "[" or "{" => 1,
                ")" or "]" or "}" => -1,
                _ => 0,
            };
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<Token>> SplitStatements(IReadOnlyList<Token> tokens)
    {
        var result = new List<IReadOnlyList<Token>>();
        var start = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Text != ";")
            {
                continue;
            }

            result.Add(tokens.Skip(start).Take(index - start + 1).ToArray());
            start = index + 1;
        }

        return result;
    }

    private static IReadOnlyList<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        for (var offset = 0; offset < source.Length;)
        {
            if (char.IsWhiteSpace(source[offset]))
            {
                offset++;
                continue;
            }

            if (source[offset] == '/' && offset + 1 < source.Length && source[offset + 1] == '/')
            {
                offset += 2;
                while (offset < source.Length && source[offset] is not '\r' and not '\n')
                {
                    offset++;
                }

                continue;
            }

            if (source[offset] == '/' && offset + 1 < source.Length && source[offset + 1] == '*')
            {
                var end = source.IndexOf("*/", offset + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new InvalidOperationException("The active source contains an unterminated block comment.");
                }

                offset = end + 2;
                continue;
            }

            if (source[offset] is '"' or '\'')
            {
                var delimiter = source[offset];
                var start = offset++;
                while (offset < source.Length)
                {
                    if (source[offset] == '\\' && offset + 1 < source.Length)
                    {
                        offset += 2;
                        continue;
                    }

                    if (source[offset++] == delimiter)
                    {
                        break;
                    }
                }

                if (source[offset - 1] != delimiter)
                {
                    throw new InvalidOperationException("The active source contains an unterminated string or quoted identifier.");
                }

                tokens.Add(new Token(
                    source[start..offset],
                    start,
                    offset,
                    delimiter == '\'' ? TokenKind.Identifier : TokenKind.String));
                continue;
            }

            if (source[offset] == '_' || char.IsLetter(source[offset]))
            {
                var start = offset++;
                while (offset < source.Length
                       && (source[offset] == '_' || char.IsLetterOrDigit(source[offset])))
                {
                    offset++;
                }

                tokens.Add(new Token(source[start..offset], start, offset, TokenKind.Identifier));
                continue;
            }

            tokens.Add(new Token(source[offset].ToString(), offset, offset + 1, TokenKind.Symbol));
            offset++;
        }

        return tokens;
    }

    private static string NormalizeEndpoint(string value)
    {
        var result = new StringBuilder(value.Length);
        var quoted = false;
        var escaped = false;
        foreach (var character in value)
        {
            if (escaped)
            {
                result.Append(character);
                escaped = false;
                continue;
            }

            if (quoted && character == '\\')
            {
                result.Append(character);
                escaped = true;
                continue;
            }

            if (character == '\'')
            {
                quoted = !quoted;
                result.Append(character);
                continue;
            }

            if (quoted || !char.IsWhiteSpace(character))
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    private static string Unquote(string identifier) =>
        identifier.Length >= 2 && identifier[0] == '\'' && identifier[^1] == '\''
            ? identifier[1..^1]
            : identifier;

    private enum TokenKind
    {
        Identifier,
        String,
        Symbol,
    }

    private sealed record Token(string Text, int Start, int End, TokenKind Kind);
}
