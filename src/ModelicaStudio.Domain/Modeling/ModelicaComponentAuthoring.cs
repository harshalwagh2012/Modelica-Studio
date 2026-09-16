using System.Text.RegularExpressions;

namespace ModelicaStudio.Domain.Modeling;

public static class ModelicaComponentType
{
    public static bool IsInstantiable(ModelicaClassInfo componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        return !componentType.IsPartial
            && componentType.Kind is ModelicaClassKind.Model
                or ModelicaClassKind.Block
                or ModelicaClassKind.Connector
                or ModelicaClassKind.Record
                or ModelicaClassKind.Class;
    }
}

public static partial class ModelicaComponentNaming
{
    public static string CreateUniqueName(
        string typeName,
        string? recommendedName,
        IEnumerable<string> existingNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(existingNames);

        var usedNames = existingNames.ToHashSet(StringComparer.Ordinal);
        var recommended = recommendedName?.Trim();
        if (!string.IsNullOrEmpty(recommended) && SimpleIdentifierPattern().IsMatch(recommended))
        {
            return AddNumericSuffixWhenNeeded(recommended, usedNames, appendInitialSuffix: false);
        }

        var typeLeaf = typeName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrEmpty(typeLeaf) || !SimpleIdentifierPattern().IsMatch(typeLeaf))
        {
            throw new ArgumentException("The component type must end in a conventional Modelica identifier.", nameof(typeName));
        }

        var baseName = char.ToLowerInvariant(typeLeaf[0]) + typeLeaf[1..];
        return AddNumericSuffixWhenNeeded(baseName, usedNames, appendInitialSuffix: true);
    }

    private static string AddNumericSuffixWhenNeeded(
        string candidate,
        IReadOnlySet<string> usedNames,
        bool appendInitialSuffix)
    {
        if (!appendInitialSuffix && !usedNames.Contains(candidate))
        {
            return candidate;
        }

        var match = TrailingNumberPattern().Match(candidate);
        var baseName = match.Success ? candidate[..match.Index] : candidate;
        var nextNumber = match.Success && int.TryParse(match.Value, out var parsed)
            ? parsed + 1
            : 1;
        while (usedNames.Contains(baseName + nextNumber))
        {
            nextNumber++;
        }

        return baseName + nextNumber;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleIdentifierPattern();

    [GeneratedRegex("[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingNumberPattern();
}

public static class ModelicaConnectionEndpoint
{
    public static bool ReferencesComponent(string endpoint, string componentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        var endpointName = endpoint.StartsWith('\'')
            ? $"'{componentName.Replace("'", "\\'", StringComparison.Ordinal)}'"
            : componentName;
        if (!endpoint.StartsWith(endpointName, StringComparison.Ordinal))
        {
            return false;
        }

        return endpoint.Length == endpointName.Length
            || endpoint[endpointName.Length] is '.' or '[';
    }
}
