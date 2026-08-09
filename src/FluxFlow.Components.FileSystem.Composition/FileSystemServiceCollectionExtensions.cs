using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddFileSystem(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignedComponent(FileSystemComponents.FileRead)
            .AddDesignedComponent(FileSystemComponents.FileWrite)
            .AddDesignedComponent(FileSystemComponents.DirectoryEnumerate)
            .AddDesignedComponent(FileSystemComponents.FileWatch);
    }

    internal static void ConfigureRead(ComponentRegistrationBuilder component)
    {
        var defaults = new FileReadOptions();
        ConfigureCommon(component, "File Read", "Reads text or bytes from a file path using configured path policy.", "file-input", "readFile");
        AddCapacity(component, defaults.BoundedCapacity);
        AddBaseDirectory(component, OptionDesignMetadataAttributeValues.Primary);
        AddAllowAbsolutePaths(component, defaults.AllowAbsolutePaths);
        component.AddOption<string>(FileSystemComponentDefinition.Options.DefaultEncoding, OptionValueKind.Text, "Default Encoding", "Encoding name used when a request does not specify one.", defaultValue: defaults.DefaultEncoding, section: "Encoding", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<long?>(FileSystemComponentDefinition.Options.MaxBytes, OptionValueKind.Number, "Max Bytes", "Optional maximum file size to read. Leave empty for unlimited reads.", defaultValue: defaults.MaxBytes, min: 1, section: "Limits", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        component
            .UseFactory(CreateFileReadNode)
            .HasInput(FileSystemComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "File read request.", true)
            .HasOutput(FileSystemComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "File content; read failures use the message error case.", true)
            .HasEvents(FileSystemComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort file-read diagnostics.");
    }

    internal static void ConfigureWrite(ComponentRegistrationBuilder component)
    {
        var defaults = new FileWriteOptions();
        ConfigureCommon(component, "File Write", "Writes text or bytes to a file path using configured path policy.", "file-output", "writeFile");
        AddCapacity(component, defaults.BoundedCapacity);
        AddBaseDirectory(component, OptionDesignMetadataAttributeValues.Primary);
        AddAllowAbsolutePaths(component, defaults.AllowAbsolutePaths);
        component.AddAttribute("omittedOptions", FileSystemComponentDefinition.Options.DefaultEncoding);
        component.AddAttribute("omittedOptionsReason", "Canonical file.write writes exact FlowContent bytes and does not encode text.");
        component
            .UseFactory(CreateFileWriteNode)
            .HasInput(FileSystemComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "File content write request.", true)
            .HasOutput(FileSystemComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "File write receipt; write failures use the message error case.", true)
            .HasEvents(FileSystemComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort file-write diagnostics.");
    }

    internal static void ConfigureDirectoryEnumerate(ComponentRegistrationBuilder component)
    {
        var defaults = new DirectoryEnumerateOptions();
        ConfigureCommon(component, "Directory Enumerate", "Enumerates matching files and directories from a configured directory.", "folder-search", "enumerateDirectory");
        AddCapacity(component, defaults.BoundedCapacity);
        AddDirectory(component, defaults.Directory);
        AddFilter(component, defaults.Filter);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeSubdirectories, OptionValueKind.Boolean, "Include Subdirectories", "Enumerate entries below child directories.", defaultValue: defaults.IncludeSubdirectories, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeFiles, OptionValueKind.Boolean, "Include Files", "Emit matching file entries.", defaultValue: defaults.IncludeFiles, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeDirectories, OptionValueKind.Boolean, "Include Directories", "Emit matching directory entries.", defaultValue: defaults.IncludeDirectories, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddBaseDirectory(component, OptionDesignMetadataAttributeValues.Advanced);
        AddAllowAbsolutePaths(component, defaults.AllowAbsolutePaths);
        component.AddOption<long?>(FileSystemComponentDefinition.Options.MaxEntries, OptionValueKind.Number, "Max Entries", "Optional maximum number of entries to emit.", min: 1, section: "Limits", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component
            .UseFactory(CreateDirectoryEnumerateNode)
            .HasOutput(FileSystemComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 0, "Directory entry.", true)
            .HasEvents(FileSystemComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 1, "Best-effort directory-enumeration diagnostics.");
    }

    internal static void ConfigureWatch(ComponentRegistrationBuilder component)
    {
        var defaults = new FileWatchOptions();
        ConfigureCommon(component, "File Watch", "Watches a configured directory and emits file change events.", "folder-sync", "watchFiles");
        AddCapacity(component, defaults.BoundedCapacity);
        AddDirectory(component, defaults.Directory);
        AddBaseDirectory(component, OptionDesignMetadataAttributeValues.Advanced);
        AddAllowAbsolutePaths(component, defaults.AllowAbsolutePaths);
        AddFilter(component, defaults.Filter);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeSubdirectories, OptionValueKind.Boolean, "Include Subdirectories", "Watch child directories.", defaultValue: defaults.IncludeSubdirectories, section: "Traversal", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<string[]>(FileSystemComponentDefinition.Options.NotifyFilters, OptionValueKind.Json, "Notify Filters", "Optional array of FileSystemWatcher notify filter names.", defaultValue: defaults.NotifyFilters, section: "Watching", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);
        component.AddOption<int?>(FileSystemComponentDefinition.Options.InternalBufferSize, OptionValueKind.Number, "Internal Buffer Size", "Optional watcher buffer size in bytes.", min: 4096, max: 65536, section: "Watching", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component
            .UseFactory(CreateFileWatchNode)
            .HasOutput(FileSystemComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 0, "File change event.", true)
            .HasEvents(FileSystemComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 1, "Best-effort file-watch diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        component.WithDisplay(displayName, "FileSystem", summary, iconKey, preferredNodeName, 460);
        component.AddResource<TimeProvider>(FileSystemComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic file-system diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
    }

    private static void AddCapacity(ComponentRegistrationBuilder component, int defaultValue)
        => component.AddOption<int>(FileSystemComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaultValue, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddBaseDirectory(ComponentRegistrationBuilder component, string importance)
        => component.AddOption<string>(FileSystemComponentDefinition.Options.BaseDirectory, OptionValueKind.Text, "Base Directory", "Optional base directory used to resolve relative paths.", section: "Paths", importance: importance, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddAllowAbsolutePaths(ComponentRegistrationBuilder component, bool defaultValue)
        => component.AddOption<bool>(FileSystemComponentDefinition.Options.AllowAbsolutePaths, OptionValueKind.Boolean, "Allow Absolute Paths", "Allow absolute paths in requests or configured directories.", defaultValue: defaultValue, section: "Paths", importance: OptionDesignMetadataAttributeValues.Advanced);

    private static void AddDirectory(ComponentRegistrationBuilder component, string defaultValue)
        => component.AddOption<string>(FileSystemComponentDefinition.Options.Directory, OptionValueKind.Text, "Directory", "Directory path to resolve and use.", true, defaultValue, section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddFilter(ComponentRegistrationBuilder component, string defaultValue)
        => component.AddOption<string>(FileSystemComponentDefinition.Options.Filter, OptionValueKind.Text, "Filter", "File-system wildcard filter.", true, defaultValue, section: "Paths", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);

    private static FileReadNode CreateFileReadNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new FileReadNode(options, clock);
    }

    private static FileWriteNode CreateFileWriteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWriteOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new FileWriteNode(options, clock);
    }

    private static DirectoryEnumerateNode CreateDirectoryEnumerateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<DirectoryEnumerateOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new DirectoryEnumerateNode(options, clock);
    }

    private static FileWatchNode CreateFileWatchNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWatchOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new FileWatchNode(options, clock);
    }
}
