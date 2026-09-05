namespace FluxFlow.Components.Sessions.Nodes;

internal sealed class SessionContentOperationException : Exception
{
    public SessionContentOperationException(
        string code,
        string message,
        bool isTransient = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        IsTransient = isTransient;
    }

    public string Code { get; }

    public bool IsTransient { get; }
}
