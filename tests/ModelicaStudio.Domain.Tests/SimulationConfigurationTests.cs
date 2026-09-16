using ModelicaStudio.Domain.Simulation;

namespace ModelicaStudio.Domain.Tests;

public sealed class SimulationConfigurationTests
{
    [Fact]
    public void Defaults_AreValid() => new SimulationConfiguration().Validate();

    [Fact]
    public void StopBeforeStart_IsRejected()
    {
        var configuration = new SimulationConfiguration { StartTime = 2, StopTime = 1 };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }
}
