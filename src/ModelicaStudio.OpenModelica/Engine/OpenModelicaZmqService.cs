using System.Diagnostics;
using ModelicaStudio.Application.Engine;
using ModelicaStudio.Domain.Diagnostics;
using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.Domain.Modeling;
using ModelicaStudio.Domain.Simulation;
using ModelicaStudio.OpenModelica.Commands;
using ModelicaStudio.OpenModelica.Parsing;
using ModelicaStudio.OpenModelica.Protocol;
using Microsoft.Extensions.Logging;

namespace ModelicaStudio.OpenModelica.Engine;

public sealed class OpenModelicaZmqService : IOpenModelicaService
{
    private readonly IOpenModelicaInstallationLocator _installationLocator;
    private readonly IOmcTransportFactory _transportFactory;
    private readonly OpenModelicaOptions _options;
    private readonly ILogger<OpenModelicaZmqService> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IOmcTransport? _transport;
    private OpenModelicaInstallation? _installation;
    private string? _configuredExecutable;

    public OpenModelicaZmqService(
        IOpenModelicaInstallationLocator installationLocator,
        IOmcTransportFactory transportFactory,
        OpenModelicaOptions options,
        ILogger<OpenModelicaZmqService> logger)
    {
        _installationLocator = installationLocator;
        _transportFactory = transportFactory;
        _options = options;
        _logger = logger;
        _configuredExecutable = options.OmcPath;
    }

    public OpenModelicaSessionState State { get; private set; } = OpenModelicaSessionState.NotDetected();

    public event EventHandler<OpenModelicaStateChangedEventArgs>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_transport?.IsRunning == true)
        {
            return;
        }

        ChangeState(new OpenModelicaSessionState(OpenModelicaSessionStatus.Starting, _installation, "Locating OpenModelica"));
        _installation = await _installationLocator.LocateAsync(_configuredExecutable, cancellationToken).ConfigureAwait(false);
        if (_installation is null)
        {
            const string detail = "OpenModelica was not detected. Configure the path to the omc executable to enable checking and simulation.";
            ChangeState(OpenModelicaSessionState.NotDetected(detail));
            throw new OpenModelicaUnavailableException(detail);
        }

        try
        {
            _transport = _transportFactory.Create();
            _transport.UnexpectedExit += HandleUnexpectedExit;
            await _transport.StartAsync(_installation, _options, cancellationToken).ConfigureAwait(false);
            var version = await ExecuteCoreAsync(OmcCommandBuilder.GetVersion(), _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            _installation = _installation with { Version = OmcResponseParser.ParseString(version), IsValid = true };
            ChangeState(new OpenModelicaSessionState(OpenModelicaSessionStatus.Ready, _installation, "Ready"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to start OpenModelica");
            ChangeState(new OpenModelicaSessionState(OpenModelicaSessionStatus.Faulted, _installation, exception.Message));
            if (_transport is not null)
            {
                await _transport.DisposeAsync().ConfigureAwait(false);
                _transport = null;
            }

            throw new OpenModelicaUnavailableException("OpenModelica was detected but its interactive session could not be started.", exception);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var transport = _transport;
        _transport = null;
        if (transport is not null)
        {
            transport.UnexpectedExit -= HandleUnexpectedExit;
            await transport.StopAsync(cancellationToken).ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        ChangeState(new OpenModelicaSessionState(OpenModelicaSessionStatus.Stopped, _installation, "Stopped"));
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfigureExecutableAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        var installation = await _installationLocator.LocateAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (installation is null
            || !string.Equals(installation.OmcPath, fullPath, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new OpenModelicaUnavailableException("The selected file is not a valid OpenModelica compiler executable.");
        }

        var previousExecutable = _configuredExecutable;
        var hadRunningTransport = _transport?.IsRunning == true;
        await StopAsync(cancellationToken).ConfigureAwait(false);
        _configuredExecutable = installation.OmcPath;
        try
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _configuredExecutable = previousExecutable;
            if (hadRunningTransport)
            {
                try
                {
                    await StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception restoreException) when (restoreException is not OperationCanceledException)
                {
                    _logger.LogError(restoreException, "Failed to restore the previous OpenModelica session");
                }
            }

            throw;
        }
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.GetVersion(),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseString(response);
    }

    public async Task<bool> LoadModelAsync(string library, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.LoadModel(library),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> LoadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            fullPath = Path.Combine(fullPath, "package.mo");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Modelica source file was not found.", fullPath);
        }

        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.LoadFile(fullPath),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<ModelCheckResult> CheckModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        return await ExecuteCompoundOperationAsync(async () =>
        {
            var response = await ExecuteCoreAsync(
                OmcCommandBuilder.CheckModel(modelName),
                _options.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
            var diagnostics = await ReadDiagnosticsCoreAsync(cancellationToken).ConfigureAwait(false);
            var summary = OmcResponseParser.ParseString(response);
            var success = !diagnostics.Any(static diagnostic => diagnostic.Severity == CompilerDiagnosticSeverity.Error)
                && summary.Contains("completed successfully", StringComparison.OrdinalIgnoreCase);
            return new ModelCheckResult(success, summary, diagnostics);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModelicaClassInfo>> GetClassNamesAsync(
        string parentClass,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteCompoundOperationAsync(async () =>
        {
            var response = await ExecuteCoreAsync(
                OmcCommandBuilder.GetClassNames(parentClass),
                _options.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
            var names = OmcResponseParser.ParseStringList(response);
            var classes = new List<ModelicaClassInfo>(names.Count);
            foreach (var returnedName in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullName = returnedName.Contains('.', StringComparison.Ordinal)
                    ? returnedName
                    : $"{parentClass}.{returnedName}";
                var restriction = await ExecuteCoreAsync(
                    OmcCommandBuilder.GetClassRestriction(fullName),
                    _options.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                var partial = await ExecuteCoreAsync(
                    OmcCommandBuilder.IsPartial(fullName),
                    _options.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                classes.Add(new ModelicaClassInfo(
                    fullName,
                    fullName[(fullName.LastIndexOf('.') + 1)..],
                    OmcResponseParser.ParseClassKind(restriction),
                    OmcResponseParser.ParseBoolean(partial)));
            }

            return classes;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModelicaGraphicalAnnotationSnapshot> GetGraphicalAnnotationAsync(
        string className,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.GetModelInstanceAnnotation(className),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return ModelInstanceAnnotationParser.Parse(OmcResponseParser.ParseString(response));
    }

    public async Task<ModelicaModelInstanceSnapshot> GetModelInstanceAsync(
        string className,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.GetModelInstance(className),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return ModelicaModelInstanceParser.Parse(OmcResponseParser.ParseString(response));
    }

    public async Task<string> GetDefaultComponentNameAsync(
        string typeName,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.GetDefaultComponentName(typeName),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseString(response);
    }

    public async Task<bool> AddComponentAsync(
        string className,
        string componentName,
        string typeName,
        ModelicaPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.AddComponent(className, componentName, typeName, placement),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> DeleteComponentAsync(
        string className,
        string componentName,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.DeleteComponent(className, componentName),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> AddConnectionAsync(
        string className,
        string from,
        string to,
        ModelicaGraphicPrimitive line,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.AddConnection(className, from, to, line),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> DeleteConnectionAsync(
        string className,
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.DeleteConnection(className, from, to),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> UpdateConnectionAnnotationAsync(
        string className,
        string from,
        string to,
        ModelicaGraphicPrimitive line,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.UpdateConnectionAnnotation(className, from, to, line),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> SetComponentPlacementAsync(
        string className,
        string componentName,
        ModelicaPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.SetComponentPlacement(className, componentName, placement),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> SaveClassAsync(
        string className,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.SaveClass(className),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> LoadStringAsync(
        string source,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.LoadString(source, sourcePath),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<bool> LoadClassContentStringAsync(
        string content,
        string className,
        int offsetX,
        int offsetY,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.LoadClassContentString(content, className, offsetX, offsetY),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseBoolean(response);
    }

    public async Task<SimulationResult> SimulateAsync(
        string modelName,
        SimulationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return await ExecuteCompoundOperationAsync(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await ExecuteCoreAsync(
                OmcCommandBuilder.Simulate(modelName, configuration),
                _options.SimulationTimeout,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var diagnostics = await ReadDiagnosticsCoreAsync(cancellationToken).ConfigureAwait(false);
            var resultFile = OmcResponseParser.ParseSimulationResultFile(response);
            var success = !string.IsNullOrWhiteSpace(resultFile)
                && !diagnostics.Any(static diagnostic => diagnostic.Severity == CompilerDiagnosticSeverity.Error);
            return new SimulationResult(success, modelName, resultFile, stopwatch.Elapsed, response, diagnostics);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadSimulationResultVariablesAsync(
        string resultFile,
        CancellationToken cancellationToken = default)
    {
        EnsureResultFileExists(resultFile);
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.ReadSimulationResultVariables(Path.GetFullPath(resultFile)),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseStringList(response);
    }

    public async Task<SimulationSeries> ReadSimulationSeriesAsync(
        string resultFile,
        string variableName,
        CancellationToken cancellationToken = default)
    {
        EnsureResultFileExists(resultFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        var response = await ExecuteOperationAsync(
            OmcCommandBuilder.ReadSimulationSeries(Path.GetFullPath(resultFile), variableName),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        var matrix = OmcResponseParser.ParseNumericMatrix(response);
        if (matrix.Count < 2 || matrix[0].Count != matrix[1].Count)
        {
            throw new FormatException("OpenModelica returned an invalid time-series matrix.");
        }

        return new SimulationSeries(variableName, null, matrix[0], matrix[1]);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _operationGate.Dispose();
    }

    private async Task<string> ExecuteOperationAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await ExecuteCompoundOperationAsync(
            () => ExecuteCoreAsync(command, timeout, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> ExecuteCompoundOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureRunning();
            ChangeState(new OpenModelicaSessionState(OpenModelicaSessionStatus.Busy, _installation, "Working"));
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            if (_transport?.IsRunning == true)
            {
                ChangeState(new OpenModelicaSessionState(OpenModelicaSessionStatus.Ready, _installation, "Ready"));
            }

            _operationGate.Release();
        }
    }

    private Task<string> ExecuteCoreAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        EnsureRunning();
        return _transport!.ExecuteAsync(command, timeout, cancellationToken);
    }

    private async Task<IReadOnlyList<CompilerDiagnostic>> ReadDiagnosticsCoreAsync(CancellationToken cancellationToken)
    {
        var response = await ExecuteCoreAsync(
            OmcCommandBuilder.GetMessages(),
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return OmcResponseParser.ParseDiagnostics(response);
    }

    private void EnsureRunning()
    {
        if (_transport?.IsRunning != true)
        {
            throw new OpenModelicaUnavailableException("The OpenModelica session is not running.");
        }
    }

    private static void EnsureResultFileExists(string resultFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultFile);
        if (!File.Exists(resultFile))
        {
            throw new FileNotFoundException("The OpenModelica simulation result file was not found.", resultFile);
        }
    }

    private void HandleUnexpectedExit(object? sender, EventArgs eventArgs)
    {
        ChangeState(new OpenModelicaSessionState(
            OpenModelicaSessionStatus.Faulted,
            _installation,
            "The OpenModelica compiler process terminated unexpectedly."));
    }

    private void ChangeState(OpenModelicaSessionState state)
    {
        State = state;
        StateChanged?.Invoke(this, new OpenModelicaStateChangedEventArgs(state));
    }
}
