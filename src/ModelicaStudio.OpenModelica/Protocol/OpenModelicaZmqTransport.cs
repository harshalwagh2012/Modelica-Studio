using System.Diagnostics;
using System.Text.RegularExpressions;
using ModelicaStudio.Application.Engine;
using ModelicaStudio.OpenModelica.Engine;
using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;

namespace ModelicaStudio.OpenModelica.Protocol;

public sealed class OpenModelicaZmqTransport(ILogger<OpenModelicaZmqTransport> logger) : IOmcTransport
{
    private static readonly Regex PortFilePattern = new(
        @"Dumped server port in file:\s*(.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Lock _lifecycleLock = new();
    private Process? _process;
    private string? _endpoint;
    private TaskCompletionSource<string>? _portFileSource;
    private bool _stopping;

    public bool IsRunning => _process is { HasExited: false } && _endpoint is not null;

    public event EventHandler? UnexpectedExit;

    public async Task StartAsync(
        OpenModelicaInstallation installation,
        OpenModelicaOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(options);

        lock (_lifecycleLock)
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            _stopping = false;
            _endpoint = null;
            _portFileSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _process = CreateProcess(installation, options);
            _process.Exited += HandleProcessExited;
            if (!_process.Start())
            {
                throw new OpenModelicaTransportException("The OpenModelica compiler process could not be started.");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        try
        {
            using var startupTimeout = new CancellationTokenSource(options.StartupTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startupTimeout.Token);
            var portFile = await _portFileSource.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            _endpoint = await ReadEndpointAsync(portFile, linked.Token).ConfigureAwait(false);
            logger.LogInformation("OpenModelica ZMQ session started at {Endpoint} with process {ProcessId}", _endpoint, _process.Id);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw new OpenModelicaTransportException($"OpenModelica did not publish a ZMQ endpoint within {options.StartupTimeout}.", exception);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<string> ExecuteAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("An OMC command is required.", nameof(command));
        }

        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var endpoint = _endpoint;
            if (!IsRunning || endpoint is null)
            {
                throw new OpenModelicaTransportException("The OpenModelica session is not running.");
            }

            logger.LogDebug("Executing OMC command {CommandName}", GetCommandName(command));
            return await Task.Run(
                () => ExecuteRequest(endpoint, command, timeout, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_lifecycleLock)
        {
            process = _process;
            _stopping = true;
        }

        if (process is null)
        {
            return;
        }

        if (!process.HasExited && _endpoint is not null)
        {
            try
            {
                await ExecuteAsync("quit()", TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OpenModelicaTransportException or OperationCanceledException)
            {
                logger.LogDebug(exception, "OMC did not acknowledge quit; terminating the process");
            }
        }

        if (!process.HasExited)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        lock (_lifecycleLock)
        {
            process.OutputDataReceived -= HandleOutput;
            process.ErrorDataReceived -= HandleOutput;
            process.Exited -= HandleProcessExited;
            process.Dispose();
            _process = null;
            _endpoint = null;
            _portFileSource = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _commandGate.Dispose();
    }

    private Process CreateProcess(OpenModelicaInstallation installation, OpenModelicaOptions options)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var workingDirectory = options.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.Combine(Path.GetTempPath(), "ModelicaStudio", sessionId);
        }

        Directory.CreateDirectory(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = installation.OmcPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--locale=C");
        startInfo.ArgumentList.Add("--interactive=zmq");
        startInfo.ArgumentList.Add($"-z={sessionId}");

        if (!string.IsNullOrWhiteSpace(installation.InstallationRoot))
        {
            startInfo.Environment["OPENMODELICAHOME"] = installation.InstallationRoot;
            var binaryDirectory = Path.GetDirectoryName(installation.OmcPath);
            if (binaryDirectory is not null)
            {
                startInfo.Environment.TryGetValue("PATH", out var existingPath);
                startInfo.Environment["PATH"] = binaryDirectory + Path.PathSeparator + existingPath;
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += HandleOutput;
        process.ErrorDataReceived += HandleOutput;
        return process;
    }

    private void HandleOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        logger.LogDebug("OMC: {Output}", eventArgs.Data);
        var match = PortFilePattern.Match(eventArgs.Data);
        if (match.Success)
        {
            _portFileSource?.TrySetResult(match.Groups[1].Value.Trim());
        }
    }

    private void HandleProcessExited(object? sender, EventArgs eventArgs)
    {
        if (_stopping)
        {
            return;
        }

        _endpoint = null;
        var exitCode = _process?.ExitCode;
        logger.LogError("OpenModelica process terminated unexpectedly with exit code {ExitCode}", exitCode);
        _portFileSource?.TrySetException(new OpenModelicaTransportException($"OpenModelica exited during startup with code {exitCode}."));
        UnexpectedExit?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<string> ReadEndpointAsync(string portFile, CancellationToken cancellationToken)
    {
        while (!File.Exists(portFile))
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }

        var endpoint = (await File.ReadAllTextAsync(portFile, cancellationToken).ConfigureAwait(false)).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new OpenModelicaTransportException($"OpenModelica returned an invalid ZMQ endpoint: '{endpoint}'.");
        }

        return endpoint;
    }

    private static string ExecuteRequest(
        string endpoint,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var socket = new RequestSocket();
        socket.Options.Linger = TimeSpan.Zero;
        socket.Connect(endpoint);
        socket.SendFrame(command);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100);
            if (socket.TryReceiveFrameString(slice, out var response))
            {
                return response;
            }
        }

        throw new OpenModelicaTransportException($"OpenModelica command '{GetCommandName(command)}' timed out after {timeout}.");
    }

    private static string GetCommandName(string command)
    {
        var parenthesis = command.IndexOf('(');
        return parenthesis > 0 ? command[..parenthesis] : command;
    }
}

public sealed class OpenModelicaZmqTransportFactory(ILoggerFactory loggerFactory) : IOmcTransportFactory
{
    public IOmcTransport Create() => new OpenModelicaZmqTransport(loggerFactory.CreateLogger<OpenModelicaZmqTransport>());
}
