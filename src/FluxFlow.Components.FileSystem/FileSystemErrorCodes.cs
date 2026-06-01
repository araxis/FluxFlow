namespace FluxFlow.Components.FileSystem;

public static class FileSystemErrorCodes
{
    public const int FileWriteInvalidPath = 7000;
    public const int FileWriteAbsolutePathDenied = 7001;
    public const int FileWriteContentMissing = 7002;
    public const int FileWriteUnsupportedEncoding = 7003;
    public const int FileWriteUnsupportedMode = 7004;
    public const int FileWriteAccessDenied = 7005;
    public const int FileWriteIoFailed = 7006;
}
