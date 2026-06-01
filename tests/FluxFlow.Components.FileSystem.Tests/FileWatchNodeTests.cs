using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FileWatchNodeTests
{
    [Fact]
    public async Task FileWatch_EmitsFileEvents()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path,
            boundedCapacity = 16
        });
        var output = new BufferBlock<FileWatchEvent>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var filePath = Path.Combine(directory.Path, "created.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var watchEvent = await ReceiveMatchingAsync(
            output,
            value => value.Name == "created.txt" &&
                     value.ChangeType is FileWatchChangeType.Created or FileWatchChangeType.Changed);

        watchEvent.Path.ShouldBe(Path.GetFullPath(filePath));
        watchEvent.Directory.ShouldBe(Path.GetFullPath(directory.Path));

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeAsync(runtimeNode);
    }

    [Fact]
    public async Task FileWatch_EmitsRenamedEvents()
    {
        using var directory = TempDirectory.Create();
        var originalPath = Path.Combine(directory.Path, "before.txt");
        var renamedPath = Path.Combine(directory.Path, "after.txt");
        await File.WriteAllTextAsync(originalPath, "hello");
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path,
            boundedCapacity = 16
        });
        var output = new BufferBlock<FileWatchEvent>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        File.Move(originalPath, renamedPath);

        var watchEvent = await ReceiveMatchingAsync(
            output,
            value => value.Name == "after.txt" &&
                     value.ChangeType == FileWatchChangeType.Renamed);

        watchEvent.Path.ShouldBe(Path.GetFullPath(renamedPath));
        watchEvent.OldPath.ShouldBe(Path.GetFullPath(originalPath));
        watchEvent.OldName.ShouldBe("before.txt");

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeAsync(runtimeNode);
    }

    [Fact]
    public async Task FileWatch_EmitsDiagnosticsAndFlowEvents()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path,
            boundedCapacity = 16
        });
        var output = new BufferBlock<FileWatchEvent>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        var events = new BufferBlock<FlowEvent>();
        LinkOutput(runtimeNode, output);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowEventSource>()!
            .Events.LinkTo(events);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "diag.txt"), "hello");
        await ReceiveMatchingAsync(output, value => value.Name == "diag.txt");

        await ReceiveDiagnosticAsync(
            diagnostics,
            FileSystemDiagnosticNames.FileWatchStarted);
        await ReceiveDiagnosticAsync(
            diagnostics,
            FileSystemDiagnosticNames.FileWatchChanged);
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowEvent.Type.ShouldBe(FileSystemEventNames.FileWatchChanged);
        flowEvent.Subject.ShouldEndWith("diag.txt");

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeAsync(runtimeNode);
    }

    [Fact]
    public async Task FileWatch_CompleteStopsAndCompletesOutput()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path
        });
        var output = new BufferBlock<FileWatchEvent>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        output.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await DisposeAsync(runtimeNode);
    }

    [Fact]
    public async Task FileWatch_MissingDirectoryFailsStartup()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = "missing",
            baseDirectory = directory.Path
        });
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("failed to start");
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWatchDirectoryMissing);

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeAsync(runtimeNode);
    }

    [Fact]
    public async Task FileWatch_RejectsAbsoluteDirectoryWhenDisabled()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = directory.Path,
            baseDirectory = directory.Path
        });
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("failed to start");
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWatchAbsolutePathDenied);

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeAsync(runtimeNode);
    }

    [Fact]
    public void FileWatch_RejectsMissingDirectoryOption()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { }));

        exception.Message.ShouldContain("directory");
    }

    [Fact]
    public void FileWatch_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { directory = ".", boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void FileWatch_RejectsUnsupportedNotifyFilter()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                directory = ".",
                notifyFilters = new[] { "DefinitelyNotAFilter" }
            }));

        exception.Message.ShouldContain("notifyFilters");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterFileSystemComponents();
        registry.TryGetFactory(FileSystemComponentTypes.FileWatch, out var factory).ShouldBeTrue();
        return factory(FileSystemTestHost.CreateContext(
            FileSystemComponentTypes.FileWatch,
            configuration,
            "watcher"));
    }

    private static void LinkOutput(RuntimeNode runtimeNode, BufferBlock<FileWatchEvent> target)
    {
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<FileWatchEvent>(
                    new PortAddress("test", new NodeName("events"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<FileWatchEvent> ReceiveMatchingAsync(
        BufferBlock<FileWatchEvent> output,
        Func<FileWatchEvent, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cancellation.IsCancellationRequested)
        {
            var value = await output.ReceiveAsync(cancellation.Token);
            if (predicate(value))
            {
                return value;
            }
        }

        throw new TimeoutException("Timed out waiting for file watch event.");
    }

    private static async Task<FlowDiagnostic> ReceiveDiagnosticAsync(
        BufferBlock<FlowDiagnostic> diagnostics,
        string name)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cancellation.IsCancellationRequested)
        {
            var value = await diagnostics.ReceiveAsync(cancellation.Token);
            if (value.Name == name)
            {
                return value;
            }
        }

        throw new TimeoutException($"Timed out waiting for diagnostic '{name}'.");
    }

    private static async ValueTask DisposeAsync(RuntimeNode runtimeNode)
    {
        if (runtimeNode.Node is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fluxflow-filesystem-watch-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
    }
}
