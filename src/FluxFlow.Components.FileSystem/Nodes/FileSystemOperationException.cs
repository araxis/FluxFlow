namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemOperationException : Exception
{
    public FileSystemOperationException(
        string code,
        string message,
        bool isTransient = false,
        Exception? innerException = null,
        string? resolvedPath = null,
        long? bytesRead = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        IsTransient = isTransient;
        ResolvedPath = resolvedPath;
        BytesRead = bytesRead;
    }

    public string Code { get; }

    public bool IsTransient { get; }

    public string? ResolvedPath { get; }

    public long? BytesRead { get; }
}
