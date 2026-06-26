using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Options;

namespace FluxFlow.Components.FileSystem.Composition;

public sealed class FileSystemComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly FileReadOptions ReadDefaults = new();
    private static readonly FileWriteOptions WriteDefaults = new();
    private static readonly DirectoryEnumerateOptions EnumerateDefaults = new();
    private static readonly FileWatchOptions WatchDefaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateReadMetadata(),
            CreateWriteMetadata(),
            CreateDirectoryEnumerateMetadata(),
            CreateWatchMetadata()
        ];

    private static ComponentDesignMetadata CreateReadMetadata()
        => CreateFileSystemMetadata(
            FileSystemCompositionNodeTypes.Read,
            "File Read",
            "Reads text or bytes from a file path using configured path policy.",
            "file-input",
            "readFile",
            [
                BoundedCapacityOption(ReadDefaults.BoundedCapacity),
                BaseDirectoryOption(),
                AllowAbsolutePathsOption(ReadDefaults.AllowAbsolutePaths),
                DefaultEncodingOption(ReadDefaults.DefaultEncoding),
                new OptionDesignMetadata
                {
                    Name = "maxBytes",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Max Bytes",
                    DefaultValue = ReadDefaults.MaxBytes,
                    Min = 1,
                    HelperText = "Optional maximum file size to read. Leave empty for unlimited reads."
                }
            ],
            TransformPorts(
                nameof(FileReadRequest),
                "File read request.",
                nameof(FileReadResult),
                "File read result."));

    private static ComponentDesignMetadata CreateWriteMetadata()
        => CreateFileSystemMetadata(
            FileSystemCompositionNodeTypes.Write,
            "File Write",
            "Writes text or bytes to a file path using configured path policy.",
            "file-output",
            "writeFile",
            [
                BoundedCapacityOption(WriteDefaults.BoundedCapacity),
                BaseDirectoryOption(),
                AllowAbsolutePathsOption(WriteDefaults.AllowAbsolutePaths),
                DefaultEncodingOption(WriteDefaults.DefaultEncoding)
            ],
            TransformPorts(
                nameof(FileWriteRequest),
                "File write request.",
                nameof(FileWriteResult),
                "File write result."));

    private static ComponentDesignMetadata CreateDirectoryEnumerateMetadata()
        => CreateFileSystemMetadata(
            FileSystemCompositionNodeTypes.DirectoryEnumerate,
            "Directory Enumerate",
            "Enumerates matching files and directories from a configured directory.",
            "folder-search",
            "enumerateDirectory",
            [
                BoundedCapacityOption(EnumerateDefaults.BoundedCapacity),
                DirectoryOption(EnumerateDefaults.Directory),
                FilterOption(EnumerateDefaults.Filter),
                new OptionDesignMetadata
                {
                    Name = "includeSubdirectories",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Include Subdirectories",
                    DefaultValue = EnumerateDefaults.IncludeSubdirectories,
                    HelperText = "Enumerate entries below child directories."
                },
                new OptionDesignMetadata
                {
                    Name = "includeFiles",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Include Files",
                    DefaultValue = EnumerateDefaults.IncludeFiles,
                    HelperText = "Emit matching file entries."
                },
                new OptionDesignMetadata
                {
                    Name = "includeDirectories",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Include Directories",
                    DefaultValue = EnumerateDefaults.IncludeDirectories,
                    HelperText = "Emit matching directory entries."
                },
                BaseDirectoryOption(),
                AllowAbsolutePathsOption(EnumerateDefaults.AllowAbsolutePaths),
                new OptionDesignMetadata
                {
                    Name = "maxEntries",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Max Entries",
                    Min = 1,
                    HelperText = "Optional maximum number of entries to emit."
                }
            ],
            SourcePorts(nameof(DirectoryEnumerateEntry), "Directory entry."));

    private static ComponentDesignMetadata CreateWatchMetadata()
        => CreateFileSystemMetadata(
            FileSystemCompositionNodeTypes.Watch,
            "File Watch",
            "Watches a configured directory and emits file change events.",
            "folder-sync",
            "watchFiles",
            [
                BoundedCapacityOption(WatchDefaults.BoundedCapacity),
                DirectoryOption(WatchDefaults.Directory),
                BaseDirectoryOption(),
                AllowAbsolutePathsOption(WatchDefaults.AllowAbsolutePaths),
                FilterOption(WatchDefaults.Filter),
                new OptionDesignMetadata
                {
                    Name = "includeSubdirectories",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Include Subdirectories",
                    DefaultValue = WatchDefaults.IncludeSubdirectories,
                    HelperText = "Watch child directories."
                },
                new OptionDesignMetadata
                {
                    Name = "notifyFilters",
                    Kind = OptionValueKind.Json,
                    DisplayName = "Notify Filters",
                    DefaultValue = WatchDefaults.NotifyFilters,
                    HelperText = "Optional array of FileSystemWatcher notify filter names."
                },
                new OptionDesignMetadata
                {
                    Name = "internalBufferSize",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Internal Buffer Size",
                    Min = 4096,
                    Max = 65536,
                    HelperText = "Optional watcher buffer size in bytes."
                }
            ],
            SourcePorts(nameof(FileWatchEvent), "File watch event."));

    private static ComponentDesignMetadata CreateFileSystemMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        IReadOnlyList<OptionDesignMetadata> options,
        IReadOnlyList<PortDesignMetadata> ports) => new()
        {
            Type = new ComponentType(type),
            DisplayName = displayName,
            Category = "FileSystem",
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 460,
            Options = options,
            Resources = ClockResources(),
            Ports = ports
        };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = "Maximum queued messages."
    };

    private static OptionDesignMetadata BaseDirectoryOption() => new()
    {
        Name = "baseDirectory",
        Kind = OptionValueKind.Text,
        DisplayName = "Base Directory",
        HelperText = "Optional base directory used to resolve relative paths."
    };

    private static OptionDesignMetadata AllowAbsolutePathsOption(bool defaultValue) => new()
    {
        Name = "allowAbsolutePaths",
        Kind = OptionValueKind.Boolean,
        DisplayName = "Allow Absolute Paths",
        DefaultValue = defaultValue,
        HelperText = "Allow absolute paths in requests or configured directories."
    };

    private static OptionDesignMetadata DefaultEncodingOption(string defaultValue) => new()
    {
        Name = "defaultEncoding",
        Kind = OptionValueKind.Text,
        DisplayName = "Default Encoding",
        DefaultValue = defaultValue,
        HelperText = "Encoding name used when a request does not specify one."
    };

    private static OptionDesignMetadata DirectoryOption(string defaultValue) => new()
    {
        Name = "directory",
        Kind = OptionValueKind.Text,
        DisplayName = "Directory",
        DefaultValue = defaultValue,
        HelperText = "Directory path to resolve and use.",
        IsRequired = true
    };

    private static OptionDesignMetadata FilterOption(string defaultValue) => new()
    {
        Name = "filter",
        Kind = OptionValueKind.Text,
        DisplayName = "Filter",
        DefaultValue = defaultValue,
        HelperText = "File-system wildcard filter.",
        IsRequired = true
    };

    private static IReadOnlyList<ResourceDesignMetadata> ClockResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = FileSystemCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic file-system diagnostics and timestamps.",
                ValueType = nameof(TimeProvider)
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> TransformPorts(
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(FileSystemCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = inputSummary,
                ValueType = inputType,
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(FileSystemCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = outputSummary,
                ValueType = outputType,
                IsPrimary = true
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> SourcePorts(
        string outputType,
        string outputSummary)
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(FileSystemCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Messages",
                Order = 0,
                Summary = outputSummary,
                ValueType = outputType,
                IsPrimary = true
            }
        ];
}
