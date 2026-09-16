using ModelicaStudio.Domain.Diagnostics;

namespace ModelicaStudio.UI.ViewModels;

public sealed record CompilerProblemViewModel(
    CompilerDiagnostic Diagnostic,
    bool IsNavigable)
{
    public string Severity => Diagnostic.Severity.ToString();
    public string SeverityColor => Diagnostic.Severity switch
    {
        CompilerDiagnosticSeverity.Error => "#9B2C2C",
        CompilerDiagnosticSeverity.Warning => "#9A6416",
        CompilerDiagnosticSeverity.Notification => "#276A9D",
        _ => "#745297",
    };

    public string LocationLabel
    {
        get
        {
            var file = string.IsNullOrWhiteSpace(Diagnostic.File)
                ? "active model"
                : Diagnostic.File == "<interactive>"
                    ? Diagnostic.File
                    : Path.GetFileName(Diagnostic.File);
            return Diagnostic.Line is null
                ? file
                : Diagnostic.Column is null
                    ? $"{file}:{Diagnostic.Line}"
                    : $"{file}:{Diagnostic.Line}:{Diagnostic.Column}";
        }
    }
}
