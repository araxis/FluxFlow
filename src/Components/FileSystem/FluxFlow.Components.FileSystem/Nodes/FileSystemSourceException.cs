namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemSourceException : IOException
{
    public FileSystemSourceException(
        int errorCode,
        string message,
        string? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Context = context;
    }

    public int ErrorCode { get; }

    public string? Context { get; }
}
