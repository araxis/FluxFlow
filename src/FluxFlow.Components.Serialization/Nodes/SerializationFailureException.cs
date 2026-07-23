namespace FluxFlow.Components.Serialization.Nodes;

internal sealed class SerializationFailureException : Exception
{
    internal SerializationFailureException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    internal string Code { get; }
}
