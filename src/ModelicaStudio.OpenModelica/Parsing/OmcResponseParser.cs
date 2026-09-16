using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelicaStudio.Domain.Diagnostics;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.OpenModelica.Parsing;

public static class OmcResponseParser
{
    private static readonly Regex ResultFilePattern = new(
        "resultFile\\s*=\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ErrorMessagePattern = new(
        @"record\s+OpenModelica\.Scripting\.ErrorMessage(?<body>.*?)end\s+OpenModelica\.Scripting\.ErrorMessage;",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    public static bool ParseBoolean(string response)
    {
        var value = response.Trim();
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new FormatException($"Expected an OpenModelica Boolean response but received '{value}'."),
        };
    }

    public static string ParseString(string response)
    {
        var value = response.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
            }
            catch (JsonException)
            {
                return value[1..^1]
                    .Replace("\\n", "\n", StringComparison.Ordinal)
                    .Replace("\\r", "\r", StringComparison.Ordinal)
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
            }
        }

        return value;
    }

    public static IReadOnlyList<string> ParseStringList(string response)
    {
        var entries = ParseList(response);
        return entries.Select(ParseString).Where(static value => value.Length > 0).ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<double>> ParseNumericMatrix(string response)
    {
        return ParseList(response)
            .Select(row => (IReadOnlyList<double>)ParseList(row)
                .Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))
                .ToArray())
            .ToArray();
    }

    public static string? ParseSimulationResultFile(string response)
    {
        var match = ResultFilePattern.Match(response);
        return match.Success ? ParseString($"\"{match.Groups[1].Value}\"") : null;
    }

    public static IReadOnlyList<CompilerDiagnostic> ParseDiagnostics(string response)
    {
        var diagnostics = new List<CompilerDiagnostic>();
        foreach (Match match in ErrorMessagePattern.Matches(response))
        {
            var body = match.Groups["body"].Value;
            var level = ExtractEnum(body, "level");
            var message = ExtractString(body, "message") ?? "OpenModelica reported an unspecified diagnostic.";
            diagnostics.Add(new CompilerDiagnostic(
                ParseSeverity(level),
                message,
                ExtractString(body, "filename"),
                ExtractInteger(body, "lineStart"),
                ExtractInteger(body, "columnStart"),
                ExtractEnum(body, "kind"),
                ExtractInteger(body, "id"),
                ExtractInteger(body, "lineEnd"),
                ExtractInteger(body, "columnEnd")));
        }

        return diagnostics;
    }

    public static ModelicaClassKind ParseClassKind(string response)
    {
        return ParseString(response).Trim().ToLowerInvariant() switch
        {
            "package" => ModelicaClassKind.Package,
            "model" => ModelicaClassKind.Model,
            "block" => ModelicaClassKind.Block,
            "connector" or "expandable connector" => ModelicaClassKind.Connector,
            "record" or "operator record" => ModelicaClassKind.Record,
            "function" or "operator function" => ModelicaClassKind.Function,
            "type" => ModelicaClassKind.Type,
            "class" => ModelicaClassKind.Class,
            _ => ModelicaClassKind.Unknown,
        };
    }

    private static IReadOnlyList<string> ParseList(string response)
    {
        var value = response.Trim();
        if (value == "{}")
        {
            return [];
        }

        if (value.Length < 2 || value[0] != '{' || value[^1] != '}')
        {
            throw new FormatException($"Expected an OpenModelica list but received '{value}'.");
        }

        var items = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        var escaped = false;
        foreach (var character in value.AsSpan(1, value.Length - 2))
        {
            if (inString)
            {
                current.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                current.Append(character);
            }
            else if (character == '{' || character == '(')
            {
                depth++;
                current.Append(character);
            }
            else if (character == '}' || character == ')')
            {
                depth--;
                current.Append(character);
            }
            else if (character == ',' && depth == 0)
            {
                items.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (inString || depth != 0)
        {
            throw new FormatException("The OpenModelica list response is unbalanced.");
        }

        if (current.Length > 0)
        {
            items.Add(current.ToString().Trim());
        }

        return items;
    }

    private static CompilerDiagnosticSeverity ParseSeverity(string? value) => value?.ToLowerInvariant() switch
    {
        "error" => CompilerDiagnosticSeverity.Error,
        "warning" => CompilerDiagnosticSeverity.Warning,
        "notification" => CompilerDiagnosticSeverity.Notification,
        _ => CompilerDiagnosticSeverity.Internal,
    };

    private static string? ExtractString(string body, string field)
    {
        var match = Regex.Match(
            body,
            "\\b" + Regex.Escape(field) + "\\s*=\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        return match.Success ? ParseString($"\"{match.Groups["value"].Value}\"") : null;
    }

    private static string? ExtractEnum(string body, string field)
    {
        var match = Regex.Match(
            body,
            $@"\b{Regex.Escape(field)}\s*=\s*\.?[A-Za-z0-9_.]+\.(?<value>[A-Za-z]+)",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static int? ExtractInteger(string body, string field)
    {
        var match = Regex.Match(body, $@"\b{Regex.Escape(field)}\s*=\s*(?<value>-?\d+)", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["value"].Value, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
