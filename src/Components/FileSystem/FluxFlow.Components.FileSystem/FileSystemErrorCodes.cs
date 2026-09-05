namespace FluxFlow.Components.FileSystem;

public static class FileSystemErrorCodes
{
    public const int DirectoryEnumerateInvalidDirectory = 7300;
    public const int DirectoryEnumerateAbsolutePathDenied = 7301;
    public const int DirectoryEnumerateDirectoryMissing = 7302;
    public const int DirectoryEnumerateNoEntryTypes = 7303;
    public const int DirectoryEnumerateAccessDenied = 7304;
    public const int DirectoryEnumerateIoFailed = 7305;
    public const int DirectoryEnumerateFailed = 7306;

    public const int FileWriteInvalidPath = 7000;
    public const int FileWriteAbsolutePathDenied = 7001;
    public const int FileWriteContentMissing = 7002;
    public const int FileWriteUnsupportedEncoding = 7003;
    public const int FileWriteUnsupportedMode = 7004;
    public const int FileWriteAccessDenied = 7005;
    public const int FileWriteIoFailed = 7006;

    public const int FileReadInvalidPath = 7100;
    public const int FileReadAbsolutePathDenied = 7101;
    public const int FileReadUnsupportedEncoding = 7102;
    public const int FileReadUnsupportedMode = 7103;
    public const int FileReadAccessDenied = 7104;
    public const int FileReadIoFailed = 7105;
    public const int FileReadNotFound = 7106;
    public const int FileReadTooLarge = 7107;

    public const int FileWatchInvalidDirectory = 7200;
    public const int FileWatchAbsolutePathDenied = 7201;
    public const int FileWatchDirectoryMissing = 7202;
    public const int FileWatchUnsupportedNotifyFilter = 7203;
    public const int FileWatchStartupFailed = 7204;
    public const int FileWatchFailed = 7205;
    public const int FileWatchOutputFull = 7206;
}
