using ModelicaStudio.Application.Engine;
using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Application.Projects;
using ModelicaStudio.Application.Settings;
using ModelicaStudio.Domain.Diagnostics;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.Domain.Projects;
using ModelicaStudio.Domain.Settings;
using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.UI.ViewModels;

namespace ModelicaStudio.UI.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task OpenDocument_LoadsGraphicalAnnotationAndEnablesViewSwitching()
    {
        var annotation = CreateAnnotation();
        var engine = new FakeOpenModelicaService(annotation);
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());

        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(document.SourcePath!);

        Assert.Same(annotation, viewModel.ActiveGraphicalAnnotation);
        Assert.NotNull(viewModel.ActiveModelInstance);
        Assert.True(viewModel.CanShowIcon);
        Assert.True(viewModel.CanShowDiagram);
        Assert.True(viewModel.IsTextView);

        viewModel.ShowIconCommand.Execute(null);

        Assert.True(viewModel.IsGraphicalView);
        Assert.True(viewModel.IsIconView);
        Assert.Equal("Icon · compiler-confirmed connector placement", viewModel.ActiveViewLabel);

        viewModel.ShowTextCommand.Execute(null);

        Assert.True(viewModel.IsTextView);
        Assert.Equal(["Demo"], engine.InstanceRequests);
    }

    [Fact]
    public async Task OpenDocument_WithoutDiagramKeepsDiagramCommandDisabled()
    {
        var annotation = CreateAnnotation() with { Diagram = null };
        var engine = new FakeOpenModelicaService(annotation);
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());

        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(document.SourcePath!);

        Assert.False(viewModel.CanShowDiagram);
        Assert.False(viewModel.ShowDiagramCommand.CanExecute(null));
        viewModel.ShowDiagramCommand.Execute(null);
        Assert.True(viewModel.IsTextView);
    }

    [Fact]
    public async Task ConnectionTool_OnlyActivatesInEditableDiagramView()
    {
        var annotation = CreateAnnotation();
        var engine = new FakeOpenModelicaService(annotation);
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);

        viewModel.ShowDiagramCommand.Execute(null);
        Assert.True(viewModel.CanUseConnectionTool);
        viewModel.ToggleConnectionTool();
        Assert.True(viewModel.IsConnectionToolActive);
        Assert.Equal("Connect ✓", viewModel.ConnectionToolLabel);

        viewModel.ShowIconCommand.Execute(null);

        Assert.False(viewModel.IsConnectionToolActive);
        Assert.False(viewModel.CanUseConnectionTool);
    }

    [Fact]
    public async Task OpenDocument_PlacedComponentEnablesDiagramWithoutClassDiagramPrimitives()
    {
        var annotation = CreateAnnotation() with { Diagram = null };
        var icon = new ModelicaGraphicalAnnotationSnapshot(
            "Demo.Part",
            "model",
            new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []),
            null,
            [],
            [],
            "{}");
        var component = new ModelicaComponentInstance(
            "part",
            "Demo.Part",
            "model",
            new ModelicaComponentPrefixes(),
            new ModelicaPlacement(true, ModelicaTransformation.Default, true, null),
            icon,
            false,
            "Demo",
            "{}");
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot(
                "Demo",
                "model",
                annotation,
                [component],
                [],
                [],
                "{}"),
        };
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());

        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(document.SourcePath!);

        Assert.True(viewModel.CanShowDiagram);
        Assert.True(viewModel.ShowDiagramCommand.CanExecute(null));
        viewModel.ShowDiagramCommand.Execute(null);
        Assert.True(viewModel.IsGraphicalView);
    }

    [Fact]
    public async Task GraphicalSelection_PopulatesInspectorAndClearsWhenLeavingCanvas()
    {
        var annotation = CreateAnnotation();
        var icon = new ModelicaGraphicalAnnotationSnapshot(
            "Demo.Plant",
            "model",
            new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []),
            null,
            [],
            [],
            "{}");
        var component = new ModelicaComponentInstance(
            "plant",
            "Demo.Plant",
            "model",
            new ModelicaComponentPrefixes(IsFinal: true, Direction: "input"),
            new ModelicaPlacement(
                true,
                new ModelicaTransformation(
                    new ModelicaPoint(20, 30),
                    new ModelicaExtent(new ModelicaPoint(-10, -5), new ModelicaPoint(10, 5)),
                    90),
                false,
                null),
            icon,
            false,
            "Demo",
            "{}");
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [component], [], [], "{}"),
        };
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(document.SourcePath!);
        viewModel.ShowDiagramCommand.Execute(null);

        viewModel.SelectGraphicalElement(new ModelicaDiagramHit(component, null));

        Assert.True(viewModel.HasGraphicalSelection);
        Assert.Same(component, viewModel.SelectedComponent);
        Assert.Equal("COMPONENT", viewModel.GraphicalSelection?.Kind);
        Assert.Contains(viewModel.GraphicalSelection!.Properties, property =>
            property.Name == "Prefixes" && property.Value == "final input");
        Assert.Contains(viewModel.GraphicalSelection.Properties, property =>
            property.Name == "Rotation" && property.Value == "90°");

        viewModel.ShowTextCommand.Execute(null);

        Assert.True(viewModel.HasNoGraphicalSelection);
        Assert.Null(viewModel.SelectedComponent);
    }

    [Fact]
    public async Task MoveComponent_ConfirmsPersistsRefreshesAndSupportsUndoRedo()
    {
        var annotation = CreateAnnotation();
        var beforeTransformation = new ModelicaTransformation(
            new ModelicaPoint(0, 0),
            new ModelicaExtent(new ModelicaPoint(-10, -10), new ModelicaPoint(10, 10)),
            0);
        var component = CreateComponent(beforeTransformation);
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [component], [], [], "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        const string compilerSource = "model Demo\n  Demo.Plant plant annotation(Placement());\nend Demo;\n";
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, compilerSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        var afterTransformation = beforeTransformation with { Origin = new ModelicaPoint(20, 30) };

        await viewModel.MoveComponentAsync("plant", afterTransformation, iconLayer: false);

        Assert.Equal(new ModelicaPoint(20, 30), Assert.Single(viewModel.ActiveModelInstance!.Components).Placement?.Transformation.Origin);
        Assert.Equal(compilerSource, viewModel.SourceText);
        Assert.False(viewModel.ActiveDocument!.IsDirty);
        Assert.True(viewModel.CanUndoGraphicalEdit);
        Assert.Equal(1, engine.SaveRequests);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Equal(new ModelicaPoint(0, 0), Assert.Single(viewModel.ActiveModelInstance!.Components).Placement?.Transformation.Origin);
        Assert.True(viewModel.CanRedoGraphicalEdit);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Equal(new ModelicaPoint(20, 30), Assert.Single(viewModel.ActiveModelInstance!.Components).Placement?.Transformation.Origin);
        Assert.Equal(3, engine.SaveRequests);
        Assert.Equal(3, engine.PlacementMutations.Count);
    }

    [Fact]
    public async Task MoveComponent_UnconfirmedCompilerStateRollsBackAndIsNotRecorded()
    {
        var annotation = CreateAnnotation();
        var beforeTransformation = ModelicaTransformation.Default;
        var component = CreateComponent(beforeTransformation);
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [component], [], [], "{}"),
            ApplyPlacementToInstance = false,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.MoveComponentAsync(
            "plant",
            beforeTransformation with { Origin = new ModelicaPoint(40, 10) },
            iconLayer: false));

        Assert.Contains("rolled back", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, engine.PlacementMutations.Count);
        Assert.Equal(beforeTransformation, engine.PlacementMutations[1].Placement.Transformation);
        Assert.Equal(1, engine.SaveRequests);
        Assert.False(viewModel.CanUndoGraphicalEdit);
        Assert.Equal(beforeTransformation, Assert.Single(viewModel.ActiveModelInstance!.Components).Placement?.Transformation);
    }

    [Fact]
    public async Task EditComponentPlacements_MovesSelectionAsOneCompilerTransaction()
    {
        var annotation = CreateAnnotation();
        var first = CreateComponent(ModelicaTransformation.Default);
        var second = CreateComponent(
            ModelicaTransformation.Default with
            {
                Origin = new ModelicaPoint(20, 0),
            }) with
        { Name = "controller" };
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [first, second], [], [], "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, document.Source),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElements(new ModelicaDiagramHit(second, null), [first, second]);

        await viewModel.EditComponentPlacementsAsync(
            [
                new ModelicaComponentTransformationEdit(
                    first.Name,
                    first.Placement!.Transformation with { Origin = new ModelicaPoint(10, 10) }),
                new ModelicaComponentTransformationEdit(
                    second.Name,
                    second.Placement!.Transformation with { Origin = new ModelicaPoint(30, 10) }),
            ],
            iconLayer: false,
            ModelicaPlacementEditKind.Move);

        Assert.Equal(2, engine.PlacementMutations.Count);
        Assert.Equal(1, engine.SaveRequests);
        Assert.Equal(2, viewModel.SelectedComponentCount);
        Assert.Equal("Undo move 2 components", viewModel.UndoGraphicalLabel);
        Assert.Collection(
            viewModel.ActiveModelInstance!.Components,
            component => Assert.Equal(new ModelicaPoint(10, 10), component.Placement!.Transformation.Origin),
            component => Assert.Equal(new ModelicaPoint(30, 10), component.Placement!.Transformation.Origin));

        await viewModel.UndoGraphicalEditAsync();

        Assert.Equal(4, engine.PlacementMutations.Count);
        Assert.Equal(2, engine.SaveRequests);
        Assert.Collection(
            viewModel.ActiveModelInstance!.Components,
            component => Assert.Equal(new ModelicaPoint(0, 0), component.Placement!.Transformation.Origin),
            component => Assert.Equal(new ModelicaPoint(20, 0), component.Placement!.Transformation.Origin));
    }

    [Fact]
    public async Task RotateSelectedComponents_RotatesWholeSelectionAndLabelsUndo()
    {
        var annotation = CreateAnnotation();
        var first = CreateComponent(ModelicaTransformation.Default);
        var second = CreateComponent(ModelicaTransformation.Default with { Rotation = 350 }) with
        { Name = "controller" };
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [first, second], [], [], "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, document.Source),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElements(new ModelicaDiagramHit(second, null), [first, second]);

        await viewModel.RotateSelectedComponentAsync(20);

        Assert.Equal(20, viewModel.ActiveModelInstance!.Components[0].Placement!.Transformation.Rotation);
        Assert.Equal(10, viewModel.ActiveModelInstance.Components[1].Placement!.Transformation.Rotation);
        Assert.Equal("Undo rotate 2 components", viewModel.UndoGraphicalLabel);
    }

    [Fact]
    public async Task AddComponent_UsesUniqueNameSnapsPersistsAndSupportsUndoRedo()
    {
        var annotation = CreateAnnotation();
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [], [], [], "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        const string originalSource = "model Demo\nend Demo;\n";
        const string addedSource =
            "model Demo\n  Modelica.Electrical.Analog.Basic.Resistor resistor1 annotation(Placement());\nend Demo;\n";
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, addedSource, originalSource, addedSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        var resistorType = new ModelicaClassInfo(
            "Modelica.Electrical.Analog.Basic.Resistor",
            "Resistor",
            ModelicaClassKind.Model);

        await viewModel.AddComponentAsync(resistorType, new ModelicaPoint(11, 9));

        var added = Assert.Single(viewModel.ActiveModelInstance!.Components);
        Assert.Equal("resistor1", added.Name);
        Assert.Equal(new ModelicaPoint(12, 10), added.Placement?.Transformation.Origin);
        Assert.Equal(addedSource, viewModel.SourceText);
        Assert.Same(added, viewModel.SelectedComponent);
        Assert.True(viewModel.CanUndoGraphicalEdit);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Components);
        Assert.Equal(originalSource, viewModel.SourceText);
        Assert.True(viewModel.CanRedoGraphicalEdit);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Equal("resistor1", Assert.Single(viewModel.ActiveModelInstance!.Components).Name);
        Assert.Equal(2, engine.ComponentAdds.Count);
        Assert.Single(engine.ComponentDeletes);
        Assert.Equal(3, engine.SaveRequests);
    }

    [Fact]
    public async Task AddComponent_UnconfirmedCompilerStateRollsBackAndIsNotRecorded()
    {
        var annotation = CreateAnnotation();
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [], [], [], "{}"),
            ApplyComponentAddToInstance = false,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.AddComponentAsync(
            new ModelicaClassInfo("Demo.Part", "Part", ModelicaClassKind.Model),
            new ModelicaPoint(0, 0)));

        Assert.Contains("rolled back", exception.Message, StringComparison.Ordinal);
        Assert.Single(engine.ComponentAdds);
        Assert.Single(engine.ComponentDeletes);
        Assert.Empty(viewModel.ActiveModelInstance!.Components);
        Assert.False(viewModel.CanUndoGraphicalEdit);
    }

    [Fact]
    public async Task DuplicateComponent_PreservesSourcePayloadAndSupportsUndoRedo()
    {
        var annotation = CreateAnnotation();
        var original = CreateComponent(ModelicaTransformation.Default);
        var duplicate = original with
        {
            Name = "plant1",
            Placement = original.Placement! with
            {
                Transformation = original.Placement.Transformation with
                {
                    Origin = new ModelicaPoint(10, 10),
                },
            },
        };
        var initialInstance = new ModelicaModelInstanceSnapshot(
            "Demo",
            "model",
            annotation,
            [original],
            [],
            [],
            "{}");
        var duplicatedInstance = initialInstance with { Components = [original, duplicate] };
        const string originalSource =
            "model Demo\n  final replaceable Demo.Plant plant(gain=42) constrainedby Demo.Plant \"configured\" annotation(Placement());\nend Demo;\n";
        const string duplicatedSource =
            "model Demo\n  final replaceable Demo.Plant plant(gain=42) constrainedby Demo.Plant \"configured\" annotation(Placement());\n  final replaceable Demo.Plant plant1(gain=42) constrainedby Demo.Plant \"configured\" annotation(Placement());\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = initialInstance,
            LoadClassContentInstanceFactory = (_, _, _) => duplicatedInstance,
            LoadStringInstanceFactory = source => source == originalSource ? initialInstance : duplicatedInstance,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, duplicatedSource, originalSource, duplicatedSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElement(new ModelicaDiagramHit(original, null));

        await viewModel.DuplicateSelectedComponentsAsync();

        var load = Assert.Single(engine.ClassContentLoads);
        Assert.Equal(10, load.OffsetX);
        Assert.Equal(10, load.OffsetY);
        Assert.Contains(
            "final replaceable Demo.Plant plant(gain=42) constrainedby Demo.Plant \"configured\" annotation(Placement());",
            load.Content,
            StringComparison.Ordinal);
        Assert.Equal("plant1", viewModel.SelectedComponent!.Name);
        Assert.Equal("Undo duplicate plant", viewModel.UndoGraphicalLabel);
        Assert.Equal(duplicatedSource, viewModel.SourceText);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Equal("plant", Assert.Single(viewModel.ActiveModelInstance!.Components).Name);
        Assert.Equal(originalSource, viewModel.SourceText);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Equal(2, viewModel.ActiveModelInstance!.Components.Count);
        Assert.Equal(2, engine.ClassContentLoads.Count);
        Assert.Equal(3, engine.SaveRequests);
    }

    [Fact]
    public async Task DeleteComponent_PreservesCompleteSourceForUndoAndSupportsRedo()
    {
        var annotation = CreateAnnotation();
        var component = CreateComponent(ModelicaTransformation.Default);
        var initialInstance = new ModelicaModelInstanceSnapshot(
            "Demo",
            "model",
            annotation,
            [component],
            [],
            [],
            "{}");
        const string originalSource =
            "model Demo\n  final Demo.Plant plant(gain=42) \"configured\" annotation(Placement());\nend Demo;\n";
        const string deletedSource = "model Demo\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = initialInstance,
            LoadStringInstanceFactory = source => source == originalSource
                ? initialInstance
                : new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [], [], [], "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, deletedSource, originalSource, deletedSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElement(new ModelicaDiagramHit(component, null));

        await viewModel.DeleteSelectedComponentAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Components);
        Assert.Equal(deletedSource, viewModel.SourceText);
        Assert.True(viewModel.CanUndoGraphicalEdit);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Equal("plant", Assert.Single(viewModel.ActiveModelInstance!.Components).Name);
        Assert.Equal(originalSource, Assert.Single(engine.SourceReplacements).Source);
        Assert.Equal(originalSource, viewModel.SourceText);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Components);
        Assert.Equal(2, engine.ComponentDeletes.Count);
        Assert.Equal(3, engine.SaveRequests);
    }

    [Fact]
    public async Task DeleteComponent_WithAttachedConnectionIsRejectedBeforeCompilerMutation()
    {
        var annotation = CreateAnnotation();
        var component = CreateComponent(ModelicaTransformation.Default);
        var connection = new ModelicaDiagramConnection("plant.port", "sink.port", null, false, "Demo", "{}");
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot(
                "Demo",
                "model",
                annotation,
                [component],
                [connection],
                [],
                "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElement(new ModelicaDiagramHit(component, null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.DeleteSelectedComponentAsync());

        Assert.Contains("connections", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanDeleteSelectedComponent);
        Assert.Empty(engine.ComponentDeletes);
        Assert.False(viewModel.CanUndoGraphicalEdit);
    }

    [Fact]
    public async Task DeleteComponents_DeletesAndRestoresWholeSelection()
    {
        var annotation = CreateAnnotation();
        var first = CreateComponent(ModelicaTransformation.Default);
        var second = CreateComponent(ModelicaTransformation.Default) with { Name = "controller" };
        var initialInstance = new ModelicaModelInstanceSnapshot(
            "Demo",
            "model",
            annotation,
            [first, second],
            [],
            [],
            "{}");
        const string originalSource =
            "model Demo\n  Demo.Plant plant annotation(Placement());\n  Demo.Plant controller annotation(Placement());\nend Demo;\n";
        const string deletedSource = "model Demo\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = initialInstance,
            LoadStringInstanceFactory = source => source == originalSource
                ? initialInstance
                : new ModelicaModelInstanceSnapshot("Demo", "model", annotation, [], [], [], "{}"),
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, deletedSource, originalSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElements(new ModelicaDiagramHit(second, null), [first, second]);

        await viewModel.DeleteSelectedComponentAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Components);
        Assert.Equal(2, engine.ComponentDeletes.Count);
        Assert.Equal(1, engine.SaveRequests);
        Assert.Equal("Undo delete 2 components", viewModel.UndoGraphicalLabel);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Equal(2, viewModel.ActiveModelInstance!.Components.Count);
        Assert.Equal(2, viewModel.SelectedComponentCount);
        Assert.Equal(originalSource, Assert.Single(engine.SourceReplacements).Source);
    }

    [Fact]
    public async Task DeleteComponent_UnconfirmedCompilerStateRestoresSourceAndIsNotRecorded()
    {
        var annotation = CreateAnnotation();
        var component = CreateComponent(ModelicaTransformation.Default);
        var initialInstance = new ModelicaModelInstanceSnapshot(
            "Demo",
            "model",
            annotation,
            [component],
            [],
            [],
            "{}");
        const string originalSource = "model Demo\n  Demo.Plant plant annotation(Placement());\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = initialInstance,
            ApplyComponentDeleteToInstance = false,
            LoadStringInstanceFactory = _ => initialInstance,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElement(new ModelicaDiagramHit(component, null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.DeleteSelectedComponentAsync());

        Assert.Contains("rolled back", exception.Message, StringComparison.Ordinal);
        Assert.Single(engine.ComponentDeletes);
        Assert.Equal(originalSource, Assert.Single(engine.SourceReplacements).Source);
        Assert.Equal("plant", Assert.Single(viewModel.ActiveModelInstance!.Components).Name);
        Assert.False(viewModel.CanUndoGraphicalEdit);
    }

    [Fact]
    public async Task AddConnection_ConfirmsPersistsAndSupportsUndoRedo()
    {
        var annotation = CreateAnnotation();
        var source = CreateConnectorComponent("source", "output", new ModelicaPoint(-40, 0));
        var sink = CreateConnectorComponent("sink", "input", new ModelicaPoint(40, 0));
        var initial = new ModelicaModelInstanceSnapshot(
            "Demo",
            "model",
            annotation,
            [source, sink],
            [],
            [],
            "{}");
        const string originalSource = "model Demo\n  Demo.Signal source, sink;\nend Demo;\n";
        const string connectedSource = "model Demo\n  Demo.Signal source, sink;\nequation\n  connect(source, sink) annotation(Line());\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation) { Instance = initial };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, connectedSource, originalSource, connectedSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        var endpoints = ModelicaConnectionEditor.GetConnectorEndpoints(viewModel.ActiveModelInstance);

        await viewModel.AddConnectionAsync(
            endpoints.Single(endpoint => endpoint.Path == "source"),
            endpoints.Single(endpoint => endpoint.Path == "sink"),
            [new ModelicaPoint(-40, 0), new ModelicaPoint(0, 0), new ModelicaPoint(40, 0)]);

        var connection = Assert.Single(viewModel.ActiveModelInstance!.Connections);
        Assert.Equal("source", connection.Left);
        Assert.Equal("sink", connection.Right);
        Assert.Same(connection, viewModel.SelectedConnection);
        Assert.Equal(connectedSource, viewModel.SourceText);
        Assert.Equal("Undo connect source to sink", viewModel.UndoGraphicalLabel);
        Assert.Single(engine.ConnectionAdds);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Connections);
        Assert.Single(engine.ConnectionDeletes);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Single(viewModel.ActiveModelInstance!.Connections);
        Assert.Equal(2, engine.ConnectionAdds.Count);
        Assert.Equal(3, engine.SaveRequests);
    }

    [Fact]
    public async Task AddConnection_UnconfirmedCompilerStateRollsBackAndIsNotRecorded()
    {
        var annotation = CreateAnnotation();
        var source = CreateConnectorComponent("source", "output", new ModelicaPoint(-40, 0));
        var sink = CreateConnectorComponent("sink", "input", new ModelicaPoint(40, 0));
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = new ModelicaModelInstanceSnapshot(
                "Demo", "model", annotation, [source, sink], [], [], "{}"),
            ApplyConnectionAddToInstance = false,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        var endpoints = ModelicaConnectionEditor.GetConnectorEndpoints(viewModel.ActiveModelInstance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.AddConnectionAsync(
            endpoints.Single(endpoint => endpoint.Path == "source"),
            endpoints.Single(endpoint => endpoint.Path == "sink"),
            [new ModelicaPoint(-40, 0), new ModelicaPoint(40, 0)]));

        Assert.Contains("rolled back", exception.Message, StringComparison.Ordinal);
        Assert.Single(engine.ConnectionAdds);
        Assert.Single(engine.ConnectionDeletes);
        Assert.Empty(viewModel.ActiveModelInstance!.Connections);
        Assert.False(viewModel.CanUndoGraphicalEdit);
    }

    [Fact]
    public async Task DeleteConnection_RestoresExactSourceAndSupportsRedo()
    {
        var annotation = CreateAnnotation();
        var source = CreateConnectorComponent("source", "output", new ModelicaPoint(-40, 0));
        var sink = CreateConnectorComponent("sink", "input", new ModelicaPoint(40, 0));
        var connection = new ModelicaDiagramConnection(
            "source",
            "sink",
            CreateConnectionLine([new ModelicaPoint(-40, 0), new ModelicaPoint(40, 0)]),
            false,
            "Demo",
            "{}");
        var initial = new ModelicaModelInstanceSnapshot(
            "Demo", "model", annotation, [source, sink], [connection], [], "{}");
        const string originalSource = "model Demo\n  Demo.Signal source, sink;\nequation\n  connect(source, sink) annotation(Line(color={1,2,3}));\nend Demo;\n";
        const string deletedSource = "model Demo\n  Demo.Signal source, sink;\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = initial,
            LoadStringInstanceFactory = sourceText => sourceText == originalSource ? initial : null,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, deletedSource, originalSource, deletedSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);
        viewModel.SelectGraphicalElement(new ModelicaDiagramHit(null, connection));

        await viewModel.DeleteSelectedConnectionAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Connections);
        Assert.Equal(deletedSource, viewModel.SourceText);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Single(viewModel.ActiveModelInstance!.Connections);
        Assert.Equal(originalSource, Assert.Single(engine.SourceReplacements).Source);
        Assert.Equal(originalSource, viewModel.SourceText);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Empty(viewModel.ActiveModelInstance!.Connections);
        Assert.Equal(2, engine.ConnectionDeletes.Count);
    }

    [Fact]
    public async Task RerouteConnection_UpdatesAnnotationAndRestoresExactSourceOnUndo()
    {
        var annotation = CreateAnnotation();
        var source = CreateConnectorComponent("source", "output", new ModelicaPoint(-40, 0));
        var sink = CreateConnectorComponent("sink", "input", new ModelicaPoint(40, 0));
        var oldLine = CreateConnectionLine([new ModelicaPoint(-40, 0), new ModelicaPoint(40, 0)]);
        var connection = new ModelicaDiagramConnection("source", "sink", oldLine, false, "Demo", "{}");
        var initial = new ModelicaModelInstanceSnapshot(
            "Demo", "model", annotation, [source, sink], [connection], [], "{}");
        const string originalSource = "model Demo\nequation\n  connect(source, sink) annotation(Line(points={{-40,0},{40,0}}));\nend Demo;\n";
        const string routedSource = "model Demo\nequation\n  connect(source, sink) annotation(Line(points={{-40,0},{0,20},{40,0}}));\nend Demo;\n";
        var engine = new FakeOpenModelicaService(annotation)
        {
            Instance = initial,
            LoadStringInstanceFactory = sourceText => sourceText == originalSource ? initial : null,
        };
        var sourcePath = Path.GetFullPath("Demo.mo");
        var document = new ModelDocument("Demo", sourcePath, originalSource);
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document, routedSource, originalSource, routedSource),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        viewModel.ShowDiagramCommand.Execute(null);

        await viewModel.RerouteConnectionAsync(
            "source",
            "sink",
            [
                new ModelicaPoint(-40, 0),
                new ModelicaPoint(0, 0),
                new ModelicaPoint(0, 20),
                new ModelicaPoint(40, 20),
                new ModelicaPoint(40, 0),
            ]);

        Assert.Equal(5, Assert.Single(viewModel.ActiveModelInstance!.Connections).Line!.Points.Count);
        Assert.Single(engine.ConnectionUpdates);
        Assert.Equal(routedSource, viewModel.SourceText);

        await viewModel.UndoGraphicalEditAsync();

        Assert.Equal(2, Assert.Single(viewModel.ActiveModelInstance!.Connections).Line!.Points.Count);
        Assert.Equal(originalSource, viewModel.SourceText);

        await viewModel.RedoGraphicalEditAsync();

        Assert.Equal(5, Assert.Single(viewModel.ActiveModelInstance!.Connections).Line!.Points.Count);
        Assert.Equal(2, engine.ConnectionUpdates.Count);
    }

    [Fact]
    public async Task CheckModel_PreservesProblemsAndSourceEditsMarkLocationsStale()
    {
        var annotation = CreateAnnotation();
        var sourcePath = Path.GetFullPath("Demo.mo");
        var diagnostic = new CompilerDiagnostic(
            CompilerDiagnosticSeverity.Error,
            "Unknown component: x",
            sourcePath,
            2,
            3,
            EndLine: 2,
            EndColumn: 4);
        var engine = new FakeOpenModelicaService(annotation)
        {
            CheckResult = new ModelCheckResult(false, "Check failed", [diagnostic]),
        };
        var document = new ModelDocument("Demo", sourcePath, "model Demo\n  x = 1;\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());

        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(sourcePath);
        await viewModel.CheckActiveModelAsync();

        var problem = Assert.Single(viewModel.Problems);
        Assert.Same(diagnostic, problem.Diagnostic);
        Assert.True(problem.IsNavigable);
        Assert.Equal("Demo.mo:2:3", problem.LocationLabel);
        Assert.False(viewModel.ProblemsAreStale);
        Assert.Equal("Problems (1)", viewModel.ProblemsTabTitle);

        viewModel.SourceText += " ";

        Assert.True(viewModel.ProblemsAreStale);
        Assert.Single(viewModel.Problems);
    }

    [Fact]
    public async Task RunSimulation_ChecksRunsReadsVariablesAndOpensResults()
    {
        var annotation = CreateAnnotation();
        var engine = new FakeOpenModelicaService(annotation)
        {
            SimulationResult = new SimulationResult(
                true,
                "Demo",
                "/tmp/Demo_res.mat",
                TimeSpan.FromSeconds(1.25),
                "simulation completed",
                []),
            ResultVariables = ["v", "time", "h"],
        };
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(document.SourcePath!);

        await viewModel.RunSimulationAsync(new SimulationConfiguration { StopTime = 3, NumberOfIntervals = 120 });

        Assert.Equal(["check:Demo", "simulate:Demo", "variables:/tmp/Demo_res.mat"], engine.SimulationOperations);
        Assert.Equal(["h", "time", "v"], viewModel.SimulationVariables);
        Assert.True(viewModel.HasSimulationResult);
        Assert.True(viewModel.IsResultsView);
        Assert.Equal("1.25 s", viewModel.SimulationElapsed);
        Assert.Contains("Completed", viewModel.SimulationStatus, StringComparison.Ordinal);

        await viewModel.LoadSimulationSeriesAsync(["h", "time", "v"]);

        Assert.Equal(2, viewModel.ResultSeries.Count);
        Assert.True(viewModel.HasResultSeries);
        Assert.Contains("2 signal", viewModel.ResultPlotStatus, StringComparison.Ordinal);
        Assert.Contains("series:h", engine.SimulationOperations);
        Assert.Contains("series:v", engine.SimulationOperations);
    }

    [Fact]
    public async Task RunSimulation_FailedCheckDoesNotInvokeSimulation()
    {
        var annotation = CreateAnnotation();
        var engine = new FakeOpenModelicaService(annotation)
        {
            CheckResult = new ModelCheckResult(
                false,
                "Check failed",
                [new CompilerDiagnostic(CompilerDiagnosticSeverity.Error, "Broken model")]),
        };
        var document = new ModelDocument("Demo", Path.GetFullPath("Demo.mo"), "model Demo\nend Demo;\n");
        await using var viewModel = new MainWindowViewModel(
            engine,
            new UnusedProjectService(),
            new StubDocumentService(document),
            new ModelicaSourceAnalyzer(),
            new UnusedSettingsService());
        await viewModel.InitializeEngineAsync();
        await viewModel.OpenModelicaFileAsync(document.SourcePath!);

        await viewModel.RunSimulationAsync();

        Assert.Equal(["check:Demo"], engine.SimulationOperations);
        Assert.False(viewModel.HasSimulationResult);
        Assert.Equal("Model check failed", viewModel.SimulationStatus);
        Assert.Single(viewModel.Problems);
    }

    private static ModelicaGraphicalAnnotationSnapshot CreateAnnotation()
    {
        var view = new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []);
        return new ModelicaGraphicalAnnotationSnapshot("Demo", "model", view, view, [], [], "{}");
    }

    private static ModelicaComponentInstance CreateComponent(ModelicaTransformation transformation)
    {
        var icon = new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []);
        return new ModelicaComponentInstance(
            "plant",
            "Demo.Plant",
            "model",
            new ModelicaComponentPrefixes(),
            new ModelicaPlacement(true, transformation, false, null),
            new ModelicaGraphicalAnnotationSnapshot("Demo.Plant", "model", icon, null, [], [], "{}"),
            false,
            "Demo",
            "{}");
    }

    private static ModelicaComponentInstance CreateConnectorComponent(
        string name,
        string direction,
        ModelicaPoint origin)
    {
        var signal = new ModelicaComponentInstance(
            "signal",
            "Real",
            "type",
            new ModelicaComponentPrefixes(),
            null,
            null,
            false,
            "Demo.Signal",
            "{}")
        {
            RootTypeName = "Real",
        };
        return new ModelicaComponentInstance(
            name,
            "Demo.Signal",
            "connector",
            new ModelicaComponentPrefixes(Direction: direction),
            new ModelicaPlacement(
                true,
                ModelicaTransformation.Default with { Origin = origin },
                true,
                ModelicaTransformation.Default with { Origin = origin }),
            new ModelicaGraphicalAnnotationSnapshot(
                "Demo.Signal",
                "connector",
                new ModelicaGraphicalView(new ModelicaGraphicalCoordinateSystem(), []),
                null,
                [],
                [],
                "{}"),
            false,
            "Demo",
            "{}")
        {
            TypeComponents = [signal],
        };
    }

    private static ModelicaGraphicPrimitive CreateConnectionLine(
        IReadOnlyList<ModelicaPoint> points) => new()
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(0),
            Points = points,
            Style = new ModelicaGraphicStyle
            {
                LineColor = new ModelicaColor(1, 2, 3),
                LinePattern = ModelicaLinePattern.Solid,
                LineThickness = 0.25,
            },
            RawJson = "{}",
        };

    private sealed class FakeOpenModelicaService(ModelicaGraphicalAnnotationSnapshot annotation)
        : IOpenModelicaService
    {
        public OpenModelicaSessionState State { get; private set; } =
            OpenModelicaSessionState.NotDetected();

        public List<string> AnnotationRequests { get; } = [];
        public List<string> InstanceRequests { get; } = [];
        public ModelicaModelInstanceSnapshot? Instance { get; set; }
        public bool PlacementMutationResult { get; init; } = true;
        public bool ApplyPlacementToInstance { get; init; } = true;
        public bool SaveClassResult { get; init; } = true;
        public string DefaultComponentName { get; init; } = string.Empty;
        public bool ComponentAddResult { get; init; } = true;
        public bool ApplyComponentAddToInstance { get; init; } = true;
        public bool ComponentDeleteResult { get; init; } = true;
        public bool ApplyComponentDeleteToInstance { get; init; } = true;
        public bool ConnectionAddResult { get; init; } = true;
        public bool ApplyConnectionAddToInstance { get; init; } = true;
        public bool ConnectionDeleteResult { get; init; } = true;
        public bool ApplyConnectionDeleteToInstance { get; init; } = true;
        public bool ConnectionUpdateResult { get; init; } = true;
        public bool ApplyConnectionUpdateToInstance { get; init; } = true;
        public bool LoadStringResult { get; init; } = true;
        public Func<string, ModelicaModelInstanceSnapshot?>? LoadStringInstanceFactory { get; init; }
        public bool LoadClassContentStringResult { get; init; } = true;
        public Func<string, int, int, ModelicaModelInstanceSnapshot?>? LoadClassContentInstanceFactory { get; init; }
        public List<(string ClassName, string ComponentName, ModelicaPlacement Placement)> PlacementMutations { get; } = [];
        public List<(string ClassName, string ComponentName, string TypeName, ModelicaPlacement Placement)> ComponentAdds { get; } = [];
        public List<(string ClassName, string ComponentName)> ComponentDeletes { get; } = [];
        public List<(string ClassName, string Left, string Right, ModelicaGraphicPrimitive Line)> ConnectionAdds { get; } = [];
        public List<(string ClassName, string Left, string Right)> ConnectionDeletes { get; } = [];
        public List<(string ClassName, string Left, string Right, ModelicaGraphicPrimitive Line)> ConnectionUpdates { get; } = [];
        public List<(string Source, string SourcePath)> SourceReplacements { get; } = [];
        public List<(string Content, string ClassName, int OffsetX, int OffsetY)> ClassContentLoads { get; } = [];
        public int SaveRequests { get; private set; }
        public ModelCheckResult CheckResult { get; init; } = new(true, "Check completed successfully.", []);
        public SimulationResult SimulationResult { get; init; } = new(
            true,
            "Demo",
            "/tmp/Demo_res.mat",
            TimeSpan.Zero,
            "simulation completed",
            []);
        public IReadOnlyList<string> ResultVariables { get; init; } = [];
        public Func<string, SimulationSeries> SeriesFactory { get; init; } = name => new SimulationSeries(
            name,
            null,
            [0, 1],
            [0, 1]);
        public List<string> SimulationOperations { get; } = [];

        public event EventHandler<OpenModelicaStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = new OpenModelicaSessionState(
                OpenModelicaSessionStatus.Ready,
                new OpenModelicaInstallation("omc", null, "OpenModelica test", true));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartAsync(CancellationToken cancellationToken = default) => StartAsync(cancellationToken);

        public Task ConfigureExecutableAsync(string executablePath, CancellationToken cancellationToken = default) =>
            StartAsync(cancellationToken);

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("OpenModelica test");

        public Task<bool> LoadModelAsync(string library, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> LoadFileAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<ModelCheckResult> CheckModelAsync(
            string modelName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationOperations.Add($"check:{modelName}");
            return Task.FromResult(CheckResult);
        }

        public Task<IReadOnlyList<ModelicaClassInfo>> GetClassNamesAsync(
            string parentClass,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelicaClassInfo>>([]);

        public Task<ModelicaGraphicalAnnotationSnapshot> GetGraphicalAnnotationAsync(
            string className,
            CancellationToken cancellationToken = default)
        {
            AnnotationRequests.Add(className);
            return Task.FromResult(annotation);
        }

        public Task<ModelicaModelInstanceSnapshot> GetModelInstanceAsync(
            string className,
            CancellationToken cancellationToken = default)
        {
            InstanceRequests.Add(className);
            return Task.FromResult(Instance ?? new ModelicaModelInstanceSnapshot(
                className,
                "model",
                annotation,
                [],
                [],
                [],
                "{}"));
        }

        public Task<string> GetDefaultComponentNameAsync(
            string typeName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DefaultComponentName);
        }

        public Task<bool> AddComponentAsync(
            string className,
            string componentName,
            string typeName,
            ModelicaPlacement placement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComponentAdds.Add((className, componentName, typeName, placement));
            if (ComponentAddResult && ApplyComponentAddToInstance && Instance is not null)
            {
                Instance = Instance with
                {
                    Components = Instance.Components
                        .Append(new ModelicaComponentInstance(
                            componentName,
                            typeName,
                            "model",
                            new ModelicaComponentPrefixes(),
                            placement,
                            annotation,
                            false,
                            className,
                            "{}"))
                        .ToArray(),
                };
            }

            return Task.FromResult(ComponentAddResult);
        }

        public Task<bool> DeleteComponentAsync(
            string className,
            string componentName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComponentDeletes.Add((className, componentName));
            if (ComponentDeleteResult && ApplyComponentDeleteToInstance && Instance is not null)
            {
                Instance = Instance with
                {
                    Components = Instance.Components
                        .Where(component => !string.Equals(component.Name, componentName, StringComparison.Ordinal))
                        .ToArray(),
                };
            }

            return Task.FromResult(ComponentDeleteResult);
        }

        public Task<bool> AddConnectionAsync(
            string className,
            string from,
            string to,
            ModelicaGraphicPrimitive line,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectionAdds.Add((className, from, to, line));
            if (ConnectionAddResult && ApplyConnectionAddToInstance && Instance is not null)
            {
                Instance = Instance with
                {
                    Connections = Instance.Connections
                        .Append(new ModelicaDiagramConnection(from, to, line, false, className, "{}"))
                        .ToArray(),
                };
            }

            return Task.FromResult(ConnectionAddResult);
        }

        public Task<bool> DeleteConnectionAsync(
            string className,
            string from,
            string to,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectionDeletes.Add((className, from, to));
            if (ConnectionDeleteResult && ApplyConnectionDeleteToInstance && Instance is not null)
            {
                Instance = Instance with
                {
                    Connections = Instance.Connections
                        .Where(connection => !ModelicaConnectionEditor.SameEndpoints(
                            connection.Left,
                            connection.Right,
                            from,
                            to))
                        .ToArray(),
                };
            }

            return Task.FromResult(ConnectionDeleteResult);
        }

        public Task<bool> UpdateConnectionAnnotationAsync(
            string className,
            string from,
            string to,
            ModelicaGraphicPrimitive line,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectionUpdates.Add((className, from, to, line));
            if (ConnectionUpdateResult && ApplyConnectionUpdateToInstance && Instance is not null)
            {
                Instance = Instance with
                {
                    Connections = Instance.Connections
                        .Select(connection => ModelicaConnectionEditor.SameEndpoints(
                            connection.Left,
                            connection.Right,
                            from,
                            to)
                                ? connection with { Line = line }
                                : connection)
                        .ToArray(),
                };
            }

            return Task.FromResult(ConnectionUpdateResult);
        }

        public Task<bool> SetComponentPlacementAsync(
            string className,
            string componentName,
            ModelicaPlacement placement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlacementMutations.Add((className, componentName, placement));
            if (PlacementMutationResult && ApplyPlacementToInstance && Instance is not null)
            {
                Instance = Instance with
                {
                    Components = Instance.Components
                        .Select(component => string.Equals(component.Name, componentName, StringComparison.Ordinal)
                            ? component with { Placement = placement }
                            : component)
                        .ToArray(),
                };
            }

            return Task.FromResult(PlacementMutationResult);
        }

        public Task<bool> SaveClassAsync(
            string className,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveRequests++;
            return Task.FromResult(SaveClassResult);
        }

        public Task<bool> LoadStringAsync(
            string source,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceReplacements.Add((source, sourcePath));
            if (LoadStringResult && LoadStringInstanceFactory?.Invoke(source) is { } replacement)
            {
                Instance = replacement;
            }

            return Task.FromResult(LoadStringResult);
        }

        public Task<bool> LoadClassContentStringAsync(
            string content,
            string className,
            int offsetX,
            int offsetY,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClassContentLoads.Add((content, className, offsetX, offsetY));
            if (LoadClassContentStringResult
                && LoadClassContentInstanceFactory?.Invoke(content, offsetX, offsetY) is { } replacement)
            {
                Instance = replacement;
            }

            return Task.FromResult(LoadClassContentStringResult);
        }

        public Task<SimulationResult> SimulateAsync(
            string modelName,
            SimulationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationOperations.Add($"simulate:{modelName}");
            return Task.FromResult(SimulationResult);
        }

        public Task<IReadOnlyList<string>> ReadSimulationResultVariablesAsync(
            string resultFile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationOperations.Add($"variables:{resultFile}");
            return Task.FromResult(ResultVariables);
        }

        public Task<SimulationSeries> ReadSimulationSeriesAsync(
            string resultFile,
            string variableName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationOperations.Add($"series:{variableName}");
            return Task.FromResult(SeriesFactory(variableName));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubDocumentService(ModelDocument document, params string[] compilerSources) : IModelicaDocumentService
    {
        private int _openCount;

        public ModelDocument Create(NewModelicaClass definition, string? sourcePath = null) =>
            throw new NotSupportedException();

        public Task<ModelDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _openCount++;
            if (_openCount == 1 || compilerSources.Length == 0)
            {
                return Task.FromResult(document);
            }

            var sourceIndex = Math.Min(_openCount - 2, compilerSources.Length - 1);
            return Task.FromResult(new ModelDocument(document.ClassName, path, compilerSources[sourceIndex]));
        }

        public Task SaveAsync(
            ModelDocument documentToSave,
            string? destinationPath = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedProjectService : IProjectService
    {
        public Task<ModelicaProjectDocument> CreateAsync(
            string projectFilePath,
            string name,
            string? rootPackage = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ModelicaProjectDocument> LoadAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            ModelicaProjectDocument project,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedSettingsService : IApplicationSettingsService
    {
        public string SettingsFilePath => "settings.json";

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
