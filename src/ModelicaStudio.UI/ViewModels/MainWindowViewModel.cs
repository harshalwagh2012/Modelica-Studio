using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ModelicaStudio.Application.Commands;
using ModelicaStudio.Application.Engine;
using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Application.Projects;
using ModelicaStudio.Application.Settings;
using ModelicaStudio.Domain;
using ModelicaStudio.Domain.Diagnostics;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.Domain.Projects;
using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.UI.Commands;
using SkiaSharp;

namespace ModelicaStudio.UI.ViewModels;

public enum ModelicaDocumentViewMode
{
    Text,
    Diagram,
    Icon,
    Results,
}

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IOpenModelicaService _openModelica;
    private readonly IProjectService _projectService;
    private readonly IModelicaDocumentService _documentService;
    private readonly IModelicaSourceAnalyzer _sourceAnalyzer;
    private readonly IApplicationSettingsService _settingsService;
    private string _engineStatus = "Checking…";
    private string _engineDetail = "Locating the OpenModelica compiler";
    private string _statusMessage = "Starting";
    private string _messagesText = "Application messages and compiler diagnostics appear here.";
    private string _sourceText = string.Empty;
    private string _librarySearchText = string.Empty;
    private bool _isEngineAvailable;
    private bool _isInitializing;
    private ModelDocument? _activeDocument;
    private ModelicaGraphicalAnnotationSnapshot? _activeGraphicalAnnotation;
    private ModelicaModelInstanceSnapshot? _activeModelInstance;
    private ModelicaDocumentViewMode _activeViewMode = ModelicaDocumentViewMode.Text;
    private ModelicaSourceAnalysis _sourceAnalysis = ModelicaSourceAnalysis.Empty;
    private ModelicaProjectDocument? _currentProject;
    private SimulationConfiguration _activeSimulationConfiguration = new();
    private SimulationResult? _lastSimulationResult;
    private CancellationTokenSource? _simulationCancellation;
    private CancellationTokenSource? _resultReadCancellation;
    private bool _isSimulationRunning;
    private string _simulationStatus = "No simulation has been run.";
    private string _simulationOutput = "Simulation translation, compilation, and runtime output appears here.";
    private string _resultPlotStatus = "Select one or more variables to plot.";
    private ModelicaComponentInstance? _selectedComponent;
    private IReadOnlyList<ModelicaComponentInstance> _selectedComponents = [];
    private ModelicaDiagramConnection? _selectedConnection;
    private GraphicalSelectionViewModel? _graphicalSelection;
    private readonly ModelCommandHistory _graphicalHistory = new();
    private bool _isGraphicalMutationRunning;
    private bool _isConnectionToolActive;

    public MainWindowViewModel(
        IOpenModelicaService openModelica,
        IProjectService projectService,
        IModelicaDocumentService documentService,
        IModelicaSourceAnalyzer sourceAnalyzer,
        IApplicationSettingsService settingsService)
    {
        _openModelica = openModelica;
        _projectService = projectService;
        _documentService = documentService;
        _sourceAnalyzer = sourceAnalyzer;
        _settingsService = settingsService;
        _openModelica.StateChanged += HandleEngineStateChanged;
        RetryEngineCommand = new AsyncCommand(InitializeEngineAsync, () => !IsInitializing);
        SaveCommand = new AsyncCommand(() => SaveActiveDocumentAsync(), () => ActiveDocument is not null);
        CheckModelCommand = new AsyncCommand(
            CheckActiveModelAsync,
            () => ActiveDocument is not null && IsEngineAvailable && !IsInitializing);
        ShowTextCommand = new RelayCommand(
            () => ActiveViewMode = ModelicaDocumentViewMode.Text,
            () => ActiveDocument is not null);
        ShowDiagramCommand = new RelayCommand(
            () => ActiveViewMode = ModelicaDocumentViewMode.Diagram,
            () => CanShowDiagram);
        ShowIconCommand = new RelayCommand(
            () => ActiveViewMode = ModelicaDocumentViewMode.Icon,
            () => CanShowIcon);
        ShowResultsCommand = new RelayCommand(
            () => ActiveViewMode = ModelicaDocumentViewMode.Results,
            () => CanShowResults);
        SimulateCommand = new AsyncCommand(
            () => RunSimulationAsync(),
            () => CanSimulate);
        StopSimulationCommand = new RelayCommand(CancelSimulation, () => IsSimulationRunning);
        UndoGraphicalCommand = new AsyncCommand(() => UndoGraphicalEditAsync(), () => CanUndoGraphicalEdit);
        RedoGraphicalCommand = new AsyncCommand(() => RedoGraphicalEditAsync(), () => CanRedoGraphicalEdit);
    }

    public string ProductName => ProductInfo.Name;
    public string WindowTitle => CurrentProject is null
        ? $"{ProductInfo.Name} — Engineering Workspace"
        : $"{CurrentProject.Project.Name} — {ProductInfo.Name}";
    public ObservableCollection<LibraryNodeViewModel> LibraryClasses { get; } = [];
    public ObservableCollection<LibraryNodeViewModel> VisibleLibraryClasses { get; } = [];
    public AsyncCommand RetryEngineCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CheckModelCommand { get; }
    public RelayCommand ShowTextCommand { get; }
    public RelayCommand ShowDiagramCommand { get; }
    public RelayCommand ShowIconCommand { get; }
    public RelayCommand ShowResultsCommand { get; }
    public AsyncCommand SimulateCommand { get; }
    public RelayCommand StopSimulationCommand { get; }
    public AsyncCommand UndoGraphicalCommand { get; }
    public AsyncCommand RedoGraphicalCommand { get; }
    public ObservableCollection<string> SimulationVariables { get; } = [];
    public ObservableCollection<ISeries> ResultSeries { get; } = [];
    public IReadOnlyList<Axis> ResultXAxes { get; } =
    [
        new Axis
        {
            Name = "Time",
            Labeler = static value => value.ToString("G5", CultureInfo.InvariantCulture),
            SeparatorsPaint = new SolidColorPaint(new SKColor(224, 226, 228)) { StrokeThickness = 1 },
        },
    ];
    public IReadOnlyList<Axis> ResultYAxes { get; } =
    [
        new Axis
        {
            Labeler = static value => value.ToString("G5", CultureInfo.InvariantCulture),
            SeparatorsPaint = new SolidColorPaint(new SKColor(224, 226, 228)) { StrokeThickness = 1 },
        },
    ];

    public ModelicaProjectDocument? CurrentProject
    {
        get => _currentProject;
        private set
        {
            if (SetProperty(ref _currentProject, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(CurrentProjectName));
                OnPropertyChanged(nameof(HasProject));
            }
        }
    }

    public string CurrentProjectName => CurrentProject?.Project.Name ?? "No project";
    public bool HasProject => CurrentProject is not null;

    public string LibrarySearchText
    {
        get => _librarySearchText;
        set
        {
            if (SetProperty(ref _librarySearchText, value))
            {
                ApplyLibraryFilter();
            }
        }
    }

    public ModelDocument? ActiveDocument
    {
        get => _activeDocument;
        private set
        {
            if (SetProperty(ref _activeDocument, value))
            {
                _sourceText = value?.Source ?? string.Empty;
                AnalyzeSourceText();
                ActiveGraphicalAnnotation = null;
                ActiveModelInstance = null;
                IsConnectionToolActive = false;
                ClearGraphicalSelection();
                _graphicalHistory.Clear();
                NotifyGraphicalEditStateChanged();
                ActiveSimulationConfiguration = LoadSimulationConfiguration(value);
                LastSimulationResult = null;
                SimulationVariables.Clear();
                ResultSeries.Clear();
                ResultPlotStatus = "Select one or more variables to plot.";
                SimulationStatus = "No simulation has been run.";
                SimulationOutput = "Simulation translation, compilation, and runtime output appears here.";
                ActiveViewMode = ModelicaDocumentViewMode.Text;
                OnPropertyChanged(nameof(SourceText));
                NotifyDocumentStateChanged();
            }
        }
    }

    public bool HasActiveDocument => ActiveDocument is not null;
    public bool ShowWelcome => ActiveDocument is null;
    public string ActiveModelName => ActiveDocument?.ClassName ?? "No active model";
    public string DocumentTabTitle => ActiveDocument is null
        ? string.Empty
        : ActiveDocument.ClassName + (ActiveDocument.IsDirty ? "  •" : string.Empty);
    public string DocumentPath => ActiveDocument?.SourcePath ?? "Unsaved Modelica document";

    public ModelicaGraphicalAnnotationSnapshot? ActiveGraphicalAnnotation
    {
        get => _activeGraphicalAnnotation;
        private set
        {
            if (SetProperty(ref _activeGraphicalAnnotation, value))
            {
                OnPropertyChanged(nameof(CanShowDiagram));
                OnPropertyChanged(nameof(CanShowIcon));
                ShowDiagramCommand.NotifyCanExecuteChanged();
                ShowIconCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ModelicaModelInstanceSnapshot? ActiveModelInstance
    {
        get => _activeModelInstance;
        private set
        {
            if (SetProperty(ref _activeModelInstance, value))
            {
                ClearGraphicalSelection();
                OnPropertyChanged(nameof(CanShowDiagram));
                OnPropertyChanged(nameof(CanShowIcon));
                OnPropertyChanged(nameof(CanSelectGraphicalElements));
                OnPropertyChanged(nameof(CanEditGraphically));
                OnPropertyChanged(nameof(CanInsertLibraryComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(CanUseConnectionTool));
                ShowDiagramCommand.NotifyCanExecuteChanged();
                ShowIconCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ModelicaDocumentViewMode ActiveViewMode
    {
        get => _activeViewMode;
        private set
        {
            if (SetProperty(ref _activeViewMode, value))
            {
                ClearGraphicalSelection();
                if (value != ModelicaDocumentViewMode.Diagram)
                {
                    IsConnectionToolActive = false;
                }
                OnPropertyChanged(nameof(IsTextView));
                OnPropertyChanged(nameof(IsGraphicalView));
                OnPropertyChanged(nameof(IsIconView));
                OnPropertyChanged(nameof(IsResultsView));
                OnPropertyChanged(nameof(ActiveViewLabel));
                OnPropertyChanged(nameof(CanSelectGraphicalElements));
                OnPropertyChanged(nameof(CanEditGraphically));
                OnPropertyChanged(nameof(CanInsertLibraryComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(CanUseConnectionTool));
                NotifyGraphicalEditStateChanged();
            }
        }
    }

    public bool CanShowDiagram => ActiveGraphicalAnnotation?.Diagram is not null
        || ActiveModelInstance?.Components.Any(static component =>
            component.Placement is { Visible: true }
            && component.TypeGraphics?.Icon is not null) == true
        || ActiveModelInstance?.Connections.Any(static connection => connection.Line is not null) == true;
    public bool CanShowIcon => ActiveGraphicalAnnotation?.Icon is not null
        || ActiveModelInstance?.Components.Any(static component =>
            component.Placement is { IconVisible: true, IconTransformation: not null }
            && component.TypeGraphics?.Icon is not null) == true;
    public bool IsTextView => ActiveViewMode == ModelicaDocumentViewMode.Text;
    public bool IsGraphicalView => ActiveViewMode is ModelicaDocumentViewMode.Diagram or ModelicaDocumentViewMode.Icon;
    public bool IsIconView => ActiveViewMode == ModelicaDocumentViewMode.Icon;
    public bool IsResultsView => ActiveViewMode == ModelicaDocumentViewMode.Results;
    public bool CanSelectGraphicalElements => IsGraphicalView && ActiveModelInstance is not null;
    public bool CanEditGraphically => IsGraphicalView
        && ActiveDocument?.SourcePath is not null
        && ActiveModelInstance is not null
        && IsEngineAvailable
        && !IsInitializing
        && !IsSimulationRunning
        && !IsGraphicalMutationRunning;
    public string ActiveViewLabel => ActiveViewMode switch
    {
        ModelicaDocumentViewMode.Diagram => "Diagram · compiler-confirmed component authoring",
        ModelicaDocumentViewMode.Icon => "Icon · compiler-confirmed connector placement",
        ModelicaDocumentViewMode.Results => "Simulation results",
        _ => "Editable Modelica source",
    };

    public ModelicaComponentInstance? SelectedComponent
    {
        get => _selectedComponent;
        private set
        {
            if (SetProperty(ref _selectedComponent, value))
            {
                OnPropertyChanged(nameof(CanMoveSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(SelectedComponentHasConnections));
                OnPropertyChanged(nameof(SelectedComponentDeletionHint));
                OnPropertyChanged(nameof(SelectedGraphicalEditHint));
            }
        }
    }

    public IReadOnlyList<ModelicaComponentInstance> SelectedComponents
    {
        get => _selectedComponents;
        private set
        {
            if (SetProperty(ref _selectedComponents, value))
            {
                OnPropertyChanged(nameof(SelectedComponentCount));
                OnPropertyChanged(nameof(CanMoveSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(SelectedComponentHasConnections));
                OnPropertyChanged(nameof(SelectedComponentDeletionHint));
                OnPropertyChanged(nameof(SelectedGraphicalEditHint));
            }
        }
    }

    public int SelectedComponentCount => SelectedComponents.Count;

    public ModelicaDiagramConnection? SelectedConnection
    {
        get => _selectedConnection;
        private set
        {
            if (SetProperty(ref _selectedConnection, value))
            {
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(SelectedGraphicalEditHint));
            }
        }
    }

    public GraphicalSelectionViewModel? GraphicalSelection
    {
        get => _graphicalSelection;
        private set
        {
            if (SetProperty(ref _graphicalSelection, value))
            {
                OnPropertyChanged(nameof(HasGraphicalSelection));
                OnPropertyChanged(nameof(HasNoGraphicalSelection));
            }
        }
    }

    public bool HasGraphicalSelection => GraphicalSelection is not null;
    public bool HasNoGraphicalSelection => !HasGraphicalSelection;
    public bool CanMoveSelectedComponent => CanEditGraphically
        && SelectedComponents.Count > 0
        && SelectedComponents.All(component => component is { IsInherited: false, Placement: not null }
            && (!IsIconView || component.Placement.IconTransformation is not null));
    public bool CanInsertLibraryComponent => CanEditGraphically && !IsIconView;
    public bool CanUseConnectionTool => CanEditGraphically && !IsIconView;
    public bool IsConnectionToolActive
    {
        get => _isConnectionToolActive;
        private set
        {
            if (SetProperty(ref _isConnectionToolActive, value))
            {
                OnPropertyChanged(nameof(ConnectionToolLabel));
            }
        }
    }
    public string ConnectionToolLabel => IsConnectionToolActive ? "Connect ✓" : "Connect";
    public bool SelectedComponentHasConnections => SelectedComponents.Any(component =>
        ComponentHasConnections(component.Name));
    public bool CanDeleteSelectedComponent => CanEditGraphically
        && SelectedComponents.Count > 0
        && SelectedComponents.All(static component => !component.IsInherited)
        && !SelectedComponentHasConnections;
    public bool CanDeleteSelectedConnection => CanEditGraphically
        && !IsIconView
        && SelectedConnection is { IsInherited: false };
    public bool CanDeleteGraphicalSelection => CanDeleteSelectedComponent || CanDeleteSelectedConnection;
    public string SelectedComponentDeletionHint => SelectedComponent switch
    {
        null => "Select a local component to delete it.",
        _ when SelectedComponents.Any(static component => component.IsInherited) =>
            "Inherited components must be deleted from their declaring class.",
        _ when SelectedComponentHasConnections =>
            "Delete the component's attached connections first, then delete the component.",
        _ when SelectedComponents.Count > 1 =>
            $"Press Delete or use the toolbar to remove all {SelectedComponents.Count} selected components with compiler-backed undo.",
        _ => "Press Delete or use the toolbar to remove this component with compiler-backed undo.",
    };
    public string SelectedGraphicalEditHint => SelectedConnection switch
    {
        { IsInherited: true } => "Inherited connections must be edited in their declaring class.",
        not null => "Drag a circular segment handle to reroute this connection on the grid, or press Delete to remove it. Changes are confirmed and saved through OpenModelica.",
        _ => SelectedComponentDeletionHint,
    };
    public bool CanUndoGraphicalEdit => IsGraphicalView
        && !IsGraphicalMutationRunning
        && _graphicalHistory.CanUndo;
    public bool CanRedoGraphicalEdit => IsGraphicalView
        && !IsGraphicalMutationRunning
        && _graphicalHistory.CanRedo;
    public string UndoGraphicalLabel => _graphicalHistory.UndoDescription is { } description
        ? $"Undo {description}"
        : "Undo graphical edit";
    public string RedoGraphicalLabel => _graphicalHistory.RedoDescription is { } description
        ? $"Redo {description}"
        : "Redo graphical edit";

    public void SelectGraphicalElement(ModelicaDiagramHit selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        SelectGraphicalElements(
            selection,
            selection.Component is { } component ? [component] : []);
    }

    public void ToggleConnectionTool()
    {
        if (!CanUseConnectionTool)
        {
            IsConnectionToolActive = false;
            return;
        }

        IsConnectionToolActive = !IsConnectionToolActive;
        StatusMessage = IsConnectionToolActive
            ? "Connection tool active · choose the first connector"
            : "Connection tool inactive";
    }

    public void SelectGraphicalElements(
        ModelicaDiagramHit selection,
        IReadOnlyList<ModelicaComponentInstance> components)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(components);
        SelectedComponents = components;
        SelectedComponent = selection.Component ?? components.LastOrDefault();
        SelectedConnection = selection.Connection;
        GraphicalSelection = components.Count > 1
            ? GraphicalSelectionViewModel.FromComponents(components)
            : SelectedComponent is not null
            ? GraphicalSelectionViewModel.FromComponent(SelectedComponent, IsIconView)
            : selection.Connection is not null
                ? GraphicalSelectionViewModel.FromConnection(selection.Connection)
                : null;
        if (GraphicalSelection is not null)
        {
            StatusMessage = $"Selected {GraphicalSelection.Kind.ToLowerInvariant()} {GraphicalSelection.Title}";
        }
    }

    public async Task MoveComponentAsync(
        string componentName,
        ModelicaTransformation transformation,
        bool iconLayer,
        CancellationToken cancellationToken = default) =>
        await EditComponentPlacementAsync(
            componentName,
            transformation,
            iconLayer,
            ModelicaPlacementEditKind.Move,
            cancellationToken).ConfigureAwait(true);

    public async Task EditComponentPlacementAsync(
        string componentName,
        ModelicaTransformation transformation,
        bool iconLayer,
        ModelicaPlacementEditKind kind,
        CancellationToken cancellationToken = default) =>
        await EditComponentPlacementsAsync(
            [new ModelicaComponentTransformationEdit(componentName, transformation)],
            iconLayer,
            kind,
            cancellationToken).ConfigureAwait(true);

    public async Task EditComponentPlacementsAsync(
        IReadOnlyList<ModelicaComponentTransformationEdit> edits,
        bool iconLayer,
        ModelicaPlacementEditKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
        {
            return;
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown placement edit kind.");
        }

        if (!CanEditGraphically)
        {
            throw new InvalidOperationException("Graphical editing requires a saved model and a connected OpenModelica session.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var before = new Dictionary<string, ModelicaPlacement>(StringComparer.Ordinal);
            var after = new Dictionary<string, ModelicaPlacement>(StringComparer.Ordinal);
            foreach (var edit in edits)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(edit.ComponentName);
                ArgumentNullException.ThrowIfNull(edit.Transformation);
                if (after.ContainsKey(edit.ComponentName))
                {
                    throw new ArgumentException($"Component '{edit.ComponentName}' appears more than once.", nameof(edits));
                }

                var component = FindEditableComponent(edit.ComponentName);
                var current = component.Placement
                    ?? throw new InvalidOperationException(
                        $"Component '{edit.ComponentName}' has no editable Placement annotation.");
                var requested = iconLayer
                    ? current with { IconVisible = true, IconTransformation = edit.Transformation }
                    : current with { Visible = true, Transformation = edit.Transformation };
                if (!PlacementsEquivalent(current, requested))
                {
                    before[edit.ComponentName] = current;
                    after[edit.ComponentName] = requested;
                }
            }

            if (after.Count == 0)
            {
                return;
            }

            var operation = kind.ToString().ToLowerInvariant();
            var target = after.Count == 1 ? after.Keys.Single() : $"{after.Count} components";
            var command = new ReversibleModelCommand(
                $"{operation} {target}",
                token => ApplyComponentPlacementsAsync(after, token),
                token => ApplyComponentPlacementsAsync(before, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = kind switch
            {
                ModelicaPlacementEditKind.Resize => $"Resized {target}",
                ModelicaPlacementEditKind.Rotate => $"Rotated {target}",
                _ => $"Moved {target}",
            };
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public Task RotateSelectedComponentAsync(
        double deltaDegrees,
        CancellationToken cancellationToken = default)
    {
        if (!CanMoveSelectedComponent)
        {
            throw new InvalidOperationException("Select a local component before rotating it.");
        }

        var edits = SelectedComponents.Select(component =>
        {
            var transformation = IsIconView
                ? component.Placement!.IconTransformation
                : component.Placement!.Transformation;
            return new ModelicaComponentTransformationEdit(
                component.Name,
                ModelicaPlacementEditor.RotateBy(
                    transformation
                        ?? throw new InvalidOperationException(
                            $"Component '{component.Name}' has no editable placement on this layer."),
                    deltaDegrees));
        }).ToArray();

        return EditComponentPlacementsAsync(
            edits,
            IsIconView,
            ModelicaPlacementEditKind.Rotate,
            cancellationToken);
    }

    public async Task DuplicateSelectedComponentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMoveSelectedComponent)
        {
            throw new InvalidOperationException("Select one or more local placed components before duplicating them.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var document = ActiveDocument
                ?? throw new InvalidOperationException("There is no active Modelica document.");
            var instance = ActiveModelInstance
                ?? throw new InvalidOperationException("There is no compiler model instance to duplicate.");
            var selected = SelectedComponents
                .Select(component => FindEditableComponent(component.Name))
                .ToArray();
            var content = ModelicaClassContentCopy.Create(document.Source, selected, instance.Connections);
            var grid = (IsIconView ? ActiveGraphicalAnnotation?.Icon : ActiveGraphicalAnnotation?.Diagram)
                ?.CoordinateSystem.Grid ?? new ModelicaPoint(2, 2);
            var offsetX = Math.Max(1, (int)Math.Round(grid.X * 5, MidpointRounding.AwayFromZero));
            var offsetY = Math.Max(1, (int)Math.Round(grid.Y * 5, MidpointRounding.AwayFromZero));
            var state = new ComponentDuplicationState(
                document.Source,
                content,
                selected,
                instance.Components.Select(static component => component.Name).ToHashSet(StringComparer.Ordinal),
                instance.Connections.Select(ConnectionIdentity).ToHashSet(StringComparer.Ordinal),
                offsetX,
                offsetY);
            var target = selected.Length == 1 ? selected[0].Name : $"{selected.Length} components";
            var command = new ReversibleModelCommand(
                $"duplicate {target}",
                token => ApplyComponentDuplicationAsync(state, token),
                token => UndoComponentDuplicationAsync(state, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = selected.Length == 1
                ? $"Duplicated {selected[0].Name} as {state.NewComponentNames.Single()}"
                : $"Duplicated {selected.Length} components";
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task AddComponentAsync(
        ModelicaClassInfo componentType,
        ModelicaPoint origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!ModelicaComponentType.IsInstantiable(componentType))
        {
            throw new InvalidOperationException($"'{componentType.FullName}' is not an instantiable Modelica component type.");
        }

        if (!CanInsertLibraryComponent)
        {
            throw new InvalidOperationException(
                "Component insertion requires a saved model, Diagram view, and a connected OpenModelica session.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var document = ActiveDocument
                ?? throw new InvalidOperationException("There is no active Modelica document.");
            if (string.Equals(componentType.FullName, document.ClassName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A class cannot contain an instance of itself.");
            }

            var recommendedName = await _openModelica
                .GetDefaultComponentNameAsync(componentType.FullName, cancellationToken)
                .ConfigureAwait(true);
            var componentName = ModelicaComponentNaming.CreateUniqueName(
                componentType.FullName,
                recommendedName,
                ActiveModelInstance?.Components.Select(static item => item.Name) ?? []);
            var grid = ActiveGraphicalAnnotation?.Diagram?.CoordinateSystem.Grid ?? new ModelicaPoint(2, 2);
            var placement = new ModelicaPlacement(
                true,
                ModelicaTransformation.Default with { Origin = ModelicaGrid.Snap(origin, grid) },
                false,
                null);
            var command = new ReversibleModelCommand(
                $"add {componentName}",
                token => ApplyComponentAdditionAsync(
                    componentName,
                    componentType.FullName,
                    placement,
                    token),
                token => ApplyComponentDeletionAsync(componentName, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"Added {componentName}";
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task DeleteSelectedComponentAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedComponents.Count == 0)
        {
            throw new InvalidOperationException("Select a component before deleting it.");
        }

        if (!CanEditGraphically)
        {
            throw new InvalidOperationException(
                "Component deletion requires a saved model and a connected OpenModelica session.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var components = SelectedComponents
                .Select(component => FindEditableComponent(component.Name))
                .ToArray();
            var connected = components.FirstOrDefault(component => ComponentHasConnections(component.Name));
            if (connected is not null)
            {
                throw new InvalidOperationException(
                    $"Delete the connections attached to '{connected.Name}' before deleting the selected components.");
            }

            var sourceBeforeDeletion = ActiveDocument?.Source
                ?? throw new InvalidOperationException("There is no active Modelica document.");
            var target = components.Length == 1 ? components[0].Name : $"{components.Length} components";
            var command = new ReversibleModelCommand(
                $"delete {target}",
                token => ApplyComponentDeletionsAsync(
                    components.Select(static component => component.Name).ToArray(),
                    token),
                token => ApplyClassSourceRestoreAsync(sourceBeforeDeletion, components, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"Deleted {target}";
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task AddConnectionAsync(
        ModelicaConnectorEndpoint left,
        ModelicaConnectorEndpoint right,
        IReadOnlyList<ModelicaPoint> route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(route);
        if (!CanEditGraphically || IsIconView)
        {
            throw new InvalidOperationException(
                "Connection creation requires a saved model, Diagram view, and a connected OpenModelica session.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var endpoints = ModelicaConnectionEditor.GetConnectorEndpoints(ActiveModelInstance);
            var currentLeft = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Path, left.Path, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Connector '{left.Path}' is no longer available.");
            var currentRight = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Path, right.Path, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Connector '{right.Path}' is no longer available.");
            var compatibility = ModelicaConnectionEditor.CheckCompatibility(
                currentLeft,
                currentRight,
                ActiveModelInstance?.Connections);
            if (!compatibility.IsCompatible)
            {
                throw new InvalidOperationException(compatibility.Reason ?? "The selected connectors are incompatible.");
            }

            var line = CreateConnectionLine(ModelicaConnectionEditor.SimplifyRoute(route));
            var command = new ReversibleModelCommand(
                $"connect {left.Path} to {right.Path}",
                token => ApplyConnectionAdditionAsync(left.Path, right.Path, line, token),
                token => ApplyConnectionDeletionAsync(left.Path, right.Path, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"Connected {left.Path} to {right.Path}";
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task DeleteSelectedConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeleteSelectedConnection)
        {
            throw new InvalidOperationException("Select a local connection in Diagram view before deleting it.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var connection = FindEditableConnection(
                SelectedConnection!.Left,
                SelectedConnection.Right);
            var sourceBeforeDeletion = ActiveDocument?.Source
                ?? throw new InvalidOperationException("There is no active Modelica document.");
            var command = new ReversibleModelCommand(
                $"delete connection {connection.Left} to {connection.Right}",
                token => ApplyConnectionDeletionAsync(connection.Left, connection.Right, token),
                token => ApplyConnectionSourceRestoreAsync(sourceBeforeDeletion, connection, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"Deleted connection {connection.Left} to {connection.Right}";
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task RerouteConnectionAsync(
        string left,
        string right,
        IReadOnlyList<ModelicaPoint> route,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);
        ArgumentNullException.ThrowIfNull(route);
        if (!CanEditGraphically || IsIconView)
        {
            throw new InvalidOperationException(
                "Connection routing requires a saved model, Diagram view, and a connected OpenModelica session.");
        }

        IsGraphicalMutationRunning = true;
        try
        {
            await PrepareGraphicalMutationAsync(cancellationToken).ConfigureAwait(true);
            var connection = FindEditableConnection(left, right);
            var sourceBeforeRoute = ActiveDocument?.Source
                ?? throw new InvalidOperationException("There is no active Modelica document.");
            var requested = (connection.Line ?? CreateConnectionLine(route)) with
            {
                Points = ModelicaConnectionEditor.SimplifyRoute(route),
                Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
                Rotation = new ModelicaAnnotationValue<double>(0),
            };
            if (connection.Line is not null && ConnectionLinesEquivalent(connection.Line, requested))
            {
                return;
            }

            var command = new ReversibleModelCommand(
                $"reroute connection {connection.Left} to {connection.Right}",
                token => ApplyConnectionRouteAsync(connection.Left, connection.Right, requested, sourceBeforeRoute, token),
                token => ApplyConnectionSourceRestoreAsync(sourceBeforeRoute, connection, token));
            await _graphicalHistory.ExecuteAsync(command, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"Rerouted connection {connection.Left} to {connection.Right}";
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task UndoGraphicalEditAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUndoGraphicalEdit)
        {
            return;
        }

        IsGraphicalMutationRunning = true;
        try
        {
            var description = _graphicalHistory.UndoDescription;
            if (await _graphicalHistory.UndoAsync(cancellationToken).ConfigureAwait(true))
            {
                StatusMessage = $"Undid {description}";
            }
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public async Task RedoGraphicalEditAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRedoGraphicalEdit)
        {
            return;
        }

        IsGraphicalMutationRunning = true;
        try
        {
            var description = _graphicalHistory.RedoDescription;
            if (await _graphicalHistory.RedoAsync(cancellationToken).ConfigureAwait(true))
            {
                StatusMessage = $"Redid {description}";
            }
        }
        finally
        {
            IsGraphicalMutationRunning = false;
            NotifyGraphicalEditStateChanged();
        }
    }

    public SimulationConfiguration ActiveSimulationConfiguration
    {
        get => _activeSimulationConfiguration;
        private set
        {
            if (SetProperty(ref _activeSimulationConfiguration, value))
            {
                OnPropertyChanged(nameof(SimulationConfigurationSummary));
            }
        }
    }

    public SimulationResult? LastSimulationResult
    {
        get => _lastSimulationResult;
        private set
        {
            if (SetProperty(ref _lastSimulationResult, value))
            {
                OnPropertyChanged(nameof(HasSimulationResult));
                OnPropertyChanged(nameof(CanShowResults));
                OnPropertyChanged(nameof(SimulationResultFile));
                OnPropertyChanged(nameof(SimulationElapsed));
                ShowResultsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSimulationRunning
    {
        get => _isSimulationRunning;
        private set
        {
            if (SetProperty(ref _isSimulationRunning, value))
            {
                OnPropertyChanged(nameof(CanSimulate));
                SimulateCommand.NotifyCanExecuteChanged();
                StopSimulationCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanEditGraphically));
                OnPropertyChanged(nameof(CanMoveSelectedComponent));
                OnPropertyChanged(nameof(CanInsertLibraryComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(CanUseConnectionTool));
            }
        }
    }

    public bool IsGraphicalMutationRunning
    {
        get => _isGraphicalMutationRunning;
        private set
        {
            if (SetProperty(ref _isGraphicalMutationRunning, value))
            {
                OnPropertyChanged(nameof(CanEditGraphically));
                OnPropertyChanged(nameof(CanMoveSelectedComponent));
                OnPropertyChanged(nameof(CanInsertLibraryComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(CanUseConnectionTool));
                NotifyGraphicalEditStateChanged();
            }
        }
    }

    public string SimulationStatus
    {
        get => _simulationStatus;
        private set => SetProperty(ref _simulationStatus, value);
    }

    public string SimulationOutput
    {
        get => _simulationOutput;
        private set => SetProperty(ref _simulationOutput, value);
    }

    public string ResultPlotStatus
    {
        get => _resultPlotStatus;
        private set => SetProperty(ref _resultPlotStatus, value);
    }

    public bool CanSimulate => ActiveDocument?.SourcePath is not null
        && IsEngineAvailable
        && !IsInitializing
        && !IsSimulationRunning;
    public bool HasSimulationResult => LastSimulationResult?.Success == true;
    public bool HasResultSeries => ResultSeries.Count > 0;
    public bool CanShowResults => HasSimulationResult;
    public string SimulationResultFile => LastSimulationResult?.ResultFile ?? "No result file";
    public string SimulationElapsed => LastSimulationResult is null
        ? "—"
        : $"{LastSimulationResult.Elapsed.TotalSeconds:0.###} s";
    public string SimulationConfigurationSummary =>
        $"{ActiveSimulationConfiguration.StartTime:G} → {ActiveSimulationConfiguration.StopTime:G} s · "
        + $"{ActiveSimulationConfiguration.NumberOfIntervals} intervals · "
        + $"{ActiveSimulationConfiguration.Method ?? "default solver"}";

    public ModelicaSourceAnalysis SourceAnalysis
    {
        get => _sourceAnalysis;
        private set
        {
            if (SetProperty(ref _sourceAnalysis, value))
            {
                OnPropertyChanged(nameof(SourceOutline));
                OnPropertyChanged(nameof(SourceOutlineTitle));
                OnPropertyChanged(nameof(WithinPackageLabel));
                OnPropertyChanged(nameof(HasSourceAnalysisIssues));
                OnPropertyChanged(nameof(SourceAnalysisIssuesText));
            }
        }
    }

    public IReadOnlyList<ModelicaSourceSymbol> SourceOutline => SourceAnalysis.Symbols;
    public string SourceOutlineTitle => $"SOURCE OUTLINE · {SourceOutline.Count}";
    public string WithinPackageLabel => SourceAnalysis.WithinPackage is null
        ? "Top-level Modelica scope"
        : $"within {SourceAnalysis.WithinPackage}";
    public bool HasSourceAnalysisIssues => SourceAnalysis.Issues.Count > 0;
    public string SourceAnalysisIssuesText => string.Join(
        Environment.NewLine,
        SourceAnalysis.Issues.Select(static issue => $"Line {issue.Line}: {issue.Message}"));

    public IReadOnlyList<CompilerProblemViewModel> Problems => ActiveDocument?.Diagnostics
        .Select(diagnostic => new CompilerProblemViewModel(
            diagnostic,
            diagnostic.Line is not null && DiagnosticTargetsActiveDocument(diagnostic)))
        .ToArray() ?? [];
    public bool HasProblems => ActiveDocument?.Diagnostics.Count > 0;
    public bool HasNoProblems => !HasProblems;
    public bool ProblemsAreStale => ActiveDocument?.DiagnosticsAreStale == true;
    public string ProblemsTabTitle => HasProblems ? $"Problems ({Problems.Count})" : "Problems";
    public string ProblemsStateText => ProblemsAreStale
        ? "Locations are from an earlier source snapshot. Run Check Model to refresh them."
        : "No compiler problems for the latest model check.";

    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (!SetProperty(ref _sourceText, value))
            {
                return;
            }

            ActiveDocument?.UpdateSource(value);
            _graphicalHistory.Clear();
            NotifyGraphicalEditStateChanged();
            AnalyzeSourceText();
            NotifyDocumentStateChanged();
        }
    }

    public string MessagesText
    {
        get => _messagesText;
        private set => SetProperty(ref _messagesText, value);
    }

    public string EngineStatus
    {
        get => _engineStatus;
        private set => SetProperty(ref _engineStatus, value);
    }

    public string EngineDetail
    {
        get => _engineDetail;
        private set => SetProperty(ref _engineDetail, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsEngineAvailable
    {
        get => _isEngineAvailable;
        private set
        {
            if (SetProperty(ref _isEngineAvailable, value))
            {
                OnPropertyChanged(nameof(IsEngineUnavailable));
                CheckModelCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanSimulate));
                OnPropertyChanged(nameof(CanEditGraphically));
                OnPropertyChanged(nameof(CanMoveSelectedComponent));
                OnPropertyChanged(nameof(CanInsertLibraryComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(CanUseConnectionTool));
                SimulateCommand.NotifyCanExecuteChanged();
                NotifyGraphicalEditStateChanged();
            }
        }
    }

    public bool IsEngineUnavailable => !IsEngineAvailable;

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (SetProperty(ref _isInitializing, value))
            {
                RetryEngineCommand.NotifyCanExecuteChanged();
                CheckModelCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanSimulate));
                OnPropertyChanged(nameof(CanEditGraphically));
                OnPropertyChanged(nameof(CanMoveSelectedComponent));
                OnPropertyChanged(nameof(CanInsertLibraryComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedComponent));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
                OnPropertyChanged(nameof(CanUseConnectionTool));
                SimulateCommand.NotifyCanExecuteChanged();
                NotifyGraphicalEditStateChanged();
            }
        }
    }

    public Task InitializeEngineAsync() =>
        InitializeEngineCoreAsync(() => _openModelica.StartAsync());

    public Task ConfigureOpenModelicaAsync(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        return InitializeEngineCoreAsync(async () =>
        {
            await _openModelica.ConfigureExecutableAsync(fullPath).ConfigureAwait(true);
            var settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            await _settingsService.SaveAsync(settings with
            {
                OpenModelicaExecutablePath = fullPath,
            }).ConfigureAwait(true);
        });
    }

    private async Task InitializeEngineCoreAsync(Func<Task> connect)
    {
        if (IsInitializing)
        {
            return;
        }

        IsInitializing = true;
        LibraryClasses.Clear();
        VisibleLibraryClasses.Clear();
        try
        {
            await connect().ConfigureAwait(true);
            var loaded = await _openModelica.LoadModelAsync("Modelica").ConfigureAwait(true);
            if (!loaded)
            {
                throw new InvalidOperationException("OpenModelica could not load the installed Modelica Standard Library.");
            }

            var classes = await _openModelica.GetClassNamesAsync("Modelica").ConfigureAwait(true);
            foreach (var item in classes)
            {
                LibraryClasses.Add(CreateLibraryNode(item));
            }

            ApplyLibraryFilter();

            IsEngineAvailable = true;
            EngineStatus = _openModelica.State.Installation?.Version ?? "Detected";
            EngineDetail = _openModelica.State.Installation?.OmcPath ?? "OpenModelica is ready";
            StatusMessage = "Ready";
            MessagesText = $"OpenModelica connected. Loaded Modelica with {classes.Count} top-level classes.";
        }
        catch (OpenModelicaUnavailableException exception)
        {
            IsEngineAvailable = _openModelica.State.Status is OpenModelicaSessionStatus.Ready or OpenModelicaSessionStatus.Busy;
            EngineStatus = IsEngineAvailable
                ? _openModelica.State.Installation?.Version ?? "Connected"
                : "Not detected";
            EngineDetail = exception.Message;
            StatusMessage = IsEngineAvailable
                ? "OpenModelica configuration unchanged"
                : "Editing available · Simulation offline";
            MessagesText = exception.Message;
        }
        catch (Exception exception)
        {
            ReportError(exception, "OpenModelica initialization failed");
        }
        finally
        {
            IsInitializing = false;
        }
    }

    public async Task CreateProjectAsync(string projectPath, string projectName)
    {
        CurrentProject = await _projectService.CreateAsync(projectPath, projectName).ConfigureAwait(true);
        ActiveDocument = null;
        StatusMessage = $"Created project {CurrentProject.Project.Name}";
        MessagesText = $"Project created at {CurrentProject.ProjectFilePath}";
    }

    public async Task OpenProjectAsync(string projectPath)
    {
        CurrentProject = await _projectService.LoadAsync(projectPath).ConfigureAwait(true);
        StatusMessage = $"Opened {CurrentProject.Project.Name}";
        MessagesText = $"Project loaded from {CurrentProject.ProjectFilePath}";

        var preferredModel = CurrentProject.Project.UiState.ActiveModel;
        var modelPath = CurrentProject.Project.ModelFiles.FirstOrDefault(path =>
            preferredModel is not null
            && string.Equals(Path.GetFileNameWithoutExtension(path), preferredModel.Split('.').Last(), StringComparison.Ordinal));
        modelPath ??= CurrentProject.Project.ModelFiles.FirstOrDefault();
        if (modelPath is not null)
        {
            var fullPath = Path.Combine(CurrentProject.ProjectDirectory, modelPath);
            if (File.Exists(fullPath))
            {
                await OpenModelicaFileAsync(fullPath).ConfigureAwait(true);
            }
        }
        else
        {
            ActiveDocument = null;
        }
    }

    public async Task OpenModelicaFileAsync(string path)
    {
        var document = await _documentService.OpenAsync(path).ConfigureAwait(true);
        ActiveDocument = document;
        StatusMessage = $"Opened {document.ClassName}";
        MessagesText = document.SynchronizationState == CompilerSynchronizationState.InvalidSource
            ? "The source could not be identified as a Modelica class. It was preserved unchanged for editing."
            : $"Opened {document.SourcePath}";

        if (IsEngineAvailable)
        {
            await SynchronizeDocumentAsync(document).ConfigureAwait(true);
        }
    }

    public async Task CreateClassAsync(NewModelicaClass definition)
    {
        var path = CurrentProject is null
            ? null
            : Path.Combine(CurrentProject.ProjectDirectory, "Models", definition.Name + ".mo");
        var document = _documentService.Create(definition, path);

        if (path is not null)
        {
            await _documentService.SaveAsync(document).ConfigureAwait(true);
            await AddDocumentToProjectAsync(document).ConfigureAwait(true);
        }

        ActiveDocument = document;
        StatusMessage = $"Created {definition.ClassType} {definition.Name}";
        MessagesText = path is null ? "New unsaved Modelica class." : $"Created {path}";

        if (IsEngineAvailable && path is not null)
        {
            await SynchronizeDocumentAsync(document).ConfigureAwait(true);
        }
    }

    public async Task SaveActiveDocumentAsync(string? destinationPath = null)
    {
        if (ActiveDocument is null)
        {
            return;
        }

        await _documentService.SaveAsync(ActiveDocument, destinationPath).ConfigureAwait(true);
        await AddDocumentToProjectAsync(ActiveDocument).ConfigureAwait(true);
        NotifyDocumentStateChanged();
        StatusMessage = $"Saved {ActiveDocument.ClassName}";
        MessagesText = $"Saved {ActiveDocument.SourcePath}";
    }

    public async Task CheckActiveModelAsync()
    {
        var document = ActiveDocument;
        if (document is null || !IsEngineAvailable)
        {
            return;
        }

        if (document.IsDirty && document.SourcePath is not null)
        {
            await SaveActiveDocumentAsync().ConfigureAwait(true);
        }

        if (document.SourcePath is not null)
        {
            await SynchronizeDocumentAsync(document).ConfigureAwait(true);
        }

        var result = await _openModelica.CheckModelAsync(document.ClassName).ConfigureAwait(true);
        document.SetDiagnostics(result.Diagnostics);
        NotifyDocumentStateChanged();
        StatusMessage = result.Success ? "Model check successful" : "Model check failed";
        MessagesText = result.Diagnostics.Count == 0
            ? result.Summary
            : string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Severity}: {diagnostic.Message} ({diagnostic.File}:{diagnostic.Line}:{diagnostic.Column})"));
    }

    public async Task ApplySimulationConfigurationAsync(SimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        ActiveSimulationConfiguration = configuration;
        if (CurrentProject is null || ActiveDocument is null)
        {
            return;
        }

        var configurations = new Dictionary<string, SimulationConfiguration>(
            CurrentProject.Project.SimulationConfigurations,
            StringComparer.Ordinal)
        {
            [ActiveDocument.ClassName] = configuration,
        };
        CurrentProject = CurrentProject with
        {
            Project = CurrentProject.Project with { SimulationConfigurations = configurations },
        };
        await _projectService.SaveAsync(CurrentProject).ConfigureAwait(true);
        StatusMessage = $"Saved simulation setup for {ActiveDocument.ClassName}";
    }

    public async Task RunSimulationAsync(SimulationConfiguration? configuration = null)
    {
        var document = ActiveDocument;
        if (document?.SourcePath is null || !IsEngineAvailable || IsSimulationRunning)
        {
            return;
        }

        var effectiveConfiguration = configuration ?? ActiveSimulationConfiguration;
        effectiveConfiguration.Validate();
        var cancellation = new CancellationTokenSource();
        _simulationCancellation = cancellation;
        IsSimulationRunning = true;
        LastSimulationResult = null;
        SimulationVariables.Clear();
        _resultReadCancellation?.Cancel();
        ResultSeries.Clear();
        OnPropertyChanged(nameof(HasResultSeries));
        ResultPlotStatus = "Select one or more variables to plot.";
        SimulationStatus = $"Preparing {document.ClassName}";
        SimulationOutput = "Saving and synchronizing the active Modelica source…";

        try
        {
            await ApplySimulationConfigurationAsync(effectiveConfiguration).ConfigureAwait(true);
            if (document.IsDirty)
            {
                await SaveActiveDocumentAsync().ConfigureAwait(true);
            }

            SimulationStatus = "Loading source into OpenModelica";
            await SynchronizeDocumentAsync(document, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (document.SynchronizationState != CompilerSynchronizationState.Synchronized)
            {
                SimulationStatus = "Source synchronization failed";
                return;
            }

            SimulationStatus = "Checking model";
            var check = await _openModelica
                .CheckModelAsync(document.ClassName, cancellation.Token)
                .ConfigureAwait(true);
            document.SetDiagnostics(check.Diagnostics);
            NotifyDocumentStateChanged();
            if (!check.Success)
            {
                SimulationStatus = "Model check failed";
                SimulationOutput = check.Summary + Environment.NewLine + FormatDiagnostics(check.Diagnostics);
                StatusMessage = "Simulation stopped · model check failed";
                return;
            }

            SimulationStatus = "Translating, compiling, and simulating";
            SimulationOutput = check.Summary;
            var result = await _openModelica
                .SimulateAsync(document.ClassName, effectiveConfiguration, cancellation.Token)
                .ConfigureAwait(true);
            document.SetDiagnostics(check.Diagnostics.Concat(result.Diagnostics));
            NotifyDocumentStateChanged();
            LastSimulationResult = result;
            SimulationOutput = result.RawResponse + Environment.NewLine + FormatDiagnostics(result.Diagnostics);
            if (!result.Success || string.IsNullOrWhiteSpace(result.ResultFile))
            {
                SimulationStatus = "Simulation failed";
                StatusMessage = "Simulation failed";
                return;
            }

            SimulationStatus = "Reading result variables";
            var variables = await _openModelica
                .ReadSimulationResultVariablesAsync(result.ResultFile, cancellation.Token)
                .ConfigureAwait(true);
            foreach (var variable in variables.Order(StringComparer.Ordinal))
            {
                SimulationVariables.Add(variable);
            }

            SimulationStatus = $"Completed · {SimulationVariables.Count} variables";
            StatusMessage = $"Simulated {document.ClassName} in {result.Elapsed.TotalSeconds:0.###} s";
            ActiveViewMode = ModelicaDocumentViewMode.Results;
        }
        catch (OperationCanceledException)
        {
            SimulationStatus = "Simulation canceled";
            SimulationOutput += Environment.NewLine + "Cancellation was requested. OpenModelica may finish terminating generated work in the background.";
            StatusMessage = "Simulation canceled";
        }
        catch (Exception exception)
        {
            SimulationStatus = "Simulation failed";
            SimulationOutput += Environment.NewLine + exception;
            ReportError(exception, "Simulation failed");
        }
        finally
        {
            if (ReferenceEquals(_simulationCancellation, cancellation))
            {
                _simulationCancellation = null;
            }

            cancellation.Dispose();
            IsSimulationRunning = false;
        }
    }

    public void CancelSimulation()
    {
        if (_simulationCancellation is null)
        {
            return;
        }

        SimulationStatus = "Cancellation requested";
        _simulationCancellation.Cancel();
    }

    public async Task LoadSimulationSeriesAsync(IEnumerable<string> variableNames)
    {
        ArgumentNullException.ThrowIfNull(variableNames);
        var resultFile = LastSimulationResult?.ResultFile;
        if (string.IsNullOrWhiteSpace(resultFile))
        {
            return;
        }

        var selectedNames = variableNames
            .Where(static name => !string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, "time", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        _resultReadCancellation?.Cancel();
        _resultReadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _resultReadCancellation = cancellation;
        ResultSeries.Clear();
        OnPropertyChanged(nameof(HasResultSeries));
        if (selectedNames.Length == 0)
        {
            ResultPlotStatus = "Select one or more non-time variables to plot.";
            return;
        }

        ResultPlotStatus = $"Reading {selectedNames.Length} result signal(s)…";
        try
        {
            var chartSeries = new List<ISeries>(selectedNames.Length);
            for (var index = 0; index < selectedNames.Length; index++)
            {
                var series = await _openModelica
                    .ReadSimulationSeriesAsync(resultFile, selectedNames[index], cancellation.Token)
                    .ConfigureAwait(true);
                var points = series.Time.Zip(series.Values)
                    .Where(static pair => double.IsFinite(pair.First) && double.IsFinite(pair.Second))
                    .Select(static pair => new ObservablePoint(pair.First, pair.Second))
                    .ToArray();
                chartSeries.Add(new LineSeries<ObservablePoint>
                {
                    Name = series.Unit is null ? series.Name : $"{series.Name} [{series.Unit}]",
                    Values = points,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(PlotColors[index % PlotColors.Length]) { StrokeThickness = 2 },
                });
            }

            if (!ReferenceEquals(_resultReadCancellation, cancellation))
            {
                return;
            }

            foreach (var series in chartSeries)
            {
                ResultSeries.Add(series);
            }

            OnPropertyChanged(nameof(HasResultSeries));
            ResultPlotStatus = $"Showing {chartSeries.Count} signal(s) · wheel/pinch to zoom, drag to pan";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ResultPlotStatus = $"Could not read selected result signals: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_resultReadCancellation, cancellation))
            {
                _resultReadCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    public void ReportError(Exception exception, string context)
    {
        IsEngineAvailable = _openModelica.State.Status is OpenModelicaSessionStatus.Ready or OpenModelicaSessionStatus.Busy;
        StatusMessage = context;
        MessagesText = $"{context}: {exception.Message}";
    }

    private void ClearGraphicalSelection()
    {
        SelectedComponents = [];
        SelectedComponent = null;
        SelectedConnection = null;
        GraphicalSelection = null;
    }

    private async Task PrepareGraphicalMutationAsync(CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        if (document.SourcePath is null)
        {
            throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        }

        if (document.IsDirty)
        {
            await SaveActiveDocumentAsync().ConfigureAwait(true);
        }

        if (document.SynchronizationState != CompilerSynchronizationState.Synchronized)
        {
            await SynchronizeDocumentAsync(document, cancellationToken).ConfigureAwait(true);
        }

        if (document.SynchronizationState != CompilerSynchronizationState.Synchronized
            || ActiveModelInstance is null)
        {
            throw new InvalidOperationException("The model could not be synchronized with OpenModelica.");
        }
    }

    private ModelicaComponentInstance FindEditableComponent(string componentName)
    {
        var component = ActiveModelInstance?.Components.FirstOrDefault(item =>
            string.Equals(item.Name, componentName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Component '{componentName}' is no longer present in the model.");
        if (component.IsInherited)
        {
            throw new InvalidOperationException("Inherited component placement must be edited in its declaring class.");
        }

        return component;
    }

    private bool ComponentHasConnections(string componentName) =>
        ActiveModelInstance?.Connections.Any(connection =>
            ModelicaConnectionEndpoint.ReferencesComponent(connection.Left, componentName)
            || ModelicaConnectionEndpoint.ReferencesComponent(connection.Right, componentName)) == true;

    private ModelicaDiagramConnection FindEditableConnection(string left, string right)
    {
        var connection = ActiveModelInstance?.Connections.FirstOrDefault(item =>
            ModelicaConnectionEditor.SameEndpoints(item.Left, item.Right, left, right))
            ?? throw new InvalidOperationException($"Connection '{left}' to '{right}' is no longer present in the model.");
        if (connection.IsInherited)
        {
            throw new InvalidOperationException("Inherited connections must be edited in their declaring class.");
        }

        return connection;
    }

    private async Task ApplyConnectionAdditionAsync(
        string left,
        string right,
        ModelicaGraphicPrimitive line,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        if (ActiveModelInstance?.Connections.Any(connection =>
                ModelicaConnectionEditor.SameEndpoints(connection.Left, connection.Right, left, right)) == true)
        {
            throw new InvalidOperationException($"Connection '{left}' to '{right}' already exists.");
        }

        var mutationApplied = false;
        try
        {
            if (!await _openModelica
                    .AddConnectionAsync(document.ClassName, left, right, line, cancellationToken)
                    .ConfigureAwait(true))
            {
                throw new InvalidOperationException($"OpenModelica rejected connection '{left}' to '{right}'.");
            }

            mutationApplied = true;
            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            var confirmed = refreshed.Connections.FirstOrDefault(connection =>
                !connection.IsInherited
                && ModelicaConnectionEditor.SameEndpoints(connection.Left, connection.Right, left, right));
            if (confirmed?.Line is null || !ConnectionLinesEquivalent(confirmed.Line, line))
            {
                throw new InvalidOperationException(
                    $"OpenModelica did not confirm connection '{left}' to '{right}' with its requested Line annotation.");
            }

            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                selectedComponentName: null,
                $"OpenModelica confirmed and saved connect({left}, {right}).",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
            SelectGraphicalElement(new ModelicaDiagramHit(null, confirmed));
        }
        catch (Exception exception)
        {
            if (!mutationApplied)
            {
                throw;
            }

            try
            {
                var deleted = await _openModelica
                    .DeleteConnectionAsync(document.ClassName, left, right, CancellationToken.None)
                    .ConfigureAwait(true);
                var restored = await _openModelica
                    .GetModelInstanceAsync(document.ClassName, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!deleted || restored.Connections.Any(connection =>
                        !connection.IsInherited
                        && ModelicaConnectionEditor.SameEndpoints(connection.Left, connection.Right, left, right)))
                {
                    throw new InvalidOperationException("OpenModelica rejected the connection-addition rollback.");
                }

                await PersistAndAcceptModelAsync(
                    document,
                    sourcePath,
                    restored,
                    selectedComponentName: null,
                    "The failed connection creation was rolled back.",
                    invalidateSimulation: false,
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Connection creation failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException("Connection creation failed and was rolled back.", exception);
        }
    }

    private async Task ApplyConnectionDeletionAsync(
        string left,
        string right,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        var connection = FindEditableConnection(left, right);
        var sourceBeforeDeletion = document.Source;
        var mutationApplied = false;
        try
        {
            if (!await _openModelica
                    .DeleteConnectionAsync(document.ClassName, connection.Left, connection.Right, cancellationToken)
                    .ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    $"OpenModelica rejected deletion of connection '{connection.Left}' to '{connection.Right}'.");
            }

            mutationApplied = true;
            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            if (refreshed.Connections.Any(item =>
                    !item.IsInherited
                    && ModelicaConnectionEditor.SameEndpoints(
                        item.Left,
                        item.Right,
                        connection.Left,
                        connection.Right)))
            {
                throw new InvalidOperationException(
                    $"OpenModelica did not confirm deletion of connection '{connection.Left}' to '{connection.Right}'.");
            }

            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                selectedComponentName: null,
                $"OpenModelica confirmed and saved deletion of connect({connection.Left}, {connection.Right}).",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (!mutationApplied)
            {
                throw;
            }

            try
            {
                await ApplyConnectionSourceRestoreAsync(
                    sourceBeforeDeletion,
                    connection,
                    CancellationToken.None,
                    invalidateSimulation: false,
                    "The failed connection deletion was rolled back.").ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Connection deletion failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException("Connection deletion failed and was rolled back.", exception);
        }
    }

    private async Task ApplyConnectionRouteAsync(
        string left,
        string right,
        ModelicaGraphicPrimitive line,
        string sourceBeforeRoute,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        var connection = FindEditableConnection(left, right);
        var mutationApplied = false;
        try
        {
            if (!await _openModelica
                    .UpdateConnectionAnnotationAsync(
                        document.ClassName,
                        connection.Left,
                        connection.Right,
                        line,
                        cancellationToken)
                    .ConfigureAwait(true))
            {
                throw new InvalidOperationException(
                    $"OpenModelica rejected the route for connection '{connection.Left}' to '{connection.Right}'.");
            }

            mutationApplied = true;
            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            var confirmed = refreshed.Connections.FirstOrDefault(item =>
                !item.IsInherited
                && ModelicaConnectionEditor.SameEndpoints(
                    item.Left,
                    item.Right,
                    connection.Left,
                    connection.Right));
            if (confirmed?.Line is null || !ConnectionLinesEquivalent(confirmed.Line, line))
            {
                throw new InvalidOperationException(
                    $"OpenModelica did not confirm the requested route for '{connection.Left}' to '{connection.Right}'.");
            }

            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                selectedComponentName: null,
                $"OpenModelica confirmed and saved the route for connect({connection.Left}, {connection.Right}).",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
            SelectGraphicalElement(new ModelicaDiagramHit(null, confirmed));
        }
        catch (Exception exception)
        {
            if (!mutationApplied)
            {
                throw;
            }

            try
            {
                await ApplyConnectionSourceRestoreAsync(
                    sourceBeforeRoute,
                    connection,
                    CancellationToken.None,
                    invalidateSimulation: false,
                    "The failed connection route was rolled back.").ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Connection routing failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException("Connection routing failed and was rolled back.", exception);
        }
    }

    private Task ApplyConnectionSourceRestoreAsync(
        string source,
        ModelicaDiagramConnection expectedConnection,
        CancellationToken cancellationToken) =>
        ApplyConnectionSourceRestoreAsync(
            source,
            expectedConnection,
            cancellationToken,
            invalidateSimulation: true,
            $"OpenModelica restored and saved connect({expectedConnection.Left}, {expectedConnection.Right}).");

    private async Task ApplyConnectionSourceRestoreAsync(
        string source,
        ModelicaDiagramConnection expectedConnection,
        CancellationToken cancellationToken,
        bool invalidateSimulation,
        string message)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        if (!await _openModelica.LoadStringAsync(source, sourcePath, cancellationToken).ConfigureAwait(true))
        {
            throw new InvalidOperationException("OpenModelica rejected the connection source snapshot.");
        }

        var refreshed = await _openModelica
            .GetModelInstanceAsync(document.ClassName, cancellationToken)
            .ConfigureAwait(true);
        var restored = refreshed.Connections.FirstOrDefault(connection =>
            !connection.IsInherited
            && ConnectionsEquivalent(connection, expectedConnection));
        if (restored is null)
        {
            throw new InvalidOperationException(
                $"OpenModelica did not restore connect({expectedConnection.Left}, {expectedConnection.Right}) from the source snapshot.");
        }

        await PersistAndAcceptModelAsync(
            document,
            sourcePath,
            refreshed,
            selectedComponentName: null,
            message,
            invalidateSimulation,
            cancellationToken).ConfigureAwait(true);
        SelectGraphicalElement(new ModelicaDiagramHit(null, restored));
    }

    private async Task ApplyComponentAdditionAsync(
        string componentName,
        string typeName,
        ModelicaPlacement placement,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        if (ActiveModelInstance?.Components.Any(component =>
                string.Equals(component.Name, componentName, StringComparison.Ordinal)) == true)
        {
            throw new InvalidOperationException($"A component named '{componentName}' already exists.");
        }

        var mutationApplied = false;
        try
        {
            if (!await _openModelica
                    .AddComponentAsync(document.ClassName, componentName, typeName, placement, cancellationToken)
                    .ConfigureAwait(true))
            {
                throw new InvalidOperationException($"OpenModelica rejected the addition of '{componentName}'.");
            }

            mutationApplied = true;
            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            var confirmed = refreshed.Components.FirstOrDefault(component =>
                !component.IsInherited
                && string.Equals(component.Name, componentName, StringComparison.Ordinal));
            if (confirmed is null
                || !string.Equals(confirmed.TypeName, typeName, StringComparison.Ordinal)
                || confirmed.Placement is null
                || !PlacementsEquivalent(confirmed.Placement, placement))
            {
                throw new InvalidOperationException(
                    $"OpenModelica did not confirm '{componentName}' with the requested type and placement.");
            }

            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                componentName,
                $"OpenModelica confirmed and saved {typeName} {componentName}.",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (!mutationApplied)
            {
                throw;
            }

            try
            {
                var deleted = await _openModelica
                    .DeleteComponentAsync(document.ClassName, componentName, CancellationToken.None)
                    .ConfigureAwait(true);
                var restored = await _openModelica
                    .GetModelInstanceAsync(document.ClassName, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!deleted || restored.Components.Any(component =>
                        !component.IsInherited
                        && string.Equals(component.Name, componentName, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("OpenModelica rejected the component-addition rollback.");
                }

                await PersistAndAcceptModelAsync(
                    document,
                    sourcePath,
                    restored,
                    selectedComponentName: null,
                    "The failed component addition was rolled back.",
                    invalidateSimulation: false,
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    $"Adding '{componentName}' failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException(
                $"Adding '{componentName}' failed and was rolled back.",
                exception);
        }
    }

    private async Task ApplyComponentDeletionAsync(
        string componentName,
        CancellationToken cancellationToken) =>
        await ApplyComponentDeletionsAsync([componentName], cancellationToken).ConfigureAwait(true);

    private async Task ApplyComponentDeletionsAsync(
        IReadOnlyList<string> componentNames,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        var components = componentNames.Select(FindEditableComponent).ToArray();
        var connected = components.FirstOrDefault(component => ComponentHasConnections(component.Name));
        if (connected is not null)
        {
            throw new InvalidOperationException(
                $"Delete the connections attached to '{connected.Name}' before deleting the component.");
        }

        var sourceBeforeDeletion = document.Source;
        var deletedNames = new List<string>();
        try
        {
            foreach (var component in components)
            {
                if (!await _openModelica
                        .DeleteComponentAsync(document.ClassName, component.Name, cancellationToken)
                        .ConfigureAwait(true))
                {
                    throw new InvalidOperationException($"OpenModelica rejected deletion of '{component.Name}'.");
                }

                deletedNames.Add(component.Name);
            }

            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            var remaining = components.FirstOrDefault(expected => refreshed.Components.Any(item =>
                !item.IsInherited
                && string.Equals(item.Name, expected.Name, StringComparison.Ordinal)));
            if (remaining is not null)
            {
                throw new InvalidOperationException($"OpenModelica did not confirm deletion of '{remaining.Name}'.");
            }

            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                selectedComponentName: null,
                components.Length == 1
                    ? $"OpenModelica confirmed and saved deletion of {components[0].Name}."
                    : $"OpenModelica confirmed and saved deletion of {components.Length} components.",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (deletedNames.Count == 0)
            {
                throw;
            }

            try
            {
                await ReplaceClassSourceCoreAsync(
                    document,
                    sourcePath,
                    sourceBeforeDeletion,
                    requiredComponents: components,
                    forbiddenComponentNames: [],
                    selectedComponentNames: components.Select(static component => component.Name).ToArray(),
                    invalidateSimulation: false,
                    "The failed component deletion was rolled back.",
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Deleting the selected component(s) failed and the compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException(
                "Deleting the selected component(s) failed and was rolled back.",
                exception);
        }
    }

    private async Task ApplyClassSourceRestoreAsync(
        string source,
        IReadOnlyList<ModelicaComponentInstance> expectedComponents,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        var currentSource = document.Source;
        var mutationApplied = false;
        try
        {
            if (!await _openModelica.LoadStringAsync(source, sourcePath, cancellationToken).ConfigureAwait(true))
            {
                throw new InvalidOperationException("OpenModelica rejected the source snapshot used to undo deletion.");
            }

            mutationApplied = true;
            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            var missing = expectedComponents.FirstOrDefault(expected =>
                !refreshed.Components.Any(component => ComponentsEquivalent(component, expected)));
            if (missing is not null)
            {
                throw new InvalidOperationException(
                    $"OpenModelica did not restore '{missing.Name}' from the deletion snapshot.");
            }

            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                selectedComponentName: null,
                expectedComponents.Count == 1
                    ? $"OpenModelica restored and saved {expectedComponents[0].Name}."
                    : $"OpenModelica restored and saved {expectedComponents.Count} components.",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
            var restoredComponents = expectedComponents
                .Select(expected => refreshed.Components.Single(component =>
                    ComponentsEquivalent(component, expected)))
                .ToArray();
            SelectGraphicalElements(
                new ModelicaDiagramHit(restoredComponents.Last(), null),
                restoredComponents);
        }
        catch (Exception exception)
        {
            if (!mutationApplied)
            {
                throw;
            }

            try
            {
                await ReplaceClassSourceCoreAsync(
                    document,
                    sourcePath,
                    currentSource,
                    requiredComponents: [],
                    forbiddenComponentNames: expectedComponents.Select(static component => component.Name).ToArray(),
                    selectedComponentNames: [],
                    invalidateSimulation: false,
                    "The failed source restoration was rolled back.",
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Restoring the deleted component(s) failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException(
                "Restoring the deleted component(s) failed and was rolled back.",
                exception);
        }
    }

    private async Task ReplaceClassSourceCoreAsync(
        ModelDocument document,
        string sourcePath,
        string source,
        IReadOnlyList<ModelicaComponentInstance> requiredComponents,
        IReadOnlyList<string> forbiddenComponentNames,
        IReadOnlyList<string> selectedComponentNames,
        bool invalidateSimulation,
        string message,
        CancellationToken cancellationToken)
    {
        if (!await _openModelica.LoadStringAsync(source, sourcePath, cancellationToken).ConfigureAwait(true))
        {
            throw new InvalidOperationException("OpenModelica rejected the class source snapshot.");
        }

        var refreshed = await _openModelica
            .GetModelInstanceAsync(document.ClassName, cancellationToken)
            .ConfigureAwait(true);
        var missing = requiredComponents.FirstOrDefault(required =>
            !refreshed.Components.Any(component => ComponentsEquivalent(component, required)));
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"OpenModelica did not restore '{missing.Name}' from the source snapshot.");
        }

        var unexpectedlyPresent = forbiddenComponentNames.FirstOrDefault(forbiddenName =>
            refreshed.Components.Any(component =>
                !component.IsInherited
                && string.Equals(component.Name, forbiddenName, StringComparison.Ordinal)));
        if (unexpectedlyPresent is not null)
        {
            throw new InvalidOperationException(
                $"OpenModelica did not restore the component-free source snapshot for '{unexpectedlyPresent}'.");
        }

        await PersistAndAcceptModelAsync(
            document,
            sourcePath,
            refreshed,
            selectedComponentName: null,
            message,
            invalidateSimulation,
            cancellationToken).ConfigureAwait(true);
        var selectedComponents = selectedComponentNames
            .Select(name => refreshed.Components.FirstOrDefault(component =>
                !component.IsInherited && string.Equals(component.Name, name, StringComparison.Ordinal)))
            .OfType<ModelicaComponentInstance>()
            .ToArray();
        if (selectedComponents.Length > 0)
        {
            SelectGraphicalElements(
                new ModelicaDiagramHit(selectedComponents.Last(), null),
                selectedComponents);
        }
    }

    private async Task PersistAndAcceptModelAsync(
        ModelDocument document,
        string sourcePath,
        ModelicaModelInstanceSnapshot refreshed,
        string? selectedComponentName,
        string message,
        bool invalidateSimulation,
        CancellationToken cancellationToken)
    {
        if (!await _openModelica.SaveClassAsync(document.ClassName, cancellationToken).ConfigureAwait(true))
        {
            throw new InvalidOperationException($"OpenModelica could not persist '{document.ClassName}'.");
        }

        var reloaded = await _documentService.OpenAsync(sourcePath, cancellationToken).ConfigureAwait(true);
        if (reloaded.SynchronizationState == CompilerSynchronizationState.InvalidSource)
        {
            throw new InvalidOperationException("OpenModelica saved source that could not be recognized as a Modelica class.");
        }

        if (!ReferenceEquals(document, ActiveDocument))
        {
            throw new InvalidOperationException("The active document changed while the graphical edit was being saved.");
        }

        document.AcceptCompilerSource(reloaded.Source, refreshed.ClassName);
        _sourceText = document.Source;
        AnalyzeSourceText();
        OnPropertyChanged(nameof(SourceText));
        ActiveModelInstance = refreshed;
        ActiveGraphicalAnnotation = refreshed.ClassGraphics;
        if (invalidateSimulation)
        {
            InvalidateSimulationResultsAfterModelEdit();
        }

        if (selectedComponentName is not null
            && refreshed.Components.FirstOrDefault(component =>
                !component.IsInherited
                && string.Equals(component.Name, selectedComponentName, StringComparison.Ordinal)) is { } selected)
        {
            SelectGraphicalElement(new ModelicaDiagramHit(selected, null));
        }

        NotifyDocumentStateChanged();
        MessagesText = message;
    }

    private async Task ApplyComponentPlacementsAsync(
        IReadOnlyDictionary<string, ModelicaPlacement> requestedPlacements,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        var currentPlacements = requestedPlacements.Keys.ToDictionary(
            static componentName => componentName,
            componentName => FindEditableComponent(componentName).Placement
                ?? throw new InvalidOperationException(
                    $"Component '{componentName}' has no editable Placement annotation."),
            StringComparer.Ordinal);
        var appliedComponents = new List<string>();
        try
        {
            foreach (var requested in requestedPlacements)
            {
                if (!await _openModelica
                        .SetComponentPlacementAsync(
                            document.ClassName,
                            requested.Key,
                            requested.Value,
                            cancellationToken)
                        .ConfigureAwait(true))
                {
                    throw new InvalidOperationException(
                        $"OpenModelica rejected the placement change for '{requested.Key}'.");
                }

                appliedComponents.Add(requested.Key);
            }

            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            foreach (var requested in requestedPlacements)
            {
                var confirmed = refreshed.Components.FirstOrDefault(item =>
                    !item.IsInherited
                    && string.Equals(item.Name, requested.Key, StringComparison.Ordinal));
                if (confirmed?.Placement is null || !PlacementsEquivalent(confirmed.Placement, requested.Value))
                {
                    throw new InvalidOperationException(
                        $"OpenModelica did not confirm the requested placement for '{requested.Key}'.");
                }
            }

            if (!await _openModelica.SaveClassAsync(document.ClassName, cancellationToken).ConfigureAwait(true))
            {
                throw new InvalidOperationException($"OpenModelica could not persist '{document.ClassName}'.");
            }

            var reloaded = await _documentService.OpenAsync(sourcePath, cancellationToken).ConfigureAwait(true);
            if (reloaded.SynchronizationState == CompilerSynchronizationState.InvalidSource)
            {
                throw new InvalidOperationException("OpenModelica saved source that could not be recognized as a Modelica class.");
            }

            if (!ReferenceEquals(document, ActiveDocument))
            {
                throw new InvalidOperationException("The active document changed while the placement was being saved.");
            }

            document.AcceptCompilerSource(reloaded.Source, refreshed.ClassName);
            _sourceText = document.Source;
            AnalyzeSourceText();
            OnPropertyChanged(nameof(SourceText));
            ActiveModelInstance = refreshed;
            ActiveGraphicalAnnotation = refreshed.ClassGraphics;
            InvalidateSimulationResultsAfterModelEdit();
            var confirmedComponents = requestedPlacements.Keys
                .Select(name => refreshed.Components.Single(component =>
                    !component.IsInherited && string.Equals(component.Name, name, StringComparison.Ordinal)))
                .ToArray();
            SelectGraphicalElements(
                new ModelicaDiagramHit(confirmedComponents.Last(), null),
                confirmedComponents);
            NotifyDocumentStateChanged();
            MessagesText = requestedPlacements.Count == 1
                ? $"OpenModelica confirmed and saved the Placement annotation for {requestedPlacements.Keys.Single()}."
                : $"OpenModelica confirmed and saved Placement annotations for {requestedPlacements.Count} components.";
        }
        catch (Exception exception)
        {
            if (appliedComponents.Count == 0)
            {
                throw;
            }

            try
            {
                foreach (var componentName in appliedComponents.AsEnumerable().Reverse())
                {
                    if (!await _openModelica
                            .SetComponentPlacementAsync(
                                document.ClassName,
                                componentName,
                                currentPlacements[componentName],
                                CancellationToken.None)
                            .ConfigureAwait(true))
                    {
                        throw new InvalidOperationException(
                            $"OpenModelica rejected the placement rollback for '{componentName}'.");
                    }
                }

                if (!await _openModelica.SaveClassAsync(document.ClassName, CancellationToken.None).ConfigureAwait(true))
                {
                    throw new InvalidOperationException("OpenModelica rejected the placement rollback.");
                }
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "The placement change failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException(
                "The placement change failed and was rolled back.",
                exception);
        }
    }

    private async Task ApplyComponentDuplicationAsync(
        ComponentDuplicationState state,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        var mutationApplied = false;
        try
        {
            if (!await _openModelica
                    .LoadClassContentStringAsync(
                        state.Content,
                        document.ClassName,
                        state.OffsetX,
                        state.OffsetY,
                        cancellationToken)
                    .ConfigureAwait(true))
            {
                throw new InvalidOperationException("OpenModelica rejected the exact component copy payload.");
            }

            mutationApplied = true;
            var refreshed = await _openModelica
                .GetModelInstanceAsync(document.ClassName, cancellationToken)
                .ConfigureAwait(true);
            var added = refreshed.Components.Where(component =>
                    !component.IsInherited && !state.OriginalComponentNames.Contains(component.Name))
                .ToArray();
            if (added.Length != state.SourceComponents.Count
                || !HaveEquivalentTypeCounts(added, state.SourceComponents)
                || !HaveEquivalentOffsetPlacements(
                    added,
                    state.SourceComponents,
                    state.OffsetX,
                    state.OffsetY))
            {
                throw new InvalidOperationException(
                    $"OpenModelica created {added.Length} component(s), but {state.SourceComponents.Count} exact copies were expected.");
            }

            var addedConnections = refreshed.Connections.Count(connection =>
                !connection.IsInherited && !state.OriginalConnectionIdentities.Contains(ConnectionIdentity(connection)));
            var selectedNames = state.SourceComponents.Select(static component => component.Name)
                .ToHashSet(StringComparer.Ordinal);
            var expectedConnections = ActiveModelInstance?.Connections.Count(connection =>
                !connection.IsInherited
                && selectedNames.Any(name => ModelicaConnectionEndpoint.ReferencesComponent(connection.Left, name))
                && selectedNames.Any(name => ModelicaConnectionEndpoint.ReferencesComponent(connection.Right, name))) ?? 0;
            if (addedConnections != expectedConnections)
            {
                throw new InvalidOperationException(
                    $"OpenModelica created {addedConnections} internal connection(s), but {expectedConnections} were expected.");
            }

            state.NewComponentNames = added.Select(static component => component.Name).ToArray();
            await PersistAndAcceptModelAsync(
                document,
                sourcePath,
                refreshed,
                selectedComponentName: null,
                added.Length == 1
                    ? $"OpenModelica duplicated and saved {added[0].Name}."
                    : $"OpenModelica duplicated and saved {added.Length} components.",
                invalidateSimulation: true,
                cancellationToken).ConfigureAwait(true);
            SelectGraphicalElements(new ModelicaDiagramHit(added.Last(), null), added);
        }
        catch (Exception exception)
        {
            if (!mutationApplied)
            {
                throw;
            }

            try
            {
                await ReplaceClassSourceCoreAsync(
                    document,
                    sourcePath,
                    state.SourceBefore,
                    state.SourceComponents,
                    state.NewComponentNames,
                    state.SourceComponents.Select(static component => component.Name).ToArray(),
                    invalidateSimulation: false,
                    "The failed duplication was rolled back.",
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Component duplication failed and its compiler rollback also failed.",
                    new AggregateException(exception, rollbackException));
            }

            throw new InvalidOperationException("Component duplication failed and was rolled back.", exception);
        }
    }

    private async Task UndoComponentDuplicationAsync(
        ComponentDuplicationState state,
        CancellationToken cancellationToken)
    {
        var document = ActiveDocument
            ?? throw new InvalidOperationException("There is no active Modelica document.");
        var sourcePath = document.SourcePath
            ?? throw new InvalidOperationException("Save the Modelica document before editing its diagram.");
        await ReplaceClassSourceCoreAsync(
            document,
            sourcePath,
            state.SourceBefore,
            state.SourceComponents,
            state.NewComponentNames,
            state.SourceComponents.Select(static component => component.Name).ToArray(),
            invalidateSimulation: true,
            state.SourceComponents.Count == 1
                ? $"OpenModelica removed the duplicate of {state.SourceComponents[0].Name}."
                : $"OpenModelica removed {state.SourceComponents.Count} duplicated components.",
            cancellationToken).ConfigureAwait(true);
    }

    private static bool HaveEquivalentTypeCounts(
        IReadOnlyList<ModelicaComponentInstance> first,
        IReadOnlyList<ModelicaComponentInstance> second) =>
        first.GroupBy(static component => component.TypeName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(second.GroupBy(static component => component.TypeName, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal)
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal));

    private static bool HaveEquivalentOffsetPlacements(
        IReadOnlyList<ModelicaComponentInstance> added,
        IReadOnlyList<ModelicaComponentInstance> source,
        double offsetX,
        double offsetY)
    {
        var remaining = added.ToList();
        foreach (var sourceComponent in source)
        {
            var expectedPlacement = sourceComponent.Placement is { } placement
                ? OffsetPlacement(placement, offsetX, offsetY)
                : null;
            var match = remaining.FindIndex(component =>
                string.Equals(component.TypeName, sourceComponent.TypeName, StringComparison.Ordinal)
                && OptionalPlacementsEquivalent(component.Placement, expectedPlacement));
            if (match < 0)
            {
                return false;
            }

            remaining.RemoveAt(match);
        }

        return remaining.Count == 0;
    }

    private static ModelicaPlacement OffsetPlacement(
        ModelicaPlacement placement,
        double offsetX,
        double offsetY) =>
        placement with
        {
            Transformation = OffsetTransformation(placement.Transformation, offsetX, offsetY),
            IconTransformation = placement.IconTransformation is { } iconTransformation
                ? OffsetTransformation(iconTransformation, offsetX, offsetY)
                : null,
        };

    private static ModelicaTransformation OffsetTransformation(
        ModelicaTransformation transformation,
        double offsetX,
        double offsetY) =>
        transformation with
        {
            Origin = new ModelicaPoint(
                transformation.Origin.X + offsetX,
                transformation.Origin.Y + offsetY),
        };

    private static string ConnectionIdentity(ModelicaDiagramConnection connection) =>
        $"{connection.Left}\u001f{connection.Right}";

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
                LineColor = new ModelicaColor(0, 0, 127),
                LinePattern = ModelicaLinePattern.Solid,
                LineThickness = 0.25,
            },
            Arrows = [ModelicaArrow.None, ModelicaArrow.None],
            ArrowSize = 3,
            Smooth = ModelicaSmooth.None,
            RawJson = "{}",
        };

    private static bool ConnectionsEquivalent(
        ModelicaDiagramConnection first,
        ModelicaDiagramConnection second) =>
        ModelicaConnectionEditor.SameEndpoints(first.Left, first.Right, second.Left, second.Right)
        && (first.Line, second.Line) switch
        {
            (null, null) => true,
            ({ } firstLine, { } secondLine) => ConnectionLinesEquivalent(firstLine, secondLine),
            _ => false,
        };

    private static bool ConnectionLinesEquivalent(
        ModelicaGraphicPrimitive first,
        ModelicaGraphicPrimitive second) =>
        first.Kind == second.Kind
        && first.Visible.StaticValue == second.Visible.StaticValue
        && PointsEquivalent(first.Origin.StaticValue, second.Origin.StaticValue)
        && NearlyEqual(first.Rotation.StaticValue, second.Rotation.StaticValue)
        && first.Points.Count == second.Points.Count
        && first.Points.Zip(second.Points).All(pair => PointsEquivalent(pair.First, pair.Second))
        && first.Style.LineColor == second.Style.LineColor
        && first.Style.LinePattern == second.Style.LinePattern
        && NearlyEqual(first.Style.LineThickness, second.Style.LineThickness)
        && first.Arrows.SequenceEqual(second.Arrows)
        && NearlyEqual(first.ArrowSize, second.ArrowSize)
        && first.Smooth == second.Smooth;

    private void InvalidateSimulationResultsAfterModelEdit()
    {
        LastSimulationResult = null;
        SimulationVariables.Clear();
        ResultSeries.Clear();
        OnPropertyChanged(nameof(HasResultSeries));
        ResultPlotStatus = "Run the updated model to produce new result signals.";
        SimulationStatus = "Model changed · simulation results invalidated";
        SimulationOutput = "The prior simulation result belonged to an earlier model revision.";
    }

    private void NotifyGraphicalEditStateChanged()
    {
        OnPropertyChanged(nameof(CanUndoGraphicalEdit));
        OnPropertyChanged(nameof(CanRedoGraphicalEdit));
        OnPropertyChanged(nameof(UndoGraphicalLabel));
        OnPropertyChanged(nameof(RedoGraphicalLabel));
        UndoGraphicalCommand.NotifyCanExecuteChanged();
        RedoGraphicalCommand.NotifyCanExecuteChanged();
    }

    private static bool PlacementsEquivalent(ModelicaPlacement first, ModelicaPlacement second) =>
        first.Visible == second.Visible
        && first.IconVisible == second.IconVisible
        && TransformationsEquivalent(first.Transformation, second.Transformation)
        && (first.IconTransformation, second.IconTransformation) switch
        {
            (null, null) => true,
            ({ } firstIcon, { } secondIcon) => TransformationsEquivalent(firstIcon, secondIcon),
            _ => false,
        };

    private sealed class ComponentDuplicationState(
        string sourceBefore,
        string content,
        IReadOnlyList<ModelicaComponentInstance> sourceComponents,
        IReadOnlySet<string> originalComponentNames,
        IReadOnlySet<string> originalConnectionIdentities,
        int offsetX,
        int offsetY)
    {
        public string SourceBefore { get; } = sourceBefore;
        public string Content { get; } = content;
        public IReadOnlyList<ModelicaComponentInstance> SourceComponents { get; } = sourceComponents;
        public IReadOnlySet<string> OriginalComponentNames { get; } = originalComponentNames;
        public IReadOnlySet<string> OriginalConnectionIdentities { get; } = originalConnectionIdentities;
        public int OffsetX { get; } = offsetX;
        public int OffsetY { get; } = offsetY;
        public IReadOnlyList<string> NewComponentNames { get; set; } = [];
    }

    private static bool OptionalPlacementsEquivalent(ModelicaPlacement? first, ModelicaPlacement? second) =>
        first is null && second is null
        || first is not null && second is not null && PlacementsEquivalent(first, second);

    private static bool ComponentsEquivalent(
        ModelicaComponentInstance first,
        ModelicaComponentInstance second) =>
        !first.IsInherited
        && string.Equals(first.Name, second.Name, StringComparison.Ordinal)
        && string.Equals(first.TypeName, second.TypeName, StringComparison.Ordinal)
        && OptionalPlacementsEquivalent(first.Placement, second.Placement);

    private static bool TransformationsEquivalent(ModelicaTransformation first, ModelicaTransformation second) =>
        PointsEquivalent(first.Origin, second.Origin)
        && PointsEquivalent(first.Extent.First, second.Extent.First)
        && PointsEquivalent(first.Extent.Second, second.Extent.Second)
        && NearlyEqual(first.Rotation, second.Rotation);

    private static bool PointsEquivalent(ModelicaPoint first, ModelicaPoint second) =>
        NearlyEqual(first.X, second.X) && NearlyEqual(first.Y, second.Y);

    private static bool NearlyEqual(double first, double second) => Math.Abs(first - second) <= 1e-9;

    public ValueTask DisposeAsync()
    {
        _simulationCancellation?.Cancel();
        _resultReadCancellation?.Cancel();
        _openModelica.StateChanged -= HandleEngineStateChanged;
        return ValueTask.CompletedTask;
    }

    private async Task SynchronizeDocumentAsync(
        ModelDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document.SourcePath is null)
        {
            return;
        }

        if (await _openModelica.LoadFileAsync(document.SourcePath, cancellationToken).ConfigureAwait(true))
        {
            document.MarkSynchronized(document.ClassName);
            try
            {
                var instance = await _openModelica
                    .GetModelInstanceAsync(document.ClassName, cancellationToken)
                    .ConfigureAwait(true);
                if (ReferenceEquals(ActiveDocument, document))
                {
                    ActiveModelInstance = instance;
                    ActiveGraphicalAnnotation = instance.ClassGraphics;
                }

                if (instance.Issues.Count > 0)
                {
                    MessagesText = string.Join(
                        Environment.NewLine,
                        instance.Issues.Select(static issue => $"Model instance: {issue.Message}"));
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(ActiveDocument, document))
                {
                    ActiveModelInstance = null;
                    ActiveGraphicalAnnotation = null;
                }

                MessagesText = $"The source loaded, but graphical annotations could not be read: {exception.Message}";
            }

            NotifyDocumentStateChanged();
        }
        else
        {
            document.MarkInvalidSource();
            MessagesText = "OpenModelica could not load the document. The edited source has been preserved.";
        }
    }

    private LibraryNodeViewModel CreateLibraryNode(ModelicaClassInfo model) =>
        new(
            model,
            (className, cancellationToken) => _openModelica.GetClassNamesAsync(className, cancellationToken),
            exception => ReportError(exception, "Could not expand the Modelica library"));

    private void ApplyLibraryFilter()
    {
        VisibleLibraryClasses.Clear();
        var query = LibrarySearchText.Trim();
        foreach (var node in LibraryClasses.Where(node =>
                     query.Length == 0
                     || node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || node.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleLibraryClasses.Add(node);
        }
    }

    private void AnalyzeSourceText()
    {
        SourceAnalysis = string.IsNullOrEmpty(_sourceText)
            ? ModelicaSourceAnalysis.Empty
            : _sourceAnalyzer.Analyze(_sourceText);
    }

    private async Task AddDocumentToProjectAsync(ModelDocument document)
    {
        if (CurrentProject is null || document.SourcePath is null)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(CurrentProject.ProjectDirectory, document.SourcePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return;
        }

        relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var modelFiles = CurrentProject.Project.ModelFiles
            .Append(relativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fullClassName = string.IsNullOrWhiteSpace(CurrentProject.Project.RootPackage)
            ? document.ClassName
            : $"{CurrentProject.Project.RootPackage}.{document.ClassName}";
        CurrentProject = CurrentProject with
        {
            Project = CurrentProject.Project with
            {
                ModelFiles = modelFiles,
                RecentlyOpenedModels = CurrentProject.Project.RecentlyOpenedModels
                    .Prepend(fullClassName)
                    .Distinct(StringComparer.Ordinal)
                    .Take(20)
                    .ToArray(),
                UiState = CurrentProject.Project.UiState with
                {
                    ActiveModel = fullClassName,
                    OpenModels = [fullClassName],
                },
            },
        };
        await _projectService.SaveAsync(CurrentProject).ConfigureAwait(true);
    }

    private void NotifyDocumentStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveDocument));
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(ActiveModelName));
        OnPropertyChanged(nameof(DocumentTabTitle));
        OnPropertyChanged(nameof(DocumentPath));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasNoProblems));
        OnPropertyChanged(nameof(ProblemsAreStale));
        OnPropertyChanged(nameof(ProblemsTabTitle));
        OnPropertyChanged(nameof(ProblemsStateText));
        OnPropertyChanged(nameof(CanSimulate));
        OnPropertyChanged(nameof(CanEditGraphically));
        OnPropertyChanged(nameof(CanMoveSelectedComponent));
        OnPropertyChanged(nameof(CanInsertLibraryComponent));
        OnPropertyChanged(nameof(CanDeleteSelectedComponent));
        OnPropertyChanged(nameof(CanDeleteSelectedConnection));
        OnPropertyChanged(nameof(CanDeleteGraphicalSelection));
        OnPropertyChanged(nameof(CanUseConnectionTool));
        SaveCommand.NotifyCanExecuteChanged();
        CheckModelCommand.NotifyCanExecuteChanged();
        SimulateCommand.NotifyCanExecuteChanged();
        ShowTextCommand.NotifyCanExecuteChanged();
    }

    private SimulationConfiguration LoadSimulationConfiguration(ModelDocument? document)
    {
        if (document is not null
            && CurrentProject?.Project.SimulationConfigurations.TryGetValue(
                document.ClassName,
                out var configuration) == true)
        {
            return configuration;
        }

        return new SimulationConfiguration();
    }

    private static string FormatDiagnostics(IEnumerable<CompilerDiagnostic> diagnostics) => string.Join(
        Environment.NewLine,
        diagnostics.Select(static diagnostic =>
            $"{diagnostic.Severity}: {diagnostic.Message} ({diagnostic.File}:{diagnostic.Line}:{diagnostic.Column})"));

    private static readonly SKColor[] PlotColors =
    [
        new(49, 83, 112),
        new(157, 76, 61),
        new(69, 116, 93),
        new(132, 96, 138),
        new(170, 117, 37),
        new(53, 126, 139),
    ];

    private bool DiagnosticTargetsActiveDocument(CompilerDiagnostic diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic.File) || diagnostic.File == "<interactive>")
        {
            return true;
        }

        if (ActiveDocument?.SourcePath is not { } sourcePath)
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(diagnostic.File),
                Path.GetFullPath(sourcePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void HandleEngineStateChanged(object? sender, OpenModelicaStateChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = eventArgs.State.Detail ?? eventArgs.State.Status.ToString();
            if (eventArgs.State.Status == OpenModelicaSessionStatus.Faulted)
            {
                IsEngineAvailable = false;
                EngineStatus = "Engine stopped";
                EngineDetail = eventArgs.State.Detail ?? "OpenModelica terminated unexpectedly.";
            }
        });
    }
}
