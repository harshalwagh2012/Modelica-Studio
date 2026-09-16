using System.Text;
using System.Text.RegularExpressions;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Application.Modeling;

public static class ModelicaSourceGenerator
{
    private static readonly Regex IdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex QualifiedIdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TopLevelClassPattern = new(
        @"(?m)^\s*(?:(?:encapsulated|partial)\s+)*(?:model|package|block|connector|record|function|class)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Generate(NewModelicaClass definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateSimpleIdentifier(definition.Name, nameof(definition.Name));
        if (!string.IsNullOrWhiteSpace(definition.WithinPackage)
            && !QualifiedIdentifierPattern.IsMatch(definition.WithinPackage))
        {
            throw new ArgumentException("The within package must be a qualified Modelica identifier.", nameof(definition));
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(definition.WithinPackage))
        {
            builder.Append("within ").Append(definition.WithinPackage).AppendLine(";");
        }

        builder.Append(GetKeyword(definition.ClassType)).Append(' ').Append(definition.Name);
        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            builder.Append(" \"").Append(EscapeString(definition.Description)).Append('"');
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.Append("end ").Append(definition.Name).AppendLine(";");
        return builder.ToString();
    }

    public static string? TryGetTopLevelClassName(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var match = TopLevelClassPattern.Match(source);
        return match.Success ? match.Groups["name"].Value : null;
    }

    public static void ValidateSimpleIdentifier(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern.IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a valid Modelica identifier.", parameterName ?? nameof(value));
        }
    }

    private static string GetKeyword(ModelicaClassType classType) => classType switch
    {
        ModelicaClassType.Model => "model",
        ModelicaClassType.Package => "package",
        ModelicaClassType.Block => "block",
        ModelicaClassType.Connector => "connector",
        ModelicaClassType.Record => "record",
        ModelicaClassType.Function => "function",
        ModelicaClassType.Class => "class",
        _ => throw new ArgumentOutOfRangeException(nameof(classType), classType, "Unsupported Modelica class type."),
    };

    private static string EscapeString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
