using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.FileSystem.Composition.Tests;

public sealed class FileSystemCompositionNodeRegistryExtensionsTests
{
    private const string WorkflowName = "main";
    private const string ComponentName = "node";
    private const string ValueRecorderType = "test.flow-value-recorder";
    private const string EventRecorderType = "test.component-event-recorder";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort(WorkflowName, ComponentName, "Input");
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort(WorkflowName, ComponentName, "Output");
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort(WorkflowName, ComponentName, "Events");

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
            .ShouldBe(typeof(FlowResult<FileReadContent>));

        var write = registry.Registrations[FileSystemCompositionNodeTypes.Write];
        write.Inputs[FileSystemCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(FileContentWriteRequest));
        write.Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<FileWriteResult>));

        registry.Registrations[FileSystemCompositionNodeTypes.DirectoryEnumerate]
            .Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowValue));

        registry.Registrations[FileSystemCompositionNodeTypes.Watch]
            .Outputs[FileSystemCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowValue));
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

        AssertTransformPorts<FileReadRequest, FlowResult<FileReadContent>>(
            metadata[FileSystemCompositionNodeTypes.Read]);
        AssertTransformPorts<FileContentWriteRequest, FlowResult<FileWriteResult>>(
            metadata[FileSystemCompositionNodeTypes.Write]);
        AssertSourcePort<FlowValue>(
            metadata[FileSystemCompositionNodeTypes.DirectoryEnumerate]);
        AssertSourcePort<FlowValue>(
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
            "allowAbsolutePaths");
        AssertOption(
            metadata[FileSystemCompositionNodeTypes.Write],
            "boundedCapacity",
            OptionValueKind.Number,
            writeDefaults.BoundedCapacity,
            min: 1);
        metadata[FileSystemCompositionNodeTypes.Write]
            .Attributes[new ComponentAttributeName("omittedOptions")].Value
            .ShouldBe("defaultEncoding");

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

        await WithTransformNodeAsync<FileReadRequest, FlowResult<FileReadContent>>(
            FileSystemCompositionNodeTypes.Read,
            async (ports, host) =>
            {
                var message = FlowMessage.Create(
                    new FileReadRequest { Path = "input.txt" },
                    new CorrelationId("read"));
                var resultReceive = ports.ReceiveAsync<FlowResult<FileReadContent>>(
                    Output,
                    Timeout);
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(
                    Events,
                    Timeout);

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.Payload.IsError.ShouldBeFalse();
                result.Payload.Value.ShouldNotBeNull().Path.ShouldBe(Path.GetFullPath(filePath));
                result.Payload.Value.Content.OriginalBytes.AsSpan().ToArray()
                    .ShouldBe(System.Text.Encoding.UTF8.GetBytes("hello"));
                result.Payload.Value.ReadAt.ShouldBe(timestamp);

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(message.CorrelationId);
                @event.Payload.Name.ShouldBe(FileSystemDiagnosticNames.FileReadSucceeded);
                @event.Payload.Timestamp.ShouldBe(timestamp);
                await host.RevisionHost.StopApplicationAsync();
            },
            Properties(
                ("baseDirectory", directory.Path),
                ("maxBytes", 32)),
            clock,
            registry => registry.RegisterFileRead());
    }

    [Fact]
    public async Task Hosted_file_write_writes_under_base_directory_and_preserves_correlation_id()
    {
        using var directory = TempDirectory.Create("write");
        var timestamp = DateTimeOffset.Parse("2026-06-19T10:30:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var expectedPath = Path.Combine(directory.Path, "nested", "output.txt");

        await WithTransformNodeAsync<FileContentWriteRequest, FlowResult<FileWriteResult>>(
            FileSystemCompositionNodeTypes.Write,
            async (ports, host) =>
            {
                var message = FlowMessage.Create(
                    new FileContentWriteRequest
                    {
                        Path = "nested/output.txt",
                        Content = FlowContent.FromBytes(
                            System.Text.Encoding.UTF8.GetBytes("written"),
                            "text/plain",
                            "utf-8")
                    },
                    new CorrelationId("write"));
                var resultReceive = ports.ReceiveAsync<FlowResult<FileWriteResult>>(
                    Output,
                    Timeout);
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(
                    Events,
                    Timeout);

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.CorrelationId.ShouldBe(message.CorrelationId);
                result.Payload.IsError.ShouldBeFalse();
                result.Payload.Value.ShouldNotBeNull().Path.ShouldBe(Path.GetFullPath(expectedPath));
                result.Payload.Value.BytesWritten.ShouldBe(7);
                result.Payload.Value.WrittenAt.ShouldBe(timestamp);
                (await File.ReadAllTextAsync(expectedPath)).ShouldBe("written");

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(message.CorrelationId);
                @event.Payload.Name.ShouldBe(FileSystemDiagnosticNames.FileWriteSucceeded);
                @event.Payload.Timestamp.ShouldBe(timestamp);
                await host.RevisionHost.StopApplicationAsync();
            },
            Properties(("baseDirectory", directory.Path)),
            clock,
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
        var entries = new MessageTracker<FlowValue>();
        var events = new MessageTracker<CompositionComponentEvent>();

        await using var host = await StartSourceNodeAsync(
            FileSystemCompositionNodeTypes.DirectoryEnumerate,
            Properties(
                ("directory", "."),
                ("baseDirectory", directory.Path),
                ("filter", "*.txt"),
                ("includeSubdirectories", true),
                ("boundedCapacity", 8)),
            clock,
            entries,
            events,
            registry => registry.RegisterDirectoryEnumerate());

        host.StartResult.Succeeded.ShouldBeTrue();
        var completed = await events.WaitForAsync(value =>
            value.Payload.Name == FileSystemDiagnosticNames.DirectoryEnumerateCompleted);
        completed.Payload.Timestamp.ShouldBe(timestamp);
        var emitted = entries.Values;
        emitted.Select(message => message.Payload.GetObject()["name"].GetString()).Order()
            .ShouldBe(["child.txt", "root.txt"]);
        emitted.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
        emitted.ShouldAllBe(message =>
            message.Payload.GetObject()["enumeratedAt"].GetDateTimeOffset() == timestamp);
        await host.RevisionHost.StopApplicationAsync();
    }

    [Fact]
    public async Task Hosted_file_watch_starts_observes_change_and_stops()
    {
        using var directory = TempDirectory.Create("watch");
        var timestamp = DateTimeOffset.Parse("2026-06-19T11:30:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var watchedPath = Path.Combine(directory.Path, "created.txt");
        var changes = new MessageTracker<FlowValue>();
        var events = new MessageTracker<CompositionComponentEvent>();

        await using var host = await StartSourceNodeAsync(
            FileSystemCompositionNodeTypes.Watch,
            Properties(
                ("directory", "."),
                ("baseDirectory", directory.Path),
                ("boundedCapacity", 16)),
            clock,
            changes,
            events,
            registry => registry.RegisterFileWatch());

        host.StartResult.Succeeded.ShouldBeTrue();
        var started = await events.WaitForAsync(value =>
            value.Payload.Name == FileSystemDiagnosticNames.FileWatchStarted);
        started.Payload.Timestamp.ShouldBe(timestamp);

        await File.WriteAllTextAsync(watchedPath, "hello");

        var change = await changes.WaitForAsync(value =>
            value.Payload.GetObject()["name"].GetString() == "created.txt" &&
            value.Payload.GetObject()["changeType"].GetString() is "Created" or "Changed");
        var changeValue = change.Payload.GetObject();
        changeValue["path"].GetString().ShouldBe(Path.GetFullPath(watchedPath));
        changeValue["directory"].GetString().ShouldBe(Path.GetFullPath(directory.Path));
        changeValue["timestamp"].GetDateTimeOffset().ShouldBe(timestamp);
        change.CorrelationId.IsEmpty.ShouldBeFalse();

        await events.WaitForAsync(value =>
            value.Payload.Name == FileSystemDiagnosticNames.FileWatchChanged);
        await host.RevisionHost.StopApplicationAsync();
    }

    [Fact]
    public async Task Hosted_file_read_emits_normal_failure_and_continues_after_missing_file()
    {
        using var directory = TempDirectory.Create("read-errors");
        var validPath = Path.Combine(directory.Path, "valid.txt");
        await File.WriteAllTextAsync(validPath, "ok");

        await WithTransformNodeAsync<FileReadRequest, FlowResult<FileReadContent>>(
            FileSystemCompositionNodeTypes.Read,
            async (ports, host) =>
            {
                var missing = FlowMessage.Create(
                    new FileReadRequest { Path = "missing.txt" },
                    new CorrelationId("missing"));
                var valid = FlowMessage.Create(
                    new FileReadRequest { Path = "valid.txt" },
                    new CorrelationId("valid"));

                var failureReceive = ports.ReceiveAsync<FlowResult<FileReadContent>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, missing)).IsAccepted.ShouldBeTrue();
                var failure = (await failureReceive).Message.ShouldNotBeNull();
                var resultReceive = ports.ReceiveAsync<FlowResult<FileReadContent>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, valid)).IsAccepted.ShouldBeTrue();
                var result = (await resultReceive).Message.ShouldNotBeNull();

                failure.CorrelationId.ShouldBe(missing.CorrelationId);
                failure.Payload.Error.ShouldNotBeNull().Code
                    .ShouldBe(FileSystemErrorCodeNames.ReadNotFound);
                result.CorrelationId.ShouldBe(valid.CorrelationId);
                result.Payload.Value.ShouldNotBeNull().Content.OriginalBytes.AsSpan().ToArray()
                    .ShouldBe(System.Text.Encoding.UTF8.GetBytes("ok"));
                await host.RevisionHost.StopApplicationAsync();
            },
            Properties(("baseDirectory", directory.Path)),
            configureRegistry: registry => registry.RegisterFileRead());
    }

    [Theory]
    [InlineData(FileSystemCompositionNodeTypes.Read, "boundedCapacity", 0, "capacity")]
    [InlineData(FileSystemCompositionNodeTypes.Read, "maxBytes", 0L, "maxBytes")]
    [InlineData(FileSystemCompositionNodeTypes.Read, "defaultEncoding", "not-a-real-encoding", "defaultEncoding")]
    [InlineData(FileSystemCompositionNodeTypes.Write, "boundedCapacity", 0, "capacity")]
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
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [optionName] = value
        };
        if ((nodeType is FileSystemCompositionNodeTypes.DirectoryEnumerate or
             FileSystemCompositionNodeTypes.Watch) &&
            !properties.ContainsKey("directory"))
        {
            properties["directory"] = ".";
        }

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(nodeType, properties),
            registry => registry
                .RegisterFileRead()
                .RegisterFileWrite()
                .RegisterDirectoryEnumerate()
                .RegisterFileWatch());

        AssertPreparationFailure(host, expectedMessage);
    }

    [Fact]
    public async Task Invalid_watch_notify_filter_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                FileSystemCompositionNodeTypes.Watch,
                Properties(
                    ("directory", "."),
                    ("notifyFilters", new[] { "DefinitelyNotAFilter" }))),
            registry => registry.RegisterFileWatch());

        AssertPreparationFailure(host, "notifyFilters");
    }

    private static async Task WithTransformNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        TimeProvider? clock = null,
        Action<CompositionNodeRegistry>? configureRegistry = null)
    {
        var componentProperties = CopyProperties(properties);
        IReadOnlyList<string>? resources = null;
        if (clock is not null)
        {
            componentProperties[FileSystemCompositionResourceNames.Clock] = "Resources.fixed";
            resources = ["fixed"];
        }

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(nodeType, componentProperties, resources),
            registry => configureRegistry?.Invoke(registry),
            configureRuntimeServices: clock is null
                ? null
                : context => context.Services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    clock));
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static async ValueTask<CanonicalApplicationTestHost> StartSourceNodeAsync(
        string nodeType,
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider clock,
        MessageTracker<FlowValue> values,
        MessageTracker<CompositionComponentEvent> events,
        Action<CompositionNodeRegistry> configureRegistry)
    {
        var componentProperties = CopyProperties(properties);
        componentProperties[FileSystemCompositionResourceNames.Clock] = "Resources.fixed";
        componentProperties[FileSystemCompositionPortNames.Output] = "valueRecorder.Input";
        componentProperties["Events"] = "eventRecorder.Input";

        return await CanonicalApplicationTestHost.StartAsync(
            SourceComponent(
                nodeType,
                componentProperties,
                ["fixed"]),
            registry =>
            {
                configureRegistry(registry);
                RegisterRecorder(registry, ValueRecorderType, values);
                RegisterRecorder(registry, EventRecorderType, events);
            },
            configureRuntimeServices: context =>
                context.Services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    clock));
    }

    private static ApplicationDefinition SourceComponent(
        string nodeType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string> resources)
        => new(
            resources.Select(name => KeyValuePair.Create<string, ResourceDefinition>(
                name,
                new ResourceInstanceDefinition("host.external"))),
            [KeyValuePair.Create(
                WorkflowName,
                new FluxFlow.Composition.Model.WorkflowDefinition(
                [
                    KeyValuePair.Create(
                        ComponentName,
                        Component(nodeType, properties)),
                    KeyValuePair.Create(
                        "valueRecorder",
                        new ComponentDefinition(ValueRecorderType)),
                    KeyValuePair.Create(
                        "eventRecorder",
                        new ComponentDefinition(EventRecorderType))
                ]))]);

    private static ComponentDefinition Component(
        string nodeType,
        IReadOnlyDictionary<string, object?> properties)
        => new(
            nodeType,
            properties.Select(property => KeyValuePair.Create(
                property.Key,
                JsonSerializer.SerializeToElement(property.Value))));

    private static void RegisterRecorder<T>(
        CompositionNodeRegistry registry,
        string nodeType,
        MessageTracker<T> tracker)
        => registry.Register(
            nodeType,
            _ =>
            {
                var node = new MessageRecordingNode<T>(tracker);
                return ValueTask.FromResult(ComposedNode.Create(
                    node,
                    inputs:
                    [
                        CompositionPorts.Input<T>("Input", node.Input)
                    ]));
            },
            inputs: [CompositionPorts.Metadata<T>("Input")]);

    private static Dictionary<string, object?> CopyProperties(
        IReadOnlyDictionary<string, object?>? properties)
        => properties?.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, object?>(StringComparer.Ordinal);

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
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
        input.ValueType?.Value.ShouldBe(TypeName(typeof(TInput)));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(FileSystemCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe(TypeName(typeof(TOutput)));
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

    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    private sealed class MessageRecordingNode<T>(MessageTracker<T> tracker)
        : FlowNode<T, T>
    {
        protected override Task ProcessAsync(FlowMessage<T> message)
        {
            tracker.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class MessageTracker<T>
    {
        private readonly object _gate = new();
        private readonly List<FlowMessage<T>> _values = [];

        public IReadOnlyList<FlowMessage<T>> Values
        {
            get
            {
                lock (_gate)
                    return _values.ToArray();
            }
        }

        public void Add(FlowMessage<T> value)
        {
            lock (_gate)
                _values.Add(value);
        }

        public async ValueTask<FlowMessage<T>> WaitForAsync(
            Func<FlowMessage<T>, bool> predicate)
        {
            using var cancellation = new CancellationTokenSource(Timeout);
            try
            {
                while (true)
                {
                    lock (_gate)
                    {
                        var value = _values.FirstOrDefault(predicate);
                        if (value is not null)
                            return value;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(20), cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for a recorded workflow message.");
            }
        }
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
