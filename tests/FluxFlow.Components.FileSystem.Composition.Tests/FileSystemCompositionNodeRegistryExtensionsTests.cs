using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.FileSystem.Composition.Tests;

public sealed class FileSystemCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void RegisterFileSystemNodes_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterFileRead()
            .RegisterFileWrite()
            .RegisterDirectoryEnumerate()
            .RegisterFileWatch();

        var read = registry.Registrations[FileSystemCompositionNodeTypes.Read];
        read.Inputs[FileSystemCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(FileReadRequest));
        read.Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FileReadResult));

        var write = registry.Registrations[FileSystemCompositionNodeTypes.Write];
        write.Inputs[FileSystemCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(FileWriteRequest));
        write.Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FileWriteResult));

        registry.Registrations[FileSystemCompositionNodeTypes.DirectoryEnumerate]
            .Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(DirectoryEnumerateEntry));

        registry.Registrations[FileSystemCompositionNodeTypes.Watch]
            .Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FileWatchEvent));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_file_system_metadata()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            FileSystemCompositionNodeTypes.Read,
            FileSystemCompositionNodeTypes.Write,
            FileSystemCompositionNodeTypes.DirectoryEnumerate,
            FileSystemCompositionNodeTypes.Watch
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("FileSystem"));
            item.SuggestedEditorWidth.ShouldBe(460);
            item.Options.ShouldNotContain(option =>
                option.Name.Value == FileSystemCompositionResourceNames.Clock);
            AssertClockResource(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_file_system_ports()
    {
        var metadata = DesignMetadataByType();

        AssertTransformPorts<FileReadRequest, FileReadResult>(
            metadata[FileSystemCompositionNodeTypes.Read]);
        AssertTransformPorts<FileWriteRequest, FileWriteResult>(
            metadata[FileSystemCompositionNodeTypes.Write]);
        AssertSourcePort<DirectoryEnumerateEntry>(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate]);
        AssertSourcePort<FileWatchEvent>(
            metadata[FileSystemCompositionNodeTypes.Watch]);
    }

    [Fact]
    public void Design_metadata_provider_describes_file_system_options()
    {
        var metadata = DesignMetadataByType();
        var readDefaults = new FileReadOptions();
        var writeDefaults = new FileWriteOptions();
        var enumerateDefaults = new DirectoryEnumerateOptions();
        var watchDefaults = new FileWatchOptions();

        AssertOptionNames(
            metadata[FileSystemCompositionNodeTypes.Read],
            "boundedCapacity",
            "baseDirectory",
            "allowAbsolutePaths",
            "defaultEncoding",
            "maxBytes");
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Read],
            "boundedCapacity",
            OptionValueKind.Number,
            readDefaults.BoundedCapacity,
            min: 1);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Read],
            "baseDirectory",
            OptionValueKind.Text);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Read],
            "allowAbsolutePaths",
            OptionValueKind.Boolean,
            readDefaults.AllowAbsolutePaths);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Read],
            "defaultEncoding",
            OptionValueKind.Text,
            readDefaults.DefaultEncoding);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Read],
            "maxBytes",
            OptionValueKind.Number,
            readDefaults.MaxBytes,
            min: 1);

        AssertOptionNames(
            metadata[FileSystemCompositionNodeTypes.Write],
            "boundedCapacity",
            "baseDirectory",
            "allowAbsolutePaths",
            "defaultEncoding");
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Write],
            "boundedCapacity",
            OptionValueKind.Number,
            writeDefaults.BoundedCapacity,
            min: 1);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Write],
            "defaultEncoding",
            OptionValueKind.Text,
            writeDefaults.DefaultEncoding);

        AssertOptionNames(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate],
            "boundedCapacity",
            "directory",
            "filter",
            "includeSubdirectories",
            "includeFiles",
            "includeDirectories",
            "baseDirectory",
            "allowAbsolutePaths",
            "maxEntries");
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate],
            "directory",
            OptionValueKind.Text,
            enumerateDefaults.Directory,
            isRequired: true);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate],
            "filter",
            OptionValueKind.Text,
            enumerateDefaults.Filter,
            isRequired: true);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate],
            "includeFiles",
            OptionValueKind.Boolean,
            enumerateDefaults.IncludeFiles);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate],
            "maxEntries",
            OptionValueKind.Number,
            min: 1);

        AssertOptionNames(
            metadata[FileSystemCompositionNodeTypes.Watch],
            "boundedCapacity",
            "directory",
            "baseDirectory",
            "allowAbsolutePaths",
            "filter",
            "includeSubdirectories",
            "notifyFilters",
            "internalBufferSize");
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Watch],
            "directory",
            OptionValueKind.Text,
            watchDefaults.Directory,
            isRequired: true);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Watch],
            "filter",
            OptionValueKind.Text,
            watchDefaults.Filter,
            isRequired: true);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Watch],
            "notifyFilters",
            OptionValueKind.Json,
            watchDefaults.NotifyFilters);
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Watch],
            "internalBufferSize",
            OptionValueKind.Number,
            min: 4096,
            max: 65536);
    }

    [Fact]
    public void Design_metadata_provider_describes_file_system_option_hints()
    {
        var metadata = DesignMetadataByType();

        var read = OptionsByName(metadata[FileSystemCompositionNodeTypes.Read]);
        AssertOptionHints(
            read["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            read["baseDirectory"],
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            read["allowAbsolutePaths"],
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            read["defaultEncoding"],
            "Encoding",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            read["maxBytes"],
            "Limits",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Number);

        var write = OptionsByName(metadata[FileSystemCompositionNodeTypes.Write]);
        AssertOptionHints(
            write["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            write["baseDirectory"],
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            write["allowAbsolutePaths"],
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            write["defaultEncoding"],
            "Encoding",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);

        var enumerate = OptionsByName(metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate]);
        AssertOptionHints(
            enumerate["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            enumerate["directory"],
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            enumerate["filter"],
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            enumerate["includeSubdirectories"],
            "Traversal",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            enumerate["includeFiles"],
            "Traversal",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            enumerate["includeDirectories"],
            "Traversal",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            enumerate["baseDirectory"],
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            enumerate["allowAbsolutePaths"],
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            enumerate["maxEntries"],
            "Limits",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);

        var watch = OptionsByName(metadata[FileSystemCompositionNodeTypes.Watch]);
        AssertOptionHints(
            watch["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            watch["directory"],
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            watch["baseDirectory"],
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            watch["allowAbsolutePaths"],
            "Paths",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            watch["filter"],
            "Paths",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            watch["includeSubdirectories"],
            "Traversal",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            watch["notifyFilters"],
            "Watching",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(
            watch["internalBufferSize"],
            "Watching",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_file_system_resource_picker_hints()
    {
        var metadata = DesignMetadataByType();

        foreach (var item in metadata.Values)
        {
            var resource = item.Resources.ShouldHaveSingleItem();

            AssertResourceHints(
                resource,
                ResourceDesignMetadataAttributeValues.Clock,
                "clock:{name}");
        }
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new FileSystemComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(4);
        catalog.TryGet(
            new ComponentType(FileSystemCompositionNodeTypes.Read),
            out var readMetadata).ShouldBeTrue();
        readMetadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("File Read");
        catalog.TryGet(
            new ComponentType(FileSystemCompositionNodeTypes.Watch),
            out var watchMetadata).ShouldBeTrue();
        watchMetadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("File Watch");
    }

    [Fact]
    public async Task Hosted_file_read_reads_from_base_directory_and_preserves_correlation_id()
    {
        using var directory = TempDirectory.Create("read");
        var filePath = Path.Combine(directory.Path, "input.txt");
        await File.WriteAllTextAsync(filePath, "hello");
        var timestamp = DateTimeOffset.Parse("2026-06-19T10:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithTransformNodeAsync<FileReadRequest, FileReadResult>(
            FileSystemCompositionNodeTypes.Read,
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var message = FlowMessage.Create(
                    new FileReadRequest { Path = "input.txt" },
                    new CorrelationId("read"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.Payload.Path.ShouldBe(Path.GetFullPath(filePath));
                result.Payload.Content.ShouldBe("hello");
                result.Payload.ReadAt.ShouldBe(timestamp);

                var @event = await ReceiveEventAsync(
                    events,
                    FileSystemDiagnosticNames.FileReadSucceeded);
                @event.CorrelationId.ShouldBe(message.CorrelationId);
                @event.Timestamp.ShouldBe(timestamp);
            },
            node => node
                .Configure("baseDirectory", directory.Path)
                .Configure("maxBytes", 32)
                .Resource(FileSystemCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock),
            registry => registry.RegisterFileRead());
    }

    [Fact]
    public async Task Hosted_file_write_writes_under_base_directory_and_preserves_correlation_id()
    {
        using var directory = TempDirectory.Create("write");
        var timestamp = DateTimeOffset.Parse("2026-06-19T10:30:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var expectedPath = Path.Combine(directory.Path, "nested", "output.txt");

        await WithTransformNodeAsync<FileWriteRequest, FileWriteResult>(
            FileSystemCompositionNodeTypes.Write,
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var message = FlowMessage.Create(
                    new FileWriteRequest
                    {
                        Path = "nested/output.txt",
                        Content = "written"
                    },
                    new CorrelationId("write"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.Payload.Path.ShouldBe(Path.GetFullPath(expectedPath));
                result.Payload.BytesWritten.ShouldBe(7);
                result.Payload.WrittenAt.ShouldBe(timestamp);
                (await File.ReadAllTextAsync(expectedPath)).ShouldBe("written");

                var @event = await ReceiveEventAsync(
                    events,
                    FileSystemDiagnosticNames.FileWriteSucceeded);
                @event.CorrelationId.ShouldBe(message.CorrelationId);
                @event.Timestamp.ShouldBe(timestamp);
            },
            node => node
                .Configure("baseDirectory", directory.Path)
                .Resource(FileSystemCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock),
            registry => registry.RegisterFileWrite());
    }

    [Fact]
    public async Task Hosted_directory_enumerate_starts_through_runtime_and_completes()
    {
        using var directory = TempDirectory.Create("enumerate");
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "nested", "child.txt"), "child");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "skip.bin"), "skip");
        var timestamp = DateTimeOffset.Parse("2026-06-19T11:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "enumerate",
                    FileSystemCompositionNodeTypes.DirectoryEnumerate,
                    node => node
                        .Configure("directory", ".")
                        .Configure("baseDirectory", directory.Path)
                        .Configure("filter", "*.txt")
                        .Configure("includeSubdirectories", true)
                        .Configure("boundedCapacity", 8)
                        .Resource(FileSystemCompositionResourceNames.Clock, "fixed")))
                .Build())
            .RegisterNodes(registry => registry.RegisterDirectoryEnumerate())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var node = runtime.Nodes.ShouldHaveSingleItem();
        var output = node.Descriptor.Outputs[FileSystemCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<DirectoryEnumerateEntry>>();
        var entries = Link(output.Source);
        var events = Link(node.Descriptor.Events.ShouldNotBeNull());

        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(Timeout);

        var emitted = await DrainUntilCompletedAsync(entries);
        emitted.Select(message => message.Payload.Name).Order()
            .ShouldBe(["child.txt", "root.txt"]);
        emitted.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
        emitted.ShouldAllBe(message => message.Payload.EnumeratedAt == timestamp);
        (await DrainUntilCompletedAsync(events))
            .Select(value => value.Name)
            .ShouldContain(FileSystemDiagnosticNames.DirectoryEnumerateCompleted);
    }

    [Fact]
    public async Task Hosted_file_watch_starts_observes_change_and_stops()
    {
        using var directory = TempDirectory.Create("watch");
        var timestamp = DateTimeOffset.Parse("2026-06-19T11:30:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var watchedPath = Path.Combine(directory.Path, "created.txt");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "watch",
                    FileSystemCompositionNodeTypes.Watch,
                    node => node
                        .Configure("directory", ".")
                        .Configure("baseDirectory", directory.Path)
                        .Configure("boundedCapacity", 16)
                        .Resource(FileSystemCompositionResourceNames.Clock, "fixed")))
                .Build())
            .RegisterNodes(registry => registry.RegisterFileWatch())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var node = runtime.Nodes.ShouldHaveSingleItem();
        var output = node.Descriptor.Outputs[FileSystemCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FileWatchEvent>>();
        var changes = Link(output.Source);
        var events = Link(node.Descriptor.Events.ShouldNotBeNull());

        await runtime.StartAsync();
        var started = await ReceiveEventAsync(
            events,
            FileSystemDiagnosticNames.FileWatchStarted);
        started.Timestamp.ShouldBe(timestamp);

        await File.WriteAllTextAsync(watchedPath, "hello");

        var change = await ReceiveMatchingAsync(
            changes,
            value => value.Name == "created.txt" &&
                     value.ChangeType is FileWatchChangeType.Created or FileWatchChangeType.Changed);
        change.Payload.Path.ShouldBe(Path.GetFullPath(watchedPath));
        change.Payload.Directory.ShouldBe(Path.GetFullPath(directory.Path));
        change.Payload.Timestamp.ShouldBe(timestamp);
        change.CorrelationId.IsEmpty.ShouldBeFalse();

        await ReceiveEventAsync(events, FileSystemDiagnosticNames.FileWatchChanged);
        await runtime.StopAsync().AsTask().WaitAsync(Timeout);
        await runtime.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Hosted_file_read_emits_errors_and_continues_after_missing_file()
    {
        using var directory = TempDirectory.Create("read-errors");
        var validPath = Path.Combine(directory.Path, "valid.txt");
        await File.WriteAllTextAsync(validPath, "ok");

        await WithTransformNodeAsync<FileReadRequest, FileReadResult>(
            FileSystemCompositionNodeTypes.Read,
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var missing = FlowMessage.Create(
                    new FileReadRequest { Path = "missing.txt" },
                    new CorrelationId("missing"));
                var valid = FlowMessage.Create(
                    new FileReadRequest { Path = "valid.txt" },
                    new CorrelationId("valid"));

                (await input.Target.SendAsync(missing).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(valid).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var result = await results.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(FileSystemErrorCodes.FileReadNotFound);
                error.CorrelationId.ShouldBe(missing.CorrelationId);
                result.CorrelationId.ShouldBe(valid.CorrelationId);
                result.Payload.Content.ShouldBe("ok");
            },
            node => node.Configure("baseDirectory", directory.Path),
            configureRegistry: registry => registry.RegisterFileRead());
    }

    [Theory]
    [InlineData(FileSystemCompositionNodeTypes.Read, "boundedCapacity", 0, "capacity")]
    [InlineData(FileSystemCompositionNodeTypes.Read, "maxBytes", 0L, "maxBytes")]
    [InlineData(FileSystemCompositionNodeTypes.Read, "defaultEncoding", "not-a-real-encoding", "defaultEncoding")]
    [InlineData(FileSystemCompositionNodeTypes.Write, "boundedCapacity", 0, "capacity")]
    [InlineData(FileSystemCompositionNodeTypes.Write, "defaultEncoding", "not-a-real-encoding", "defaultEncoding")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "directory", "", "directory")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "filter", "", "filter")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "includeFiles", false, "includeFiles")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "maxEntries", 0L, "maxEntries")]
    [InlineData(FileSystemCompositionNodeTypes.Watch, "boundedCapacity", 0, "boundedCapacity")]
    [InlineData(FileSystemCompositionNodeTypes.Watch, "directory", "", "directory")]
    [InlineData(FileSystemCompositionNodeTypes.Watch, "filter", "", "filter")]
    [InlineData(FileSystemCompositionNodeTypes.Watch, "internalBufferSize", 1024, "internalBufferSize")]
    public async Task Invalid_configuration_surfaces_factory_diagnostic(
        string nodeType,
        string optionName,
        object value,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    nodeType,
                    node =>
                    {
                        if (nodeType is FileSystemCompositionNodeTypes.DirectoryEnumerate or
                            FileSystemCompositionNodeTypes.Watch)
                        {
                            node.Configure("directory", ".");
                        }

                        node.Configure(optionName, value);
                    }))
                .Build())
            .RegisterNodes(registry => registry
                .RegisterFileRead()
                .RegisterFileWrite()
                .RegisterDirectoryEnumerate()
                .RegisterFileWatch())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_watch_notify_filter_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "watch",
                    FileSystemCompositionNodeTypes.Watch,
                    node => node
                        .Configure("directory", ".")
                        .Configure("notifyFilters", new[] { "DefinitelyNotAFilter" })))
                .Build())
            .RegisterNodes(registry => registry.RegisterFileWatch())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("notifyFilters", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WithTransformNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<
            CompositionInputPort<TInput>,
            CompositionOutputPort<TOutput>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null,
        Action<CompositionNodeRegistry>? configureRegistry = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registry => configureRegistry?.Invoke(registry))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[FileSystemCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<TInput>>();
        var output = descriptor.Outputs[FileSystemCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<TOutput>>();

        await run(input, output, descriptor);
    }

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => new FileSystemComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static void AssertTransformPorts<TInput, TOutput>(
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(FileSystemCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(typeof(TInput).Name);
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(FileSystemCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe(typeof(TOutput).Name);
        output.IsPrimary.ShouldBeTrue();
    }

    private static void AssertSourcePort<TOutput>(
        ComponentDesignMetadata metadata)
    {
        metadata.Ports.Count.ShouldBe(1);

        var output = metadata.Ports[0];
        output.Name.Value.ShouldBe(FileSystemCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(0);
        output.ValueType?.Value.ShouldBe(typeof(TOutput).Name);
        output.IsPrimary.ShouldBeTrue();
    }

    private static void AssertOptionNames(
        ComponentDesignMetadata metadata,
        params string[] names)
        => metadata.Options.Select(option => option.Name.Value)
            .ShouldBe(names, ignoreOrder: false);

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null,
        double? max = null,
        bool isRequired = false)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        if (defaultValue is string[] expectedArray)
        {
            option.DefaultValue.ShouldBeOfType<string[]>().ShouldBe(expectedArray);
        }
        else
        {
            option.DefaultValue.ShouldBe(defaultValue);
        }

        option.Min.ShouldBe(min);
        option.Max.ShouldBe(max);
        option.IsRequired.ShouldBe(isRequired);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();

        resource.Name.Value.ShouldBe(FileSystemCompositionResourceNames.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);

        if (editor is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> sink)
    {
        var items = new List<T>();
        while (await sink.OutputAvailableAsync().WaitAsync(Timeout))
        {
            while (sink.TryReceive(out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static async Task<FlowMessage<FileWatchEvent>> ReceiveMatchingAsync(
        BufferBlock<FlowMessage<FileWatchEvent>> output,
        Func<FileWatchEvent, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        while (!cancellation.IsCancellationRequested)
        {
            var value = await output.ReceiveAsync(cancellation.Token);
            if (predicate(value.Payload))
            {
                return value;
            }
        }

        throw new TimeoutException("Timed out waiting for file watch event.");
    }

    private static async Task<FlowEvent> ReceiveEventAsync(
        BufferBlock<FlowEvent> events,
        string name)
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        while (!cancellation.IsCancellationRequested)
        {
            var value = await events.ReceiveAsync(cancellation.Token);
            if (value.Name == name)
            {
                return value;
            }
        }

        throw new TimeoutException($"Timed out waiting for event '{name}'.");
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create(string label)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fluxflow-filesystem-composition-{label}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a watcher may still hold a handle briefly.
            }
        }
    }
}
