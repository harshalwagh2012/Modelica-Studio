using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.UI.ViewModels;

namespace ModelicaStudio.UI.Tests;

public sealed class LibraryNodeViewModelTests
{
    [Fact]
    public void InstantiableState_ReflectsCompilerClassMetadata()
    {
        var model = new LibraryNodeViewModel(
            new ModelicaClassInfo("Demo.Plant", "Plant", ModelicaClassKind.Model));
        var package = new LibraryNodeViewModel(
            new ModelicaClassInfo("Demo.Utilities", "Utilities", ModelicaClassKind.Package));
        var partial = new LibraryNodeViewModel(
            new ModelicaClassInfo("Demo.Base", "Base", ModelicaClassKind.Model, IsPartial: true));

        Assert.True(model.CanInstantiate);
        Assert.False(package.CanInstantiate);
        Assert.False(partial.CanInstantiate);
    }

    [Fact]
    public void Constructor_ExpandableClassContainsLazyPlaceholder()
    {
        var node = CreateNode((_, _) => Task.FromResult<IReadOnlyList<ModelicaClassInfo>>([]));

        var placeholder = Assert.Single(node.Children);

        Assert.True(placeholder.IsPlaceholder);
        Assert.Equal("Expand to load", placeholder.Name);
    }

    [Fact]
    public async Task LoadChildren_LoadsOnceAndSortsChildren()
    {
        var calls = 0;
        var node = CreateNode((_, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<ModelicaClassInfo>>(
            [
                new("Modelica.Zeta", "Zeta", ModelicaClassKind.Package),
                new("Modelica.Alpha", "Alpha", ModelicaClassKind.Package),
            ]);
        });

        await node.LoadChildrenAsync();
        await node.LoadChildrenAsync();

        Assert.Equal(1, calls);
        Assert.Equal(["Alpha", "Zeta"], node.Children.Select(static child => child.Name));
        Assert.All(node.Children, static child => Assert.False(child.IsPlaceholder));
    }

    [Fact]
    public async Task LoadChildren_FailureLeavesRetryPlaceholderAndCanRetry()
    {
        var calls = 0;
        var node = CreateNode((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new IOException("compiler unavailable");
            }

            return Task.FromResult<IReadOnlyList<ModelicaClassInfo>>(
                [new("Modelica.Blocks", "Blocks", ModelicaClassKind.Package)]);
        });

        await Assert.ThrowsAsync<IOException>(() => node.LoadChildrenAsync());
        Assert.True(Assert.Single(node.Children).IsPlaceholder);

        await node.LoadChildrenAsync();

        Assert.Equal(2, calls);
        Assert.Equal("Blocks", Assert.Single(node.Children).Name);
    }

    [Fact]
    public void Constructor_TypeClassIsALeaf()
    {
        var node = new LibraryNodeViewModel(
            new ModelicaClassInfo("Modelica.Units.SI.Time", "Time", ModelicaClassKind.Type),
            (_, _) => Task.FromResult<IReadOnlyList<ModelicaClassInfo>>([]));

        Assert.Empty(node.Children);
    }

    private static LibraryNodeViewModel CreateNode(
        Func<string, CancellationToken, Task<IReadOnlyList<ModelicaClassInfo>>> loader) =>
        new(new ModelicaClassInfo("Modelica", "Modelica", ModelicaClassKind.Package), loader);
}
