using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
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
    [InlineData(FileSystemCompositionNodeTypes.Write, "boundedCapacity", 0, "capacity")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "directory", "", "directory")]
    [InlineData(FileSystemCompositionNodeTypes.DirectoryEnumerate, "maxEntries", 0L, "maxEntries")]
    [InlineData(FileSystemCompositionNodeTypes.Watch, "directory", "", "directory")]
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
