using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaPlacementEditorTests
{
    private static readonly ModelicaTransformation Placement = new(
        new ModelicaPoint(20, 10),
        new ModelicaExtent(new ModelicaPoint(-10, -5), new ModelicaPoint(10, 5)),
        0);

    [Fact]
    public void GetHandlePoints_RotatesCornersAndRotationHandle()
    {
        var handles = ModelicaPlacementEditor.GetHandlePoints(Placement with { Rotation = 90 });

        AssertPoint(25, 0, handles[ModelicaPlacementHandle.First]);
        AssertPoint(15, 20, handles[ModelicaPlacementHandle.Second]);
        AssertPoint(3, 10, handles[ModelicaPlacementHandle.Rotation]);
    }

    [Fact]
    public void HitTestHandle_PrefersRotationHandle()
    {
        var handle = ModelicaPlacementEditor.HitTestHandle(
            Placement,
            new ModelicaPoint(20.5, 27),
            1);

        Assert.Equal(ModelicaPlacementHandle.Rotation, handle);
    }

    [Fact]
    public void Resize_SnapsDraggedCornerAndKeepsOppositeCorner()
    {
        var resized = ModelicaPlacementEditor.Resize(
            Placement,
            ModelicaPlacementHandle.Second,
            new ModelicaPoint(33, 18),
            new ModelicaPoint(2, 2));

        Assert.Equal(new ModelicaPoint(-10, -5), resized.Extent.First);
        Assert.Equal(new ModelicaPoint(14, 8), resized.Extent.Second);
        Assert.Equal(Placement.Origin, resized.Origin);
    }

    [Fact]
    public void Resize_UsesPlacementLocalCoordinatesWhenRotated()
    {
        var resized = ModelicaPlacementEditor.Resize(
            Placement with { Rotation = 90 },
            ModelicaPlacementHandle.Second,
            new ModelicaPoint(12, 24),
            new ModelicaPoint(2, 2));

        Assert.Equal(new ModelicaPoint(14, 8), resized.Extent.Second);
    }

    [Fact]
    public void Resize_PreventsCollapsedExtentButAllowsMirroring()
    {
        var collapsed = ModelicaPlacementEditor.Resize(
            Placement,
            ModelicaPlacementHandle.First,
            new ModelicaPoint(30, 15),
            new ModelicaPoint(2, 2));
        var mirrored = ModelicaPlacementEditor.Resize(
            Placement,
            ModelicaPlacementHandle.First,
            new ModelicaPoint(34, 19),
            new ModelicaPoint(2, 2));

        Assert.Equal(new ModelicaPoint(8, 3), collapsed.Extent.First);
        Assert.Equal(new ModelicaPoint(14, 10), mirrored.Extent.First);
    }

    [Theory]
    [InlineData(20, 40, 0)]
    [InlineData(50, 10, 270)]
    [InlineData(20, -20, 180)]
    [InlineData(-10, 10, 90)]
    [InlineData(0, 30, 45)]
    public void RotateTowards_UsesPositiveYAxisAsZeroAndSnaps(
        double x,
        double y,
        double expectedRotation)
    {
        var rotated = ModelicaPlacementEditor.RotateTowards(Placement, new ModelicaPoint(x, y));

        Assert.Equal(expectedRotation, rotated.Rotation, 8);
    }

    [Fact]
    public void RotateTowards_LeavesPlacementUnchangedAtOrigin()
    {
        var rotated = ModelicaPlacementEditor.RotateTowards(Placement, Placement.Origin);

        Assert.Same(Placement, rotated);
    }

    [Theory]
    [InlineData(350, 20, 10)]
    [InlineData(10, -20, 350)]
    public void RotateBy_NormalizesRotation(double initial, double delta, double expected)
    {
        var rotated = ModelicaPlacementEditor.RotateBy(Placement with { Rotation = initial }, delta);

        Assert.Equal(expected, rotated.Rotation, 8);
    }

    private static void AssertPoint(double x, double y, ModelicaPoint actual)
    {
        Assert.Equal(x, actual.X, 8);
        Assert.Equal(y, actual.Y, 8);
    }
}
