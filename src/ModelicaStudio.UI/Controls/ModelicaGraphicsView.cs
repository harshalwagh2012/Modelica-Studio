using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.UI.Controls;

public sealed class ModelicaGraphicsView : Control
{
    private const double CanvasPadding = 28;
    private const int EllipseSegments = 64;
    private readonly ModelicaCoordinateTransformer _coordinateTransformer = new();
    private ModelicaComponentInstance? _dragComponent;
    private ModelicaTransformation? _dragStartTransformation;
    private ModelicaTransformation? _dragPreviewTransformation;
    private ModelicaPoint _dragStartPoint;
    private bool _dragIconLayer;
    private ModelicaPlacementEditKind _dragKind;
    private ModelicaPlacementHandle? _dragHandle;
    private IReadOnlyDictionary<string, ModelicaTransformation>? _dragStartTransformations;
    private IReadOnlyDictionary<string, ModelicaTransformation>? _dragPreviewTransformations;
    private ModelicaPoint? _boxSelectionStart;
    private ModelicaPoint? _boxSelectionCurrent;
    private IReadOnlyList<ModelicaComponentInstance> _boxSelectionBase = [];
    private ModelicaClassInfo? _libraryDropType;
    private ModelicaPoint? _libraryDropOrigin;
    private ModelicaConnectorEndpoint? _hoverConnectorEndpoint;
    private ModelicaConnectorEndpoint? _connectionStartEndpoint;
    private ModelicaPoint? _connectionPreviewPoint;
    private bool _connectionPointerGestureStarted;
    private ModelicaDiagramConnection? _routeDragConnection;
    private int _routeDragSegmentIndex = -1;
    private IReadOnlyList<ModelicaPoint>? _routeDragPreview;

    public static readonly StyledProperty<ModelicaGraphicalAnnotationSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, ModelicaGraphicalAnnotationSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<ModelicaModelInstanceSnapshot?> InstanceSnapshotProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, ModelicaModelInstanceSnapshot?>(nameof(InstanceSnapshot));

    public static readonly StyledProperty<bool> ShowIconProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, bool>(nameof(ShowIcon));

    public static readonly StyledProperty<ModelicaComponentInstance?> SelectedComponentProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, ModelicaComponentInstance?>(nameof(SelectedComponent));

    public static readonly StyledProperty<IReadOnlyList<ModelicaComponentInstance>> SelectedComponentsProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, IReadOnlyList<ModelicaComponentInstance>>(
            nameof(SelectedComponents),
            []);

    public static readonly StyledProperty<ModelicaDiagramConnection?> SelectedConnectionProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, ModelicaDiagramConnection?>(nameof(SelectedConnection));

    public static readonly StyledProperty<bool> IsGraphicalEditingEnabledProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, bool>(nameof(IsGraphicalEditingEnabled));

    public static readonly StyledProperty<bool> IsConnectionToolActiveProperty =
        AvaloniaProperty.Register<ModelicaGraphicsView, bool>(nameof(IsConnectionToolActive));

    static ModelicaGraphicsView()
    {
        AffectsRender<ModelicaGraphicsView>(
            SnapshotProperty,
            InstanceSnapshotProperty,
            ShowIconProperty,
            SelectedComponentProperty,
            SelectedComponentsProperty,
            SelectedConnectionProperty,
            IsConnectionToolActiveProperty);
    }

    public ModelicaGraphicsView()
    {
        Focusable = true;
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, HandleLibraryDragOver);
        DragDrop.AddDragLeaveHandler(this, HandleLibraryDragLeave);
        DragDrop.AddDropHandler(this, HandleLibraryDrop);
    }

    public ModelicaGraphicalAnnotationSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public ModelicaModelInstanceSnapshot? InstanceSnapshot
    {
        get => GetValue(InstanceSnapshotProperty);
        set => SetValue(InstanceSnapshotProperty, value);
    }

    public bool ShowIcon
    {
        get => GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public ModelicaComponentInstance? SelectedComponent
    {
        get => GetValue(SelectedComponentProperty);
        set => SetValue(SelectedComponentProperty, value);
    }

    public IReadOnlyList<ModelicaComponentInstance> SelectedComponents
    {
        get => GetValue(SelectedComponentsProperty);
        set => SetValue(SelectedComponentsProperty, value);
    }

    public ModelicaDiagramConnection? SelectedConnection
    {
        get => GetValue(SelectedConnectionProperty);
        set => SetValue(SelectedConnectionProperty, value);
    }

    public bool IsGraphicalEditingEnabled
    {
        get => GetValue(IsGraphicalEditingEnabledProperty);
        set => SetValue(IsGraphicalEditingEnabledProperty, value);
    }

    public bool IsConnectionToolActive
    {
        get => GetValue(IsConnectionToolActiveProperty);
        set => SetValue(IsConnectionToolActiveProperty, value);
    }

    public event EventHandler<ModelicaGraphicsSelectionChangedEventArgs>? GraphicalSelectionChanged;
    public event EventHandler<ModelicaComponentPlacementRequestedEventArgs>? ComponentPlacementRequested;
    public event EventHandler<ModelicaComponentDropRequestedEventArgs>? ComponentDropRequested;
    public event EventHandler<ModelicaComponentDeletionRequestedEventArgs>? ComponentDeletionRequested;
    public event EventHandler<ModelicaComponentDuplicationRequestedEventArgs>? ComponentDuplicationRequested;
    public event EventHandler<ModelicaConnectionCreationRequestedEventArgs>? ConnectionCreationRequested;
    public event EventHandler<ModelicaConnectionRouteRequestedEventArgs>? ConnectionRouteRequested;
    public event EventHandler<ModelicaConnectionDeletionRequestedEventArgs>? ConnectionDeletionRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsConnectionToolActiveProperty && !IsConnectionToolActive
            || change.Property == ShowIconProperty && ShowIcon
            || change.Property == InstanceSnapshotProperty)
        {
            ResetConnectionToolState();
            ResetConnectionRouteDrag();
        }

        if (change.Property == IsConnectionToolActiveProperty)
        {
            Cursor = IsConnectionToolActive
                ? new Cursor(StandardCursorType.Cross)
                : Cursor.Default;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, Bounds);

        var snapshot = Snapshot;
        var view = SelectView(snapshot);
        var hasInstanceGraphics = HasRenderableInstanceGraphics(InstanceSnapshot);
        if ((snapshot is null && !hasInstanceGraphics)
            || (view is null && !hasInstanceGraphics)
            || Bounds.Width <= CanvasPadding * 2
            || Bounds.Height <= CanvasPadding * 2)
        {
            DrawEmptyState(context, snapshot is null ? "No graphical annotation loaded" : "This class has no graphical annotation");
            return;
        }

        var drawingBounds = new Rect(
            CanvasPadding,
            CanvasPadding,
            Bounds.Width - (CanvasPadding * 2),
            Bounds.Height - (CanvasPadding * 2));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(250, 250, 248)), new Pen(Brushes.LightGray), drawingBounds);

        if (snapshot is not null)
        {
            foreach (var inherited in snapshot.InheritedAnnotations.Reverse())
            {
                var inheritedView = ShowIcon ? inherited.Icon : inherited.Diagram;
                if (inheritedView is not null)
                {
                    RenderView(
                        context,
                        inheritedView,
                        drawingBounds,
                        snapshot.ClassName,
                        inheritedView.CoordinateSystem.Coordinates);
                }
            }

            if (view is not null)
            {
                RenderView(context, view, drawingBounds, snapshot.ClassName, view.CoordinateSystem.Coordinates);
            }
        }

        var rootCoordinateSystem = view?.CoordinateSystem.Coordinates ?? ModelicaCoordinateSystem.Default;
        RenderComponentInstances(context, InstanceSnapshot, rootCoordinateSystem, drawingBounds);
        if (!ShowIcon)
        {
            RenderConnections(context, InstanceSnapshot, rootCoordinateSystem, drawingBounds);
            RenderConnectorEndpoints(context, rootCoordinateSystem, drawingBounds);
            RenderConnectionPreview(context, rootCoordinateSystem, drawingBounds);
        }

        RenderSelection(context, rootCoordinateSystem, drawingBounds);
        RenderBoxSelection(context, rootCoordinateSystem, drawingBounds);
        RenderLibraryDropPreview(context, rootCoordinateSystem, drawingBounds);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = eventArgs.GetPosition(this);
        var drawingBounds = CreateDrawingBounds();
        var view = SelectView(Snapshot);
        var rootCoordinateSystem = view?.CoordinateSystem.Coordinates ?? ModelicaCoordinateSystem.Default;
        var modelPoint = drawingBounds.Width > 0 && drawingBounds.Height > 0
            ? ToModelica(point, rootCoordinateSystem, drawingBounds)
            : default;
        var modelTolerance = drawingBounds.Width > 0 && drawingBounds.Height > 0
            ? ModelTolerance(point, rootCoordinateSystem, drawingBounds)
            : 0;
        if (IsGraphicalEditingEnabled
            && IsConnectionToolActive
            && !ShowIcon
            && drawingBounds.Contains(point))
        {
            var hadStart = _connectionStartEndpoint is not null;
            HandleConnectionToolClick(modelPoint, Math.Max(
                modelTolerance,
                ModelDistanceForCanvasPixels(9, rootCoordinateSystem, drawingBounds)));
            Focus();
            if (!hadStart && _connectionStartEndpoint is not null)
            {
                _connectionPointerGestureStarted = true;
                eventArgs.Pointer.Capture(this);
            }
            eventArgs.Handled = true;
            return;
        }

        if (IsGraphicalEditingEnabled
            && !ShowIcon
            && drawingBounds.Contains(point)
            && TryStartConnectionRouteDrag(
                modelPoint,
                Math.Max(modelTolerance, ModelDistanceForCanvasPixels(8, rootCoordinateSystem, drawingBounds))))
        {
            Focus();
            eventArgs.Pointer.Capture(this);
            eventArgs.Handled = true;
            return;
        }

        if (IsGraphicalEditingEnabled
            && drawingBounds.Contains(point)
            && TryStartPlacementHandleDrag(
                modelPoint,
                modelTolerance,
                ModelDistanceForCanvasPixels(18, rootCoordinateSystem, drawingBounds)))
        {
            Focus();
            eventArgs.Pointer.Capture(this);
            eventArgs.Handled = true;
            return;
        }

        var hit = drawingBounds.Width > 0
            && drawingBounds.Height > 0
            && drawingBounds.Contains(point)
            ? ModelicaDiagramHitTester.HitTest(
                InstanceSnapshot,
                modelPoint,
                modelTolerance,
                ShowIcon)
            : ModelicaDiagramHit.None;

        var shiftPressed = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var currentSelection = EffectiveSelectedComponents();
        if (hit.Component is { } hitComponent)
        {
            var selected = UpdateComponentSelection(currentSelection, hitComponent, shiftPressed);
            var primary = selected.FirstOrDefault(component =>
                    string.Equals(component.Name, hitComponent.Name, StringComparison.Ordinal))
                ?? selected.LastOrDefault();
            SetSelection(primary is null ? ModelicaDiagramHit.None : new ModelicaDiagramHit(primary, null), selected);
        }
        else if (hit.Connection is not null)
        {
            SetSelection(hit, []);
        }
        else
        {
            _boxSelectionBase = shiftPressed ? currentSelection : [];
            _boxSelectionStart = modelPoint;
            _boxSelectionCurrent = modelPoint;
            if (!shiftPressed)
            {
                SetSelection(ModelicaDiagramHit.None, []);
            }

            Focus();
            eventArgs.Pointer.Capture(this);
            eventArgs.Handled = true;
            return;
        }

        Focus();
        if (IsGraphicalEditingEnabled && hit.Component is { } component)
        {
            var transformations = GetEditableSelectionTransformations(EffectiveSelectedComponents());
            if (transformations.Count > 0
                && transformations.ContainsKey(component.Name))
            {
                _dragComponent = component;
                _dragStartTransformation = transformations[component.Name];
                _dragPreviewTransformation = null;
                _dragStartTransformations = transformations;
                _dragPreviewTransformations = null;
                _dragStartPoint = modelPoint;
                _dragIconLayer = ShowIcon;
                _dragKind = ModelicaPlacementEditKind.Move;
                _dragHandle = null;
                eventArgs.Pointer.Capture(this);
            }
        }

        eventArgs.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        var currentDrawingBounds = CreateDrawingBounds();
        if (currentDrawingBounds.Width > 0 && currentDrawingBounds.Height > 0)
        {
            var currentView = SelectView(Snapshot);
            var currentCoordinateSystem = currentView?.CoordinateSystem.Coordinates
                ?? ModelicaCoordinateSystem.Default;
            var currentModelPoint = ToModelica(
                eventArgs.GetPosition(this),
                currentCoordinateSystem,
                currentDrawingBounds);
            if (IsConnectionToolActive && !ShowIcon)
            {
                _connectionPreviewPoint = currentModelPoint;
                _hoverConnectorEndpoint = ModelicaConnectionEditor.HitTestEndpoint(
                    ModelicaConnectionEditor.GetConnectorEndpoints(InstanceSnapshot),
                    currentModelPoint,
                    ModelDistanceForCanvasPixels(9, currentCoordinateSystem, currentDrawingBounds));
                InvalidateVisual();
            }

            if (_routeDragConnection is not null
                && _routeDragSegmentIndex >= 0
                && eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var routeGrid = currentView?.CoordinateSystem.Grid ?? new ModelicaPoint(2, 2);
                _routeDragPreview = ModelicaConnectionEditor.MoveOrthogonalSegment(
                    EffectiveConnectionPoints(_routeDragConnection, InstanceSnapshot),
                    _routeDragSegmentIndex,
                    currentModelPoint,
                    routeGrid);
                InvalidateVisual();
                eventArgs.Handled = true;
                return;
            }
        }

        if (_boxSelectionStart is not null
            && eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var boxDrawingBounds = CreateDrawingBounds();
            if (boxDrawingBounds.Width > 0 && boxDrawingBounds.Height > 0)
            {
                var boxView = SelectView(Snapshot);
                var coordinateSystem = boxView?.CoordinateSystem.Coordinates ?? ModelicaCoordinateSystem.Default;
                _boxSelectionCurrent = ToModelica(eventArgs.GetPosition(this), coordinateSystem, boxDrawingBounds);
                InvalidateVisual();
            }

            eventArgs.Handled = true;
            return;
        }

        if (_dragComponent is null
            || _dragStartTransformation is null
            || !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var drawingBounds = CreateDrawingBounds();
        if (drawingBounds.Width <= 0 || drawingBounds.Height <= 0)
        {
            return;
        }

        var view = SelectView(Snapshot);
        var rootCoordinateSystem = view?.CoordinateSystem.Coordinates ?? ModelicaCoordinateSystem.Default;
        var currentPoint = ToModelica(eventArgs.GetPosition(this), rootCoordinateSystem, drawingBounds);
        var grid = view?.CoordinateSystem.Grid ?? new ModelicaPoint(2, 2);
        _dragPreviewTransformation = _dragKind switch
        {
            ModelicaPlacementEditKind.Resize when _dragHandle is { } handle =>
                ModelicaPlacementEditor.Resize(_dragStartTransformation, handle, currentPoint, grid),
            ModelicaPlacementEditKind.Rotate =>
                ModelicaPlacementEditor.RotateTowards(_dragStartTransformation, currentPoint),
            _ => _dragStartTransformation with
            {
                Origin = ModelicaGrid.Snap(
                    new ModelicaPoint(
                        _dragStartTransformation.Origin.X + currentPoint.X - _dragStartPoint.X,
                        _dragStartTransformation.Origin.Y + currentPoint.Y - _dragStartPoint.Y),
                    grid),
            },
        };
        if (_dragKind == ModelicaPlacementEditKind.Move
            && _dragStartTransformations is { Count: > 0 } startTransformations)
        {
            var unsnappedPrimaryOrigin = new ModelicaPoint(
                _dragStartTransformation.Origin.X + currentPoint.X - _dragStartPoint.X,
                _dragStartTransformation.Origin.Y + currentPoint.Y - _dragStartPoint.Y);
            var snappedPrimaryOrigin = ModelicaGrid.Snap(unsnappedPrimaryOrigin, grid);
            var delta = new ModelicaPoint(
                snappedPrimaryOrigin.X - _dragStartTransformation.Origin.X,
                snappedPrimaryOrigin.Y - _dragStartTransformation.Origin.Y);
            _dragPreviewTransformations = startTransformations.ToDictionary(
                static pair => pair.Key,
                pair => pair.Value with
                {
                    Origin = new ModelicaPoint(
                        pair.Value.Origin.X + delta.X,
                        pair.Value.Origin.Y + delta.Y),
                },
                StringComparer.Ordinal);
            _dragPreviewTransformation = _dragPreviewTransformations[_dragComponent.Name];
        }

        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_connectionPointerGestureStarted)
        {
            _connectionPointerGestureStarted = false;
            eventArgs.Pointer.Capture(null);
            if (_connectionStartEndpoint is not null
                && _hoverConnectorEndpoint is { } hovered
                && !string.Equals(
                    _connectionStartEndpoint.Path,
                    hovered.Path,
                    StringComparison.Ordinal))
            {
                HandleConnectionToolClick(hovered.Position, double.Epsilon);
            }

            eventArgs.Handled = true;
            return;
        }

        if (_routeDragConnection is { } routeConnection)
        {
            var route = _routeDragPreview;
            ResetConnectionRouteDrag();
            eventArgs.Pointer.Capture(null);
            if (route is not null)
            {
                ConnectionRouteRequested?.Invoke(
                    this,
                    new ModelicaConnectionRouteRequestedEventArgs(routeConnection, route));
            }

            eventArgs.Handled = true;
            return;
        }

        if (_boxSelectionStart is { } selectionStart)
        {
            var selectionEnd = _boxSelectionCurrent ?? selectionStart;
            var selected = ModelicaDiagramHitTester.SelectComponentsInBox(
                InstanceSnapshot,
                new ModelicaExtent(selectionStart, selectionEnd),
                ShowIcon);
            var combined = MergeSelections(_boxSelectionBase, selected);
            var primary = combined.LastOrDefault();
            ResetBoxSelection();
            eventArgs.Pointer.Capture(null);
            SetSelection(
                primary is null ? ModelicaDiagramHit.None : new ModelicaDiagramHit(primary, null),
                combined);
            eventArgs.Handled = true;
            return;
        }

        if (_dragComponent is null)
        {
            return;
        }

        var component = _dragComponent;
        var requested = _dragPreviewTransformation;
        var iconLayer = _dragIconLayer;
        var kind = _dragKind;
        var transformations = _dragPreviewTransformations;
        ResetDrag();
        eventArgs.Pointer.Capture(null);
        if (requested is not null)
        {
            var requests = transformations is { Count: > 0 }
                ? EffectiveSelectedComponents()
                    .Where(item => transformations.ContainsKey(item.Name))
                    .Select(item => new ModelicaComponentTransformationRequest(item, transformations[item.Name]))
                    .ToArray()
                : [new ModelicaComponentTransformationRequest(component, requested)];
            ComponentPlacementRequested?.Invoke(
                this,
                new ModelicaComponentPlacementRequestedEventArgs(requests, iconLayer, kind));
        }

        eventArgs.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs)
    {
        base.OnPointerCaptureLost(eventArgs);
        ResetDrag();
        ResetBoxSelection();
        ResetConnectionRouteDrag();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (eventArgs.Key == Key.Escape && (_connectionStartEndpoint is not null || IsConnectionToolActive))
        {
            ResetConnectionToolState();
            eventArgs.Handled = true;
            return;
        }

        if (IsGraphicalEditingEnabled
            && (eventArgs.Key is Key.Delete or Key.Back)
            && SelectedConnection is { IsInherited: false } connection)
        {
            ConnectionDeletionRequested?.Invoke(
                this,
                new ModelicaConnectionDeletionRequestedEventArgs(connection));
            eventArgs.Handled = true;
            return;
        }

        var selectedComponents = EffectiveSelectedComponents();
        if (!IsGraphicalEditingEnabled
            || SelectedComponent is not { IsInherited: false, Placement: not null } component
            || selectedComponents.Count == 0)
        {
            return;
        }

        if (eventArgs.Key is Key.Delete or Key.Back)
        {
            ComponentDeletionRequested?.Invoke(
                this,
                new ModelicaComponentDeletionRequestedEventArgs(selectedComponents));
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.D
            && (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control)
                || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            ComponentDuplicationRequested?.Invoke(
                this,
                new ModelicaComponentDuplicationRequestedEventArgs(selectedComponents));
            eventArgs.Handled = true;
            return;
        }

        var transformation = ShowIcon
            ? component.Placement.IconTransformation
            : component.Placement.Transformation;
        if (transformation is null)
        {
            return;
        }

        var view = SelectView(Snapshot);
        var grid = view?.CoordinateSystem.Grid ?? new ModelicaPoint(2, 2);
        var multiplier = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
        var steps = eventArgs.Key switch
        {
            Key.Left => (-multiplier, 0),
            Key.Right => (multiplier, 0),
            Key.Up => (0, multiplier),
            Key.Down => (0, -multiplier),
            _ => ((int Horizontal, int Vertical)?)null,
        };
        if (steps is null)
        {
            return;
        }

        var transformations = GetEditableSelectionTransformations(selectedComponents);
        if (transformations.Count != selectedComponents.Count)
        {
            return;
        }

        var requests = selectedComponents.Select(item => new ModelicaComponentTransformationRequest(
            item,
            transformations[item.Name] with
            {
                Origin = ModelicaGrid.Move(
                    transformations[item.Name].Origin,
                    grid,
                    steps.Value.Horizontal,
                    steps.Value.Vertical),
            })).ToArray();
        ComponentPlacementRequested?.Invoke(
            this,
            new ModelicaComponentPlacementRequestedEventArgs(
                requests,
                ShowIcon,
                ModelicaPlacementEditKind.Move));
        eventArgs.Handled = true;
    }

    private void HandleLibraryDragOver(object? sender, DragEventArgs eventArgs)
    {
        var componentType = eventArgs.DataTransfer.TryGetValue(ModelicaDragData.LibraryClassFormat);
        if (!CanAcceptLibraryDrop(componentType))
        {
            ClearLibraryDropPreview();
            eventArgs.DragEffects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        var drawingBounds = CreateDrawingBounds();
        if (drawingBounds.Width <= 0 || drawingBounds.Height <= 0
            || !drawingBounds.Contains(eventArgs.GetPosition(this)))
        {
            ClearLibraryDropPreview();
            eventArgs.DragEffects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        var view = SelectView(Snapshot);
        var rootCoordinateSystem = view?.CoordinateSystem.Coordinates ?? ModelicaCoordinateSystem.Default;
        var grid = view?.CoordinateSystem.Grid ?? new ModelicaPoint(2, 2);
        _libraryDropType = componentType;
        _libraryDropOrigin = ModelicaGrid.Snap(
            ToModelica(eventArgs.GetPosition(this), rootCoordinateSystem, drawingBounds),
            grid);
        eventArgs.DragEffects = DragDropEffects.Copy;
        eventArgs.Handled = true;
        InvalidateVisual();
    }

    private void HandleLibraryDragLeave(object? sender, DragEventArgs eventArgs)
    {
        ClearLibraryDropPreview();
        eventArgs.Handled = true;
    }

    private void HandleLibraryDrop(object? sender, DragEventArgs eventArgs)
    {
        var componentType = eventArgs.DataTransfer.TryGetValue(ModelicaDragData.LibraryClassFormat);
        var origin = _libraryDropOrigin;
        ClearLibraryDropPreview();
        if (!CanAcceptLibraryDrop(componentType) || origin is null)
        {
            eventArgs.DragEffects = DragDropEffects.None;
            eventArgs.Handled = true;
            return;
        }

        eventArgs.DragEffects = DragDropEffects.Copy;
        eventArgs.Handled = true;
        ComponentDropRequested?.Invoke(
            this,
            new ModelicaComponentDropRequestedEventArgs(componentType!, origin.Value));
    }

    private bool CanAcceptLibraryDrop(ModelicaClassInfo? componentType) =>
        IsGraphicalEditingEnabled
        && !ShowIcon
        && componentType is not null
        && ModelicaComponentType.IsInstantiable(componentType)
        && !string.Equals(componentType.FullName, InstanceSnapshot?.ClassName, StringComparison.Ordinal);

    private void ClearLibraryDropPreview()
    {
        if (_libraryDropType is null && _libraryDropOrigin is null)
        {
            return;
        }

        _libraryDropType = null;
        _libraryDropOrigin = null;
        InvalidateVisual();
    }

    private ModelicaGraphicalView? SelectView(ModelicaGraphicalAnnotationSnapshot? snapshot) =>
        ShowIcon ? snapshot?.Icon : snapshot?.Diagram;

    private void RenderView(
        DrawingContext context,
        ModelicaGraphicalView view,
        Rect drawingBounds,
        string className,
        ModelicaCoordinateSystem canvasCoordinateSystem,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform = null)
    {
        foreach (var primitive in view.Graphics.Where(static item => item.Visible.StaticValue))
        {
            RenderPrimitive(
                context,
                primitive,
                canvasCoordinateSystem,
                drawingBounds,
                className,
                modelTransform);
        }
    }

    private void RenderComponentInstances(
        DrawingContext context,
        ModelicaModelInstanceSnapshot? instance,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        if (instance is null)
        {
            return;
        }

        foreach (var component in instance.Components)
        {
            var placement = component.Placement;
            var transformation = ShowIcon ? placement?.IconTransformation : placement?.Transformation;
            var isVisible = ShowIcon ? placement?.IconVisible == true : placement?.Visible == true;
            var graphics = component.TypeGraphics;
            if (!isVisible || transformation is null || graphics?.Icon is null)
            {
                continue;
            }

            if (_dragIconLayer == ShowIcon
                && _dragPreviewTransformations?.TryGetValue(component.Name, out var groupPreview) == true)
            {
                transformation = groupPreview;
            }
            else if (_dragPreviewTransformation is not null
                && _dragIconLayer == ShowIcon
                && string.Equals(_dragComponent?.Name, component.Name, StringComparison.Ordinal))
            {
                transformation = _dragPreviewTransformation;
            }

            foreach (var inherited in graphics.InheritedAnnotations.Reverse())
            {
                if (inherited.Icon is not null)
                {
                    RenderPlacedIconLayer(
                        context,
                        inherited.Icon,
                        component.Name,
                        transformation,
                        rootCoordinateSystem,
                        drawingBounds);
                }
            }

            RenderPlacedIconLayer(
                context,
                graphics.Icon,
                component.Name,
                transformation,
                rootCoordinateSystem,
                drawingBounds);
            if (!string.Equals(component.Restriction, "connector", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(component.Restriction, "expandable connector", StringComparison.OrdinalIgnoreCase))
            {
                RenderNestedConnectorInstances(
                    context,
                    component,
                    transformation,
                    graphics.Icon.CoordinateSystem.Coordinates.Extent,
                    rootCoordinateSystem,
                    drawingBounds);
            }
        }
    }

    private void RenderNestedConnectorInstances(
        DrawingContext context,
        ModelicaComponentInstance owner,
        ModelicaTransformation ownerTransformation,
        ModelicaExtent ownerSourceExtent,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        foreach (var connector in owner.TypeComponents.Where(static component =>
                     string.Equals(component.Restriction, "connector", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(component.Restriction, "expandable connector", StringComparison.OrdinalIgnoreCase)))
        {
            var connectorTransformation = connector.Placement is { IconVisible: true } connectorPlacement
                ? connectorPlacement.IconTransformation ?? connectorPlacement.Transformation
                : null;
            var icon = connector.TypeGraphics?.Icon;
            if (connectorTransformation is null || icon is null)
            {
                continue;
            }

            foreach (var inherited in connector.TypeGraphics!.InheritedAnnotations.Reverse())
            {
                if (inherited.Icon is not null)
                {
                    RenderNestedConnectorIconLayer(
                        context,
                        inherited.Icon,
                        connector.Name,
                        connectorTransformation,
                        ownerSourceExtent,
                        ownerTransformation,
                        rootCoordinateSystem,
                        drawingBounds);
                }
            }

            RenderNestedConnectorIconLayer(
                context,
                icon,
                connector.Name,
                connectorTransformation,
                ownerSourceExtent,
                ownerTransformation,
                rootCoordinateSystem,
                drawingBounds);
        }
    }

    private void RenderNestedConnectorIconLayer(
        DrawingContext context,
        ModelicaGraphicalView icon,
        string connectorName,
        ModelicaTransformation connectorTransformation,
        ModelicaExtent ownerSourceExtent,
        ModelicaTransformation ownerTransformation,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        var connectorSourceExtent = icon.CoordinateSystem.Coordinates.Extent;
        RenderView(
            context,
            icon,
            drawingBounds,
            connectorName,
            rootCoordinateSystem,
            point =>
            {
                var ownerIconPoint = ModelicaPlacementTransform.Apply(
                    point,
                    connectorSourceExtent,
                    connectorTransformation);
                return ModelicaPlacementTransform.Apply(
                    ownerIconPoint,
                    ownerSourceExtent,
                    ownerTransformation);
            });
    }

    private void RenderPlacedIconLayer(
        DrawingContext context,
        ModelicaGraphicalView icon,
        string componentName,
        ModelicaTransformation transformation,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        var sourceExtent = icon.CoordinateSystem.Coordinates.Extent;
        RenderView(
            context,
            icon,
            drawingBounds,
            componentName,
            rootCoordinateSystem,
            point => ModelicaPlacementTransform.Apply(point, sourceExtent, transformation));
    }

    private void RenderConnections(
        DrawingContext context,
        ModelicaModelInstanceSnapshot? instance,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        if (instance is null)
        {
            return;
        }

        foreach (var connection in instance.Connections)
        {
            var line = CreateRenderableConnectionLine(instance, connection);
            if (line is null || !line.Visible.StaticValue)
            {
                continue;
            }

            RenderPrimitive(
                context,
                line,
                rootCoordinateSystem,
                drawingBounds,
                instance.ClassName);
        }
    }

    private void RenderConnectorEndpoints(
        DrawingContext context,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        var endpoints = ModelicaConnectionEditor.GetConnectorEndpoints(InstanceSnapshot);
        foreach (var group in endpoints.GroupBy(static endpoint => endpoint.Position))
        {
            var endpoint = group.First();
            var isStart = _connectionStartEndpoint is not null
                && group.Any(item => string.Equals(
                    item.Path,
                    _connectionStartEndpoint.Path,
                    StringComparison.Ordinal));
            var isHover = _hoverConnectorEndpoint is not null
                && group.Any(item => string.Equals(
                    item.Path,
                    _hoverConnectorEndpoint.Path,
                    StringComparison.Ordinal));
            var compatible = isHover && _connectionStartEndpoint is not null
                ? ModelicaConnectionEditor.CheckCompatibility(
                    _connectionStartEndpoint,
                    _hoverConnectorEndpoint!,
                    InstanceSnapshot?.Connections).IsCompatible
                : true;
            var color = isHover && !compatible
                ? Color.FromRgb(176, 55, 48)
                : isStart || isHover
                    ? Color.FromRgb(38, 102, 166)
                    : Color.FromRgb(82, 99, 112);
            var canvas = ToCanvas(endpoint.Position, rootCoordinateSystem, drawingBounds);
            var radius = IsConnectionToolActive || isStart || isHover ? 5d : 3.5d;
            context.DrawEllipse(
                isStart ? new SolidColorBrush(color) : Brushes.White,
                new Pen(new SolidColorBrush(color), isStart || isHover ? 2 : 1.25),
                canvas,
                radius,
                radius);

            if (isStart || isHover)
            {
                var label = group.Count() > 1
                    ? $"{endpoint.Path} · {group.Count()} indexed endpoints"
                    : endpoint.Path;
                var layout = new TextLayout(
                    label,
                    new Typeface(FontFamily.Default),
                    11,
                    new SolidColorBrush(color));
                layout.Draw(context, new Point(canvas.X + 8, canvas.Y - layout.Height - 5));
            }
        }
    }

    private void RenderConnectionPreview(
        DrawingContext context,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        if (_routeDragConnection is { } routeConnection
            && _routeDragPreview is { } route
            && CreateRenderableConnectionLine(InstanceSnapshot, routeConnection) is { } routeLine)
        {
            RenderPrimitive(
                context,
                routeLine with
                {
                    Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
                    Rotation = new ModelicaAnnotationValue<double>(0),
                    Points = route,
                    Style = routeLine.Style with
                    {
                        LineColor = new ModelicaColor(38, 102, 166),
                        LinePattern = ModelicaLinePattern.Solid,
                        LineThickness = Math.Max(1.25, routeLine.Style.LineThickness),
                    },
                },
                rootCoordinateSystem,
                drawingBounds,
                InstanceSnapshot?.ClassName ?? string.Empty);
        }

        if (_connectionStartEndpoint is null || _connectionPreviewPoint is null)
        {
            return;
        }

        var end = _hoverConnectorEndpoint?.Position ?? _connectionPreviewPoint.Value;
        if (end == _connectionStartEndpoint.Position)
        {
            return;
        }

        var compatible = _hoverConnectorEndpoint is null
            || ModelicaConnectionEditor.CheckCompatibility(
                _connectionStartEndpoint,
                _hoverConnectorEndpoint,
                InstanceSnapshot?.Connections).IsCompatible;
        var preview = new ModelicaGraphicPrimitive
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(0),
            Points = ModelicaConnectionEditor.CreateOrthogonalRoute(_connectionStartEndpoint.Position, end),
            Style = new ModelicaGraphicStyle
            {
                LineColor = compatible ? new ModelicaColor(38, 102, 166) : new ModelicaColor(176, 55, 48),
                LinePattern = ModelicaLinePattern.Dash,
                LineThickness = 1,
            },
            RawJson = "{}",
        };
        RenderPrimitive(
            context,
            preview,
            rootCoordinateSystem,
            drawingBounds,
            InstanceSnapshot?.ClassName ?? string.Empty);
    }

    private void RenderSelection(
        DrawingContext context,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        var selectedComponents = EffectiveSelectedComponents();
        foreach (var component in selectedComponents)
        {
            var placement = component.Placement;
            var transformation = ShowIcon ? placement?.IconTransformation : placement?.Transformation;
            var isVisible = ShowIcon ? placement?.IconVisible == true : placement?.Visible == true;
            if (isVisible && transformation is not null)
            {
                if (_dragIconLayer == ShowIcon
                    && _dragPreviewTransformations?.TryGetValue(component.Name, out var groupPreview) == true)
                {
                    transformation = groupPreview;
                }
                else if (_dragPreviewTransformation is not null
                    && _dragIconLayer == ShowIcon
                    && string.Equals(_dragComponent?.Name, component.Name, StringComparison.Ordinal))
                {
                    transformation = _dragPreviewTransformation;
                }

                var points = ModelicaDiagramHitTester.GetPlacementPolygon(transformation)
                    .Select(point => ToCanvas(point, rootCoordinateSystem, drawingBounds))
                    .ToArray();
                var geometry = new StreamGeometry();
                using (var geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(points[0], false);
                    foreach (var point in points.Skip(1))
                    {
                        geometryContext.LineTo(point);
                    }

                    geometryContext.EndFigure(true);
                }

                var selectionPen = new Pen(new SolidColorBrush(Color.FromRgb(38, 102, 166)), 2)
                {
                    DashStyle = DashStyle.Dash,
                };
                context.DrawGeometry(null, selectionPen, geometry);
                if (IsGraphicalEditingEnabled
                    && selectedComponents.Count == 1
                    && !component.IsInherited)
                {
                    var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(38, 102, 166)), 1.5);
                    var rotationOffset = ModelDistanceForCanvasPixels(18, rootCoordinateSystem, drawingBounds);
                    var handles = ModelicaPlacementEditor.GetHandlePoints(transformation, rotationOffset);
                    var rotationAnchor = ModelicaGraphicTransform.Apply(
                        new ModelicaPoint(
                            (transformation.Extent.First.X + transformation.Extent.Second.X) / 2,
                            transformation.Extent.MaximumY),
                        transformation.Origin,
                        transformation.Rotation);
                    var canvasRotationAnchor = ToCanvas(rotationAnchor, rootCoordinateSystem, drawingBounds);
                    var canvasRotationHandle = ToCanvas(
                        handles[ModelicaPlacementHandle.Rotation],
                        rootCoordinateSystem,
                        drawingBounds);
                    context.DrawLine(selectionPen, canvasRotationAnchor, canvasRotationHandle);
                    foreach (var handle in handles.Where(static pair => pair.Key != ModelicaPlacementHandle.Rotation))
                    {
                        var canvasHandle = ToCanvas(handle.Value, rootCoordinateSystem, drawingBounds);
                        context.DrawRectangle(
                            Brushes.White,
                            handlePen,
                            new Rect(canvasHandle.X - 3.5, canvasHandle.Y - 3.5, 7, 7));
                    }

                    context.DrawEllipse(
                        Brushes.White,
                        handlePen,
                        canvasRotationHandle,
                        4,
                        4);
                }
            }
        }

        if (!ShowIcon
            && SelectedConnection is { } selectedConnection
            && CreateRenderableConnectionLine(InstanceSnapshot, selectedConnection) is { } line)
        {
            var selectedLine = _routeDragConnection is not null
                && ModelicaConnectionEditor.SameEndpoints(
                    _routeDragConnection.Left,
                    _routeDragConnection.Right,
                    selectedConnection.Left,
                    selectedConnection.Right)
                && _routeDragPreview is { } preview
                    ? line with
                    {
                        Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
                        Rotation = new ModelicaAnnotationValue<double>(0),
                        Points = preview,
                    }
                    : line;
            RenderPrimitive(
                context,
                selectedLine with
                {
                    Style = selectedLine.Style with
                    {
                        LineColor = new ModelicaColor(38, 102, 166),
                        LinePattern = ModelicaLinePattern.Solid,
                        LineThickness = Math.Max(1.25, line.Style.LineThickness),
                    },
                },
                rootCoordinateSystem,
                drawingBounds,
                InstanceSnapshot?.ClassName ?? string.Empty);
            if (IsGraphicalEditingEnabled && !selectedConnection.IsInherited)
            {
                var points = _routeDragPreview ?? EffectiveConnectionPoints(selectedConnection, InstanceSnapshot);
                var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(38, 102, 166)), 1.5);
                for (var index = 0; index < points.Count - 1; index++)
                {
                    var first = points[index];
                    var second = points[index + 1];
                    if (Math.Abs(first.X - second.X) > 1e-9
                        && Math.Abs(first.Y - second.Y) > 1e-9)
                    {
                        continue;
                    }

                    var midpoint = new ModelicaPoint(
                        (first.X + second.X) / 2,
                        (first.Y + second.Y) / 2);
                    var canvasHandle = ToCanvas(midpoint, rootCoordinateSystem, drawingBounds);
                    context.DrawEllipse(Brushes.White, handlePen, canvasHandle, 4, 4);
                }
            }
        }
    }

    private void RenderBoxSelection(
        DrawingContext context,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        if (_boxSelectionStart is not { } start || _boxSelectionCurrent is not { } current)
        {
            return;
        }

        var canvasStart = ToCanvas(start, rootCoordinateSystem, drawingBounds);
        var canvasCurrent = ToCanvas(current, rootCoordinateSystem, drawingBounds);
        var selectionRect = new Rect(
            Math.Min(canvasStart.X, canvasCurrent.X),
            Math.Min(canvasStart.Y, canvasCurrent.Y),
            Math.Abs(canvasCurrent.X - canvasStart.X),
            Math.Abs(canvasCurrent.Y - canvasStart.Y));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(38, 102, 166)), 1)
        {
            DashStyle = DashStyle.Dash,
        };
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, 38, 102, 166)), pen, selectionRect);
    }

    private void RenderLibraryDropPreview(
        DrawingContext context,
        ModelicaCoordinateSystem rootCoordinateSystem,
        Rect drawingBounds)
    {
        if (_libraryDropType is null || _libraryDropOrigin is not { } origin)
        {
            return;
        }

        var canvasOrigin = ToCanvas(origin, rootCoordinateSystem, drawingBounds);
        var previewPen = new Pen(new SolidColorBrush(Color.FromRgb(38, 102, 166)), 2)
        {
            DashStyle = DashStyle.Dash,
        };
        var previewRect = new Rect(canvasOrigin.X - 22, canvasOrigin.Y - 22, 44, 44);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(32, 38, 102, 166)), previewPen, previewRect);
        context.DrawLine(previewPen, new Point(canvasOrigin.X - 7, canvasOrigin.Y), new Point(canvasOrigin.X + 7, canvasOrigin.Y));
        context.DrawLine(previewPen, new Point(canvasOrigin.X, canvasOrigin.Y - 7), new Point(canvasOrigin.X, canvasOrigin.Y + 7));
        var layout = new TextLayout(
            $"Add {_libraryDropType.Name}",
            new Typeface(FontFamily.Default),
            12,
            new SolidColorBrush(Color.FromRgb(38, 102, 166)));
        layout.Draw(context, new Point(canvasOrigin.X - (layout.Width / 2), previewRect.Bottom + 6));
    }

    private void RenderPrimitive(
        DrawingContext context,
        ModelicaGraphicPrimitive primitive,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds,
        string className,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform = null)
    {
        switch (primitive.Kind)
        {
            case ModelicaGraphicKind.Rectangle:
                DrawExtentPolygon(context, primitive, coordinateSystem, drawingBounds, RectanglePoints(primitive.Extent), modelTransform);
                break;
            case ModelicaGraphicKind.Ellipse:
                var ellipseClosed = IsFullEllipse(primitive)
                    || primitive.EllipseClosure != ModelicaEllipseClosure.None;
                DrawPolyline(
                    context,
                    primitive,
                    coordinateSystem,
                    drawingBounds,
                    EllipsePoints(primitive),
                    ellipseClosed,
                    modelTransform);
                break;
            case ModelicaGraphicKind.Line:
                DrawPolyline(context, primitive, coordinateSystem, drawingBounds, primitive.Points, false, modelTransform);
                break;
            case ModelicaGraphicKind.Polygon:
                DrawPolyline(context, primitive, coordinateSystem, drawingBounds, primitive.Points, true, modelTransform);
                break;
            case ModelicaGraphicKind.Text:
                DrawText(context, primitive, coordinateSystem, drawingBounds, className, modelTransform);
                break;
            case ModelicaGraphicKind.Bitmap:
                DrawBitmapPlaceholder(context, primitive, coordinateSystem, drawingBounds, modelTransform);
                break;
        }
    }

    private void DrawExtentPolygon(
        DrawingContext context,
        ModelicaGraphicPrimitive primitive,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds,
        IReadOnlyList<ModelicaPoint> points,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform)
    {
        if (points.Count < 3)
        {
            return;
        }

        DrawPolyline(context, primitive, coordinateSystem, drawingBounds, points, true, modelTransform);
    }

    private void DrawPolyline(
        DrawingContext context,
        ModelicaGraphicPrimitive primitive,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds,
        IReadOnlyList<ModelicaPoint> points,
        bool closed,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform = null)
    {
        if (points.Count < 2)
        {
            return;
        }

        var transformed = points
            .Select(point => ToCanvas(point, primitive, coordinateSystem, drawingBounds, modelTransform))
            .ToArray();
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(transformed[0], closed && ShouldFill(primitive));
            foreach (var point in transformed.Skip(1))
            {
                geometryContext.LineTo(point);
            }

            geometryContext.EndFigure(closed);
        }

        var pen = CreatePen(primitive);
        context.DrawGeometry(CreateFillBrush(primitive), pen, geometry);
        if (primitive.Kind == ModelicaGraphicKind.Line && pen is not null)
        {
            var arrows = primitive.Arrows.Count >= 2
                ? primitive.Arrows
                : [ModelicaArrow.None, ModelicaArrow.None];
            DrawArrow(context, transformed[0], transformed[1], arrows[0], primitive.ArrowSize, pen);
            DrawArrow(
                context,
                transformed[^1],
                transformed[^2],
                arrows[1],
                primitive.ArrowSize,
                pen);
        }
    }

    private static void DrawArrow(
        DrawingContext context,
        Point tip,
        Point adjacent,
        ModelicaArrow arrow,
        double arrowSize,
        Pen pen)
    {
        if (arrow is ModelicaArrow.None or ModelicaArrow.Unknown)
        {
            return;
        }

        var deltaX = adjacent.X - tip.X;
        var deltaY = adjacent.Y - tip.Y;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance <= double.Epsilon)
        {
            return;
        }

        var unitX = deltaX / distance;
        var unitY = deltaY / distance;
        var length = Math.Max(5, arrowSize * 2);
        var halfWidth = length * 0.45;
        var baseX = tip.X + (unitX * length);
        var baseY = tip.Y + (unitY * length);
        var left = new Point(baseX - (unitY * halfWidth), baseY + (unitX * halfWidth));
        var right = new Point(baseX + (unitY * halfWidth), baseY - (unitX * halfWidth));
        if (arrow == ModelicaArrow.Open)
        {
            context.DrawLine(pen, tip, left);
            context.DrawLine(pen, tip, right);
            return;
        }

        if (arrow == ModelicaArrow.Half)
        {
            context.DrawLine(pen, tip, left);
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(tip, true);
            geometryContext.LineTo(left);
            geometryContext.LineTo(right);
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(pen.Brush, pen, geometry);
    }

    private void DrawText(
        DrawingContext context,
        ModelicaGraphicPrimitive primitive,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds,
        string className,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform)
    {
        var extent = primitive.Extent?.StaticValue;
        if (extent is null || string.IsNullOrEmpty(primitive.TextString))
        {
            return;
        }

        var corners = RectanglePoints(primitive.Extent)
            .Select(point => ToCanvas(point, primitive, coordinateSystem, drawingBounds, modelTransform))
            .ToArray();
        if (corners.Length == 0)
        {
            return;
        }

        var bounds = BoundingRect(corners);
        var text = primitive.TextString.Replace("%name", ClassLeafName(className), StringComparison.Ordinal);
        var fontFamily = string.IsNullOrWhiteSpace(primitive.FontName)
            ? FontFamily.Default
            : new FontFamily(primitive.FontName);
        var fontWeight = primitive.TextStyles.Contains("Bold", StringComparer.OrdinalIgnoreCase)
            ? FontWeight.Bold
            : FontWeight.Normal;
        var fontStyle = primitive.TextStyles.Contains("Italic", StringComparer.OrdinalIgnoreCase)
            ? FontStyle.Italic
            : FontStyle.Normal;
        var fontSize = primitive.FontSize > 0
            ? Math.Clamp(primitive.FontSize, 7, Math.Max(7, bounds.Height))
            : Math.Clamp(bounds.Height * 0.55, 7, 32);
        var textLayout = new TextLayout(
            text,
            new Typeface(fontFamily, fontStyle, fontWeight),
            fontSize,
            CreateBrush(primitive.TextColor),
            ToTextAlignment(primitive.TextAlignment),
            TextWrapping.NoWrap,
            TextTrimming.CharacterEllipsis,
            maxWidth: Math.Max(1, bounds.Width),
            maxHeight: Math.Max(1, bounds.Height));
        var x = primitive.TextAlignment switch
        {
            ModelicaTextAlignment.Left => bounds.Left,
            ModelicaTextAlignment.Right => bounds.Right - textLayout.Width,
            _ => bounds.Center.X - (textLayout.Width / 2),
        };
        var y = bounds.Center.Y - (textLayout.Height / 2);
        textLayout.Draw(context, new Point(x, y));
    }

    private void DrawBitmapPlaceholder(
        DrawingContext context,
        ModelicaGraphicPrimitive primitive,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform)
    {
        var corners = RectanglePoints(primitive.Extent)
            .Select(point => ToCanvas(point, primitive, coordinateSystem, drawingBounds, modelTransform))
            .ToArray();
        if (corners.Length != 4)
        {
            return;
        }

        var placeholder = primitive with
        {
            Style = primitive.Style with
            {
                FillPattern = ModelicaFillPattern.None,
                LineColor = new ModelicaColor(130, 130, 130),
                LinePattern = ModelicaLinePattern.Solid,
            },
        };
        DrawPolyline(context, placeholder, coordinateSystem, drawingBounds, RectanglePoints(primitive.Extent), true, modelTransform);
        var pen = CreatePen(placeholder);
        if (pen is not null)
        {
            context.DrawLine(pen, corners[0], corners[2]);
            context.DrawLine(pen, corners[1], corners[3]);
        }
    }

    private Point ToCanvas(
        ModelicaPoint point,
        ModelicaGraphicPrimitive primitive,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds,
        Func<ModelicaPoint, ModelicaPoint>? modelTransform)
    {
        var modelPoint = ModelicaGraphicTransform.Apply(
            point,
            primitive.Origin.StaticValue,
            primitive.Rotation.StaticValue);
        if (modelTransform is not null)
        {
            modelPoint = modelTransform(modelPoint);
        }

        var canvasPoint = _coordinateTransformer.ModelicaToCanvas(
            modelPoint,
            coordinateSystem,
            drawingBounds.Width,
            drawingBounds.Height);
        return new Point(drawingBounds.X + canvasPoint.X, drawingBounds.Y + canvasPoint.Y);
    }

    private Point ToCanvas(
        ModelicaPoint point,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds)
    {
        var canvasPoint = _coordinateTransformer.ModelicaToCanvas(
            point,
            coordinateSystem,
            drawingBounds.Width,
            drawingBounds.Height);
        return new Point(drawingBounds.X + canvasPoint.X, drawingBounds.Y + canvasPoint.Y);
    }

    private ModelicaPoint ToModelica(
        Point point,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds) => _coordinateTransformer.CanvasToModelica(
            new CanvasPoint(point.X - drawingBounds.X, point.Y - drawingBounds.Y),
            coordinateSystem,
            drawingBounds.Width,
            drawingBounds.Height);

    private double ModelTolerance(
        Point point,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds)
    {
        const double canvasTolerance = 7;
        var modelPoint = ToModelica(point, coordinateSystem, drawingBounds);
        var offsetX = ToModelica(new Point(point.X + canvasTolerance, point.Y), coordinateSystem, drawingBounds);
        var offsetY = ToModelica(new Point(point.X, point.Y + canvasTolerance), coordinateSystem, drawingBounds);
        return Math.Max(Math.Abs(offsetX.X - modelPoint.X), Math.Abs(offsetY.Y - modelPoint.Y));
    }

    private double ModelDistanceForCanvasPixels(
        double canvasPixels,
        ModelicaCoordinateSystem coordinateSystem,
        Rect drawingBounds)
    {
        var center = drawingBounds.Center;
        var modelPoint = ToModelica(center, coordinateSystem, drawingBounds);
        var offsetX = ToModelica(new Point(center.X + canvasPixels, center.Y), coordinateSystem, drawingBounds);
        var offsetY = ToModelica(new Point(center.X, center.Y + canvasPixels), coordinateSystem, drawingBounds);
        return Math.Max(Math.Abs(offsetX.X - modelPoint.X), Math.Abs(offsetY.Y - modelPoint.Y));
    }

    private void HandleConnectionToolClick(ModelicaPoint point, double tolerance)
    {
        var endpoints = ModelicaConnectionEditor.GetConnectorEndpoints(InstanceSnapshot);
        var endpoint = ModelicaConnectionEditor.HitTestEndpoint(endpoints, point, tolerance);
        if (endpoint is null)
        {
            ResetConnectionToolState();
            return;
        }

        if (_connectionStartEndpoint is null)
        {
            _connectionStartEndpoint = endpoint;
            _hoverConnectorEndpoint = endpoint;
            _connectionPreviewPoint = endpoint.Position;
            SetSelection(ModelicaDiagramHit.None, []);
            InvalidateVisual();
            return;
        }

        var compatibility = ModelicaConnectionEditor.CheckCompatibility(
            _connectionStartEndpoint,
            endpoint,
            InstanceSnapshot?.Connections);
        if (!compatibility.IsCompatible)
        {
            _hoverConnectorEndpoint = endpoint;
            _connectionPreviewPoint = endpoint.Position;
            InvalidateVisual();
            return;
        }

        var start = _connectionStartEndpoint;
        var route = ModelicaConnectionEditor.CreateOrthogonalRoute(start.Position, endpoint.Position);
        ResetConnectionToolState();
        ConnectionCreationRequested?.Invoke(
            this,
            new ModelicaConnectionCreationRequestedEventArgs(start, endpoint, route));
    }

    private bool TryStartConnectionRouteDrag(ModelicaPoint point, double tolerance)
    {
        if (SelectedConnection is not { IsInherited: false } connection)
        {
            return false;
        }

        var points = EffectiveConnectionPoints(connection, InstanceSnapshot);
        var closestIndex = -1;
        var closestDistance = double.PositiveInfinity;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var first = points[index];
            var second = points[index + 1];
            if (Math.Abs(first.X - second.X) > 1e-9
                && Math.Abs(first.Y - second.Y) > 1e-9)
            {
                continue;
            }

            var midpoint = new ModelicaPoint(
                (first.X + second.X) / 2,
                (first.Y + second.Y) / 2);
            var distance = Distance(point, midpoint);
            if (distance <= tolerance && distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        if (closestIndex < 0)
        {
            return false;
        }

        _routeDragConnection = connection;
        _routeDragSegmentIndex = closestIndex;
        _routeDragPreview = null;
        return true;
    }

    private static IReadOnlyList<ModelicaPoint> EffectiveConnectionPoints(
        ModelicaDiagramConnection connection,
        ModelicaModelInstanceSnapshot? instance = null)
    {
        if (instance is not null)
        {
            return ModelicaConnectionEditor.GetConnectionPoints(instance, connection);
        }

        if (connection.Line is not { } line)
        {
            return [];
        }

        return line.Points.Select(point => ModelicaGraphicTransform.Apply(
            point,
            line.Origin.StaticValue,
            line.Rotation.StaticValue)).ToArray();
    }

    private static ModelicaGraphicPrimitive? CreateRenderableConnectionLine(
        ModelicaModelInstanceSnapshot? instance,
        ModelicaDiagramConnection connection)
    {
        if (connection.Line is { Points.Count: >= 2 } line)
        {
            return line;
        }

        if (instance is null)
        {
            return null;
        }

        var points = ModelicaConnectionEditor.GetConnectionPoints(instance, connection);
        if (points.Count < 2)
        {
            return null;
        }

        return new ModelicaGraphicPrimitive
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
            RawJson = "{}",
        };
    }

    private void ResetConnectionToolState()
    {
        _hoverConnectorEndpoint = null;
        _connectionStartEndpoint = null;
        _connectionPreviewPoint = null;
        _connectionPointerGestureStarted = false;
        InvalidateVisual();
    }

    private void ResetConnectionRouteDrag()
    {
        _routeDragConnection = null;
        _routeDragSegmentIndex = -1;
        _routeDragPreview = null;
        InvalidateVisual();
    }

    private static double Distance(ModelicaPoint first, ModelicaPoint second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private bool TryStartPlacementHandleDrag(
        ModelicaPoint point,
        double tolerance,
        double rotationHandleOffset)
    {
        if (EffectiveSelectedComponents().Count != 1
            || SelectedComponent is not { IsInherited: false, Placement: not null } component)
        {
            return false;
        }

        var transformation = ShowIcon
            ? component.Placement.IconTransformation
            : component.Placement.Transformation;
        if (transformation is null)
        {
            return false;
        }

        var handle = ModelicaPlacementEditor.HitTestHandle(
            transformation,
            point,
            tolerance,
            rotationHandleOffset);
        if (handle is null)
        {
            return false;
        }

        _dragComponent = component;
        _dragStartTransformation = transformation;
        _dragPreviewTransformation = null;
        _dragStartPoint = point;
        _dragIconLayer = ShowIcon;
        _dragHandle = handle;
        _dragKind = handle == ModelicaPlacementHandle.Rotation
            ? ModelicaPlacementEditKind.Rotate
            : ModelicaPlacementEditKind.Resize;
        return true;
    }

    private IReadOnlyList<ModelicaComponentInstance> EffectiveSelectedComponents()
    {
        if (SelectedComponents.Count > 0)
        {
            return SelectedComponents;
        }

        return SelectedComponent is { } component ? [component] : [];
    }

    private IReadOnlyDictionary<string, ModelicaTransformation> GetEditableSelectionTransformations(
        IReadOnlyList<ModelicaComponentInstance> components)
    {
        var transformations = new Dictionary<string, ModelicaTransformation>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            if (component.IsInherited || component.Placement is null)
            {
                return new Dictionary<string, ModelicaTransformation>();
            }

            var transformation = ShowIcon
                ? component.Placement.IconTransformation
                : component.Placement.Transformation;
            if (transformation is null)
            {
                return new Dictionary<string, ModelicaTransformation>();
            }

            transformations[component.Name] = transformation;
        }

        return transformations;
    }

    private static IReadOnlyList<ModelicaComponentInstance> UpdateComponentSelection(
        IReadOnlyList<ModelicaComponentInstance> current,
        ModelicaComponentInstance component,
        bool toggle)
    {
        if (!toggle)
        {
            return current.Any(item => string.Equals(item.Name, component.Name, StringComparison.Ordinal))
                ? current
                : [component];
        }

        if (current.Any(item => string.Equals(item.Name, component.Name, StringComparison.Ordinal)))
        {
            return current
                .Where(item => !string.Equals(item.Name, component.Name, StringComparison.Ordinal))
                .ToArray();
        }

        return [.. current, component];
    }

    private static IReadOnlyList<ModelicaComponentInstance> MergeSelections(
        IReadOnlyList<ModelicaComponentInstance> first,
        IReadOnlyList<ModelicaComponentInstance> second) =>
        first.Concat(second)
            .DistinctBy(static component => component.Name, StringComparer.Ordinal)
            .ToArray();

    private void SetSelection(
        ModelicaDiagramHit selection,
        IReadOnlyList<ModelicaComponentInstance> components)
    {
        SetCurrentValue(SelectedComponentsProperty, components);
        SetCurrentValue(SelectedComponentProperty, selection.Component);
        SetCurrentValue(SelectedConnectionProperty, selection.Connection);
        GraphicalSelectionChanged?.Invoke(
            this,
            new ModelicaGraphicsSelectionChangedEventArgs(selection, components));
        InvalidateVisual();
    }

    private Rect CreateDrawingBounds() => new(
        CanvasPadding,
        CanvasPadding,
        Math.Max(0, Bounds.Width - (CanvasPadding * 2)),
        Math.Max(0, Bounds.Height - (CanvasPadding * 2)));

    private void ResetDrag()
    {
        _dragComponent = null;
        _dragStartTransformation = null;
        _dragPreviewTransformation = null;
        _dragHandle = null;
        _dragKind = ModelicaPlacementEditKind.Move;
        _dragStartTransformations = null;
        _dragPreviewTransformations = null;
        InvalidateVisual();
    }

    private void ResetBoxSelection()
    {
        _boxSelectionStart = null;
        _boxSelectionCurrent = null;
        _boxSelectionBase = [];
        InvalidateVisual();
    }

    private bool HasRenderableInstanceGraphics(ModelicaModelInstanceSnapshot? instance) =>
        instance?.Components.Any(component =>
            component.TypeGraphics?.Icon is not null
            && (ShowIcon
                ? component.Placement is { IconVisible: true, IconTransformation: not null }
                : component.Placement is { Visible: true })) == true
        || (!ShowIcon && instance?.Connections.Count > 0);

    private static IReadOnlyList<ModelicaPoint> RectanglePoints(ModelicaAnnotationValue<ModelicaExtent>? extent)
    {
        if (extent is null)
        {
            return [];
        }

        var value = extent.StaticValue;
        return
        [
            value.First,
            new ModelicaPoint(value.Second.X, value.First.Y),
            value.Second,
            new ModelicaPoint(value.First.X, value.Second.Y),
        ];
    }

    private static IReadOnlyList<ModelicaPoint> EllipsePoints(ModelicaGraphicPrimitive primitive)
    {
        var extent = primitive.Extent?.StaticValue;
        if (extent is null)
        {
            return [];
        }

        var centerX = (extent.Value.First.X + extent.Value.Second.X) / 2;
        var centerY = (extent.Value.First.Y + extent.Value.Second.Y) / 2;
        var radiusX = extent.Value.Width / 2;
        var radiusY = extent.Value.Height / 2;
        var sweep = primitive.EndAngle - primitive.StartAngle;
        if (Math.Abs(sweep) < double.Epsilon)
        {
            sweep = 360;
        }

        var points = Enumerable.Range(0, EllipseSegments + 1)
            .Select(index =>
            {
                var angle = primitive.StartAngle + (sweep * index / EllipseSegments);
                var radians = angle * (Math.PI / 180d);
                return new ModelicaPoint(
                    centerX + (radiusX * Math.Cos(radians)),
                    centerY + (radiusY * Math.Sin(radians)));
            })
            .ToList();
        if (Math.Abs(sweep) < 360 && primitive.EllipseClosure == ModelicaEllipseClosure.Radial)
        {
            points.Add(new ModelicaPoint(centerX, centerY));
        }

        return points;
    }

    private static bool IsFullEllipse(ModelicaGraphicPrimitive primitive) =>
        Math.Abs(primitive.EndAngle - primitive.StartAngle) >= 360
        || Math.Abs(primitive.EndAngle - primitive.StartAngle) < double.Epsilon;

    private static Rect BoundingRect(IReadOnlyList<Point> points)
    {
        var minimumX = points.Min(static point => point.X);
        var minimumY = points.Min(static point => point.Y);
        var maximumX = points.Max(static point => point.X);
        var maximumY = points.Max(static point => point.Y);
        return new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }

    private static IBrush? CreateFillBrush(ModelicaGraphicPrimitive primitive) =>
        ShouldFill(primitive) ? CreateBrush(primitive.Style.FillColor) : null;

    private static bool ShouldFill(ModelicaGraphicPrimitive primitive) =>
        primitive.Style.FillPattern != ModelicaFillPattern.None;

    private static Pen? CreatePen(ModelicaGraphicPrimitive primitive)
    {
        if (primitive.Style.LinePattern == ModelicaLinePattern.None)
        {
            return null;
        }

        return new Pen(CreateBrush(primitive.Style.LineColor), Math.Max(0.75, primitive.Style.LineThickness * 2))
        {
            DashStyle = primitive.Style.LinePattern switch
            {
                ModelicaLinePattern.Dash => DashStyle.Dash,
                ModelicaLinePattern.Dot => DashStyle.Dot,
                ModelicaLinePattern.DashDot => DashStyle.DashDot,
                ModelicaLinePattern.DashDotDot => DashStyle.DashDotDot,
                _ => null,
            },
        };
    }

    private static SolidColorBrush CreateBrush(ModelicaColor color)
    {
        var effectiveColor = color.UsesInheritedColor ? ModelicaColor.Black : color;
        return new SolidColorBrush(Color.FromRgb(
            (byte)Math.Clamp(effectiveColor.Red, 0, 255),
            (byte)Math.Clamp(effectiveColor.Green, 0, 255),
            (byte)Math.Clamp(effectiveColor.Blue, 0, 255)));
    }

    private static TextAlignment ToTextAlignment(ModelicaTextAlignment alignment) => alignment switch
    {
        ModelicaTextAlignment.Left => TextAlignment.Left,
        ModelicaTextAlignment.Right => TextAlignment.Right,
        _ => TextAlignment.Center,
    };

    private static string ClassLeafName(string className) =>
        className.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? className;

    private void DrawEmptyState(DrawingContext context, string message)
    {
        var layout = new TextLayout(message, new Typeface(FontFamily.Default), 13, Brushes.Gray);
        layout.Draw(
            context,
            new Point(
                Math.Max(12, (Bounds.Width - layout.Width) / 2),
                Math.Max(12, (Bounds.Height - layout.Height) / 2)));
    }
}

public sealed class ModelicaGraphicsSelectionChangedEventArgs(
    ModelicaDiagramHit selection,
    IReadOnlyList<ModelicaComponentInstance> components) : EventArgs
{
    public ModelicaDiagramHit Selection { get; } = selection;
    public IReadOnlyList<ModelicaComponentInstance> Components { get; } = components;
}

public sealed record ModelicaComponentTransformationRequest(
    ModelicaComponentInstance Component,
    ModelicaTransformation Transformation);

public sealed class ModelicaComponentPlacementRequestedEventArgs(
    IReadOnlyList<ModelicaComponentTransformationRequest> requests,
    bool iconLayer,
    ModelicaPlacementEditKind kind) : EventArgs
{
    public IReadOnlyList<ModelicaComponentTransformationRequest> Requests { get; } = requests;
    public bool IconLayer { get; } = iconLayer;
    public ModelicaPlacementEditKind Kind { get; } = kind;
}

public sealed class ModelicaComponentDropRequestedEventArgs(
    ModelicaClassInfo componentType,
    ModelicaPoint origin) : EventArgs
{
    public ModelicaClassInfo ComponentType { get; } = componentType;
    public ModelicaPoint Origin { get; } = origin;
}

public sealed class ModelicaComponentDeletionRequestedEventArgs(
    IReadOnlyList<ModelicaComponentInstance> components) : EventArgs
{
    public IReadOnlyList<ModelicaComponentInstance> Components { get; } = components;
}

public sealed class ModelicaComponentDuplicationRequestedEventArgs(
    IReadOnlyList<ModelicaComponentInstance> components) : EventArgs
{
    public IReadOnlyList<ModelicaComponentInstance> Components { get; } = components;
}

public sealed class ModelicaConnectionCreationRequestedEventArgs(
    ModelicaConnectorEndpoint left,
    ModelicaConnectorEndpoint right,
    IReadOnlyList<ModelicaPoint> route) : EventArgs
{
    public ModelicaConnectorEndpoint Left { get; } = left;
    public ModelicaConnectorEndpoint Right { get; } = right;
    public IReadOnlyList<ModelicaPoint> Route { get; } = route;
}

public sealed class ModelicaConnectionRouteRequestedEventArgs(
    ModelicaDiagramConnection connection,
    IReadOnlyList<ModelicaPoint> route) : EventArgs
{
    public ModelicaDiagramConnection Connection { get; } = connection;
    public IReadOnlyList<ModelicaPoint> Route { get; } = route;
}

public sealed class ModelicaConnectionDeletionRequestedEventArgs(
    ModelicaDiagramConnection connection) : EventArgs
{
    public ModelicaDiagramConnection Connection { get; } = connection;
}
