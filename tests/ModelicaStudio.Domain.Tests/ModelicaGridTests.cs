using ModelicaStudio.Domain.Graphics;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaGridTests
{
    [Theory]
    [InlineData(3.1, 4.9, 4, 4)]
    [InlineData(-3.1, -5, -4, -6)]
    [InlineData(1, -1, 2, -2)]
    public void Snap_UsesIndependentModelicaGridAxes(
        double x,
        double y,
        double expectedX,
        double expectedY)
    {
        var result = ModelicaGrid.Snap(new ModelicaPoint(x, y), new ModelicaPoint(2, 2));

        Assert.Equal(new ModelicaPoint(expectedX, expectedY), result);
    }

    [Fact]
    public void Move_AppliesRequestedGridSteps()
    {
        var result = ModelicaGrid.Move(new ModelicaPoint(10, 20), new ModelicaPoint(2, 5), -5, 1);

        Assert.Equal(new ModelicaPoint(0, 25), result);
    }
}
