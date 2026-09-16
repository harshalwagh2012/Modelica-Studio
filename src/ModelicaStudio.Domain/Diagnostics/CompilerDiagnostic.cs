namespace ModelicaStudio.Domain.Diagnostics;

public enum CompilerDiagnosticSeverity
{
    Internal,
    Notification,
    Warning,
    Error,
}

public sealed record CompilerDiagnostic(
    CompilerDiagnosticSeverity Severity,
    string Message,
    string? File = null,
    int? Line = null,
    int? Column = null,
    string? Kind = null,
    int? Id = null,
    int? EndLine = null,
    int? EndColumn = null);
