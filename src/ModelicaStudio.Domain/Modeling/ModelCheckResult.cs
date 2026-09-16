using ModelicaStudio.Domain.Diagnostics;

namespace ModelicaStudio.Domain.Modeling;

public sealed record ModelCheckResult(
    bool Success,
    string Summary,
    IReadOnlyList<CompilerDiagnostic> Diagnostics);
