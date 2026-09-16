using ModelicaStudio.Domain.Diagnostics;

namespace ModelicaStudio.Domain.Modeling;

public enum ModelViewKind
{
    Diagram,
    Icon,
    ModelicaText,
    Documentation,
}

public enum CompilerSynchronizationState
{
    Unknown,
    Synchronized,
    SourceModified,
    InvalidSource,
}

public sealed class ModelDocument
{
    private string? _lastSavedSource;

    public ModelDocument(string className, string? sourcePath, string source, bool isNew = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentNullException.ThrowIfNull(source);
        ClassName = className;
        SourcePath = sourcePath;
        Source = source;
        IsDirty = isNew;
        _lastSavedSource = isNew ? null : source;
        SynchronizationState = isNew
            ? CompilerSynchronizationState.SourceModified
            : CompilerSynchronizationState.Unknown;
    }

    public string ClassName { get; private set; }
    public string? SourcePath { get; private set; }
    public string Source { get; private set; }
    public ModelViewKind SelectedView { get; set; } = ModelViewKind.ModelicaText;
    public bool IsDirty { get; private set; }
    public CompilerSynchronizationState SynchronizationState { get; private set; }
    public DateTimeOffset? LastCompilerSynchronization { get; private set; }
    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; private set; } = [];
    public bool DiagnosticsAreStale { get; private set; }

    public void UpdateSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(Source, source, StringComparison.Ordinal))
        {
            return;
        }

        Source = source;
        IsDirty = _lastSavedSource is null
            || !string.Equals(Source, _lastSavedSource, StringComparison.Ordinal);
        SynchronizationState = CompilerSynchronizationState.SourceModified;
        DiagnosticsAreStale = Diagnostics.Count > 0;
    }

    public void MarkSaved(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SourcePath = Path.GetFullPath(path);
        _lastSavedSource = Source;
        IsDirty = false;
    }

    public void MarkSynchronized(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ClassName = className;
        SynchronizationState = CompilerSynchronizationState.Synchronized;
        LastCompilerSynchronization = DateTimeOffset.UtcNow;
    }

    public void MarkInvalidSource() => SynchronizationState = CompilerSynchronizationState.InvalidSource;

    public void AcceptCompilerSource(string source, string className)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        Source = source;
        ClassName = className;
        _lastSavedSource = source;
        IsDirty = false;
        SynchronizationState = CompilerSynchronizationState.Synchronized;
        LastCompilerSynchronization = DateTimeOffset.UtcNow;
        DiagnosticsAreStale = Diagnostics.Count > 0;
    }

    public void SetDiagnostics(IEnumerable<CompilerDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics.ToArray();
        DiagnosticsAreStale = false;
    }

    public void ClearDiagnostics()
    {
        Diagnostics = [];
        DiagnosticsAreStale = false;
    }
}

public enum ModelicaClassType
{
    Model,
    Package,
    Block,
    Connector,
    Record,
    Function,
    Class,
}

public sealed record NewModelicaClass(
    string Name,
    ModelicaClassType ClassType = ModelicaClassType.Model,
    string? Description = null,
    string? WithinPackage = null);
