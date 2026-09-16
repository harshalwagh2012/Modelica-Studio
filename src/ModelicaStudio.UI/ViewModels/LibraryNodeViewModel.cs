using System.Collections.ObjectModel;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.UI.ViewModels;

public sealed class LibraryNodeViewModel : ObservableObject
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ModelicaClassInfo>>>? _loadChildren;
    private readonly Action<Exception>? _reportError;
    private bool _childrenLoaded;
    private bool _isExpanded;
    private bool _isLoading;

    public LibraryNodeViewModel(
        ModelicaClassInfo model,
        Func<string, CancellationToken, Task<IReadOnlyList<ModelicaClassInfo>>>? loadChildren = null,
        Action<Exception>? reportError = null)
    {
        Model = model;
        Name = model.Name;
        FullName = model.FullName;
        Kind = model.Kind;
        _loadChildren = loadChildren;
        _reportError = reportError;
        if (loadChildren is not null && CanContainClasses(model.Kind))
        {
            Children.Add(CreatePlaceholder("Expand to load"));
        }
    }

    private LibraryNodeViewModel(string statusText)
    {
        Name = statusText;
        FullName = string.Empty;
        Kind = ModelicaClassKind.Unknown;
        IsPlaceholder = true;
    }

    public string Name { get; }
    public string FullName { get; }
    public ModelicaClassKind Kind { get; }
    public ModelicaClassInfo? Model { get; }
    public bool CanInstantiate => Model is not null && ModelicaComponentType.IsInstantiable(Model);
    public string KindLabel => IsPlaceholder ? string.Empty : Kind.ToString().ToUpperInvariant();
    public bool IsPlaceholder { get; }
    public ObservableCollection<LibraryNodeViewModel> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value && !_childrenLoaded)
            {
                _ = LoadChildrenSafelyAsync();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task LoadChildrenAsync(CancellationToken cancellationToken = default)
    {
        if (_childrenLoaded || IsLoading || _loadChildren is null || IsPlaceholder)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var classes = await _loadChildren(FullName, cancellationToken).ConfigureAwait(true);
            Children.Clear();
            foreach (var child in classes.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                Children.Add(new LibraryNodeViewModel(child, _loadChildren, _reportError));
            }

            _childrenLoaded = true;
        }
        catch
        {
            Children.Clear();
            Children.Add(CreatePlaceholder("Unable to load · collapse and retry"));
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadChildrenSafelyAsync()
    {
        try
        {
            await LoadChildrenAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _reportError?.Invoke(exception);
        }
    }

    private static bool CanContainClasses(ModelicaClassKind kind) => kind is not ModelicaClassKind.Type;

    private static LibraryNodeViewModel CreatePlaceholder(string text) => new(text);
}
