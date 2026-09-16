namespace ModelicaStudio.OpenModelica.Protocol;

public sealed class OpenModelicaTransportException : Exception
{
    public OpenModelicaTransportException(string message)
        : base(message)
    {
    }

    public OpenModelicaTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
