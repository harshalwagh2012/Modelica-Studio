using Microsoft.Extensions.Logging.Abstractions;
using ModelicaStudio.Application.Engine;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.OpenModelica.Engine;
using ModelicaStudio.OpenModelica.Protocol;
using System.Text.Json;

namespace ModelicaStudio.OpenModelica.Tests;

public sealed class OpenModelicaConfigurationTests
{
    [Fact]
    public async Task ConfigureExecutable_InvalidSelectionDoesNotStopRunningSession()
    {
        var initialPath = Path.GetFullPath("initial-omc");
        var invalidPath = Path.GetFullPath("invalid-omc");
        var locator = new FakeInstallationLocator(path =>
            string.Equals(path, initialPath, StringComparison.Ordinal)
                ? Installation(initialPath)
                : null);
        var transport = new FakeTransport();
        await using var service = CreateService(locator, new FakeTransportFactory(transport), initialPath);
        await service.StartAsync();

        await Assert.ThrowsAsync<OpenModelicaUnavailableException>(() =>
            service.ConfigureExecutableAsync(invalidPath));

        Assert.True(transport.IsRunning);
        Assert.Equal(0, transport.StopCount);
        Assert.Equal(OpenModelicaSessionStatus.Ready, service.State.Status);
    }

    [Fact]
    public async Task ConfigureExecutable_ValidSelectionRestartsWithSelectedCompiler()
    {
        var initialPath = Path.GetFullPath("initial-omc");
        var selectedPath = Path.GetFullPath("selected-omc");
        var locator = new FakeInstallationLocator(path => path switch
        {
            var value when string.Equals(value, initialPath, StringComparison.Ordinal) => Installation(initialPath),
            var value when string.Equals(value, selectedPath, StringComparison.Ordinal) => Installation(selectedPath),
            _ => null,
        });
        var initialTransport = new FakeTransport();
        var selectedTransport = new FakeTransport();
        await using var service = CreateService(
            locator,
            new FakeTransportFactory(initialTransport, selectedTransport),
            initialPath);
        await service.StartAsync();

        await service.ConfigureExecutableAsync(selectedPath);

        Assert.Equal(1, initialTransport.StopCount);
        Assert.Equal(selectedPath, selectedTransport.StartedWith?.OmcPath);
        Assert.True(selectedTransport.IsRunning);
        Assert.Equal(OpenModelicaSessionStatus.Ready, service.State.Status);
    }

    [Fact]
    public async Task ConfigureExecutable_InteractiveStartFailureRestoresPreviousSession()
    {
        var initialPath = Path.GetFullPath("initial-omc");
        var selectedPath = Path.GetFullPath("selected-omc");
        var locator = new FakeInstallationLocator(path => path switch
        {
            var value when string.Equals(value, initialPath, StringComparison.Ordinal) => Installation(initialPath),
            var value when string.Equals(value, selectedPath, StringComparison.Ordinal) => Installation(selectedPath),
            _ => null,
        });
        var initialTransport = new FakeTransport();
        var failingTransport = new FakeTransport { StartException = new IOException("interactive startup failed") };
        var restoredTransport = new FakeTransport();
        await using var service = CreateService(
            locator,
            new FakeTransportFactory(initialTransport, failingTransport, restoredTransport),
            initialPath);
        await service.StartAsync();

        await Assert.ThrowsAsync<OpenModelicaUnavailableException>(() =>
            service.ConfigureExecutableAsync(selectedPath));

        Assert.Equal(1, initialTransport.StopCount);
        Assert.Equal(initialPath, restoredTransport.StartedWith?.OmcPath);
        Assert.True(restoredTransport.IsRunning);
        Assert.Equal(OpenModelicaSessionStatus.Ready, service.State.Status);
    }

    [Fact]
    public async Task GetGraphicalAnnotation_ReturnsTypedParsedSnapshot()
    {
        var compilerPath = Path.GetFullPath("omc");
        var locator = new FakeInstallationLocator(_ => Installation(compilerPath));
        var annotationJson = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Annotations",
            "basic-shapes.json"));
        var transport = new FakeTransport
        {
            ResponseFactory = command => command == "getVersion()"
                ? "\"OpenModelica 1.25.0\""
                : JsonSerializer.Serialize(annotationJson),
        };
        await using var service = CreateService(locator, new FakeTransportFactory(transport), compilerPath);
        await service.StartAsync();

        var result = await service.GetGraphicalAnnotationAsync("M");

        Assert.Equal("M", result.ClassName);
        Assert.Single(Assert.IsType<ModelicaStudio.Domain.Graphics.ModelicaGraphicalView>(result.Icon).Graphics);
        Assert.Contains(transport.Commands, command => command.StartsWith("getModelInstanceAnnotation(M", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetModelInstance_ReturnsTypedComponentsAndConnections()
    {
        var compilerPath = Path.GetFullPath("omc");
        var locator = new FakeInstallationLocator(_ => Installation(compilerPath));
        var instanceJson = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Instances",
            "component-placement.json"));
        var transport = new FakeTransport
        {
            ResponseFactory = command => command == "getVersion()"
                ? "\"OpenModelica 1.25.0\""
                : JsonSerializer.Serialize(instanceJson),
        };
        await using var service = CreateService(locator, new FakeTransportFactory(transport), compilerPath);
        await service.StartAsync();

        var result = await service.GetModelInstanceAsync("Fixture.System");

        Assert.Equal(3, result.Components.Count);
        Assert.Single(result.Connections);
        Assert.Contains(
            "getModelInstance(Fixture.System, prettyPrint=false)",
            transport.Commands);
    }

    [Fact]
    public async Task PlacementMutationAndSave_ReturnAuthoritativeBooleanResults()
    {
        var compilerPath = Path.GetFullPath("omc");
        var locator = new FakeInstallationLocator(_ => Installation(compilerPath));
        var transport = new FakeTransport
        {
            ResponseFactory = command => command == "getVersion()"
                ? "\"OpenModelica 1.27.0\""
                : "true",
        };
        await using var service = CreateService(locator, new FakeTransportFactory(transport), compilerPath);
        await service.StartAsync();
        var placement = new ModelicaStudio.Domain.Graphics.ModelicaPlacement(
            true,
            ModelicaStudio.Domain.Graphics.ModelicaTransformation.Default,
            false,
            null);

        var updated = await service.SetComponentPlacementAsync("Demo", "plant", placement);
        var saved = await service.SaveClassAsync("Demo");

        Assert.True(updated);
        Assert.True(saved);
        Assert.Contains(transport.Commands, command => command.StartsWith(
            "setElementAnnotation(Demo.plant, $Code((Placement(",
            StringComparison.Ordinal));
        Assert.Contains("save(Demo)", transport.Commands);
    }

    [Fact]
    public async Task ComponentAuthoringAndSourceReplacement_ReturnAuthoritativeResponses()
    {
        var compilerPath = Path.GetFullPath("omc");
        var locator = new FakeInstallationLocator(_ => Installation(compilerPath));
        var transport = new FakeTransport
        {
            ResponseFactory = command => command switch
            {
                "getVersion()" => "\"OpenModelica 1.27.0\"",
                "getDefaultComponentName(Demo.Part)" => "\"part\"",
                _ => "true",
            },
        };
        await using var service = CreateService(locator, new FakeTransportFactory(transport), compilerPath);
        await service.StartAsync();
        var placement = new ModelicaStudio.Domain.Graphics.ModelicaPlacement(
            true,
            ModelicaStudio.Domain.Graphics.ModelicaTransformation.Default,
            false,
            null);

        var defaultName = await service.GetDefaultComponentNameAsync("Demo.Part");
        var added = await service.AddComponentAsync("Demo", "part1", "Demo.Part", placement);
        var deleted = await service.DeleteComponentAsync("Demo", "part1");
        var connectionLine = new ModelicaGraphicPrimitive
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(0),
            Points = [new ModelicaPoint(-10, 0), new ModelicaPoint(10, 0)],
            RawJson = "{}",
        };
        var connected = await service.AddConnectionAsync("Demo", "source.y", "sink.u", connectionLine);
        var rerouted = await service.UpdateConnectionAnnotationAsync(
            "Demo",
            "source.y",
            "sink.u",
            connectionLine);
        var disconnected = await service.DeleteConnectionAsync("Demo", "source.y", "sink.u");
        var loaded = await service.LoadStringAsync("model Demo end Demo;", "/models/Demo.mo");
        var merged = await service.LoadClassContentStringAsync("Demo.Part part1;", "Demo", 10, 10);

        Assert.Equal("part", defaultName);
        Assert.True(added);
        Assert.True(deleted);
        Assert.True(connected);
        Assert.True(rerouted);
        Assert.True(disconnected);
        Assert.True(loaded);
        Assert.True(merged);
        Assert.Contains(transport.Commands, command => command.StartsWith("addComponent(part1, Demo.Part, Demo,", StringComparison.Ordinal));
        Assert.Contains("deleteComponent(part1, Demo)", transport.Commands);
        Assert.Contains(transport.Commands, command => command.StartsWith(
            "addConnection(source.y, sink.u, Demo, annotate=Line(",
            StringComparison.Ordinal));
        Assert.Contains(transport.Commands, command => command.StartsWith(
            "updateConnectionAnnotation(Demo, \"source.y\", \"sink.u\", \"annotate=Line(",
            StringComparison.Ordinal));
        Assert.Contains("deleteConnection(source.y, sink.u, Demo)", transport.Commands);
        Assert.Contains(transport.Commands, command => command.StartsWith("loadString(\"model Demo end Demo;\"", StringComparison.Ordinal));
        Assert.Contains("loadClassContentString(\"Demo.Part part1;\", Demo, 10, 10)", transport.Commands);
    }

    [Fact]
    public async Task LibraryClasses_IncludeCompilerReportedRestrictionAndPartialState()
    {
        var compilerPath = Path.GetFullPath("omc");
        var locator = new FakeInstallationLocator(_ => Installation(compilerPath));
        var transport = new FakeTransport
        {
            ResponseFactory = command => command switch
            {
                "getVersion()" => "\"OpenModelica 1.27.0\"",
                "getClassNames(Demo, recursive=false, qualified=true, sort=true)" =>
                    "{Demo.PartialPlant,Demo.Controller}",
                "getClassRestriction(Demo.PartialPlant)" => "\"model\"",
                "getClassRestriction(Demo.Controller)" => "\"block\"",
                "isPartial(Demo.PartialPlant)" => "true",
                "isPartial(Demo.Controller)" => "false",
                _ => throw new InvalidOperationException($"Unexpected command: {command}"),
            },
        };
        await using var service = CreateService(locator, new FakeTransportFactory(transport), compilerPath);
        await service.StartAsync();

        var classes = await service.GetClassNamesAsync("Demo");

        Assert.Collection(
            classes,
            item =>
            {
                Assert.Equal(ModelicaStudio.Domain.Modeling.ModelicaClassKind.Model, item.Kind);
                Assert.True(item.IsPartial);
            },
            item =>
            {
                Assert.Equal(ModelicaStudio.Domain.Modeling.ModelicaClassKind.Block, item.Kind);
                Assert.False(item.IsPartial);
            });
    }

    private static OpenModelicaZmqService CreateService(
        IOpenModelicaInstallationLocator locator,
        IOmcTransportFactory transportFactory,
        string initialPath) =>
        new(
            locator,
            transportFactory,
            new OpenModelicaOptions { OmcPath = initialPath },
            NullLogger<OpenModelicaZmqService>.Instance);

    private static OpenModelicaInstallation Installation(string path) =>
        new(path, Path.GetDirectoryName(path), "OpenModelica test", true);

    private sealed class FakeInstallationLocator(Func<string?, OpenModelicaInstallation?> locate)
        : IOpenModelicaInstallationLocator
    {
        public Task<OpenModelicaInstallation?> LocateAsync(
            string? configuredExecutable = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(locate(configuredExecutable));
        }
    }

    private sealed class FakeTransportFactory(params FakeTransport[] transports) : IOmcTransportFactory
    {
        private int _index;

        public IOmcTransport Create() => transports[_index++];
    }

    private sealed class FakeTransport : IOmcTransport
    {
        public bool IsRunning { get; private set; }
        public OpenModelicaInstallation? StartedWith { get; private set; }
        public int StopCount { get; private set; }
        public Exception? StartException { get; init; }
        public Func<string, string>? ResponseFactory { get; init; }
        public List<string> Commands { get; } = [];

        public event EventHandler? UnexpectedExit;

        public Task StartAsync(
            OpenModelicaInstallation installation,
            OpenModelicaOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StartException is not null)
            {
                throw StartException;
            }

            StartedWith = installation;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task<string> ExecuteAsync(
            string command,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return Task.FromResult(ResponseFactory?.Invoke(command) ?? "\"OpenModelica 1.25.0\"");
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }

        public void RaiseUnexpectedExit() => UnexpectedExit?.Invoke(this, EventArgs.Empty);
    }
}
