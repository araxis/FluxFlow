using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;

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
            FileSystemComponentTypes.Read,
            "File Read",
            "Reads text or bytes from a file path using configured path policy.",
            "file-input",
            "readFile");

        builder
            .AddOption(BoundedCapacityOption(ReadDefaults.BoundedCapacity))
            .AddOption(BaseDirectoryOption(OptionDesignMetadataAttributeValues.Primary))
            .AddOption(AllowAbsolutePathsOption(ReadDefaults.AllowAbsolutePaths))
            .AddOption(DefaultEncodingOption(ReadDefaults.DefaultEncoding))
            .AddOption(
                "maxBytes",
                OptionValueKind.Number,
                displayName: "Max Bytes",
                helperText: "Optional maximum file size to read. Leave empty for unlimited reads.",
                defaultValue: ReadDefaults.MaxBytes,
                min: 1,
                attributes: OptionAttributes(
                    "Limits",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number));

        AddTransformPorts(
            builder,
            nameof(FileReadRequest),
            "File read request.",
            nameof(FileReadContent),
            "File content; read failures use the message error case.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateWriteMetadata()
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemComponentTypes.Write,
            "File Write",
            "Writes text or bytes to a file path using configured path policy.",
            "file-output",
            "writeFile");

        builder
            .AddOption(BoundedCapacityOption(WriteDefaults.BoundedCapacity))
            .AddOption(BaseDirectoryOption(OptionDesignMetadataAttributeValues.Primary))
            .AddOption(AllowAbsolutePathsOption(WriteDefaults.AllowAbsolutePaths));

        builder
            .AddAttribute("omittedOptions", "defaultEncoding")
            .AddAttribute(
                "omittedOptionsReason",
                "Canonical file.write writes exact FlowContent bytes and does not encode text.");

        AddTransformPorts(
            builder,
            nameof(FileContentWriteRequest),
            "File content write request.",
            nameof(FileWriteResult),
            "File write receipt; write failures use the message error case.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateDirectoryEnumerateMetadata()
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemComponentTypes.DirectoryEnumerate,
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
                defaultValue: EnumerateDefaults.IncludeSubdirectories,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "includeFiles",
                OptionValueKind.Boolean,
                displayName: "Include Files",
                helperText: "Emit matching file entries.",
                defaultValue: EnumerateDefaults.IncludeFiles,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "includeDirectories",
                OptionValueKind.Boolean,
                displayName: "Include Directories",
                helperText: "Emit matching directory entries.",
                defaultValue: EnumerateDefaults.IncludeDirectories,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(BaseDirectoryOption(OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(AllowAbsolutePathsOption(EnumerateDefaults.AllowAbsolutePaths))
            .AddOption(
                "maxEntries",
                OptionValueKind.Number,
                displayName: "Max Entries",
                helperText: "Optional maximum number of entries to emit.",
                min: 1,
                attributes: OptionAttributes(
                    "Limits",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number));

        AddSourcePort(builder, nameof(DirectoryEntry), "Directory entry.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateWatchMetadata()
    {
        var builder = CreateFileSystemMetadataBuilder(
            FileSystemComponentTypes.Watch,
            "File Watch",
            "Watches a configured directory and emits file change events.",
            "folder-sync",
            "watchFiles");

        builder
            .AddOption(BoundedCapacityOption(WatchDefaults.BoundedCapacity))
            .AddOption(DirectoryOption(WatchDefaults.Directory))
            .AddOption(BaseDirectoryOption(OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(AllowAbsolutePathsOption(WatchDefaults.AllowAbsolutePaths))
            .AddOption(FilterOption(WatchDefaults.Filter))
            .AddOption(
                "includeSubdirectories",
                OptionValueKind.Boolean,
                displayName: "Include Subdirectories",
                helperText: "Watch child directories.",
                defaultValue: WatchDefaults.IncludeSubdirectories,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "notifyFilters",
                OptionValueKind.Json,
                displayName: "Notify Filters",
                helperText: "Optional array of FileSystemWatcher notify filter names.",
                defaultValue: WatchDefaults.NotifyFilters,
                attributes: OptionAttributes(
                    "Watching",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Json))
            .AddOption(
                "internalBufferSize",
                OptionValueKind.Number,
                displayName: "Internal Buffer Size",
                helperText: "Optional watcher buffer size in bytes.",
                min: 4096,
                max: 65536,
                attributes: OptionAttributes(
                    "Watching",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number));

        AddSourcePort(builder, nameof(FileChange), "File change event.");

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
                FileSystemComponentResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic file-system diagnostics and timestamps.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"));

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue)
        => OptionDesignMetadataFactory.BoundedCapacity(
            defaultValue,
            "Maximum queued messages.");

    private static OptionDesignMetadata BaseDirectoryOption(string importance) => new()
    {
        Name = new ComponentOptionName("baseDirectory"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Base Directory"),
        HelperText = new ComponentMetadataText("Optional base directory used to resolve relative paths."),
        Attributes = OptionAttributeMap(
            "Paths",
            importance,
            OptionDesignMetadataAttributeValues.Text)
    };

    private static OptionDesignMetadata AllowAbsolutePathsOption(bool defaultValue) => new()
    {
        Name = new ComponentOptionName("allowAbsolutePaths"),
        Kind = OptionValueKind.Boolean,
        DisplayName = new ComponentMetadataText("Allow Absolute Paths"),
        DefaultValue = defaultValue,
        HelperText = new ComponentMetadataText("Allow absolute paths in requests or configured directories."),
        Attributes = OptionAttributeMap(
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced)
    };

    private static OptionDesignMetadata DefaultEncodingOption(string defaultValue) => new()
    {
        Name = new ComponentOptionName("defaultEncoding"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Default Encoding"),
        DefaultValue = defaultValue,
        HelperText = new ComponentMetadataText("Encoding name used when a request does not specify one."),
        Attributes = OptionAttributeMap(
            "Encoding",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text)
    };

    private static OptionDesignMetadata DirectoryOption(string defaultValue) => new()
    {
        Name = new ComponentOptionName("directory"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Directory"),
        DefaultValue = defaultValue,
        HelperText = new ComponentMetadataText("Directory path to resolve and use."),
        IsRequired = true,
        Attributes = OptionAttributeMap(
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text)
    };

    private static OptionDesignMetadata FilterOption(string defaultValue) => new()
    {
        Name = new ComponentOptionName("filter"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Filter"),
        DefaultValue = defaultValue,
        HelperText = new ComponentMetadataText("File-system wildcard filter."),
        IsRequired = true,
        Attributes = OptionAttributeMap(
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text)
    };

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributeMap(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.CreateMap(
            section: section,
            importance: importance,
            editor: editor);

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddTransformPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                FileSystemComponentPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                FileSystemComponentPortNames.Output,
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
            FileSystemComponentPortNames.Output,
            displayName: "Output",
            group: "Messages",
            order: 0,
            summary: outputSummary,
            valueType: outputType,
            isPrimary: true);
}
