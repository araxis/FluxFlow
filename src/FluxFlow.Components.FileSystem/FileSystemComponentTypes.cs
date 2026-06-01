using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.FileSystem;

public static class FileSystemComponentTypes
{
    public static readonly NodeType FileRead = new("file.read");
    public static readonly NodeType FileWatch = new("file.watch");
    public static readonly NodeType FileWrite = new("file.write");
}
