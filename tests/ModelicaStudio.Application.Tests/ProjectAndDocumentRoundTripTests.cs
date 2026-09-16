using ModelicaStudio.Application.Projects;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.Domain.Projects;
using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.Infrastructure.Files;
using ModelicaStudio.Infrastructure.Modeling;
using ModelicaStudio.Infrastructure.Projects;

namespace ModelicaStudio.Application.Tests;

public sealed class ProjectAndDocumentRoundTripTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "ModelicaStudio.Application.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Project_CreateSaveLoad_RoundTripsMetadataAndStructure()
    {
        var service = new ProjectService(new AtomicFileWriter());
        var projectPath = Path.Combine(_temporaryDirectory, "DriveTrain", "DriveTrain.modelicaproj");
        var created = await service.CreateAsync(projectPath, "Drive Train");
        var updated = created with
        {
            Project = created.Project with
            {
                ModelFiles = ["Models/Plant.mo"],
                AdditionalLibraryPaths = ["/opt/modelica/libraries"],
                RecentlyOpenedModels = ["DriveTrain.Plant"],
                SimulationConfigurations = new Dictionary<string, SimulationConfiguration>
                {
                    ["DriveTrain.Plant"] = new() { StopTime = 12 },
                },
                UiState = new ProjectUiState { ActiveModel = "DriveTrain.Plant", InspectorPanelWidth = 344 },
            },
        };

        await service.SaveAsync(updated);
        var loaded = await service.LoadAsync(projectPath);

        Assert.Equal("Drive Train", loaded.Project.Name);
        Assert.Equal("DriveTrain", loaded.Project.RootPackage);
        Assert.Equal(["Models/Plant.mo"], loaded.Project.ModelFiles);
        Assert.Equal(12, loaded.Project.SimulationConfigurations["DriveTrain.Plant"].StopTime);
        Assert.Equal(344, loaded.Project.UiState.InspectorPanelWidth);
        Assert.True(Directory.Exists(Path.Combine(loaded.ProjectDirectory, "Models")));
        Assert.True(Directory.Exists(Path.Combine(loaded.ProjectDirectory, "Libraries")));
        Assert.True(Directory.Exists(Path.Combine(loaded.ProjectDirectory, "Results")));
        Assert.True(Directory.Exists(Path.Combine(loaded.ProjectDirectory, ".modelicastudio")));
    }

    [Fact]
    public async Task Project_Save_RejectsModelPathTraversal()
    {
        var service = new ProjectService(new AtomicFileWriter());
        var document = new ModelicaProjectDocument(
            Path.Combine(_temporaryDirectory, "Unsafe.modelicaproj"),
            new ModelicaProject { Name = "Unsafe", ModelFiles = ["../outside.mo"] });

        await Assert.ThrowsAsync<InvalidModelicaProjectException>(() => service.SaveAsync(document));
    }

    [Fact]
    public async Task ModelDocument_CreateEditSaveOpen_PreservesSource()
    {
        var service = new ModelicaDocumentService(new AtomicFileWriter());
        var path = Path.Combine(_temporaryDirectory, "Models", "Controller.mo");
        var document = service.Create(new NewModelicaClass("Controller"), path);
        document.UpdateSource(document.Source.Replace("\n\n", "\n  parameter Real k = 2;\n\n", StringComparison.Ordinal));

        await service.SaveAsync(document);
        var reopened = await service.OpenAsync(path);

        Assert.False(document.IsDirty);
        Assert.Equal("Controller", reopened.ClassName);
        Assert.Contains("parameter Real k = 2;", reopened.Source, StringComparison.Ordinal);
        Assert.Equal(CompilerSynchronizationState.Unknown, reopened.SynchronizationState);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task ModelDocument_OpenInvalidSource_PreservesTextAndMarksInvalid()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "Broken.mo");
        const string source = "this is not valid Modelica";
        await File.WriteAllTextAsync(path, source);
        var service = new ModelicaDocumentService(new AtomicFileWriter());

        var document = await service.OpenAsync(path);

        Assert.Equal(source, document.Source);
        Assert.Equal(CompilerSynchronizationState.InvalidSource, document.SynchronizationState);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
