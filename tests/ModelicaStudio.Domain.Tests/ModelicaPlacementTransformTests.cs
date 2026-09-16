using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaPlacementTransformTests
{
    private static readonly ModelicaExtent Source = new(
        new ModelicaPoint(-100, -100),
        new ModelicaPoint(100, 100));

    [Fact]
    public void Apply_MapsSourceExtentIntoPlacementExtent()
    {
        var placement = new ModelicaTransformation(
            new ModelicaPoint(20, 30),
            new ModelicaExtent(new ModelicaPoint(-10, -20), new ModelicaPoint(10, 20)),
            0);

        Assert.Equal(new ModelicaPoint(10, 10), ModelicaPlacementTransform.Apply(Source.First, Source, placement));
        Assert.Equal(new ModelicaPoint(30, 50), ModelicaPlacementTransform.Apply(Source.Second, Source, placement));
        Assert.Equal(new ModelicaPoint(20, 30), ModelicaPlacementTransform.Apply(new ModelicaPoint(0, 0), Source, placement));
    }

    [Fact]
    public void Apply_PreservesFlippedPlacementAxes()
    {
        var placement = new ModelicaTransformation(
            new ModelicaPoint(0, 0),
            new ModelicaExtent(new ModelicaPoint(10, -20), new ModelicaPoint(-10, 20)),
            0);

        Assert.Equal(new ModelicaPoint(10, -20), ModelicaPlacementTransform.Apply(Source.First, Source, placement));
        Assert.Equal(new ModelicaPoint(-10, 20), ModelicaPlacementTransform.Apply(Source.Second, Source, placement));
    }

    [Fact]
    public void Apply_RotatesMappedPointAroundPlacementOrigin()
    {
        var placement = new ModelicaTransformation(
            new ModelicaPoint(20, 30),
            new ModelicaExtent(new ModelicaPoint(-10, -10), new ModelicaPoint(10, 10)),
            90);

        var result = ModelicaPlacementTransform.Apply(new ModelicaPoint(100, 0), Source, placement);

        Assert.Equal(20, result.X, 10);
        Assert.Equal(40, result.Y, 10);
    }
}
