using Microsoft.Extensions.Logging.Abstractions;
using ModelicaStudio.Application.Engine;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.OpenModelica.Engine;
using ModelicaStudio.OpenModelica.Protocol;

namespace ModelicaStudio.IntegrationTests;

public sealed class OpenModelicaBackendIntegrationTests
{
    [OpenModelicaFact]
    [Trait("Category", "OpenModelica")]
    public async Task BouncingBall_CompletesBackendVerticalSlice()
    {
        var locator = new OpenModelicaInstallationLocator(NullLogger<OpenModelicaInstallationLocator>.Instance);
        var installation = await locator.LocateAsync();
        Assert.NotNull(installation);

        var workingDirectory = Path.Combine(Path.GetTempPath(), "ModelicaStudio.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var options = new OpenModelicaOptions
        {
            OmcPath = installation.OmcPath,
            WorkingDirectory = workingDirectory,
            SimulationTimeout = TimeSpan.FromMinutes(5),
        };
        var loggerFactory = NullLoggerFactory.Instance;
        await using var service = new OpenModelicaZmqService(
            locator,
            new OpenModelicaZmqTransportFactory(loggerFactory),
            options,
            NullLogger<OpenModelicaZmqService>.Instance);

        try
        {
            await service.StartAsync();
            Assert.Contains("OpenModelica", await service.GetVersionAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.True(await service.LoadModelAsync("Modelica"));
            Assert.True(await service.LoadFileAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BouncingBall.mo")));

            var check = await service.CheckModelAsync("BouncingBall");
            Assert.True(check.Success, check.Summary);

            var annotation = await service.GetGraphicalAnnotationAsync("BouncingBall");
            Assert.Equal("BouncingBall", annotation.ClassName);
            Assert.Equal(2, Assert.IsType<ModelicaGraphicalView>(annotation.Icon).Graphics.Count);

            var instance = await service.GetModelInstanceAsync("BouncingBall");
            Assert.Equal("BouncingBall", instance.ClassName);
            Assert.Contains(instance.Components, component => component.Name == "h");

            var simulation = await service.SimulateAsync(
                "BouncingBall",
                new SimulationConfiguration { StopTime = 3, NumberOfIntervals = 120 });
            Assert.True(simulation.Success, simulation.RawResponse);
            Assert.NotNull(simulation.ResultFile);
            Assert.True(File.Exists(simulation.ResultFile));

            var variables = await service.ReadSimulationResultVariablesAsync(simulation.ResultFile);
            Assert.Contains("h", variables);
            var height = await service.ReadSimulationSeriesAsync(simulation.ResultFile, "h");
            Assert.Equal(height.Time.Count, height.Values.Count);
            Assert.True(height.Values.Max() - height.Values.Min() > 0.1);
        }
        finally
        {
            await service.StopAsync();
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }
}

public sealed class OpenModelicaFactAttribute : FactAttribute
{
    public OpenModelicaFactAttribute()
    {
        if (!OpenModelicaInstallationLocator.GetCandidates().Any(File.Exists))
        {
            Skip = "OpenModelica is not installed; native OMC integration was not executed.";
        }
    }
}
