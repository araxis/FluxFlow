using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Designer;

public static class FileSystemDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddFileSystemDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(FileSystemComponentDefinition.Types.Read, DescribeRead)
            .AddDesignMetadata(FileSystemComponentDefinition.Types.Write, DescribeWrite)
            .AddDesignMetadata(FileSystemComponentDefinition.Types.DirectoryEnumerate, DescribeDirectoryEnumerate)
            .AddDesignMetadata(FileSystemComponentDefinition.Types.Watch, DescribeWatch);
    }

    private static ComponentDesignMetadata DescribeRead(ComponentDesignMetadata metadata)
    {
        var defaults = new FileReadOptions();
        metadata = metadata.WithDisplay("File Read", "FileSystem", "Reads text or bytes from a file path using configured path policy.", "file-input", "readFile", 460);
        metadata = metadata.WithResource(FileSystemComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic file-system diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BaseDirectory, OptionValueKind.Text, "Base Directory", "Optional base directory used to resolve relative paths.", section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.AllowAbsolutePaths, OptionValueKind.Boolean, "Allow Absolute Paths", "Allow absolute paths in requests or configured directories.", defaultValue: defaults.AllowAbsolutePaths, section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.DefaultEncoding, OptionValueKind.Text, "Default Encoding", "Encoding name used when a request does not specify one.", defaultValue: defaults.DefaultEncoding, section: "Encoding", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.MaxBytes, OptionValueKind.Number, "Max Bytes", "Optional maximum file size to read. Leave empty for unlimited reads.", defaultValue: defaults.MaxBytes, min: 1, section: "Limits", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(FileSystemComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "File read request.", true)
            .WithPort(FileSystemComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "File content; read failures use the message error case.", true)
            .WithPort(FileSystemComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort file-read diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeWrite(ComponentDesignMetadata metadata)
    {
        var defaults = new FileWriteOptions();
        metadata = metadata.WithDisplay("File Write", "FileSystem", "Writes text or bytes to a file path using configured path policy.", "file-output", "writeFile", 460);
        metadata = metadata.WithResource(FileSystemComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic file-system diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BaseDirectory, OptionValueKind.Text, "Base Directory", "Optional base directory used to resolve relative paths.", section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.AllowAbsolutePaths, OptionValueKind.Boolean, "Allow Absolute Paths", "Allow absolute paths in requests or configured directories.", defaultValue: defaults.AllowAbsolutePaths, section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithAttribute("omittedOptions", FileSystemComponentDefinition.Options.DefaultEncoding, appendExistingWith: ",");
        metadata = metadata.WithAttribute("omittedOptionsReason", "Canonical file.write writes exact FlowContent bytes and does not encode text.", appendExistingWith: " ");
        metadata = metadata
            .WithPort(FileSystemComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "File content write request.", true)
            .WithPort(FileSystemComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "File write receipt; write failures use the message error case.", true)
            .WithPort(FileSystemComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort file-write diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeDirectoryEnumerate(ComponentDesignMetadata metadata)
    {
        var defaults = new DirectoryEnumerateOptions();
        metadata = metadata.WithDisplay("Directory Enumerate", "FileSystem", "Enumerates matching files and directories from a configured directory.", "folder-search", "enumerateDirectory", 460);
        metadata = metadata.WithResource(FileSystemComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic file-system diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.Directory, OptionValueKind.Text, "Directory", "Directory path to resolve and use.", defaultValue: defaults.Directory, section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.Filter, OptionValueKind.Text, "Filter", "File-system wildcard filter.", defaultValue: defaults.Filter, section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.IncludeSubdirectories, OptionValueKind.Boolean, "Include Subdirectories", "Enumerate entries below child directories.", defaultValue: defaults.IncludeSubdirectories, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.IncludeFiles, OptionValueKind.Boolean, "Include Files", "Emit matching file entries.", defaultValue: defaults.IncludeFiles, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.IncludeDirectories, OptionValueKind.Boolean, "Include Directories", "Emit matching directory entries.", defaultValue: defaults.IncludeDirectories, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BaseDirectory, OptionValueKind.Text, "Base Directory", "Optional base directory used to resolve relative paths.", section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.AllowAbsolutePaths, OptionValueKind.Boolean, "Allow Absolute Paths", "Allow absolute paths in requests or configured directories.", defaultValue: defaults.AllowAbsolutePaths, section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.MaxEntries, OptionValueKind.Number, "Max Entries", "Optional maximum number of entries to emit.", min: 1, section: "Limits", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(FileSystemComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 0, "Directory entry.", true)
            .WithPort(FileSystemComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 1, "Best-effort directory-enumeration diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeWatch(ComponentDesignMetadata metadata)
    {
        var defaults = new FileWatchOptions();
        metadata = metadata.WithDisplay("File Watch", "FileSystem", "Watches a configured directory and emits file change events.", "folder-sync", "watchFiles", 460);
        metadata = metadata.WithResource(FileSystemComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic file-system diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.Directory, OptionValueKind.Text, "Directory", "Directory path to resolve and use.", defaultValue: defaults.Directory, section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.BaseDirectory, OptionValueKind.Text, "Base Directory", "Optional base directory used to resolve relative paths.", section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.AllowAbsolutePaths, OptionValueKind.Boolean, "Allow Absolute Paths", "Allow absolute paths in requests or configured directories.", defaultValue: defaults.AllowAbsolutePaths, section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.Filter, OptionValueKind.Text, "Filter", "File-system wildcard filter.", defaultValue: defaults.Filter, section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.IncludeSubdirectories, OptionValueKind.Boolean, "Include Subdirectories", "Watch child directories.", defaultValue: defaults.IncludeSubdirectories, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.NotifyFilters, OptionValueKind.Json, "Notify Filters", "Optional array of FileSystemWatcher notify filter names.", defaultValue: defaults.NotifyFilters, section: "Watching", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);
        metadata = metadata.WithOption(FileSystemComponentDefinition.Options.InternalBufferSize, OptionValueKind.Number, "Internal Buffer Size", "Optional watcher buffer size in bytes.", min: 4096, max: 65536, section: "Watching", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(FileSystemComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 0, "File change event.", true)
            .WithPort(FileSystemComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 1, "Best-effort file-watch diagnostics.");
        return metadata;
    }
}
