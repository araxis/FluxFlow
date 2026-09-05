namespace FluxFlow.Components.Storage.Nodes;

internal sealed class StorageContentOperationException : Exception
{
    public StorageContentOperationException(
        string code,
        string message,
        bool isTransient = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code.Trim();
        IsTransient = isTransient;
    }

    public string Code { get; }

    public bool IsTransient { get; }
}
