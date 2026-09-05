namespace FluxFlow.Components.FileSystem.Composition;

public static partial class FileSystemComponentDefinition
{
    public static class Options
    {
        public const string BoundedCapacity = "boundedCapacity";
        public const string BaseDirectory = "baseDirectory";
        public const string AllowAbsolutePaths = "allowAbsolutePaths";
        public const string DefaultEncoding = "defaultEncoding";
        public const string MaxBytes = "maxBytes";
        public const string Directory = "directory";
        public const string Filter = "filter";
        public const string IncludeSubdirectories = "includeSubdirectories";
        public const string IncludeFiles = "includeFiles";
        public const string IncludeDirectories = "includeDirectories";
        public const string MaxEntries = "maxEntries";
        public const string NotifyFilters = "notifyFilters";
        public const string InternalBufferSize = "internalBufferSize";
    }

    public static class Types
    {
        public const string Read = "file.read";
        public const string Write = "file.write";
        public const string DirectoryEnumerate = "directory.list";
        public const string Watch = "file.watch";
    }

    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Clock = "clock"; }
}
