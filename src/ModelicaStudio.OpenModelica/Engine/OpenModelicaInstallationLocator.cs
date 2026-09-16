using System.Diagnostics;
using System.Runtime.InteropServices;
using ModelicaStudio.Application.Engine;
using Microsoft.Extensions.Logging;

namespace ModelicaStudio.OpenModelica.Engine;

public sealed class OpenModelicaInstallationLocator(ILogger<OpenModelicaInstallationLocator> logger)
    : IOpenModelicaInstallationLocator
{
    public async Task<OpenModelicaInstallation?> LocateAsync(
        string? configuredExecutable = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in GetCandidates(configuredExecutable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = await ValidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (installation is not null)
            {
                logger.LogInformation("Detected OpenModelica {Version} at {OmcPath}", installation.Version, installation.OmcPath);
                return installation;
            }
        }

        logger.LogWarning("OpenModelica compiler was not detected");
        return null;
    }

    public static IReadOnlyList<string> GetCandidates(string? configuredExecutable = null)
    {
        var candidates = new List<string>();
        Add(configuredExecutable);

        var openModelicaHome = Environment.GetEnvironmentVariable("OPENMODELICAHOME");
        if (!string.IsNullOrWhiteSpace(openModelicaHome))
        {
            Add(Path.Combine(openModelicaHome, "bin", ExecutableName));
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Add(Path.Combine(directory, ExecutableName));
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            Add("/Applications/OpenModelica.app/Contents/Resources/bin/omc");
            Add("/opt/homebrew/bin/omc");
            Add("/opt/local/bin/omc");
            Add("/usr/local/bin/omc");
            Add("/opt/openmodelica/bin/omc");
        }
        else if (OperatingSystem.IsWindows())
        {
            AddWindowsInstallations(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddWindowsInstallations(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }
        else
        {
            Add("/usr/bin/omc");
            Add("/usr/local/bin/omc");
        }

        return candidates.Distinct(PathComparer).ToArray();

        void Add(string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                candidates.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate)));
            }
        }

        void AddWindowsInstallations(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root, "OpenModelica*", SearchOption.TopDirectoryOnly))
                {
                    Add(Path.Combine(directory, "bin", ExecutableName));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // A locked installation folder should not prevent checking later candidates.
            }
        }
    }

    private async Task<OpenModelicaInstallation?> ValidateAsync(string candidate, CancellationToken cancellationToken)
    {
        if (!File.Exists(candidate))
        {
            return null;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("--version");
            process.Start();

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var output = string.Join(' ', await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false)).Trim();

            if (process.ExitCode != 0 || !output.Contains("OpenModelica", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var binaryDirectory = Directory.GetParent(candidate)?.FullName;
            var root = binaryDirectory is null ? null : Directory.GetParent(binaryDirectory)?.FullName;
            return new OpenModelicaInstallation(candidate, root, output, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(exception, "OpenModelica candidate {OmcPath} failed validation", candidate);
            return null;
        }
    }

    private static string ExecutableName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "omc.exe" : "omc";

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
