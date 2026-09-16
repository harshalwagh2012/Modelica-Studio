using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Simulation;

namespace ModelicaStudio.OpenModelica.Commands;

public static class OmcCommandBuilder
{
    private static readonly Regex ModelicaIdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ModelicaComponentReferencePattern = new(
        @"^(?:[A-Za-z_][A-Za-z0-9_]*|'(?:[^'\\]|\\.)+')(?:\[[1-9][0-9]*(?:,[1-9][0-9]*)*\])?(?:\.(?:[A-Za-z_][A-Za-z0-9_]*|'(?:[^'\\]|\\.)+')(?:\[[1-9][0-9]*(?:,[1-9][0-9]*)*\])?)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string GetVersion() => "getVersion()";

    public static string LoadModel(string library) => $"loadModel({Identifier(library)})";

    public static string LoadFile(string path) => $"loadFile({String(path)})";

    public static string CheckModel(string modelName) => $"checkModel({Identifier(modelName)})";

    public static string GetMessages() => "getMessagesStringInternal()";

    public static string GetClassNames(string parentClass) =>
        $"getClassNames({Identifier(parentClass)}, recursive=false, qualified=true, sort=true)";

    public static string GetClassRestriction(string className) =>
        $"getClassRestriction({Identifier(className)})";

    public static string IsPartial(string className) => $"isPartial({Identifier(className)})";

    public static string GetModelInstanceAnnotation(string className) =>
        $"getModelInstanceAnnotation({Identifier(className)}, {{{String("Icon")},{String("Diagram")}}}, false)";

    public static string GetModelInstance(string className) =>
        $"getModelInstance({Identifier(className)}, prettyPrint=false)";

    public static string GetDefaultComponentName(string typeName) =>
        $"getDefaultComponentName({Identifier(typeName)})";

    public static string AddComponent(
        string className,
        string componentName,
        string typeName,
        ModelicaPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return $"addComponent({SimpleIdentifier(componentName)}, {Identifier(typeName)}, {Identifier(className)}, annotate={Placement(placement)})";
    }

    public static string DeleteComponent(string className, string componentName) =>
        $"deleteComponent({SimpleIdentifier(componentName)}, {Identifier(className)})";

    public static string AddConnection(
        string className,
        string from,
        string to,
        ModelicaGraphicPrimitive line) =>
        $"addConnection({ComponentReference(from)}, {ComponentReference(to)}, {Identifier(className)}, annotate={ConnectionLine(line)})";

    public static string DeleteConnection(string className, string from, string to) =>
        $"deleteConnection({ComponentReference(from)}, {ComponentReference(to)}, {Identifier(className)})";

    public static string UpdateConnectionAnnotation(
        string className,
        string from,
        string to,
        ModelicaGraphicPrimitive line) =>
        $"updateConnectionAnnotation({Identifier(className)}, {String(ComponentReference(from))}, {String(ComponentReference(to))}, {String($"annotate={ConnectionLine(line)}")})";

    public static string SetComponentPlacement(
        string className,
        string componentName,
        ModelicaPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var elementPath = Identifier($"{Identifier(className)}.{SimpleIdentifier(componentName)}");
        return $"setElementAnnotation({elementPath}, $Code(({Placement(placement)})))";
    }

    public static string SaveClass(string className) => $"save({Identifier(className)})";

    public static string LoadString(string source, string sourcePath) =>
        $"loadString({String(source)}, {String(sourcePath)}, \"UTF-8\", merge=false, uses=true, notify=false)";

    public static string LoadClassContentString(
        string content,
        string className,
        int offsetX,
        int offsetY) =>
        $"loadClassContentString({String(content)}, {Identifier(className)}, {offsetX.ToString(CultureInfo.InvariantCulture)}, {offsetY.ToString(CultureInfo.InvariantCulture)})";

    public static string Simulate(string modelName, SimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        var command = new StringBuilder("simulate(");
        command.Append(Identifier(modelName));
        command.Append(", startTime=").Append(Number(configuration.StartTime));
        command.Append(", stopTime=").Append(Number(configuration.StopTime));
        command.Append(", numberOfIntervals=").Append(configuration.NumberOfIntervals.ToString(CultureInfo.InvariantCulture));
        command.Append(", tolerance=").Append(Number(configuration.Tolerance));

        if (!string.IsNullOrWhiteSpace(configuration.Method))
        {
            command.Append(", method=").Append(String(configuration.Method));
        }

        command.Append(", outputFormat=").Append(String(configuration.OutputFormat));
        if (!string.IsNullOrWhiteSpace(configuration.VariableFilter))
        {
            command.Append(", variableFilter=").Append(String(configuration.VariableFilter));
        }

        if (!string.IsNullOrWhiteSpace(configuration.ResultFileName))
        {
            command.Append(", fileNamePrefix=").Append(String(configuration.ResultFileName));
        }

        return command.Append(')').ToString();
    }

    public static string ReadSimulationResultVariables(string resultFile) =>
        $"readSimulationResultVars({String(resultFile)})";

    public static string ReadSimulationSeries(string resultFile, string variableName) =>
        $"readSimulationResult({String(resultFile)}, {{{String("time")},{String(variableName)}}})";

    public static string String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";
    }

    public static string Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ModelicaIdentifierPattern.IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe qualified Modelica identifier.", nameof(value));
        }

        return value;
    }

    public static string ComponentReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ModelicaComponentReferencePattern.IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe Modelica component reference.", nameof(value));
        }

        return value;
    }

    private static string SimpleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('.', StringComparison.Ordinal)
            || !ModelicaIdentifierPattern.IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe Modelica component identifier.", nameof(value));
        }

        return value;
    }

    private static string Placement(ModelicaPlacement placement)
    {
        var fields = new List<string>
        {
            $"visible={Boolean(placement.Visible)}",
            $"transformation={Transformation(placement.Transformation)}",
            $"iconVisible={Boolean(placement.IconVisible)}",
        };
        if (placement.IconTransformation is not null)
        {
            fields.Add($"iconTransformation={Transformation(placement.IconTransformation)}");
        }

        return $"Placement({string.Join(", ", fields)})";
    }

    private static string ConnectionLine(ModelicaGraphicPrimitive line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Kind != ModelicaGraphicKind.Line)
        {
            throw new ArgumentException("A connection annotation must be a Line primitive.", nameof(line));
        }

        if (line.Points.Count < 2)
        {
            throw new ArgumentException("A connection annotation requires at least two points.", nameof(line));
        }

        var color = line.Style.LineColor;
        if (color.Red is < 0 or > 255 || color.Green is < 0 or > 255 || color.Blue is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Connection colors must use RGB values from 0 through 255.");
        }

        if (!double.IsFinite(line.Style.LineThickness) || line.Style.LineThickness < 0
            || !double.IsFinite(line.ArrowSize) || line.ArrowSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Connection line widths and arrow sizes must be finite and non-negative.");
        }

        var arrows = line.Arrows.Count >= 2
            ? line.Arrows.Take(2).ToArray()
            : [ModelicaArrow.None, ModelicaArrow.None];
        return "Line("
            + $"visible={Boolean(line.Visible.StaticValue)}, "
            + $"origin={Point(line.Origin.StaticValue)}, "
            + $"rotation={Number(line.Rotation.StaticValue)}, "
            + $"points={{{string.Join(',', line.Points.Select(Point))}}}, "
            + $"color={{{color.Red.ToString(CultureInfo.InvariantCulture)},{color.Green.ToString(CultureInfo.InvariantCulture)},{color.Blue.ToString(CultureInfo.InvariantCulture)}}}, "
            + $"pattern=LinePattern.{EnumMember(line.Style.LinePattern)}, "
            + $"thickness={Number(line.Style.LineThickness)}, "
            + $"arrow={{Arrow.{EnumMember(arrows[0])},Arrow.{EnumMember(arrows[1])}}}, "
            + $"arrowSize={Number(line.ArrowSize)}, "
            + $"smooth=Smooth.{EnumMember(line.Smooth)})";
    }

    private static string EnumMember<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        if (value.ToString() == "Unknown")
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Unknown Modelica enumeration values cannot be serialized.");
        }

        return value.ToString();
    }

    private static string Transformation(ModelicaTransformation transformation)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        return "transformation("
            + $"origin={Point(transformation.Origin)}, "
            + $"extent={Extent(transformation.Extent)}, "
            + $"rotation={Number(transformation.Rotation)})";
    }

    private static string Extent(ModelicaExtent extent) =>
        $"{{{Point(extent.First)},{Point(extent.Second)}}}";

    private static string Point(ModelicaPoint point) =>
        $"{{{Number(point.X)},{Number(point.Y)}}}";

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "OMC numeric arguments must be finite.");
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
