using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemCompositionNodeTypes
{
    public const string Read = "file.read";

    public const string Write = "file.write";

    public const string DirectoryEnumerate = "directory.list";
    public const string LegacyDirectoryEnumerate = "directory.enumerate";

    public const string Watch = "file.watch";

    internal static CompositionComponentTypeDescriptor DirectoryEnumerateDescriptor { get; } =
        new(DirectoryEnumerate, [LegacyDirectoryEnumerate]);
}
