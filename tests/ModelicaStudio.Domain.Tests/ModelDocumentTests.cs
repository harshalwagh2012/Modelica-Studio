using ModelicaStudio.Domain.Diagnostics;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelDocumentTests
{
    [Fact]
    public void UpdateSource_ReturningToSavedTextClearsDirtyState()
    {
        const string savedSource = "model Demo\nend Demo;";
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), savedSource);

        document.UpdateSource(savedSource + "\n");
        Assert.True(document.IsDirty);

        document.UpdateSource(savedSource);

        Assert.False(document.IsDirty);
        Assert.Equal(CompilerSynchronizationState.SourceModified, document.SynchronizationState);
    }

    [Fact]
    public void MarkSaved_UpdatesDirtyBaseline()
    {
        var document = new ModelDocument("Demo", null, "model Demo\nend Demo;", isNew: true);
        document.MarkSaved(Path.GetFullPath("Demo.mo"));

        document.UpdateSource(document.Source + "\n");
        Assert.True(document.IsDirty);

        document.UpdateSource("model Demo\nend Demo;");
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void UpdateSource_PreservesCompilerDiagnosticsButMarksTheirLocationsStale()
    {
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;");
        var diagnostic = new CompilerDiagnostic(
            CompilerDiagnosticSeverity.Error,
            "Missing semicolon",
            document.SourcePath,
            2,
            9);
        document.SetDiagnostics([diagnostic]);

        document.UpdateSource("model Demo\nend Demo\n");

        Assert.Equal([diagnostic], document.Diagnostics);
        Assert.True(document.DiagnosticsAreStale);
    }

    [Fact]
    public void SetDiagnostics_ReplacesPriorResultsAndMakesLocationsCurrent()
    {
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;");
        document.SetDiagnostics([
            new CompilerDiagnostic(CompilerDiagnosticSeverity.Warning, "First"),
        ]);
        document.UpdateSource(document.Source + "\n");
        var replacement = new CompilerDiagnostic(CompilerDiagnosticSeverity.Error, "Replacement");

        document.SetDiagnostics([replacement]);

        Assert.Equal([replacement], document.Diagnostics);
        Assert.False(document.DiagnosticsAreStale);
    }

    [Fact]
    public void AcceptCompilerSource_UpdatesSavedBaselineAndMarksDiagnosticsStale()
    {
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo end Demo;");
        document.SetDiagnostics([
            new CompilerDiagnostic(CompilerDiagnosticSeverity.Warning, "Earlier warning", Line: 1, Column: 1),
        ]);
        document.UpdateSource("model Demo\nend Demo;");

        document.AcceptCompilerSource("model Renamed\nend Renamed;\n", "Renamed");

        Assert.Equal("Renamed", document.ClassName);
        Assert.Equal("model Renamed\nend Renamed;\n", document.Source);
        Assert.False(document.IsDirty);
        Assert.Equal(CompilerSynchronizationState.Synchronized, document.SynchronizationState);
        Assert.True(document.DiagnosticsAreStale);
        document.UpdateSource("model Renamed\nend Renamed;\n");
        Assert.False(document.IsDirty);
    }
}
