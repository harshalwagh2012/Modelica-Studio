using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.UI.ViewModels;

namespace ModelicaStudio.UI.Tests;

public sealed class SimulationSetupViewModelTests
{
    [Fact]
    public void ConstructorAndCreateConfiguration_RoundTripSettings()
    {
        var source = new SimulationConfiguration
        {
            StartTime = 0.25,
            StopTime = 4,
            NumberOfIntervals = 750,
            Tolerance = 1e-7,
            Method = "ida",
            OutputFormat = "mat",
            VariableFilter = "h|v",
        };
        var viewModel = new SimulationSetupViewModel(source);

        var success = viewModel.TryCreateConfiguration(out var result);

        Assert.True(success);
        Assert.Equal(source, result);
        Assert.Equal(0.005m, viewModel.Interval);
        Assert.False(viewModel.HasValidationError);
    }

    [Fact]
    public void CreateConfiguration_InvalidTimeRangeReturnsInlineValidation()
    {
        var viewModel = new SimulationSetupViewModel(new SimulationConfiguration())
        {
            StartTime = 2,
            StopTime = 1,
        };

        var success = viewModel.TryCreateConfiguration(out var result);

        Assert.False(success);
        Assert.Null(result);
        Assert.True(viewModel.HasValidationError);
        Assert.Contains("greater than start time", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }
}
