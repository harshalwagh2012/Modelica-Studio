namespace ModelicaStudio.Application.Engine;

public sealed class OpenModelicaUnavailableException : Exception
{
    public OpenModelicaUnavailableException(string message)
        : base(message)
    {
    }

    public OpenModelicaUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
