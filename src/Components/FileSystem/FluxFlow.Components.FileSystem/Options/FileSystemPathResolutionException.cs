namespace FluxFlow.Components.FileSystem.Options;

internal sealed class FileSystemPathResolutionException : Exception
{
    public FileSystemPathResolutionException(int code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public int Code { get; }
}
