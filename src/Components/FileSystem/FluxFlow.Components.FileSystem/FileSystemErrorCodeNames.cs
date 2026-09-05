namespace FluxFlow.Components.FileSystem;

public static class FileSystemErrorCodeNames
{
    public const string ReadInvalidPath = "file.read.invalid_path";
    public const string ReadAbsolutePathDenied = "file.read.absolute_path_denied";
    public const string ReadUnsupportedEncoding = "file.read.unsupported_encoding";
    public const string ReadUnsupportedMode = "file.read.unsupported_mode";
    public const string ReadAccessDenied = "file.read.access_denied";
    public const string ReadIoFailed = "file.read.io_failed";
    public const string ReadNotFound = "file.read.not_found";
    public const string ReadTooLarge = "file.read.too_large";
    public const string WriteInvalidPath = "file.write.invalid_path";
    public const string WriteAbsolutePathDenied = "file.write.absolute_path_denied";
    public const string WriteContentMissing = "file.write.content_missing";
    public const string WriteContentUnavailable = "file.write.content_unavailable";
    public const string WriteUnsupportedMode = "file.write.unsupported_mode";
    public const string WriteAccessDenied = "file.write.access_denied";
    public const string WriteIoFailed = "file.write.io_failed";
}
