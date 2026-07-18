namespace FluxFlow.Components.Serialization.Nodes;

internal sealed class FlowSerializationException : Exception
{
    public FlowSerializationException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
