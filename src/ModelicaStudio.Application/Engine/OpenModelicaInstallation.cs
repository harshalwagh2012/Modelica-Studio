namespace ModelicaStudio.Application.Engine;

public sealed record OpenModelicaInstallation(
    string OmcPath,
    string? InstallationRoot,
    string Version,
    bool IsValid);

public enum OpenModelicaSessionStatus
{
    NotDetected,
    Stopped,
    Starting,
    Ready,
    Busy,
    Faulted,
}

public sealed record OpenModelicaSessionState(
    OpenModelicaSessionStatus Status,
    OpenModelicaInstallation? Installation = null,
    string? Detail = null)
{
    public static OpenModelicaSessionState NotDetected(string? detail = null) =>
        new(OpenModelicaSessionStatus.NotDetected, Detail: detail);
}

public sealed class OpenModelicaStateChangedEventArgs(OpenModelicaSessionState state) : EventArgs
{
    public OpenModelicaSessionState State { get; } = state;
}
