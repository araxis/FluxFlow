using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.FileSystem.Composition;

public static partial class FileSystemComponentDefinition
{
    private static readonly FileReadOptions ReadDefaults = new();
    private static readonly FileWriteOptions WriteDefaults = new();
    private static readonly DirectoryEnumerateOptions EnumerateDefaults = new();
    private static readonly FileWatchOptions WatchDefaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
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
            FileSystemComponentDefinition.Types.Read,
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
                Options.MaxBytes,
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
            FileSystemComponentDefinition.Types.Write,
            "File Write",
            "Writes text or bytes to a file path using configured path policy.",
            "file-output",
            "writeFile");

        builder
            .AddOption(BoundedCapacityOption(WriteDefaults.BoundedCapacity))
            .AddOption(BaseDirectoryOption(OptionDesignMetadataAttributeValues.Primary))
            .AddOption(AllowAbsolutePathsOption(WriteDefaults.AllowAbsolutePaths));

        builder
            .AddAttribute("omittedOptions", Options.DefaultEncoding)
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
            FileSystemComponentDefinition.Types.DirectoryEnumerate,
            "Directory Enumerate",
            "Enumerates matching files and directories from a configured directory.",
            "folder-search",
            "enumerateDirectory");

        builder
            .AddOption(BoundedCapacityOption(EnumerateDefaults.BoundedCapacity))
            .AddOption(DirectoryOption(EnumerateDefaults.Directory))
            .AddOption(FilterOption(EnumerateDefaults.Filter))
            .AddOption(
                Options.IncludeSubdirectories,
                OptionValueKind.Boolean,
                displayName: "Include Subdirectories",
                helperText: "Enumerate entries below child directories.",
                defaultValue: EnumerateDefaults.IncludeSubdirectories,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.IncludeFiles,
                OptionValueKind.Boolean,
                displayName: "Include Files",
                helperText: "Emit matching file entries.",
                defaultValue: EnumerateDefaults.IncludeFiles,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.IncludeDirectories,
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
                Options.MaxEntries,
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
            FileSystemComponentDefinition.Types.Watch,
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
                Options.IncludeSubdirectories,
                OptionValueKind.Boolean,
                displayName: "Include Subdirectories",
                helperText: "Watch child directories.",
                defaultValue: WatchDefaults.IncludeSubdirectories,
                attributes: OptionAttributes(
                    "Traversal",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.NotifyFilters,
                OptionValueKind.Json,
                displayName: "Notify Filters",
                helperText: "Optional array of FileSystemWatcher notify filter names.",
                defaultValue: WatchDefaults.NotifyFilters,
                attributes: OptionAttributes(
                    "Watching",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Json))
            .AddOption(
                Options.InternalBufferSize,
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
                FileSystemComponentDefinition.Resources.Clock,
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
        Name = new ComponentOptionName(Options.BaseDirectory),
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
        Name = new ComponentOptionName(Options.AllowAbsolutePaths),
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
        Name = new ComponentOptionName(Options.DefaultEncoding),
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
        Name = new ComponentOptionName(Options.Directory),
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
        Name = new ComponentOptionName(Options.Filter),
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
                FileSystemComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                FileSystemComponentDefinition.Ports.Output,
                displayName: Ports.Output,
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
            FileSystemComponentDefinition.Ports.Output,
            displayName: Ports.Output,
            group: "Messages",
            order: 0,
            summary: outputSummary,
            valueType: outputType,
            isPrimary: true);


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

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Read =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.BaseDirectory),
                ComponentOptions.Metadata<bool>(Options.AllowAbsolutePaths),
                ComponentOptions.Metadata<string>(Options.DefaultEncoding),
                ComponentOptions.Metadata<long?>(Options.MaxBytes)
            ],
            Types.Write =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.BaseDirectory),
                ComponentOptions.Metadata<bool>(Options.AllowAbsolutePaths)
            ],
            Types.DirectoryEnumerate =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.Directory, isRequired: true),
                ComponentOptions.Metadata<string>(Options.Filter, isRequired: true),
                ComponentOptions.Metadata<bool>(Options.IncludeSubdirectories),
                ComponentOptions.Metadata<bool>(Options.IncludeFiles),
                ComponentOptions.Metadata<bool>(Options.IncludeDirectories),
                ComponentOptions.Metadata<string>(Options.BaseDirectory),
                ComponentOptions.Metadata<bool>(Options.AllowAbsolutePaths),
                ComponentOptions.Metadata<long?>(Options.MaxEntries)
            ],
            Types.Watch =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.Directory, isRequired: true),
                ComponentOptions.Metadata<string>(Options.BaseDirectory),
                ComponentOptions.Metadata<bool>(Options.AllowAbsolutePaths),
                ComponentOptions.Metadata<string>(Options.Filter, isRequired: true),
                ComponentOptions.Metadata<bool>(Options.IncludeSubdirectories),
                ComponentOptions.Metadata<string[]>(Options.NotifyFilters),
                ComponentOptions.Metadata<int?>(Options.InternalBufferSize)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Read =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Write =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.DirectoryEnumerate =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Watch =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Read = "file.read";
    
        public const string Write = "file.write";
    
        public const string DirectoryEnumerate = "directory.list";
        public const string Watch = "file.watch";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    }
}
