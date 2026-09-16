using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ModelicaStudio.Domain;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.UI.Controls;
using ModelicaStudio.UI.ViewModels;

namespace ModelicaStudio.UI.Views;

public sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType ModelicaFileType = new("Modelica source")
    {
        Patterns = ["*.mo"],
        AppleUniformTypeIdentifiers = ["public.source-code"],
        MimeTypes = ["text/plain"],
    };
    private static readonly FilePickerFileType ProjectFileType = new("Modelica Studio project")
    {
        Patterns = ["*.modelicaproj"],
        MimeTypes = ["application/json"],
    };
    private MainWindowViewModel? _viewModel;
    private bool _allowClose;
    private bool _isClosePromptOpen;
    private bool _isSynchronizingSourceEditor;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        SourceEditor.TextChanged += HandleSourceEditorTextChanged;
        SynchronizeSourceEditor();
        SynchronizeDiagnostics();
        Opened += HandleOpened;
        Closing += HandleClosing;
        Closed += HandleClosed;
    }

    private async void HandleOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= HandleOpened;
        if (_viewModel is not null)
        {
            await _viewModel.InitializeEngineAsync().ConfigureAwait(true);
        }
    }

    private async void HandleClosed(object? sender, EventArgs eventArgs)
    {
        Closing -= HandleClosing;
        Closed -= HandleClosed;
        SourceEditor.TextChanged -= HandleSourceEditorTextChanged;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
            await _viewModel.DisposeAsync().ConfigureAwait(true);
        }
    }

    private async void HandleNewProject(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create Modelica Studio Project",
            SuggestedFileName = "NewProject" + ProductInfo.ProjectExtension,
            DefaultExtension = ProductInfo.ProjectExtension.TrimStart('.'),
            FileTypeChoices = [ProjectFileType],
            ShowOverwritePrompt = true,
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        if (!await ConfirmDocumentReplacementAsync().ConfigureAwait(true))
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.CreateProjectAsync(path, Path.GetFileNameWithoutExtension(path)),
            "Could not create project").ConfigureAwait(true);
    }

    private async void HandleOpenProject(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Modelica Studio Project",
            AllowMultiple = false,
            FileTypeFilter = [ProjectFileType],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null && await ConfirmDocumentReplacementAsync().ConfigureAwait(true))
        {
            await RunUiActionAsync(() => _viewModel.OpenProjectAsync(path), "Could not open project").ConfigureAwait(true);
        }
    }

    private async void HandleOpenModelicaFile(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Modelica Source",
            AllowMultiple = false,
            FileTypeFilter = [ModelicaFileType],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null && await ConfirmDocumentReplacementAsync().ConfigureAwait(true))
        {
            await RunUiActionAsync(() => _viewModel.OpenModelicaFileAsync(path), "Could not open Modelica source").ConfigureAwait(true);
        }
    }

    private async void HandleNewClass(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        var initialType = sender is MenuItem { Tag: "Package" }
            ? ModelicaClassType.Package
            : ModelicaClassType.Model;
        var dialog = new NewClassDialog(initialType, _viewModel.CurrentProject?.Project.RootPackage);
        var definition = await dialog.ShowDialog<NewModelicaClass?>(this);
        if (definition is not null && await ConfirmDocumentReplacementAsync().ConfigureAwait(true))
        {
            await RunUiActionAsync(() => _viewModel.CreateClassAsync(definition), "Could not create Modelica class").ConfigureAwait(true);
        }
    }

    private async void HandleSave(object? sender, RoutedEventArgs eventArgs)
    {
        await TrySaveActiveDocumentAsync().ConfigureAwait(true);
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainWindowViewModel.SourceText))
        {
            SynchronizeSourceEditor();
        }

        if (eventArgs.PropertyName is nameof(MainWindowViewModel.Problems)
            or nameof(MainWindowViewModel.ProblemsAreStale)
            or nameof(MainWindowViewModel.ActiveDocument))
        {
            SynchronizeDiagnostics();
        }
    }

    private void HandleSourceEditorTextChanged(object? sender, EventArgs eventArgs)
    {
        if (_viewModel is null || _isSynchronizingSourceEditor)
        {
            return;
        }

        _viewModel.SourceText = SourceEditor.Text ?? string.Empty;
    }

    private void SynchronizeSourceEditor()
    {
        if (_viewModel is null || string.Equals(SourceEditor.Text, _viewModel.SourceText, StringComparison.Ordinal))
        {
            return;
        }

        _isSynchronizingSourceEditor = true;
        try
        {
            SourceEditor.SourceText = _viewModel.SourceText;
        }
        finally
        {
            _isSynchronizingSourceEditor = false;
        }
    }

    private async void HandleUndo(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.IsGraphicalView == true)
        {
            await RunUiActionAsync(
                () => _viewModel.UndoGraphicalEditAsync(),
                "Could not undo graphical edit").ConfigureAwait(true);
        }
        else if (_viewModel?.IsTextView == true && SourceEditor.CanUndo)
        {
            SourceEditor.Undo();
        }
    }

    private async void HandleRedo(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.IsGraphicalView == true)
        {
            await RunUiActionAsync(
                () => _viewModel.RedoGraphicalEditAsync(),
                "Could not redo graphical edit").ConfigureAwait(true);
        }
        else if (_viewModel?.IsTextView == true && SourceEditor.CanRedo)
        {
            SourceEditor.Redo();
        }
    }

    private void HandleCut(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.IsTextView == true)
        {
            SourceEditor.Cut();
        }
    }

    private void HandleCopy(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.IsTextView == true)
        {
            SourceEditor.Copy();
        }
    }

    private void HandlePaste(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.IsTextView == true)
        {
            SourceEditor.Paste();
        }
    }

    private void HandleFind(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.IsTextView == true)
        {
            SourceEditor.OpenSearch();
        }
    }

    private async void HandleSimulationSetup(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.ActiveDocument is null)
        {
            return;
        }

        var decision = await new SimulationSetupDialog(_viewModel.ActiveSimulationConfiguration)
            .ShowDialog<SimulationSetupDecision?>(this)
            .ConfigureAwait(true);
        if (decision is null)
        {
            return;
        }

        await RunUiActionAsync(
            async () =>
            {
                if (decision.RunImmediately)
                {
                    await _viewModel.RunSimulationAsync(decision.Configuration).ConfigureAwait(true);
                }
                else
                {
                    await _viewModel.ApplySimulationConfigurationAsync(decision.Configuration).ConfigureAwait(true);
                }
            },
            "Could not apply simulation setup").ConfigureAwait(true);
    }

    private async void HandleSimulationVariableSelection(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_viewModel is null || sender is not ListBox listBox)
        {
            return;
        }

        await _viewModel
            .LoadSimulationSeriesAsync(listBox.SelectedItems?.OfType<string>() ?? [])
            .ConfigureAwait(true);
    }

    private void HandleGraphicalSelectionChanged(
        object? sender,
        ModelicaGraphicsSelectionChangedEventArgs eventArgs) =>
        _viewModel?.SelectGraphicalElements(eventArgs.Selection, eventArgs.Components);

    private void HandleToggleConnectionTool(object? sender, RoutedEventArgs eventArgs) =>
        _viewModel?.ToggleConnectionTool();

    private async void HandleConnectionCreationRequested(
        object? sender,
        ModelicaConnectionCreationRequestedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.AddConnectionAsync(eventArgs.Left, eventArgs.Right, eventArgs.Route),
            $"Could not connect {eventArgs.Left.Path} to {eventArgs.Right.Path}").ConfigureAwait(true);
    }

    private async void HandleConnectionRouteRequested(
        object? sender,
        ModelicaConnectionRouteRequestedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.RerouteConnectionAsync(
                eventArgs.Connection.Left,
                eventArgs.Connection.Right,
                eventArgs.Route),
            $"Could not reroute {eventArgs.Connection.Left} to {eventArgs.Connection.Right}").ConfigureAwait(true);
    }

    private async void HandleConnectionDeletionRequested(
        object? sender,
        ModelicaConnectionDeletionRequestedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.DeleteSelectedConnectionAsync(),
            $"Could not delete {eventArgs.Connection.Left} to {eventArgs.Connection.Right}").ConfigureAwait(true);
    }

    private async void HandleComponentPlacementRequested(
        object? sender,
        ModelicaComponentPlacementRequestedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.EditComponentPlacementsAsync(
                eventArgs.Requests.Select(static request =>
                    new ModelicaComponentTransformationEdit(
                        request.Component.Name,
                        request.Transformation)).ToArray(),
                eventArgs.IconLayer,
                eventArgs.Kind),
            $"Could not {eventArgs.Kind.ToString().ToLowerInvariant()} the selected component(s)").ConfigureAwait(true);
    }

    private async void HandleRotateSelectedCounterClockwise(object? sender, RoutedEventArgs eventArgs) =>
        await RotateSelectedComponentAsync(-90).ConfigureAwait(true);

    private async void HandleRotateSelectedClockwise(object? sender, RoutedEventArgs eventArgs) =>
        await RotateSelectedComponentAsync(90).ConfigureAwait(true);

    private async Task RotateSelectedComponentAsync(double deltaDegrees)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.RotateSelectedComponentAsync(deltaDegrees),
            "Could not rotate the selected component").ConfigureAwait(true);
    }

    private async void HandleLibraryDragPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_viewModel?.CanInsertLibraryComponent != true
            || sender is not Control
            {
                DataContext: LibraryNodeViewModel
                {
                    CanInstantiate: true,
                    Model: { } componentType,
                },
            } control
            || !eventArgs.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(ModelicaDragData.LibraryClassFormat, componentType));
        eventArgs.Handled = true;
        try
        {
            await DragDrop
                .DoDragDropAsync(eventArgs, dataTransfer, DragDropEffects.Copy)
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError(exception, $"Could not drag {componentType.FullName}");
        }
    }

    private async void HandleComponentDropRequested(
        object? sender,
        ModelicaComponentDropRequestedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.AddComponentAsync(eventArgs.ComponentType, eventArgs.Origin),
            $"Could not add {eventArgs.ComponentType.FullName}").ConfigureAwait(true);
    }

    private async void HandleComponentDeletionRequested(
        object? sender,
        ModelicaComponentDeletionRequestedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.DeleteSelectedComponentAsync(),
            $"Could not delete {eventArgs.Components.Count} selected component(s)").ConfigureAwait(true);
    }

    private async void HandleComponentDuplicationRequested(
        object? sender,
        ModelicaComponentDuplicationRequestedEventArgs eventArgs) =>
        await DuplicateSelectedComponentsAsync(eventArgs.Components.Count).ConfigureAwait(true);

    private async void HandleDuplicateSelectedComponents(object? sender, RoutedEventArgs eventArgs) =>
        await DuplicateSelectedComponentsAsync(_viewModel?.SelectedComponentCount ?? 0).ConfigureAwait(true);

    private async Task DuplicateSelectedComponentsAsync(int componentCount)
    {
        if (_viewModel is null)
        {
            return;
        }

        await RunUiActionAsync(
            () => _viewModel.DuplicateSelectedComponentsAsync(),
            $"Could not duplicate {componentCount} selected component(s)").ConfigureAwait(true);
    }

    private async void HandleDeleteSelectedComponent(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        var deleteConnection = _viewModel.CanDeleteSelectedConnection;
        await RunUiActionAsync(
            deleteConnection
                ? () => _viewModel.DeleteSelectedConnectionAsync()
                : () => _viewModel.DeleteSelectedComponentAsync(),
            deleteConnection
                ? "Could not delete the selected connection"
                : "Could not delete the selected component").ConfigureAwait(true);
    }

    private void HandleProblemClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel?.ActiveDocument is null
            || sender is not Button { Tag: CompilerProblemViewModel problem })
        {
            return;
        }

        _viewModel.ShowTextCommand.Execute(null);
        SourceEditor.NavigateTo(problem.Diagnostic, _viewModel.ActiveDocument.SourcePath);
    }

    private void SynchronizeDiagnostics()
    {
        SourceEditor.SetDiagnostics(
            _viewModel?.ActiveDocument?.Diagnostics ?? [],
            _viewModel?.ActiveDocument?.SourcePath,
            _viewModel?.ActiveDocument?.DiagnosticsAreStale == true);
    }

    private async void HandleConfigureOpenModelica(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose OpenModelica Compiler Executable (omc)",
            AllowMultiple = false,
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            await RunUiActionAsync(
                () => _viewModel.ConfigureOpenModelicaAsync(path),
                "Could not configure OpenModelica").ConfigureAwait(true);
        }
    }

    private async Task<bool> TrySaveActiveDocumentAsync()
    {
        if (_viewModel?.ActiveDocument is null)
        {
            return true;
        }

        var path = _viewModel.ActiveDocument.SourcePath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Modelica Source",
                SuggestedFileName = _viewModel.ActiveDocument.ClassName + ".mo",
                DefaultExtension = "mo",
                FileTypeChoices = [ModelicaFileType],
                ShowOverwritePrompt = true,
            });
            path = file?.TryGetLocalPath();
        }

        if (path is not null)
        {
            try
            {
                await _viewModel.SaveActiveDocumentAsync(path).ConfigureAwait(true);
                return true;
            }
            catch (Exception exception)
            {
                _viewModel.ReportError(exception, "Could not save Modelica source");
            }
        }

        return false;
    }

    private void HandleExit(object? sender, RoutedEventArgs eventArgs) => Close();

    private async void HandleClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowClose || _viewModel?.ActiveDocument?.IsDirty != true)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_isClosePromptOpen)
        {
            return;
        }

        _isClosePromptOpen = true;
        try
        {
            if (await ConfirmDocumentReplacementAsync().ConfigureAwait(true))
            {
                _allowClose = true;
                Close();
            }
        }
        finally
        {
            _isClosePromptOpen = false;
        }
    }

    private async Task<bool> ConfirmDocumentReplacementAsync()
    {
        if (_viewModel?.ActiveDocument?.IsDirty != true)
        {
            return true;
        }

        var decision = await new UnsavedChangesDialog(_viewModel.ActiveDocument.ClassName)
            .ShowDialog<UnsavedChangesDecision?>(this)
            .ConfigureAwait(true);
        return decision switch
        {
            UnsavedChangesDecision.Save => await TrySaveActiveDocumentAsync().ConfigureAwait(true),
            UnsavedChangesDecision.Discard => true,
            _ => false,
        };
    }

    private async Task RunUiActionAsync(Func<Task> action, string context)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _viewModel?.ReportError(exception, context);
        }
    }
}
