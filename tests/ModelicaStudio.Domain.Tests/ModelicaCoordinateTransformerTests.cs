using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaCoordinateTransformerTests
{
    private static readonly ModelicaCoordinateSystem DefaultSystem = new(
        new ModelicaExtent(new ModelicaPoint(-100, -100), new ModelicaPoint(100, 100)));

    private readonly ModelicaCoordinateTransformer _transformer = new();

    [Theory]
    [InlineData(-100, 100, 0, 0)]
    [InlineData(0, 0, 100, 100)]
    [InlineData(100, -100, 200, 200)]
    public void ModelicaToCanvas_FlipsYAxis(double modelX, double modelY, double canvasX, double canvasY)
    {
        var actual = _transformer.ModelicaToCanvas(new ModelicaPoint(modelX, modelY), DefaultSystem, 200, 200);

        Assert.Equal(canvasX, actual.X, 8);
        Assert.Equal(canvasY, actual.Y, 8);
    }

    [Fact]
    public void PreserveAspectRatio_CentersLetterboxedCoordinates()
    {
        var actual = _transformer.ModelicaToCanvas(new ModelicaPoint(-100, 100), DefaultSystem, 400, 200);

        Assert.Equal(100, actual.X, 8);
        Assert.Equal(0, actual.Y, 8);
    }

    [Fact]
    public void DisabledAspectRatio_UsesIndependentAxisScales()
    {
        var system = DefaultSystem with { PreserveAspectRatio = false };
        var actual = _transformer.ModelicaToCanvas(new ModelicaPoint(0, 0), system, 400, 200);

        Assert.Equal(200, actual.X, 8);
        Assert.Equal(100, actual.Y, 8);
    }

    [Theory]
    [InlineData(-91.25, 62.5)]
    [InlineData(0, 0)]
    [InlineData(99.75, -45.125)]
    public void Transform_RoundTrips(double x, double y)
    {
        var model = new ModelicaPoint(x, y);
        var canvas = _transformer.ModelicaToCanvas(model, DefaultSystem, 713, 411);
        var roundTrip = _transformer.CanvasToModelica(canvas, DefaultSystem, 713, 411);

        Assert.Equal(model.X, roundTrip.X, 8);
        Assert.Equal(model.Y, roundTrip.Y, 8);
    }

    [Fact]
    public void ZeroSizedExtent_IsRejected()
    {
        var system = new ModelicaCoordinateSystem(
            new ModelicaExtent(new ModelicaPoint(0, 0), new ModelicaPoint(0, 10)));

        Assert.Throws<ArgumentException>(() =>
            _transformer.ModelicaToCanvas(new ModelicaPoint(0, 0), system, 200, 200));
    }
}
