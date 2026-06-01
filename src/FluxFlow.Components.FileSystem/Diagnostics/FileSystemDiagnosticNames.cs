namespace FluxFlow.Components.FileSystem.Diagnostics;

public static class FileSystemDiagnosticNames
{
    public const string DirectoryEnumerateStarted = "directory.enumerate.started";
    public const string DirectoryEnumerateCompleted = "directory.enumerate.completed";
    public const string DirectoryEnumerateEntry = "directory.enumerate.entry";
    public const string DirectoryEnumerateFailed = "directory.enumerate.failed";
    public const string FileWriteSucceeded = "file.write.succeeded";
    public const string FileWriteFailed = "file.write.failed";
    public const string FileReadSucceeded = "file.read.succeeded";
    public const string FileReadFailed = "file.read.failed";
    public const string FileWatchStarted = "file.watch.started";
    public const string FileWatchStopped = "file.watch.stopped";
    public const string FileWatchChanged = "file.watch.changed";
    public const string FileWatchFailed = "file.watch.failed";
    public const string FileWatchDropped = "file.watch.dropped";
}
