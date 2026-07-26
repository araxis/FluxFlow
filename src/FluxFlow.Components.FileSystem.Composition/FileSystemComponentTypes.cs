namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemComponentTypes
{
    public const string Read = "file.read";

    public const string Write = "file.write";

    public const string DirectoryEnumerate = "directory.list";
    public const string LegacyDirectoryEnumerate = "directory.enumerate";

    public const string Watch = "file.watch";
}
