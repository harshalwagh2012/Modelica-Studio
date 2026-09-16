using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaGraphicTransformTests
{
    [Fact]
    public void Apply_TranslatesWithoutRotation()
    {
        var result = ModelicaGraphicTransform.Apply(new ModelicaPoint(4, -2), new ModelicaPoint(10, 3), 0);

        Assert.Equal(14, result.X, 10);
        Assert.Equal(1, result.Y, 10);
    }

    [Fact]
    public void Apply_RotatesCounterClockwiseThenTranslates()
    {
        var result = ModelicaGraphicTransform.Apply(new ModelicaPoint(2, 0), new ModelicaPoint(10, 3), 90);

        Assert.Equal(10, result.X, 10);
        Assert.Equal(5, result.Y, 10);
    }

    [Fact]
    public void Apply_RejectsNonFiniteRotation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ModelicaGraphicTransform.Apply(new ModelicaPoint(1, 1), new ModelicaPoint(0, 0), double.NaN));
    }
}
