namespace FluxFlow.Components.Serialization.Nodes;

internal sealed class SerializationNodeException : Exception
{
    public SerializationNodeException(
        int code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public int Code { get; }
}
