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
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemCompositionNodeTypes.Read,
            "File Read",
            "Reads text or bytes from a file path using configured path policy.",
            "file-input",
            "readFile");

        builder
            .AddOption(BoundedCapacityOption(ReadDefaults.BoundedCapacity))
            .AddOption(BaseDirectoryOption())
            .AddOption(AllowAbsolutePathsOption(ReadDefaults.AllowAbsolutePaths))
            .AddOption(DefaultEncodingOption(ReadDefaults.DefaultEncoding))
            .AddOption(
                "maxBytes",
                OptionValueKind.Number,
                displayName: "Max Bytes",
                helperText: "Optional maximum file size to read. Leave empty for unlimited reads.",
                defaultValue: ReadDefaults.MaxBytes,
                min: 1);

        AddTransformPorts(
            builder,
            nameof(FileReadRequest),
            "File read request.",
            nameof(FileReadResult),
            "File read result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateWriteMetadata()
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemCompositionNodeTypes.Write,
            "File Write",
            "Writes text or bytes to a file path using configured path policy.",
            "file-output",
            "writeFile");

        builder
            .AddOption(BoundedCapacityOption(WriteDefaults.BoundedCapacity))
            .AddOption(BaseDirectoryOption())
            .AddOption(AllowAbsolutePathsOption(WriteDefaults.AllowAbsolutePaths))
            .AddOption(DefaultEncodingOption(WriteDefaults.DefaultEncoding));

        AddTransformPorts(
            builder,
            nameof(FileWriteRequest),
            "File write request.",
            nameof(FileWriteResult),
            "File write result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateDirectoryEnumerateMetadata()
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemCompositionNodeTypes.DirectoryEnumerate,
            "Directory Enumerate",
            "Enumerates matching files and directories from a configured directory.",
            "folder-search",
            "enumerateDirectory");

        builder
            .AddOption(BoundedCapacityOption(EnumerateDefaults.BoundedCapacity))
            .AddOption(DirectoryOption(EnumerateDefaults.Directory))
            .AddOption(FilterOption(EnumerateDefaults.Filter))
            .AddOption(
                "includeSubdirectories",
                OptionValueKind.Boolean,
                displayName: "Include Subdirectories",
                helperText: "Enumerate entries below child directories.",
                defaultValue: EnumerateDefaults.IncludeSubdirectories)
            .AddOption(
                "includeFiles",
                OptionValueKind.Boolean,
                displayName: "Include Files",
                helperText: "Emit matching file entries.",
                defaultValue: EnumerateDefaults.IncludeFiles)
            .AddOption(
                "includeDirectories",
                OptionValueKind.Boolean,
                displayName: "Include Directories",
                helperText: "Emit matching directory entries.",
                defaultValue: EnumerateDefaults.IncludeDirectories)
            .AddOption(BaseDirectoryOption())
            .AddOption(AllowAbsolutePathsOption(EnumerateDefaults.AllowAbsolutePaths))
            .AddOption(
                "maxEntries",
                OptionValueKind.Number,
                displayName: "Max Entries",
                helperText: "Optional maximum number of entries to emit.",
                min: 1);

        AddSourcePort(builder, nameof(DirectoryEnumerateEntry), "Directory entry.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateWatchMetadata()
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemCompositionNodeTypes.Watch,
            "File Watch",
            "Watches a configured directory and emits file change events.",
            "folder-sync",
            "watchFiles");

        builder
            .AddOption(BoundedCapacityOption(WatchDefaults.BoundedCapacity))
            .AddOption(DirectoryOption(WatchDefaults.Directory))
            .AddOption(BaseDirectoryOption())
            .AddOption(AllowAbsolutePathsOption(WatchDefaults.AllowAbsolutePaths))
            .AddOption(FilterOption(WatchDefaults.Filter))
            .AddOption(
                "includeSubdirectories",
                OptionValueKind.Boolean,
                displayName: "Include Subdirectories",
                helperText: "Watch child directories.",
                defaultValue: WatchDefaults.IncludeSubdirectories)
            .AddOption(
                "notifyFilters",
                OptionValueKind.Json,
                displayName: "Notify Filters",
                helperText: "Optional array of FileSystemWatcher notify filter names.",
                defaultValue: WatchDefaults.NotifyFilters)
            .AddOption(
                "internalBufferSize",
                OptionValueKind.Number,
                displayName: "Internal Buffer Size",
                helperText: "Optional watcher buffer size in bytes.",
                min: 4096,
                max: 65536);

        AddSourcePort(builder, nameof(FileWatchEvent), "File watch event.");

        return builder.Build();
    }

    private static ComponentDesignMetadataBuilder CreateFileSystemMetadataBuilder(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName)
        => new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "FileSystem",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 460)
            .AddResource(
                FileSystemCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic file-system diagnostics and timestamps.",
                valueType: nameof(TimeProvider));

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

    private static void AddTransformPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                FileSystemCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                FileSystemCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);

    private static void AddSourcePort(
        ComponentDesignMetadataBuilder builder,
        string outputType,
        string outputSummary)
        => builder.AddOutputPort(
            FileSystemCompositionPortNames.Output,
            displayName: "Output",
            group: "Messages",
            order: 0,
            summary: outputSummary,
            valueType: outputType,
            isPrimary: true);
}
