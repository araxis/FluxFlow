using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.FileSystem;

public sealed class FileSystemComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(FileSystemComponentTypes.FileWrite, "File Write", "fileWrite", "Writes files from explicit file write requests.",
            "FileWriteRequest", "FileWriteResult"),
        Metadata(FileSystemComponentTypes.FileRead, "File Read", "fileRead", "Reads files from explicit file read requests.",
            "FileReadRequest", "FileReadResult",
            Number("maxBytes", "Max bytes", 16777216, 1)),
        Metadata(FileSystemComponentTypes.FileWatch, "File Watch", "fileWatch", "Watches a configured path and emits file change events.",
            null, "FileWatchEvent",
            Number("internalBufferSize", "Internal buffer bytes", 8192, 4096, 65536)),
        Metadata(FileSystemComponentTypes.DirectoryEnumerate, "Directory Enumerate", "directoryEnumerate", "Enumerates directory entries from explicit requests.",
            "DirectoryEnumerateRequest", "DirectoryEnumerateEntry")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        string? inputType,
        string outputType,
        params OptionDesignMetadata[] extraOptions) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "File System",
            Summary = summary,
            IconKey = "file-system",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 480,
            Options =
            [
                Text("path", "Path"),
                Number("boundedCapacity", "Capacity", 128, 1),
                .. extraOptions
            ],
            Ports = inputType is null
                ? [Port(FileSystemComponentPorts.Output, PortDirection.Output, outputType, true)]
                : [
                    Port(FileSystemComponentPorts.Input, PortDirection.Input, inputType, true),
                    Port(FileSystemComponentPorts.Result, PortDirection.Output, outputType, true, 1)
                ]
        };

    private static OptionDesignMetadata Text(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Number(
        string name,
        string displayName,
        object defaultValue,
        double min,
        double? max = null) => new()
        {
            Name = name,
            Kind = OptionValueKind.Number,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            Min = min,
            Max = max
        };

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
